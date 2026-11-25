using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Updates;
using static WinMon.Tests.UpdateTests;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private sealed class UpdateUi : IDisposable
    {
        private readonly Dictionary<object, object?> _resources = [];
        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        internal MainWindow Window { get; }
        internal Func<bool, CancellationToken, Task> Check { get; }
        internal int UnexpectedInstallationLookups { get; set; }

        internal UpdateUi(string root, bool diagnostic = false)
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
            foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                         "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
                if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
            if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
            if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
            Window = new MainWindow(diagnostic, new MonitorPreferences { KeepHistoryAcrossRestarts = false },
                new PreferenceStore(Path.Combine(root, "settings.json")));
            Window.Loaded -= Window.OnLoaded;
            Check = (typeof(MainWindow).GetMethod("CheckForUpdatesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Missing update-check callback"))
                .CreateDelegate<Func<bool, CancellationToken, Task>>(Window);
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
        }

        internal FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing update boundary: " + name);
        internal void Attach(UpdateClient client) => Field("_updateClient").SetValue(Window, client);
        internal SemaphoreSlim Gate => (SemaphoreSlim)Field("_updateGate").GetValue(Window)!;

        public void Dispose()
        {
            foreach (var view in new FrameworkElement[] { Window.UsageView, Window.Processes, Window.ServiceView, Window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Window.CloseForFixture();
            var resources = Application.Current.Resources;
            foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
            foreach (var (key, value) in _resources) resources[key] = value;
            SynchronizationContext.SetSynchronizationContext(_context);
            Assert.That(UnexpectedInstallationLookups, Is.Zero, "The publication boundary unexpectedly reached installation lookup");
        }
    }

    private static void FinishWindowCheck(Task task)
    {
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!bounded.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = bounded.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        bounded.GetAwaiter().GetResult();
    }

    private static (string, Visibility, Visibility, Visibility, Visibility, Visibility) UpdateUiState(MainWindow window) =>
        (window.UpdateStatusText.Text, window.UpdateFooter.Visibility, window.ReleaseNotesButton.Visibility,
            window.UpdateNowButton.Visibility, window.DismissUpdateButton.Visibility, window.CancelUpdateButton.Visibility);

    private static void AssertWindowOffer(MainWindow window, string version) =>
        Assert.That(UpdateUiState(window), Is.EqualTo((Strings.Format(Strings.UpdateAvailable, version), Visibility.Visible,
            Visibility.Visible, Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));

    [TestCase("missing")]
    [TestCase("closing")]
    [TestCase("downloading")]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckGuardsLeaveTheCurrentNoticeAndTransportUntouched(string guard)
    {
        using var ui = new UpdateUi(_directory);
        var requests = 0;
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        })), "unused", _directory);
        if (guard != "missing") ui.Attach(client);
        if (guard == "closing") ui.Window.IsClosing = true;
        if (guard == "downloading") ui.Field("_updateDownloading").SetValue(ui.Window, true);
        ui.Window.ShowUpdateStatus("existing capture", false);
        var before = UpdateUiState(ui.Window);
        FinishWindowCheck(ui.Check(true, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
        Assert.That(requests, Is.Zero);
    }

#if WINMON_STORE
    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckInStoreRetainsPolicyRejectionWithoutHttpOrUpdaterWork()
    {
        using var ui = new UpdateUi(_directory);
        var requests = 0;
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(_ =>
        {
            requests++;
            throw new AssertionException("Store checks must not contact HTTP");
        })), "unused", _directory);
        ui.Attach(client);
        FinishWindowCheck(ui.Check(true, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateFailed, Visibility.Visible, Visibility.Collapsed,
            Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)));
        Assert.That(requests, Is.Zero);
        Assert.That(Directory.Exists(_directory), Is.False);
    }
