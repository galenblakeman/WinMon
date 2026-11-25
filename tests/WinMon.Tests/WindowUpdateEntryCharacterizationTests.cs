using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Resources;
using WinMon.Updates;
using static WinMon.Tests.UpdateTests;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static Task? WindowUpdateTransfer(UpdateUi ui) => (Task?)ui.Field("_updateDownloadTask").GetValue(ui.Window);
    private static void ClickWindowUpdateNow(UpdateUi ui) => ui.Window.UpdateNowButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static (bool, bool, bool) UpdateActionEnablement(MainWindow window) =>
        (window.UpdateNowButton.IsEnabled, window.ReleaseNotesButton.IsEnabled, window.DismissUpdateButton.IsEnabled);

    private static void FinishWindowTransfer(UpdateUi ui)
    {
        FinishWindowCheck(WindowUpdateTransfer(ui) ?? throw new AssertionException("Update now did not start its coordinator"));
        // Drain the async-void entry handler's continuation after its tracked coordinator has completed.
        FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowWithoutAClientOrOfferLeavesActionsAndTransportUntouched(bool clientPresent)
    {
        using var ui = new UpdateUi(_directory);
        var requests = 0;
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(_ =>
        {
            requests++;
            throw new AssertionException("An ineligible update must not check HTTP");
        })), "unused", _directory);
        if (clientPresent) ui.Attach(client);
        ui.Window.ShowUpdateStatus("existing notice", true);
        ui.Window.UpdateNowButton.IsEnabled = false;
        ui.Window.DismissUpdateButton.IsEnabled = false;
        var before = UpdateUiState(ui.Window);
        var enabled = UpdateActionEnablement(ui.Window);
        ClickWindowUpdateNow(ui);
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
        Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo(enabled));
        Assert.That(WindowUpdateTransfer(ui), Is.Null);
        Assert.That(requests, Is.Zero);
        Assert.That(Directory.Exists(_directory), Is.False);
    }

