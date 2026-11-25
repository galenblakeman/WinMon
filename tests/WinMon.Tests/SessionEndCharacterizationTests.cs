using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;
using System.Windows.Threading;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed partial class SessionEndCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private string? _temporaryFolder;
    private SynchronizationContext? _previousContext;
    private WinMon.Preferences.PreferenceStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText"))
            app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button)))
            app.Resources[typeof(Button)] = new Style(typeof(Button));
        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.SessionEnd." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_temporaryFolder);
        _store = new WinMon.Preferences.PreferenceStore(System.IO.Path.Combine(_temporaryFolder, "settings.json"));
        _window = CreateWindow(false);
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme(false, false);
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());

    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        if (_temporaryFolder is not null)
        {
            System.IO.Directory.Delete(_temporaryFolder, recursive: true);
            _temporaryFolder = null;
        }
    }

    private MainWindow CreateWindow(bool diagnostic) => new(diagnostic,
        new WinMon.Preferences.MonitorPreferences { KeepHistoryAcrossRestarts = false, Width = 1000, Height = 840 }, _store);

    private static void CloseOwnedWindow(MainWindow window)
    {
        foreach (var view in new FrameworkElement[] { window.UsageView, window.Processes, window.ServiceView, window.ConnectionView })
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        window.CloseForFixture();
    }

    private static void LoadWithoutSampling(MainWindow window)
    {
        Assert.That(WinMon.Updates.UpdateRuntime.RestartHistory, Is.Null, "Tests must not read a process-wide restart handoff");
        Assert.That(window.SamplingTask, Is.Null);
        window.StopForFixtureAsync().GetAwaiter().GetResult();
        var before = Application.Current.Windows.Cast<Window>().ToHashSet();
        var unexpected = new List<Window>();
        DispatcherHookEventHandler guard = (_, _) =>
        {
            foreach (var added in Application.Current.Windows.Cast<Window>().Where(added => !before.Contains(added) && added.IsVisible).ToArray())
            {
                unexpected.Add(added);
                added.Close();
            }
        };
        window.Dispatcher.Hooks.OperationCompleted += guard;
        try { window.OnLoaded(window, new RoutedEventArgs(FrameworkElement.LoadedEvent)); }
        finally { window.Dispatcher.Hooks.OperationCompleted -= guard; }
        Assert.Multiple(() =>
        {
            Assert.That(unexpected, Is.Empty, "Cancelled startup must not display an initialization error");
            Assert.That(window.Lifetime.IsCancellationRequested, Is.True);
            Assert.That(window.SamplingTask, Is.Null);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    private static T Complete<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
            var timedOut = false;
            deadline.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
            deadline.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { deadline.Stop(); }
            Assert.That(timedOut, Is.False, "Owned persistence operation did not complete");
        }
        return task.GetAwaiter().GetResult();
    }

    private static void Complete(Task task) => Complete(Finish(task));
    private static async Task<bool> Finish(Task task) { await task; return true; }

    private static MonitorSample CurrentSample() => new(DateTimeOffset.UtcNow, 42, [], 0, false,
        System.Diagnostics.Stopwatch.GetTimestamp()) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    [TestCase("Diagnostic")]
    [TestCase("NotLoaded")]
    [TestCase("Closing")]
    public void GuardedSessionEndPreservesEveryOwnedStoreFile(string guard)
    {
        var window = _window;
        if (guard == "Diagnostic")
        {
            window = CreateWindow(true);
            window.Loaded -= window.OnLoaded;
            window.ApplyTheme(false, false);
        }
        try
        {
            if (guard != "NotLoaded") LoadWithoutSampling(window);
            if (guard == "Closing") window.IsClosing = true;
            var paths = new[] { System.IO.Path.Combine(_temporaryFolder!, "settings.json"), _store.UsageArchivePath,
                _store.ResourceTrendArchivePath, _store.NormalHistoryPath };
            foreach (var path in paths) System.IO.File.WriteAllText(path, "owned sentinel " + System.IO.Path.GetFileName(path));
            var before = paths.ToDictionary(path => path, System.IO.File.ReadAllBytes);

            window.SaveForSessionEnd();

            Assert.Multiple(() =>
            {
                foreach (var entry in before) Assert.That(System.IO.File.ReadAllBytes(entry.Key), Is.EqualTo(entry.Value));
                Assert.That(System.IO.Directory.GetFiles(_temporaryFolder!), Is.EquivalentTo(paths));
            });
        }
        finally { if (!ReferenceEquals(window, _window)) CloseOwnedWindow(window); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LoadedSessionEndPersistsActualBoundsAndFullSizeWhileLifetimeIsCancelled(bool compact)
    {
        LoadWithoutSampling(_window);
        _window.Width = 1000;
        _window.Height = 840;
        _window.Left = 55;
        _window.Top = 65;
        _window.Topmost = true;
        _window.Preferences = _window.Preferences with { AlwaysOnTop = false };
        if (compact) _window.SetCompactView(true);
        var left = _window.Left;
        var top = _window.Top;

        _window.SaveForSessionEnd();

        var saved = Complete(_store.LoadAsync(CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(saved.Width, Is.EqualTo(1000));
            Assert.That(saved.Height, Is.EqualTo(840));
            Assert.That(saved.Left, Is.EqualTo(left));
            Assert.That(saved.Top, Is.EqualTo(top));
            Assert.That(saved.AlwaysOnTop, Is.True);
            Assert.That(saved.CompactMode, Is.EqualTo(compact));
            Assert.That(_window.Lifetime.IsCancellationRequested, Is.True);
            Assert.That(_window.SamplingTask, Is.Null);
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void OptionalOwnedUsageAndTrendArchivesSaveWithoutDispatchingTheirContinuations(bool usage, bool trends)
    {
        if (usage) Complete(_window.LoadUsageArchiveForFixtureAsync());
        if (trends) Complete(_window.LoadResourceTrendsForFixtureAsync());
        var sample = CurrentSample();
        if (usage) _window.DailyUsageArchive!.Add(sample);
        if (trends)
        {
            _window.Preferences = _window.Preferences with { KeepResourceTrends = true };
            _window.RecordResourceTrendForFixture(sample);
        }
        LoadWithoutSampling(_window);

        _window.SaveForSessionEnd();

        Assert.Multiple(() =>
        {
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
            Assert.That(System.IO.File.Exists(_store.UsageArchivePath), Is.EqualTo(usage));
            Assert.That(System.IO.File.Exists(_store.ResourceTrendArchivePath), Is.EqualTo(trends));
        });
        if (usage)
        {
            var restored = Complete(UsageArchive.LoadAsync(_store.UsageArchivePath));
            Assert.Multiple(() =>
            {
                Assert.That(restored.Export().Days.Sum(day => day.ObservedSeconds), Is.EqualTo(1));
                Assert.That(restored.Export().Days.Sum(day => day.Totals.MachineBusyTicks), Is.EqualTo(4_200_000));
            });
        }
        if (trends)
        {
            var restored = Complete(ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath));
            Assert.That(restored.Snapshot().Buckets.Sum(bucket => bucket.Values[ResourceTrendMetric.Cpu].WeightedSum), Is.EqualTo(42));
        }
    }

    [Test]
    public void UsageArchiveWithoutItsOwnershipLockIsNotWritten()
    {
        _window.DailyUsageArchive = new UsageArchive(TimeZoneInfo.Utc);
        _window.DailyUsageArchive.Add(CurrentSample());
        LoadWithoutSampling(_window);

        _window.SaveForSessionEnd();

        Assert.That(System.IO.File.Exists(_store.UsageArchivePath), Is.False);
        Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HistoryOptInControlsTheSaveWithoutDeletingAnExistingOptOutFile(bool keep)
    {
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = keep };
        var sample = CurrentSample();
        _window.Session.Record(sample);
        if (!keep) System.IO.File.WriteAllText(_store.NormalHistoryPath, "owned existing history");

        _window.SaveForSessionEnd();

        if (keep)
        {
            var restored = RestartHistory.ReadNormal(_store.NormalHistoryPath, RestartClock.System, DateTimeOffset.UtcNow, 600);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Value.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
            Assert.That(restored.Value.Samples.Single().Timestamp, Is.EqualTo(sample.Timestamp));
        }
        else Assert.That(System.IO.File.ReadAllText(_store.NormalHistoryPath), Is.EqualTo("owned existing history"));
    }

    [Test]
    public void PreferenceWriteFailureDoesNotPreventOptInHistorySaving()
    {
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        var blocked = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_temporaryFolder!, "settings.json"));

        Assert.DoesNotThrow(_window.SaveForSessionEnd);

        Assert.Multiple(() =>
        {
            Assert.That(System.IO.Directory.Exists(blocked.FullName), Is.True);
            Assert.That(System.IO.File.Exists(_store.NormalHistoryPath), Is.True);
        });
    }

    [Test]
    public void UsageWriteFailureDoesNotPreventPreferencesOrHistorySaving()
    {
        Complete(_window.LoadUsageArchiveForFixtureAsync());
        _window.DailyUsageArchive!.Add(CurrentSample());
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        System.IO.Directory.CreateDirectory(_store.UsageArchivePath);

        Assert.DoesNotThrow(_window.SaveForSessionEnd);

        Assert.Multiple(() =>
        {
            Assert.That(System.IO.Directory.Exists(_store.UsageArchivePath), Is.True);
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
            Assert.That(System.IO.File.Exists(_store.NormalHistoryPath), Is.True);
        });
    }

    [Test]
    public void HistoryWriteFailurePreservesItsTargetAndDoesNotUndoPreferences()
    {
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        System.IO.Directory.CreateDirectory(_store.NormalHistoryPath);

        Assert.DoesNotThrow(_window.SaveForSessionEnd);

        Assert.That(System.IO.Directory.Exists(_store.NormalHistoryPath), Is.True);
        Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DirtyTrendsSaveAfterSuccessfulOrFailedPreviousCheckpoint(bool previousFailure)
    {
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        _window.Preferences = _window.Preferences with { KeepResourceTrends = true };
        _window.RecordResourceTrendForFixture(CurrentSample());
        if (previousFailure) _window.SetResourceTrendWriteForFixture(Task.FromException(new System.IO.IOException("owned checkpoint failure")));
        LoadWithoutSampling(_window);

        _window.SaveForSessionEnd();

        var restored = Complete(ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath));
        Assert.That(restored.Snapshot().Buckets.Sum(bucket => bucket.Values[ResourceTrendMetric.Cpu].WeightedSum), Is.EqualTo(42));
        Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
    }

    [Test]
    public void FailedTrendSaveDoesNotPreventPreferencesSaving()
    {
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        _window.Preferences = _window.Preferences with { KeepResourceTrends = true };
        _window.RecordResourceTrendForFixture(CurrentSample());
        LoadWithoutSampling(_window);
        System.IO.Directory.CreateDirectory(_store.ResourceTrendArchivePath);

        Assert.DoesNotThrow(_window.SaveForSessionEnd);

        Assert.That(System.IO.Directory.Exists(_store.ResourceTrendArchivePath), Is.True);
        Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
    }

    private sealed class CapturedTrace : System.Diagnostics.TraceListener
    {
        internal readonly System.Collections.Concurrent.ConcurrentQueue<string> Messages = new();
        public override void Write(string? message) { if (message is not null) Messages.Enqueue(message); }
        public override void WriteLine(string? message) { if (message is not null) Messages.Enqueue(message); }
    }

    [Test]
    public void TrendDeadlineReturnsToPreferencesWhileItsPredecessorIsStillPending()
    {
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        _window.Preferences = _window.Preferences with { KeepResourceTrends = true };
        _window.RecordResourceTrendForFixture(CurrentSample());
        LoadWithoutSampling(_window);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetResourceTrendWriteForFixture(pending.Task);
        using var trace = new CapturedTrace();
        System.Diagnostics.Trace.Listeners.Add(trace);
        try
        {
            _window.SaveForSessionEnd();

            Assert.Multiple(() =>
            {
                Assert.That(pending.Task.IsCompleted, Is.False);
                Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.True);
                Assert.That(trace.Messages.Any(message => message.Contains("Resource trend save reached the session-end deadline.", StringComparison.Ordinal)), Is.True);
            });
        }
        finally
        {
            pending.TrySetResult();
            try { Complete(_window.SaveResourceTrendsForFixtureAsync()); }
            finally { System.Diagnostics.Trace.Listeners.Remove(trace); }
        }
        Assert.That(System.IO.File.Exists(_store.ResourceTrendArchivePath), Is.True);
    }
}
