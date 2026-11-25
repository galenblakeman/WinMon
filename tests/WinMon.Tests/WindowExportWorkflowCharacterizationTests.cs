using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

internal sealed class CsvWriteCall(string path, CsvExportKind kind, IReadOnlyList<MonitorSample> samples,
    IReadOnlyList<AppUsage> apps, DateTimeOffset since, long busy, long unattributed,
    IReadOnlyList<ServiceFrame>? services, IReadOnlyList<ConnectionCapture>? connections, UsagePeriod? period)
{
    internal string Path { get; } = path;
    internal CsvExportKind Kind { get; } = kind;
    internal IReadOnlyList<MonitorSample> Samples { get; } = samples;
    internal IReadOnlyList<AppUsage> Apps { get; } = apps;
    internal DateTimeOffset Since { get; } = since;
    internal long Busy { get; } = busy;
    internal long Unattributed { get; } = unattributed;
    internal IReadOnlyList<ServiceFrame>? Services { get; } = services;
    internal IReadOnlyList<ConnectionCapture>? Connections { get; } = connections;
    internal UsagePeriod? Period { get; } = period;
    internal int Thread { get; } = Environment.CurrentManagedThreadId;
}

internal sealed class OwnedCsvExportOperations(string destination, CsvExportChoice? choice) : ICsvExportOperations, IDisposable
{
    internal Func<CsvExportKind, string?, Window, CsvExportChoice?> ChooseAction { get; set; } = (_, _, _) => choice;
    internal Func<SaveFileDialog, Window, bool?>? SaveAction { get; set; }
    internal Action<CsvWriteCall>? WriteAction { get; set; }
    internal ConcurrentQueue<(CsvExportKind Initial, string? Note, Window Owner)> Choices { get; } = new();
    internal ConcurrentQueue<(SaveFileDialog Dialog, Window Owner, string InitialName)> Saves { get; } = new();
    internal ConcurrentQueue<CsvWriteCall> Writes { get; } = new();
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int ExpectedEntries { get; set; } = 1;
    internal ManualResetEventSlim Release { get; } = new(true);
    public CsvExportChoice? Choose(CsvExportKind initial, string? usageNote, Window owner)
    {
        Choices.Enqueue((initial, usageNote, owner));
        return ChooseAction(initial, usageNote, owner);
    }
    public bool? ShowSave(SaveFileDialog dialog, Window owner)
    {
        Saves.Enqueue((dialog, owner, dialog.FileName));
        if (SaveAction is not null) return SaveAction(dialog, owner);
        dialog.FileName = destination;
        return true;
    }
    public void Write(string path, CsvExportKind kind, IReadOnlyList<MonitorSample> samples, IReadOnlyList<AppUsage> apps,
        DateTimeOffset since, long busyTicks, long notAttributedTicks, IReadOnlyList<ServiceFrame>? services,
        IReadOnlyList<ConnectionCapture>? connections, UsagePeriod? period)
    {
        var call = new CsvWriteCall(path, kind, samples, apps, since, busyTicks, notAttributedTicks, services, connections, period);
        Writes.Enqueue(call);
        if (Writes.Count >= ExpectedEntries) Entered.TrySetResult();
        if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Owned export writer was not released");
        WriteAction?.Invoke(call);
    }
    public void Dispose() { Release.Set(); Release.Dispose(); }
}

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static void InvokeExportWorkflow(UpdateUi ui, OwnedCsvExportOperations operations,
        string[]? expectedNotices = null, Action<Action>? whilePending = null,
        Action<IReadOnlyList<Exception>>? verifyErrors = null, Action? onNotice = null, bool showParent = true)
    {
        expectedNotices ??= [];
        ui.Window.ApplyTheme(false, false);
        if (showParent && !ui.Window.IsClosing && !ui.Window.IsVisible)
        {
            ui.Window.ShowActivated = false;
            ui.Window.ShowInTaskbar = false;
            ui.Window.Title = "Owned export workflow fixture";
            ui.Window.Show();
        }
        Assert.That(CurrentExportWrites, Is.Empty);
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var observed = new HashSet<MessageDialog>();
        var messages = new List<string>();
        var context = new WindowActionContext(ui.Window.Dispatcher);
        DispatcherOperation? watch = null;
        Exception? failure = null;
        Window[] OwnedDialogs() => Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
        void Watch()
        {
            foreach (var window in OwnedDialogs())
            {
                if (window is not MessageDialog dialog)
                {
                    failure ??= new AssertionException("Unexpected export dependency window: " + window.Title);
                    window.Close();
                    continue;
                }
                if (!dialog.IsVisible || !observed.Add(dialog)) continue;
                try
                {
                    Assert.That(dialog.Owner, ui.Window.IsVisible ? Is.SameAs(ui.Window) : Is.Null);
                    Assert.That(dialog.Title, Is.EqualTo(Strings.ExportCsv));
                    var message = SettingsEntryFlow.Children((DependencyObject)dialog.Content).OfType<TextBlock>().Single().Text;
                    messages.Add(message);
                    Assert.That(expectedNotices, Does.Contain(message));
                    onNotice?.Invoke();
                    SettingsEntryFlow.Children((DependencyObject)dialog.Content).OfType<Button>().Single()
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure ??= error; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
        }
        void Begin()
        {
            var previous = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(context);
                ui.Window.ExportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Send, ui.Window.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) =>
        {
            failure ??= new AssertionException("Export workflow exceeded its owned modal bound");
            operations.Release.Set();
            foreach (var window in OwnedDialogs()) if (window.IsVisible) window.Close();
        };
        using var scope = CsvExportOperations.OverrideForTests(operations);
        try
        {
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Watch));
            timeout.Start();
            Begin();
            try { whilePending?.Invoke(Begin); }
            catch (Exception error) { failure ??= error; }
            finally { operations.Release.Set(); }
            FinishWindowCheck(context.Completion.Task);
            FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            if (failure is not null) throw new AssertionException(failure.ToString());
            if (verifyErrors is null) Assert.That(context.Errors, Is.Empty);
            else verifyErrors(context.Errors);
            Assert.That(messages, Is.EquivalentTo(expectedNotices));
            Assert.That(OwnedDialogs(), Is.Empty);
            Assert.That(CurrentExportWrites, Is.Empty);
        }
        finally
        {
            operations.Release.Set();
            timeout.Stop();
            watch?.Abort();
            foreach (var window in OwnedDialogs()) if (window.IsVisible) window.Close();
        }
    }

    private OwnedCsvExportOperations CsvOperations(CsvExportKind kind, bool range = false) =>
        new(Path.Combine(_directory, "owned.csv"), new CsvExportChoice(kind, range, "owned-choice.csv"));

    private static MonitorSample[] SeedExportFrames(UpdateUi ui)
    {
        ui.Window.Preferences = ui.Window.Preferences with { ChartRangeSeconds = 120, RetentionSeconds = 600 };
        ui.Window.Session.Usage.Reset(DateTimeOffset.UnixEpoch);
        long[] moments = [80 * Stopwatch.Frequency - 1, 80 * Stopwatch.Frequency, 80 * Stopwatch.Frequency + 1, 200 * Stopwatch.Frequency];
        var samples = moments.Select(moment => new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(moment / (double)Stopwatch.Frequency),
            10, [], 0, MonotonicTimestamp: moment)).ToArray();
        foreach (var sample in samples)
            ui.Window.Session.Record(sample, services: new ServiceFrame(sample, []),
                connections: new NetworkConnectionsFrame([], NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
        return samples;
    }

    [TestCase(CsvExportKind.ResourceHistory, false)]
    [TestCase(CsvExportKind.ResourceHistory, true)]
    [TestCase(CsvExportKind.ProcessSamples, false)]
    [TestCase(CsvExportKind.ProcessSamples, true)]
    [TestCase(CsvExportKind.UsageTotals, false)]
    [TestCase(CsvExportKind.UsageTotals, true)]
    [TestCase(CsvExportKind.Services, false)]
    [TestCase(CsvExportKind.Services, true)]
    [TestCase(CsvExportKind.Connections, false)]
    [TestCase(CsvExportKind.Connections, true)]
    [TestCase(CsvExportKind.NetworkTraffic, false)]
    [TestCase(CsvExportKind.NetworkTraffic, true)]
    public void ExportWorkflowPreservesEachKindsSnapshotAndExactMonotonicRangeBoundary(CsvExportKind kind, bool range)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var samples = SeedExportFrames(ui);
        using var operations = CsvOperations(kind, range);
        var uiThread = Environment.CurrentManagedThreadId;
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))]);
        var call = operations.Writes.Single();
        var expected = range ? samples.Skip(1).ToArray() : samples;
        Assert.Multiple(() =>
        {
            Assert.That(call.Kind, Is.EqualTo(kind));
            Assert.That(call.Path, Is.EqualTo(Path.Combine(_directory, "owned.csv")));
            Assert.That(call.Thread, Is.Not.EqualTo(uiThread));
            Assert.That(call.Samples, Is.EqualTo(kind is CsvExportKind.ResourceHistory or CsvExportKind.ProcessSamples ? expected : []));
            Assert.That(call.Services!.Select(frame => frame.Sample), Is.EqualTo(kind == CsvExportKind.Services ? expected : []));
            Assert.That(call.Connections!.Select(frame => frame.Sample), Is.EqualTo(kind is CsvExportKind.Connections or CsvExportKind.NetworkTraffic ? expected : []));
            Assert.That(call.Apps, Is.Empty);
            Assert.That(call.Period, Is.Null);
            Assert.That(call.Since, Is.EqualTo(DateTimeOffset.UnixEpoch));
            Assert.That(call.Busy, Is.Zero);
            Assert.That(call.Unattributed, Is.Zero);
        });
        var save = operations.Saves.Single();
        Assert.Multiple(() =>
        {
            Assert.That(save.Owner, Is.SameAs(ui.Window));
            Assert.That(save.InitialName, Is.EqualTo("owned-choice.csv"));
            Assert.That(save.Dialog.Title, Is.EqualTo(Strings.SaveCsv));
            Assert.That(save.Dialog.Filter, Is.EqualTo(Strings.ExportCsvFilter));
            Assert.That(save.Dialog.DefaultExt.TrimStart('.'), Is.EqualTo("csv"));
            Assert.That(save.Dialog.AddExtension, Is.True);
            Assert.That(save.Dialog.OverwritePrompt, Is.True);
        });
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(CsvExportKind.ResourceHistory)]
    [TestCase(CsvExportKind.ProcessSamples)]
    [TestCase(CsvExportKind.Services)]
    [TestCase(CsvExportKind.Connections)]
    [TestCase(CsvExportKind.NetworkTraffic)]
    public void ExportWorkflowEmptyDataShowsTheExistingNoticeWithoutRegisteringAWriter(CsvExportKind kind)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var operations = CsvOperations(kind, true);
        InvokeExportWorkflow(ui, operations, [Strings.ExportNoData], onNotice: () => Assert.That(CurrentExportWrites, Is.Empty));
        Assert.That(operations.Saves.Count, Is.EqualTo(1));
        Assert.That(operations.Writes, Is.Empty);
    }

    [TestCase("false")]
    [TestCase("null")]
    [TestCase("closing-after-choice")]
    [TestCase("closing-after-save")]
    public void ExportWorkflowCanceledPickerAndLateClosingPreventAnySnapshotWrite(string condition)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        if (condition == "closing-after-choice") operations.ChooseAction = (_, _, _) =>
        {
            ui.Window.IsClosing = true;
            return new CsvExportChoice(CsvExportKind.ResourceHistory, false, "owned.csv");
        };
        operations.SaveAction = (_, _) =>
        {
            if (condition == "closing-after-save") { ui.Window.IsClosing = true; return true; }
            return condition == "false" ? false : null;
        };
        InvokeExportWorkflow(ui, operations);
        Assert.That(operations.Writes, Is.Empty);
        Assert.That(operations.Saves.Count, Is.EqualTo(condition == "closing-after-choice" ? 0 : 1));
    }

    private static Exception ExportFailure(string kind) => kind switch
    {
        "io" => new IOException("Owned write error"),
        "access" => new UnauthorizedAccessException("Owned access error"),
        "argument" => new ArgumentException("Owned argument error"),
        "unsupported" => new NotSupportedException("Owned unsupported error"),
        _ => new SecurityException("Owned security error")
    };

    [TestCase("io", false)]
    [TestCase("access", false)]
    [TestCase("argument", false)]
    [TestCase("unsupported", false)]
    [TestCase("security", false)]
    [TestCase("io", true)]
    [TestCase("access", true)]
    [TestCase("argument", true)]
    [TestCase("unsupported", true)]
    [TestCase("security", true)]
    public void ExportWorkflowExpectedPickerAndWriterFailuresPreserveTheMessageAndCleanup(string kind, bool writing)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        var error = ExportFailure(kind);
        if (writing) operations.WriteAction = _ => throw error;
        else operations.SaveAction = (_, _) => throw error;
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportFailed, error.Message)],
            onNotice: () => Assert.That(CurrentExportWrites, Is.Empty));
        Assert.That(operations.Writes.Count, Is.EqualTo(writing ? 1 : 0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExportWorkflowShutdownJoinWaitsForPendingWritesAndSuppressesTheirFailures(bool fail)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        operations.Release.Reset();
        var failure = new IOException("Owned pending failure");
        if (fail) operations.WriteAction = _ => throw failure;
        var join = typeof(MainWindow).GetMethod("WhenExportsFinishAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Task>>();
        InvokeExportWorkflow(ui, operations, [fail ? Strings.Format(Strings.ExportFailed, failure.Message)
                : Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))],
            whilePending: _ =>
            {
                FinishWindowCheck(operations.Entered.Task);
                Assert.That(CurrentExportWrites.Count, Is.EqualTo(1));
                Assert.That(CurrentExportWrites.Single().IsCompleted, Is.False);
                var joined = join();
                Assert.That(joined.IsCompleted, Is.False);
                operations.Release.Set();
                FinishWindowCheck(joined);
                Assert.That(joined.IsCompletedSuccessfully, Is.True);
            });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExportWorkflowClosingDuringAWriteSuppressesCompletionAndFailureNotices(bool fail)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        operations.Release.Reset();
        if (fail) operations.WriteAction = _ => throw new IOException("Owned closing write failure");
        InvokeExportWorkflow(ui, operations, whilePending: _ =>
        {
            FinishWindowCheck(operations.Entered.Task);
            ui.Window.IsClosing = true;
        });
        Assert.That(operations.Writes.Count, Is.EqualTo(1));
    }

    [Test]
    public void ExportWorkflowKeepsCompletedWriteRegisteredUntilTheSuccessNoticeCloses()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))],
            onNotice: () =>
            {
                Assert.That(CurrentExportWrites.Count, Is.EqualTo(1));
                Assert.That(CurrentExportWrites.Single().IsCompletedSuccessfully, Is.True);
            });
    }

    [Test]
    public void ExportWorkflowLifetimeCancellationDoesNotAbandonAnAlreadyStartedWrite()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        operations.Release.Reset();
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))],
            whilePending: _ =>
            {
                FinishWindowCheck(operations.Entered.Task);
                ui.Window.Lifetime.Cancel();
                Assert.That(CurrentExportWrites.Single().IsCompleted, Is.False);
                Assert.That(CurrentExportWrites.Single().IsCanceled, Is.False);
            });
        Assert.That(operations.Writes.Count, Is.EqualTo(1));
    }

    [TestCase(CsvExportKind.ResourceHistory)]
    [TestCase(CsvExportKind.ProcessSamples)]
    [TestCase(CsvExportKind.Services)]
    [TestCase(CsvExportKind.Connections)]
    [TestCase(CsvExportKind.NetworkTraffic)]
    public void ExportWorkflowCapturedArraysRemainStableWhileTheOwnedSessionRecordsAnotherFrame(CsvExportKind kind)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var samples = SeedExportFrames(ui);
        using var operations = CsvOperations(kind);
        operations.Release.Reset();
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))],
            whilePending: _ =>
            {
                FinishWindowCheck(operations.Entered.Task);
                var next = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(201), 11, [], 0,
                    MonotonicTimestamp: 201 * Stopwatch.Frequency);
                ui.Window.Session.Record(next, services: new ServiceFrame(next, []),
                    connections: new NetworkConnectionsFrame([], NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
                var call = operations.Writes.Single();
                Assert.That(ui.Window.Session.History.Samples.Count, Is.EqualTo(5));
                Assert.That(call.Samples, Is.EqualTo(kind is CsvExportKind.ResourceHistory or CsvExportKind.ProcessSamples ? samples : []));
                Assert.That(call.Services!.Select(frame => frame.Sample), Is.EqualTo(kind == CsvExportKind.Services ? samples : []));
                Assert.That(call.Connections!.Select(frame => frame.Sample), Is.EqualTo(kind is CsvExportKind.Connections or CsvExportKind.NetworkTraffic ? samples : []));
            });
    }

    [TestCase(CsvExportKind.Services)]
    [TestCase(CsvExportKind.Connections)]
    [TestCase(CsvExportKind.NetworkTraffic)]
    public void ExportWorkflowRangeWithoutResourceHistoryRetainsExistingServiceAndConnectionCaptures(CsvExportKind kind)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var operations = CsvOperations(kind, true);
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 0, [], 0);
        ui.Window.Session.Services.Add(new ServiceFrame(sample, [], Available: false));
        ui.Window.Session.Connections.Add(sample, null, [], null);
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))]);
        var call = operations.Writes.Single();
        Assert.That(call.Samples, Is.Empty);
        Assert.That(call.Services!.Count, Is.EqualTo(kind == CsvExportKind.Services ? 1 : 0));
        Assert.That(call.Connections!.Count, Is.EqualTo(kind == CsvExportKind.Services ? 0 : 1));
        Assert.That(ui.Window.Session.History.Samples, Is.Empty);
    }

    private static MonitorSample UsageExportSample(DateTimeOffset timestamp, long moment) =>
        new(timestamp, 50, [new ProcessUsage(424242, 123456, "Owned usage app", 20)
            { ImagePath = @"C:\OwnedFixture\usage.exe", HasAppWindow = true }], 0,
            MonotonicTimestamp: moment) { IntervalSeconds = 1, IntervalCpuTicks = 1000 };

    [TestCase(false)]
    [TestCase(true)]
    public void ExportWorkflowLiveAndSelectedUsageKeepTheirDistinctTotalsAndPeriod(bool selected)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.Session.Usage.Reset(DateTimeOffset.UnixEpoch);
        var sample = UsageExportSample(DateTimeOffset.UnixEpoch.AddSeconds(200), 200 * Stopwatch.Frequency);
        ui.Window.Session.Record(sample, sample);
        if (selected) ui.Window.SelectedSample = sample;
        using var operations = CsvOperations(CsvExportKind.UsageTotals);
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))], showParent: false);
        var call = operations.Writes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Apps.Count, Is.EqualTo(1));
            Assert.That(call.Apps[0].Name, Is.EqualTo("Owned usage app"));
            Assert.That(call.Apps[0].CpuTicks, Is.EqualTo(200));
            Assert.That(call.Busy, Is.EqualTo(500));
            Assert.That(call.Unattributed, Is.EqualTo(300));
            Assert.That(call.Since, Is.EqualTo(DateTimeOffset.UnixEpoch));
        });
        if (!selected) Assert.That(call.Period, Is.Null);
        else
        {
            Assert.That(call.Period, Is.Not.Null);
            Assert.That(call.Period!.MachineBusyTicks, Is.EqualTo(500));
            Assert.That(call.Period.NotAttributedTicks, Is.EqualTo(300));
            Assert.That(call.Period.Since, Is.EqualTo(sample.Timestamp.AddSeconds(-1)));
            Assert.That(call.Period.Latest, Is.EqualTo(sample.Timestamp));
            Assert.That(call.Period.Complete, Is.False);
            Assert.That(call.Period.Archived, Is.False);
        }
    }

    [TestCase("expired-selected")]
    [TestCase("missing-archive")]
    public void ExportWorkflowUnavailableScopedUsageShowsNoDataInsteadOfWritingLiveTotals(string scope)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.Session.Usage.Add(UsageExportSample(DateTimeOffset.UnixEpoch.AddSeconds(200), 200 * Stopwatch.Frequency));
        if (scope == "expired-selected") ui.Window.SelectedSample = new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0);
        else ui.Window.UsageView.ArchiveScope.SelectedIndex = 1;
        using var operations = CsvOperations(CsvExportKind.UsageTotals);
        InvokeExportWorkflow(ui, operations, [Strings.ExportNoData], showParent: false);
        Assert.That(operations.Writes, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExportWorkflowArchivePeriodKeepsCalendarBoundsAndExistingEmptyArchiveBehavior(bool populated)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        var day = DateTime.SpecifyKind(ui.Window.UsageView.ArchiveDate, DateTimeKind.Unspecified);
        typeof(UsageView).GetField("_archiveDate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ui.Window.UsageView, day);
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        if (populated) archive.Add(UsageExportSample(new DateTimeOffset(day, TimeSpan.Zero).AddHours(12), 200 * Stopwatch.Frequency));
        ui.Field("_usageArchive").SetValue(ui.Window, archive);
        ui.Window.UsageView.ArchiveScope.SelectedIndex = 1;
        using var operations = CsvOperations(CsvExportKind.UsageTotals);
        InvokeExportWorkflow(ui, operations, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))], showParent: false);
        var period = operations.Writes.Single().Period;
        Assert.That(period, Is.Not.Null, "An existing empty archive is currently exported as a valid empty period");
        Assert.Multiple(() =>
        {
            Assert.That(period!.Archived, Is.True);
            Assert.That(period.Since, Is.EqualTo(new DateTimeOffset(day, TimeSpan.Zero)));
            Assert.That(period.Latest, Is.EqualTo(new DateTimeOffset(day.AddDays(1), TimeSpan.Zero)));
            Assert.That(period.Complete, Is.False);
            Assert.That(period.Apps.Count, Is.EqualTo(populated ? 1 : 0));
            Assert.That(period.MachineBusyTicks, Is.EqualTo(populated ? 500 : 0));
        });
    }

    [TestCase("choice")]
    [TestCase("choice-io")]
    [TestCase("save")]
    [TestCase("write")]
    public void ExportWorkflowUnexpectedFailuresRetainTheirPropagationBoundaryAndAllowRetry(string stage)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        Exception expected = stage == "choice-io" ? new IOException("Owned pre-try failure")
            : new InvalidOperationException("Owned unexpected export failure");
        if (stage.StartsWith("choice", StringComparison.Ordinal)) operations.ChooseAction = (_, _, _) => throw expected;
        else if (stage == "save") operations.SaveAction = (_, _) => throw expected;
        else operations.WriteAction = _ => throw expected;
        InvokeExportWorkflow(ui, operations, verifyErrors: errors => Assert.That(errors, Is.EqualTo(new[] { expected })));
        using var retry = CsvOperations(CsvExportKind.ResourceHistory);
        InvokeExportWorkflow(ui, retry, [Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"))]);
        Assert.That(retry.Writes.Count, Is.EqualTo(1));
    }

    [Test]
    public void ExportWorkflowJoinRechecksWritesStartedWhileAnEarlierWriteIsStillPending()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        SeedExportFrames(ui);
        using var operations = CsvOperations(CsvExportKind.ResourceHistory);
        using var firstRelease = new ManualResetEventSlim();
        using var secondRelease = new ManualResetEventSlim();
        var entered = 0;
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operations.WriteAction = _ =>
        {
            var number = Interlocked.Increment(ref entered);
            if (number == 2) bothEntered.TrySetResult();
            if (!(number == 1 ? firstRelease : secondRelease).Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Owned concurrent export was not released");
        };
        var join = typeof(MainWindow).GetMethod("WhenExportsFinishAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<Task>>();
        var saved = Strings.Format(Strings.ExportSaved, Path.Combine(_directory, "owned.csv"));
        try
        {
            InvokeExportWorkflow(ui, operations, [saved, saved], whilePending: begin =>
            {
                try
                {
                    FinishWindowCheck(operations.Entered.Task);
                    var first = CurrentExportWrites.Single();
                    var joined = join();
                    Assert.That(joined.IsCompleted, Is.False);
                    begin();
                    FinishWindowCheck(bothEntered.Task);
                    Assert.That(CurrentExportWrites.Count, Is.EqualTo(2));
                    firstRelease.Set();
                    FinishWindowCheck(first);
                    FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
                    Assert.That(joined.IsCompleted, Is.False, "The join must recheck and wait for the newer write");
                    secondRelease.Set();
                    FinishWindowCheck(joined);
                }
                finally { firstRelease.Set(); secondRelease.Set(); }
            });
        }
        finally { firstRelease.Set(); secondRelease.Set(); }
        Assert.That(operations.Writes.Count, Is.EqualTo(2));
    }
}
