using System.Collections;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class FlightRecorderEntryCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private readonly List<FlightRecorderWindow> _recorders = [];

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true) { ShowInTaskbar = false };
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme();
        _window.Show();
        Assert.That(_window.SamplingTask, Is.Null, "The owned diagnostic window must not start native sampling.");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var recorder in _recorders.Where(recorder => recorder.IsVisible).ToArray()) recorder.Close();
        _recorders.Clear();
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private FlightRecorderWindow Open()
    {
        _window.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var recorder = _window.OwnedWindows.OfType<FlightRecorderWindow>().Single();
        if (!_recorders.Contains(recorder)) _recorders.Add(recorder);
        return recorder;
    }

    private static MonitorSample Sample(int second, double cpu, int pid = 1) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), cpu,
            [new ProcessUsage(pid, pid * 1000, "p" + pid, cpu)], 0,
            MonotonicTimestamp: (100L + second) * Stopwatch.Frequency) { IntervalSeconds = 1 };

    private MonitorSample Record(int second, double cpu, int pid = 1, bool compacted = false)
    {
        var full = Sample(second, cpu, pid);
        var shown = compacted ? full with { Processes = [], CompactedProcesses = 1 } : full;
        var network = new ProcessNetworkFrame(shown.MonotonicTimestamp, Stopwatch.Frequency,
            ProcessNetworkQuality.Provisional, [new(pid, pid * 1000, 100, 200, 1000, 2000)]) { Owners = full.Processes };
        _window.Session.Record(shown, full, network: network);
        return shown;
    }

    private static IncidentEventResult Events(DateTimeOffset timestamp, string provider) =>
        new([new(timestamp, provider, 42, IncidentEventLevel.Warning, provider)],
            [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.Complete)], false);

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyHistoryOpensAnOwnedRecorderWithoutInvokingEitherEventReaderAndCanReopen(bool suppliedReader)
    {
        var probes = 0;
        _window.FlightRecorderEventReader = suppliedReader ? (_, _, _) =>
        {
            probes++;
            throw new AssertionException("Empty history must not query any event reader.");
        } : null;
        var first = Open();
        Assert.Multiple(() =>
        {
            Assert.That(first.Owner, Is.SameAs(_window));
            Assert.That(first.IsVisible, Is.True);
            Assert.That(first.Selection, Is.Null);
            Assert.That(first.SaveButton.IsEnabled, Is.False);
            Assert.That(first.RefreshButton.IsEnabled, Is.True);
            Assert.That(first.Recording().Observations, Is.Empty);
            Assert.That(first.Recording().WindowsEvents, Is.SameAs(IncidentEventResult.Unknown));
            Assert.That(first.EventReadPending, Is.False);
            Assert.That(probes, Is.Zero);
        });
        first.Close();
        Assert.That(_window.OwnedWindows.OfType<FlightRecorderWindow>(), Is.Empty);
        var second = Open();
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Owner, Is.SameAs(_window));
            Assert.That(second.Selection, Is.Null);
            Assert.That(probes, Is.Zero);
        });
    }

    [TestCase(WindowState.Normal)]
    [TestCase(WindowState.Minimized)]
    [TestCase(WindowState.Maximized)]
    public void RepeatedClickReusesTheRecorderRestoresOnlyMinimizedStateAndKeepsItsFrozenHistory(WindowState state)
    {
        var first = Record(0, 20);
        var peak = Record(1, 80);
        var reads = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var events = Events(peak.Timestamp, "initial reader");
        _window.FlightRecorderEventReader = (start, end, _) => { reads.Add((start, end)); return Task.FromResult(events); };
        var recorder = Open();
        Assert.That(recorder.Selection, Is.SameAs(peak));
        recorder.Select(0);
        recorder.WindowState = state;
        Record(2, 99);
        _window.FlightRecorderEventReader = (_, _, _) => throw new AssertionException("Reusing the recorder must not replace or call its reader.");

        var reused = Open();

        Assert.Multiple(() =>
        {
            Assert.That(reused, Is.SameAs(recorder));
            Assert.That(_window.OwnedWindows.OfType<FlightRecorderWindow>().Count(), Is.EqualTo(1));
            Assert.That(recorder.WindowState, Is.EqualTo(state == WindowState.Minimized ? WindowState.Normal : state));
            Assert.That(recorder.Selection, Is.SameAs(first));
            Assert.That(recorder.Recording().Observations.Select(observation => observation.Sample), Is.EqualTo(new[] { first, peak }));
            Assert.That(recorder.Recording().WindowsEvents, Is.SameAs(events));
            Assert.That(reads, Is.EqualTo(new[] { (first.Timestamp, peak.Timestamp) }));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void RefreshUsesLatestHistoryFullLifecycleAndNetworkOwnersButPreservesTheSelectedSample(int selected)
    {
        var first = Record(0, 20, 1, true);
        var second = Record(1, 80, 2, true);
        var reads = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        _window.FlightRecorderEventReader = (start, end, _) =>
        {
            reads.Add((start, end));
            return Task.FromResult(Events(end, "range " + reads.Count));
        };
        var recorder = Open();
        recorder.Select(selected);
        var before = recorder.Selection;
        var third = Record(2, 99, 3, true);
        Assert.That(recorder.Recording().Observations, Has.Length.EqualTo(2));

        recorder.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var observations = recorder.Recording().Observations;
        Assert.Multiple(() =>
        {
            Assert.That(recorder.Selection, Is.SameAs(before));
            Assert.That(observations.Select(observation => observation.Sample), Is.EqualTo(new[] { first, second, third }));
            Assert.That(observations[1].Started!.Select(process => process.Name), Is.EqualTo(new[] { "p2" }));
            Assert.That(observations[1].Stopped!.Select(process => process.Name), Is.EqualTo(new[] { "p1" }));
            Assert.That(observations[2].Started!.Select(process => process.Name), Is.EqualTo(new[] { "p3" }));
            Assert.That(observations[2].Stopped!.Select(process => process.Name), Is.EqualTo(new[] { "p2" }));
            Assert.That(observations[2].Owners.Single().ProcessId, Is.EqualTo(3));
            Assert.That(observations[2].Network!.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(reads, Is.EqualTo(new[] { (first.Timestamp, second.Timestamp), (first.Timestamp, third.Timestamp) }));
            Assert.That(recorder.Recording().WindowsEvents.Events.Single().Provider, Is.EqualTo("range 2"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TrendsAndRetentionNoticeAreFrozenUntilRefreshThenUseTheCurrentArchiveAndPreference(bool keepTrends)
    {
        Record(0, 20);
        _window.FlightRecorderEventReader = (_, _, _) => Task.FromResult(IncidentEventResult.Unknown);
        _window.Preferences = _window.Preferences with { KeepResourceTrends = keepTrends };
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(-2);
        var trend = Sample(0, 30) with { Timestamp = minute.AddSeconds(10) };
        _window.ResourceTrendsForFixture.Add(trend);
        var recorder = Open();
        recorder.HistoryMode.SelectedIndex = 1;
        recorder.SelectTrendMinuteForFixture(minute);
        var initialCoverage = recorder.TrendCoverage.Text;
        var initialSelection = recorder.TrendSelection.Text;
        Assert.Multiple(() =>
        {
            Assert.That(initialCoverage.Contains(Strings.ResourceTrendsOff, StringComparison.Ordinal), Is.EqualTo(!keepTrends));
            Assert.That(initialSelection, Does.Contain("30.0%"));
        });
        _window.ResourceTrendsForFixture.Add(trend with { Timestamp = minute.AddSeconds(11),
            MonotonicTimestamp = trend.MonotonicTimestamp + Stopwatch.Frequency, TotalCpuPercent = 80 });
        _window.Preferences = _window.Preferences with { KeepResourceTrends = !keepTrends };
        Assert.That(Open(), Is.SameAs(recorder));
        Assert.Multiple(() =>
        {
            Assert.That(recorder.TrendSelection.Text, Is.EqualTo(initialSelection));
            Assert.That(recorder.TrendCoverage.Text, Is.EqualTo(initialCoverage));
        });

        recorder.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(recorder.TrendSelection.Text, Does.Contain("55.0%"));
            Assert.That(recorder.TrendCoverage.Text.Contains(Strings.ResourceTrendsOff, StringComparison.Ordinal), Is.EqualTo(keepTrends));
        });
    }

    [Test]
    public void ClosingCancelsPendingReadAndReopeningUsesTheNewReaderWithoutApplyingLateResults() => Run(async () =>
    {
        var sample = Record(0, 20);
        var pending = new TaskCompletionSource<IncidentEventResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token = default;
        _window.FlightRecorderEventReader = (_, _, cancellation) => { token = cancellation; return pending.Task; };
        var first = Open();
        try
        {
            Assert.That(first.EventReadPending, Is.True);
            first.Close();
            Assert.That(token.IsCancellationRequested, Is.True);
            var currentEvents = Events(sample.Timestamp, "new reader");
            _window.FlightRecorderEventReader = (_, _, _) => Task.FromResult(currentEvents);
            var reopened = Open();
            Assert.That(reopened, Is.Not.SameAs(first));
            pending.SetResult(Events(sample.Timestamp, "late old reader"));
            await pending.Task;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Multiple(() =>
            {
                Assert.That(first.IsVisible, Is.False);
                Assert.That(first.EventReadPending, Is.False);
                Assert.That(reopened.Recording().WindowsEvents, Is.SameAs(currentEvents));
                Assert.That(reopened.EventMappings.Select(mapping => mapping.Event.Provider), Is.EqualTo(new[] { "new reader" }));
                Assert.That(_window.OwnedWindows.OfType<FlightRecorderWindow>().Single(), Is.SameAs(reopened));
            });
        }
        finally { pending.TrySetResult(IncidentEventResult.Unknown); }
    });

    [TestCase(false)]
    [TestCase(true)]
    public void EventReaderFailuresLeaveTheOwnedRecorderUsableAndReportUnavailableCoverage(bool synchronous)
    {
        Record(0, 20);
        var failure = new InvalidOperationException("owned reader failure");
        _window.FlightRecorderEventReader = (_, _, _) => synchronous ? throw failure : Task.FromException<IncidentEventResult>(failure);
        var recorder = Open();
        Assert.Multiple(() =>
        {
            Assert.That(recorder.IsVisible, Is.True);
            Assert.That(recorder.Owner, Is.SameAs(_window));
            Assert.That(recorder.EventReadPending, Is.False);
            Assert.That(recorder.SaveButton.IsEnabled, Is.True);
            Assert.That(recorder.RefreshButton.IsEnabled, Is.True);
            Assert.That(recorder.Recording().WindowsEvents.Events, Is.Empty);
            Assert.That(recorder.Recording().WindowsEvents.Coverage.Select(coverage => coverage.Status),
                Is.EqualTo(new[] { IncidentEventCoverageStatus.Unreadable, IncidentEventCoverageStatus.Unreadable }));
            Assert.That(Open(), Is.SameAs(recorder));
        });
    }

    private static void Run(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = action();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