#if !WINMON_STORE
    private UpdateClient WindowPreStagingClient(UpdateUi ui, RSA key, Func<string?> version, Func<string?>? processPath = null) =>
        WindowReleaseClient(key, version, processPath ?? (() => Path.Combine(_directory, "unpublished-host.exe")),
            _ =>
            {
                ui.UnexpectedInstallationLookups++;
                throw new AssertionException("The publication guard must stop before installation lookup");
            });

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowClosingOrPendingGuardsKeepTheExistingActionsAndTask(bool pending)
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var reads = 0;
        using var client = WindowPreStagingClient(ui, key, () => { reads++; return "99999.1.1"; });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        var outstanding = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (pending) ui.Field("_updateDownloadTask").SetValue(ui.Window, outstanding.Task);
        else ui.Window.IsClosing = true;
        ui.Window.UpdateNowButton.IsEnabled = false;
        ui.Window.DismissUpdateButton.IsEnabled = false;
        var before = UpdateUiState(ui.Window);
        var enabled = UpdateActionEnablement(ui.Window);
        var task = WindowUpdateTransfer(ui);
        var previousReads = reads;
        try
        {
            ClickWindowUpdateNow(ui);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo(enabled));
            Assert.That(WindowUpdateTransfer(ui), Is.SameAs(task));
            Assert.That(reads, Is.EqualTo(previousReads));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        finally { outstanding.TrySetResult(true); }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowQueuedWithdrawalBlocksReentryThenRestoresActionsWithoutStaging()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        string? version = "99999.1.1";
        var reads = 0;
        using var client = WindowPreStagingClient(ui, key, () => { reads++; return version; });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        var beforeReads = reads;
        var ownsGate = true;
        ui.Gate.Wait();
        try
        {
            version = null;
            ClickWindowUpdateNow(ui);
            var transfer = WindowUpdateTransfer(ui);
            Assert.That(transfer, Is.Not.Null);
            Assert.That(transfer!.IsCompleted, Is.False);
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((false, false, false)));
            ClickWindowUpdateNow(ui);
            Assert.That(WindowUpdateTransfer(ui), Is.SameAs(transfer));
            Assert.That(reads, Is.EqualTo(beforeReads));
            ui.Gate.Release(); ownsGate = false;
            FinishWindowTransfer(ui);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpToDate, Visibility.Visible, Visibility.Collapsed,
                Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((true, true, true)));
            Assert.That(reads, Is.EqualTo(beforeReads + 1));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        finally { if (ownsGate) ui.Gate.Release(); FinishWindowTransfer(ui); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowPreStagingFailuresStayGenericAndCompletedAttemptsCanBeRetried(bool foreignCancellation)
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var targetReads = 0;
        using var client = WindowPreStagingClient(ui, key, () => "99999.1.1", () =>
        {
            targetReads++;
            if (foreignCancellation) throw new OperationCanceledException("private process boundary");
            return Path.Combine(_directory, "unpublished-host.exe");
        });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            ClickWindowUpdateNow(ui);
            FinishWindowTransfer(ui);
            Assert.That(WindowUpdateTransfer(ui)!.IsCompletedSuccessfully, Is.True);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateFailed, Visibility.Visible, Visibility.Visible,
                Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((true, true, true)));
            Assert.That(targetReads, Is.EqualTo(attempt));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, "99999.1.1");
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowRecheckFailureUsesTheRetainedSignedOfferBeforeThePublicationGuard()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var fail = false;
        var targetReads = 0;
        using var client = WindowPreStagingClient(ui, key,
            () => fail ? throw new HttpRequestException("private recheck endpoint") : "99999.1.1",
            () => { targetReads++; return Path.Combine(_directory, "unpublished-host.exe"); });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        fail = true;
        ClickWindowUpdateNow(ui);
        FinishWindowTransfer(ui);
        Assert.That(targetReads, Is.EqualTo(1), "The signed fallback reaches publication validation");
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateFailed, Visibility.Visible, Visibility.Visible,
            Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));
        Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((true, true, true)));
        Assert.That(ui.Window.UpdateStatusText.Text, Does.Not.Contain("private recheck endpoint"));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowRetainsTheNewerSignedVersionReturnedByItsImmediateRecheck()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var version = "99999.1.1";
        var targetReads = 0;
        using var client = WindowPreStagingClient(ui, key, () => version,
            () => { targetReads++; return Path.Combine(_directory, "unpublished-host.exe"); });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, version);
        version = "99999.1.2";
        ClickWindowUpdateNow(ui);
        FinishWindowTransfer(ui);
        Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo(Strings.UpdateFailed));
        Assert.That(targetReads, Is.EqualTo(1));
        RefreshWindowUpdateText(ui.Window)();
        AssertWindowOffer(ui.Window, version);
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowDistinguishesCancelButtonFromForeignRecheckCancellation(bool foreignCancellation)
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        using var initial = WindowPreStagingClient(ui, key, () => "99999.1.1");
        ui.Attach(initial);
        FinishWindowCheck(ui.Check(false, default));
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var targetReads = 0;
        using var pending = new UpdateClient(new HttpClient(new AsyncWindowUpdateHandler(token =>
        {
            calls++;
            return response.Task.WaitAsync(token);
        })), key.ExportSubjectPublicKeyInfoPem(), _directory,
            processPath: () => { targetReads++; return Path.Combine(_directory, "unpublished-host.exe"); },
            isInstalled: _ =>
            {
                ui.UnexpectedInstallationLookups++;
                throw new AssertionException("Never stage an update");
            });
        ui.Attach(pending);
        ClickWindowUpdateNow(ui);
        try
        {
            Assert.That(WindowUpdateTransfer(ui)!.IsCompleted, Is.False);
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((false, false, false)));
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateChecking, Visibility.Visible, Visibility.Collapsed,
                Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible)));
            if (foreignCancellation) response.SetException(new OperationCanceledException("private recheck cancellation"));
            else ui.Window.CancelUpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FinishWindowTransfer(ui);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((foreignCancellation ? Strings.UpdateFailed : Strings.UpdateCancelled,
                Visibility.Visible, Visibility.Visible, Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((true, true, true)));
            Assert.That(targetReads, Is.EqualTo(foreignCancellation ? 1 : 0));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(Directory.Exists(_directory), Is.False);
            ui.Attach(initial);
            FinishWindowCheck(ui.Check(false, default));
            AssertWindowOffer(ui.Window, "99999.1.1");
        }
        finally { response.TrySetCanceled(); FinishWindowTransfer(ui); }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowClosingWhileQueuedStopsBeforeRecheckingAndKeepsClosingButtonsDisabled()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var reads = 0;
        using var client = WindowPreStagingClient(ui, key, () => { reads++; return "99999.1.1"; });
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        var before = UpdateUiState(ui.Window);
        var beforeReads = reads;
        var ownsGate = true;
        ui.Gate.Wait();
        try
        {
            ClickWindowUpdateNow(ui);
            Assert.That(WindowUpdateTransfer(ui)!.IsCompleted, Is.False);
            ui.Window.IsClosing = true;
            ui.Gate.Release(); ownsGate = false;
            FinishWindowTransfer(ui);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((false, false, false)));
            Assert.That(reads, Is.EqualTo(beforeReads));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        finally { if (ownsGate) ui.Gate.Release(); FinishWindowTransfer(ui); }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateNowLifetimeCancellationBeforeTheGateKeepsTheCallerLeaseAndExistingFailurePolicy()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        using var client = WindowPreStagingClient(ui, key, () => "99999.1.1");
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        ui.Gate.Wait();
        try
        {
            ClickWindowUpdateNow(ui);
            Assert.That(WindowUpdateTransfer(ui)!.IsCompleted, Is.False);
            ((CancellationTokenSource)ui.Field("_updateLifetime").GetValue(ui.Window)!).Cancel();
            FinishWindowTransfer(ui);
            Assert.That(ui.Gate.CurrentCount, Is.Zero);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateFailed, Visibility.Visible, Visibility.Visible,
                Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));
            Assert.That(UpdateActionEnablement(ui.Window), Is.EqualTo((true, true, true)));
            Assert.That(Directory.Exists(_directory), Is.False);
        }
        finally { if (ui.Gate.CurrentCount == 0) ui.Gate.Release(); FinishWindowTransfer(ui); }
    }
#endif
}