#else
    private UpdateClient WindowReleaseClient(RSA key, Func<string?> version, Func<string?>? processPath = null,
        Func<string, bool>? isInstalled = null) => new(new HttpClient(new DelegateHandler(request =>
    {
        var current = version();
        if (request.RequestUri!.AbsolutePath.EndsWith("/latest", StringComparison.Ordinal))
            return current is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : JsonResponse(Release("v" + current));
        Assert.That(current, Is.Not.Null);
        var (manifest, signature) = SignedManifest(key, current!);
        if (request.RequestUri.AbsolutePath.EndsWith(UpdateClient.DesktopManifestName, StringComparison.Ordinal)) return BytesResponse(manifest);
        if (request.RequestUri.AbsolutePath.EndsWith(UpdateClient.DesktopSignatureName, StringComparison.Ordinal)) return BytesResponse(Encoding.UTF8.GetBytes(signature));
        throw new AssertionException("Unexpected update request: " + request.RequestUri);
    })), key.ExportSubjectPublicKeyInfoPem(), _directory, processPath: processPath, isInstalled: isInstalled);

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckKeepsRequestedAndBackgroundNoOfferNoticesDistinct(bool requested)
    {
        using var ui = new UpdateUi(_directory);
        var requests = 0;
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(request =>
        {
            Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo("https://api.github.com/repos/galenblakeman/WinMon/releases/latest"));
            requests++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        })), "unused", _directory);
        ui.Attach(client);
        ui.Window.ShowUpdateStatus("existing capture", false);
        var before = UpdateUiState(ui.Window);
        FinishWindowCheck(ui.Check(requested, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(requested
            ? (Strings.UpToDate, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)
            : before));
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckPreservesDismissalForTheSameVersionAndShowsChangedOrRequestedOffers()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var version = "99999.1.1";
        using var client = WindowReleaseClient(key, () => version);
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, version);
        ui.Window.DismissUpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(ui.Window.UpdateFooter.Visibility, Is.EqualTo(Visibility.Collapsed));
        FinishWindowCheck(ui.Check(false, default));
        Assert.That(ui.Window.UpdateFooter.Visibility, Is.EqualTo(Visibility.Collapsed));
        FinishWindowCheck(ui.Check(true, default));
        AssertWindowOffer(ui.Window, version);
        ui.Window.DismissUpdateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        version = "99999.1.2";
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, version);
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckWithdrawsAnOfferWithoutHidingAnUnrelatedCaptureNotice(bool captureNotice)
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        string? version = "99999.1.1";
        using var client = WindowReleaseClient(key, () => version);
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, version);
        if (captureNotice) ui.Window.ShowUpdateStatus("capture saved", false);
        version = null;
        FinishWindowCheck(ui.Check(false, default));
        Assert.That(ui.Window.UpdateFooter.Visibility, Is.EqualTo(captureNotice ? Visibility.Visible : Visibility.Collapsed));
        if (captureNotice) Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo("capture saved"));
        var before = UpdateUiState(ui.Window);
        FinishWindowCheck(ui.Check(false, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
    }

    private sealed class AsyncWindowUpdateHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri!.AbsoluteUri, Is.EqualTo("https://api.github.com/repos/galenblakeman/WinMon/releases/latest"));
            return response(cancellationToken);
        }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckClosingDuringHttpKeepsTheNoticeAndReleasesTheGate()
    {
        using var ui = new UpdateUi(_directory);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new UpdateClient(new HttpClient(new AsyncWindowUpdateHandler(token =>
            ++calls == 1 ? response.Task.WaitAsync(token) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))), "unused", _directory);
        ui.Attach(client);
        ui.Window.ShowUpdateStatus("existing capture", false);
        var before = UpdateUiState(ui.Window);
        var check = ui.Check(false, default);
        try
        {
            Assert.That(check.IsCompleted, Is.False);
            ui.Window.IsClosing = true;
            response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            FinishWindowCheck(check);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo(before));
            ui.Window.IsClosing = false;
            FinishWindowCheck(ui.Check(true, default));
            Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo(Strings.UpToDate));
            Assert.That(calls, Is.EqualTo(2));
        }
        finally
        {
            if (!response.Task.IsCompleted) response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            FinishWindowCheck(check);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckOwnedCancellationPreservesTheNoticeAndGateOwnership(bool inFlight, bool requested)
    {
        using var ui = new UpdateUi(_directory);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new UpdateClient(new HttpClient(new AsyncWindowUpdateHandler(token =>
        {
            calls++;
            return inFlight && calls == 1 ? response.Task.WaitAsync(token) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        })), "unused", _directory);
        ui.Attach(client);
        ui.Window.ShowUpdateStatus("existing capture", false);
        var before = UpdateUiState(ui.Window);
        var gate = ui.Gate;
        var ownsGate = !inFlight;
        if (ownsGate) gate.Wait();
        using var cancellation = new CancellationTokenSource();
        var check = ui.Check(requested, cancellation.Token);
        try
        {
            Assert.That(check.IsCompleted, Is.False);
            Assert.That(calls, Is.EqualTo(inFlight ? 1 : 0));
            cancellation.Cancel();
            FinishWindowCheck(check);
            Assert.That(UpdateUiState(ui.Window), Is.EqualTo(requested
                ? (Strings.UpdateChecking, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)
                : before));
            if (ownsGate) { Assert.That(gate.CurrentCount, Is.Zero); gate.Release(); ownsGate = false; }
            FinishWindowCheck(ui.Check(true, default));
            Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo(Strings.UpToDate));
        }
        finally
        {
            cancellation.Cancel();
            response.TrySetCanceled();
            FinishWindowCheck(check);
            if (ownsGate && gate.CurrentCount == 0) gate.Release();
        }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckSerializesConcurrentRequestsAndLetsTheWaitingUserCheckFinish()
    {
        using var ui = new UpdateUi(_directory);
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var client = new UpdateClient(new HttpClient(new AsyncWindowUpdateHandler(token =>
            ++calls == 1 ? response.Task.WaitAsync(token) : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))), "unused", _directory);
        ui.Attach(client);
        var first = ui.Check(false, default);
        var second = ui.Check(true, default);
        var both = Task.WhenAll(first, second);
        try
        {
            Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo(Strings.UpdateChecking));
            response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            FinishWindowCheck(both);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ui.Window.UpdateStatusText.Text, Is.EqualTo(Strings.UpToDate));
            Assert.That(ui.Window.UpdateNowButton.Visibility, Is.EqualTo(Visibility.Collapsed));
        }
        finally
        {
            if (!response.Task.IsCompleted) response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            FinishWindowCheck(both);
        }
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckFailureKeepsTheExistingSignedOfferActionsAvailableForRetry()
    {
        using var ui = new UpdateUi(_directory);
        using var key = RSA.Create(2048);
        var fail = false;
        using var client = WindowReleaseClient(key, () => fail ? throw new HttpRequestException("private endpoint") : "99999.1.1");
        ui.Attach(client);
        FinishWindowCheck(ui.Check(false, default));
        AssertWindowOffer(ui.Window, "99999.1.1");
        fail = true;
        FinishWindowCheck(ui.Check(true, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo((Strings.UpdateFailed, Visibility.Visible, Visibility.Visible,
            Visibility.Visible, Visibility.Visible, Visibility.Collapsed)));
        Assert.That(ui.Window.UpdateNowButton.IsEnabled && ui.Window.ReleaseNotesButton.IsEnabled, Is.True);
        Assert.That(ui.Window.UpdateStatusText.Text, Does.Not.Contain("private endpoint"));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowUpdateCheckFailuresStayGenericAndBackgroundChecksKeepTheCurrentNotice(bool foreignCancellation, bool requested)
    {
        using var ui = new UpdateUi(_directory);
        var calls = 0;
        using var client = new UpdateClient(new HttpClient(new AsyncWindowUpdateHandler(_ =>
        {
            calls++;
            return Task.FromException<HttpResponseMessage>(foreignCancellation
                ? new OperationCanceledException("private transport detail") : new HttpRequestException("private transport detail"));
        })), "unused", _directory);
        ui.Attach(client);
        ui.Window.ShowUpdateStatus("existing capture", false);
        var before = UpdateUiState(ui.Window);
        FinishWindowCheck(ui.Check(requested, default));
        Assert.That(UpdateUiState(ui.Window), Is.EqualTo(requested
            ? (Strings.UpdateFailed, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed)
            : before));
        Assert.That(ui.Window.UpdateStatusText.Text, Does.Not.Contain("private transport detail"));
        Assert.That(calls, Is.EqualTo(1));
    }
#endif
}
