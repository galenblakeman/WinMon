using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using System.Windows.Threading;
using System.Reflection;
#if !WINMON_STORE
using WinMon.Updates;
#endif

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetUICulture("en-US")]
public sealed class NormalShutdownCharacterizationTests
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
        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.Shutdown." + Guid.NewGuid().ToString("N"));
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

    private void CloseAndWait(Action? afterStarting = null, Action? releasePending = null)
    {
        Assert.That(_window.Processes.SuspendedProcesses.Pending, Is.Empty);
        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        EventHandler observeClosed = (_, _) => closed.TrySetResult(true);
        DispatcherUnhandledExceptionEventHandler observeFailure = (_, e) =>
        {
            failure = e.Exception;
            e.Handled = true;
            closed.TrySetResult(false);
        };
        _window.Closed += observeClosed;
        _window.Dispatcher.UnhandledException += observeFailure;
        try
        {
            try
            {
                _window.Close();
                afterStarting?.Invoke();
            }
            finally { releasePending?.Invoke(); }
            Complete(closed.Task);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.That(_window.IsClosing, Is.True);
        }
        finally
        {
            _window.Closed -= observeClosed;
            _window.Dispatcher.UnhandledException -= observeFailure;
        }
    }

    [Test]
    public void RealCloseWaitsForAnOwnedCheckpointBeforeSavingPreferencesAndHistoryAndReleasingItsLock()
    {
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        LoadWithoutSampling(_window);
        var sample = CurrentSample();
        _window.Session.Record(sample);
        _window.RecordResourceTrendForFixture(sample);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetResourceTrendWriteForFixture(pending.Task);
        var closingCancelled = new List<bool>();
        _window.Closing += (_, e) => closingCancelled.Add(e.Cancel);
        CloseAndWait(() => Assert.Multiple(() =>
        {
            Assert.That(closingCancelled, Is.EqualTo(new[] { true }));
            Assert.That(_window.IsClosing, Is.True);
            Assert.That(_window.IsEnabled, Is.False);
            Assert.That(pending.Task.IsCompleted, Is.False);
            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(_temporaryFolder!, "settings.json")), Is.False);
            Assert.That(System.IO.File.Exists(_store.NormalHistoryPath), Is.False);
        }), () => pending.TrySetResult());
        Assert.That(closingCancelled, Is.EqualTo(new[] { true, false }));
        var saved = Complete(_store.LoadAsync(CancellationToken.None));
        Assert.That(saved.Width, Is.EqualTo(_window.Width));
        var history = RestartHistory.ReadNormal(_store.NormalHistoryPath, RestartClock.System,
            DateTimeOffset.Now, _window.Preferences.RetentionSeconds);
        Assert.That(history, Is.Not.Null);
        Assert.That(history!.Value.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
        var trends = Complete(ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath));
        Assert.That(trends.Snapshot().Buckets.Sum(bucket => bucket.Values[ResourceTrendMetric.Cpu].WeightedSum), Is.EqualTo(42));
        using var released = new System.IO.FileStream(_store.ResourceTrendArchivePath + ".lock",
            System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
    }
    private string SettingsPath => System.IO.Path.Combine(_temporaryFolder!, "settings.json");

    private void CloseAfterCheckpoint()
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetResourceTrendWriteForFixture(pending.Task);
        CloseAndWait(releasePending: () => pending.TrySetResult());
    }

    private void AssertArchiveLockReleased(string path)
    {
        using var released = new System.IO.FileStream(path + ".lock", System.IO.FileMode.Open,
            System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void CloseAfterAnAwaitedCheckpointSavesEachOwnedOptionalArchiveAndPreferences(bool usage, bool trends)
    {
        if (usage) Complete(_window.LoadUsageArchiveForFixtureAsync());
        if (trends) Complete(_window.LoadResourceTrendsForFixtureAsync());
        LoadWithoutSampling(_window);
        var sample = CurrentSample();
        if (usage) _window.DailyUsageArchive!.Add(sample);
        if (trends) _window.RecordResourceTrendForFixture(sample);
        _window.Left = 55;
        _window.Top = 65;
        _window.Topmost = true;
        CloseAfterCheckpoint();
        var saved = Complete(_store.LoadAsync(CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(saved.Left, Is.EqualTo(55));
            Assert.That(saved.Top, Is.EqualTo(65));
            Assert.That(saved.Width, Is.EqualTo(1000));
            Assert.That(saved.Height, Is.EqualTo(840));
            Assert.That(saved.AlwaysOnTop, Is.True);
            Assert.That(System.IO.File.Exists(_store.UsageArchivePath), Is.EqualTo(usage));
            Assert.That(System.IO.File.Exists(_store.ResourceTrendArchivePath), Is.EqualTo(trends));
        });
        if (usage)
        {
            var restored = Complete(UsageArchive.LoadAsync(_store.UsageArchivePath));
            Assert.That(restored.Export().Days.Sum(day => day.ObservedSeconds), Is.EqualTo(1));
            AssertArchiveLockReleased(_store.UsageArchivePath);
        }
        if (trends)
        {
            var restored = Complete(ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath));
            Assert.That(restored.Snapshot().Buckets.Sum(bucket => bucket.Values[ResourceTrendMetric.Cpu].WeightedSum), Is.EqualTo(42));
            AssertArchiveLockReleased(_store.ResourceTrendArchivePath);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CloseAfterAnAwaitedCheckpointDeletesOptOutHistoryAndSavesOptInHistory(bool keep)
    {
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = keep };
        _window.Session.Record(CurrentSample());
        System.IO.File.WriteAllText(_store.NormalHistoryPath, "owned previous history");
        CloseAfterCheckpoint();
        Assert.That(System.IO.File.Exists(_store.NormalHistoryPath), Is.EqualTo(keep));
        if (keep)
        {
            var restored = RestartHistory.ReadNormal(_store.NormalHistoryPath, RestartClock.System, DateTimeOffset.UtcNow, 600);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Value.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
        }
    }

    [Test]
    public void AfterAnAwaitedCheckpointPreferenceFailureClosesAndReleasesArchivesButLeavesPreviousHistoryUntouched()
    {
        Complete(_window.LoadUsageArchiveForFixtureAsync());
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        LoadWithoutSampling(_window);
        var sample = CurrentSample();
        _window.DailyUsageArchive!.Add(sample);
        _window.RecordResourceTrendForFixture(sample);
        _window.Session.Record(sample);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        System.IO.File.WriteAllText(_store.NormalHistoryPath, "owned previous history");
        System.IO.Directory.CreateDirectory(SettingsPath);
        CloseAfterCheckpoint();
        Assert.Multiple(() =>
        {
            Assert.That(System.IO.Directory.Exists(SettingsPath), Is.True);
            Assert.That(System.IO.File.ReadAllText(_store.NormalHistoryPath), Is.EqualTo("owned previous history"));
            Assert.That(System.IO.File.Exists(_store.UsageArchivePath), Is.True);
            Assert.That(System.IO.File.Exists(_store.ResourceTrendArchivePath), Is.True);
        });
        AssertArchiveLockReleased(_store.UsageArchivePath);
        AssertArchiveLockReleased(_store.ResourceTrendArchivePath);
    }

    [TestCase("Usage")]
    [TestCase("Trends")]
    public void AfterAnAwaitedCheckpointArchiveFailureStillSavesPreferencesAndReleasesEitherLock(string failing)
    {
        Complete(_window.LoadUsageArchiveForFixtureAsync());
        Complete(_window.LoadResourceTrendsForFixtureAsync());
        LoadWithoutSampling(_window);
        var sample = CurrentSample();
        _window.DailyUsageArchive!.Add(sample);
        _window.RecordResourceTrendForFixture(sample);
        var path = failing == "Usage" ? _store.UsageArchivePath : _store.ResourceTrendArchivePath;
        System.IO.Directory.CreateDirectory(path);
        CloseAfterCheckpoint();
        Assert.Multiple(() =>
        {
            Assert.That(System.IO.Directory.Exists(path), Is.True);
            Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
            Assert.That(System.IO.File.Exists(failing == "Usage" ? _store.ResourceTrendArchivePath : _store.UsageArchivePath), Is.True);
        });
        AssertArchiveLockReleased(_store.UsageArchivePath);
        AssertArchiveLockReleased(_store.ResourceTrendArchivePath);
    }

    [Test]
    public void RepeatedCloseAndSessionEndRequestsDoNotStartAnotherSaveWhileACheckpointIsPending()
    {
        LoadWithoutSampling(_window);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetResourceTrendWriteForFixture(pending.Task);
        var cancellations = new List<bool>();
        _window.Closing += (_, e) => cancellations.Add(e.Cancel);
        CloseAndWait(() =>
        {
            _window.Close();
            _window.SaveForSessionEnd();
            Assert.Multiple(() =>
            {
                Assert.That(cancellations, Is.EqualTo(new[] { true, true }));
                Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
                Assert.That(_window.IsEnabled, Is.False);
            });
        }, () => pending.TrySetResult());
        Assert.That(cancellations, Is.EqualTo(new[] { true, true, false }));
        Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
    }

    [Test]
    public void BusyServiceActionDefersCloseWithoutStartingShutdownOrWritingPreferences()
    {
        _window.ServiceActionBusy = true;
        var cancellations = new List<bool>();
        _window.Closing += (_, e) => cancellations.Add(e.Cancel);
        try
        {
            _window.Close();
            Assert.Multiple(() =>
            {
                Assert.That(cancellations, Is.EqualTo(new[] { true }));
                Assert.That(_window.CloseAfterServiceAction, Is.True);
                Assert.That(_window.IsClosing, Is.False);
                Assert.That(_window.IsEnabled, Is.True);
                Assert.That(_window.Lifetime.IsCancellationRequested, Is.False);
                Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
            });
        }
        finally { _window.ServiceActionBusy = false; }
    }

    [Test]
    public void AlreadyClosingGuardCancelsTheRequestWithoutCancellingTheLifetime()
    {
        _window.IsClosing = true;
        var cancellations = new List<bool>();
        _window.Closing += (_, e) => cancellations.Add(e.Cancel);
        _window.Close();
        Assert.Multiple(() =>
        {
            Assert.That(cancellations, Is.EqualTo(new[] { true }));
            Assert.That(_window.Lifetime.IsCancellationRequested, Is.False);
            Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NormalCloseWaitsForAnOwnedCaptureWriteAndSuppressesFailureNoticesDuringShutdown(bool fail)
    {
        LoadWithoutSampling(_window);
        var sample = CurrentSample();
        var marker = new MainWindow.CaptureMarker(new CaptureFired(
            new CaptureRuleSetting(CaptureMetric.Cpu, true, 90, 1), sample, sample, 97, [97]), Guid.NewGuid(), "owned rule");
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writing = _window.WriteCaptureForFixtureAsync(marker, pending.Task);
        var footer = _window.UpdateFooter.Visibility;
        CloseAndWait(() => Assert.Multiple(() =>
        {
            Assert.That(writing.IsCompleted, Is.False);
            Assert.That(_window.IsClosing, Is.True);
            Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
        }), () =>
        {
            if (fail) pending.TrySetException(new System.IO.IOException("owned capture failure"));
            else pending.TrySetResult("owned-report.html");
        });
        Complete(writing);
        Assert.Multiple(() =>
        {
            Assert.That(marker.WriteFailed, Is.EqualTo(fail));
            Assert.That(marker.ReportPath, Is.EqualTo(fail ? null : "owned-report.html"));
            Assert.That(_window.UpdateFooter.Visibility, Is.EqualTo(footer));
            Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
        });
    }

    [Test]
    public void DiagnosticCloseWithOnlyCompletedWorkClosesAfterTheOriginalClosingEventReturns()
    {
        foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        _window.CloseForFixture();
        _window = CreateWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _window.StopForFixtureAsync().GetAwaiter().GetResult();
        var closingCancelled = new List<bool>();
        _window.Closing += (_, e) => closingCancelled.Add(e.Cancel);
        CloseAndWait(() => Assert.That(closingCancelled, Is.EqualTo(new[] { true })));
        Assert.Multiple(() =>
        {
            Assert.That(closingCancelled, Is.EqualTo(new[] { true, false }));
            Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(_window));
            Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SamplingFailureOrCancellationStillSavesOwnedExitPreferencesAndHistory(bool cancelled)
    {
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        var sampling = cancelled ? Task.FromCanceled(new CancellationToken(true))
            : Task.FromException(new System.IO.IOException("owned sampling failure"));
        var property = typeof(MainWindow).GetProperty(nameof(MainWindow.SamplingTask), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing sampling completion boundary");
        property.SetValue(_window, sampling);

        CloseAfterCheckpoint();

        Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
        var history = RestartHistory.ReadNormal(_store.NormalHistoryPath, RestartClock.System, DateTimeOffset.UtcNow, 600);
        Assert.That(history, Is.Not.Null);
        Assert.That(history!.Value.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
        Assert.That(sampling.IsCanceled, Is.EqualTo(cancelled));
        Assert.That(sampling.IsFaulted, Is.EqualTo(!cancelled));
    }

    [Test]
    public void EarlierPreferenceTaskFailureDoesNotPreventSavingOwnedExitPreferences()
    {
        LoadWithoutSampling(_window);
        SetCloseField("_settingsWriteTask", Task.FromException(new System.IO.IOException("owned earlier preference failure")));
        _window.Left = 57;
        _window.Top = 67;

        CloseAfterCheckpoint();

        var saved = Complete(_store.LoadAsync(CancellationToken.None));
        Assert.That(saved.Left, Is.EqualTo(57));
        Assert.That(saved.Top, Is.EqualTo(67));
    }

    private void SetCloseField(string name, object? value) =>
        (typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing close state boundary: " + name)).SetValue(_window, value);

#if !WINMON_STORE
    [TestCase(false)]
    [TestCase(true)]
    public void UpdateExitSavesOwnedPreferencesAndRestartHistoryBeforeCommit(bool failedEarlierWrite)
    {
        using var signals = new OwnedUpdateCloseSignals();
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        _window.Left = 58;
        _window.Top = 68;
        System.IO.File.WriteAllText(_store.NormalHistoryPath, "owned pre-update history");
        if (failedEarlierWrite)
            SetCloseField("_settingsWriteTask", Task.FromException(new System.IO.IOException("owned earlier update preference failure")));
        var target = System.IO.Path.Combine(_temporaryFolder!, "owned-restart.json");
        var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        SetCloseField("_restartHistoryTarget", (target, token));
        SetCloseField("_updateRestartPending", true);
        var sampling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var property = typeof(MainWindow).GetProperty(nameof(MainWindow.SamplingTask), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing sampling completion boundary");
        property.SetValue(_window, sampling.Task);
        var cancellation = new TaskCompletionSource<(bool Preferences, bool History, bool Commit)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _window.Lifetime.Token.Register(() => cancellation.TrySetResult(
            (System.IO.File.Exists(SettingsPath), System.IO.File.Exists(target), signals.Committed)));

        CloseAndWait(() =>
        {
            Assert.That(Complete(cancellation.Task), Is.EqualTo((true, false, false)),
                "Update preferences must be durable before lifetime cancellation, restart capture and commit");
            Assert.That(_window.IsClosing, Is.True);
            Assert.That(sampling.Task.IsCompleted, Is.False);
            Assert.That(signals.Committed, Is.False);
        }, () => sampling.TrySetResult());

        var saved = Complete(_store.LoadAsync(CancellationToken.None));
        var history = RestartHistory.Read(target, token, RestartClock.System);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Left, Is.EqualTo(58));
            Assert.That(saved.Top, Is.EqualTo(68));
            Assert.That(signals.Committed, Is.True);
            Assert.That(signals.Cancelled, Is.False);
            Assert.That(System.IO.File.ReadAllText(_store.NormalHistoryPath), Is.EqualTo("owned pre-update history"));
            Assert.That(history, Is.Not.Null);
        });
        Assert.That(history!.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
    }

    [Test]
    public void PendingUpdatePreferenceWriteRejectsRepeatedCloseBeforeCancellingLifetime()
    {
        using var signals = new OwnedUpdateCloseSignals();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetCloseField("_settingsWriteTask", pending.Task);
        SetCloseField("_updateRestartPending", true);
        var requests = new List<bool>();
        _window.Closing += (_, e) => requests.Add(e.Cancel);

        CloseAndWait(() =>
        {
            _window.Close();
            Assert.Multiple(() =>
            {
                Assert.That(requests, Is.EqualTo(new[] { true, true }));
                Assert.That(_window.IsClosing, Is.False);
                Assert.That(_window.IsEnabled, Is.False);
                Assert.That(_window.Lifetime.IsCancellationRequested, Is.False);
                Assert.That(System.IO.File.Exists(SettingsPath), Is.False);
                Assert.That(signals.Committed, Is.False);
                Assert.That(signals.Cancelled, Is.False);
            });
        }, () => pending.TrySetResult());

        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(new[] { true, true, false }));
            Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
            Assert.That(signals.Committed, Is.True);
            Assert.That(signals.Cancelled, Is.False);
        });
    }

    [Test]
    public void UpdateExitPreferenceFailureCancelsOwnedHelperAndAllowsNormalCloseRetry()
    {
        using var signals = new OwnedUpdateCloseSignals();
        LoadWithoutSampling(_window);
        _window.Preferences = _window.Preferences with { KeepHistoryAcrossRestarts = true };
        _window.Session.Record(CurrentSample());
        System.IO.File.WriteAllText(_store.NormalHistoryPath, "owned history before failed update");
        System.IO.Directory.CreateDirectory(SettingsPath);
        SetCloseField("_updateRestartPending", true);
        var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            ?? throw new AssertionException("Missing status text observation");
        EventHandler changed = (_, _) =>
        {
            if (_window.UpdateStatusText.Text == WinMon.Resources.Strings.UpdateFailed) failed.TrySetResult(true);
        };
        descriptor.AddValueChanged(_window.UpdateStatusText, changed);
        try
        {
            _window.Close();
            Complete(failed.Task);
        }
        finally { descriptor.RemoveValueChanged(_window.UpdateStatusText, changed); }

        Assert.Multiple(() =>
        {
            Assert.That(_window.IsClosing, Is.False);
            Assert.That(_window.IsEnabled, Is.True);
            Assert.That(Application.Current.Windows.Cast<Window>(), Does.Contain(_window));
            Assert.That(signals.Committed, Is.False);
            Assert.That(signals.Cancelled, Is.True);
            Assert.That(System.IO.Directory.Exists(SettingsPath), Is.True);
            Assert.That(System.IO.File.ReadAllText(_store.NormalHistoryPath), Is.EqualTo("owned history before failed update"));
            Assert.That(_window.UpdateStatusText.Text, Is.EqualTo(WinMon.Resources.Strings.UpdateFailed));
        });
        System.IO.Directory.Delete(SettingsPath);
        CloseAfterCheckpoint();
        Assert.That(System.IO.File.Exists(SettingsPath), Is.True);
        Assert.That(signals.Committed, Is.False);
        Assert.That(signals.Cancelled, Is.True);
        var history = RestartHistory.ReadNormal(_store.NormalHistoryPath, RestartClock.System, DateTimeOffset.UtcNow, 600);
        Assert.That(history, Is.Not.Null);
        Assert.That(history!.Value.Samples.Single().TotalCpuPercent, Is.EqualTo(42));
    }

    private sealed class OwnedUpdateCloseSignals : IDisposable
    {
        private readonly EventWaitHandle _commit;
        private readonly EventWaitHandle _cancel;
        internal bool Committed => _commit.WaitOne(0);
        internal bool Cancelled => _cancel.WaitOne(0);

        internal OwnedUpdateCloseSignals()
        {
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var commit = typeof(UpdateLauncher).GetField("_commit", flags) ?? throw new AssertionException("Missing update commit boundary");
            var cancel = typeof(UpdateLauncher).GetField("_cancel", flags) ?? throw new AssertionException("Missing update cancel boundary");
            Assert.That(commit.GetValue(null), Is.Null, "No real update job may be present");
            Assert.That(cancel.GetValue(null), Is.Null, "No real update job may be present");
            var prefix = @"Local\WinMon.Test.Close." + Guid.NewGuid().ToString("N");
            _commit = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Commit");
            try
            {
                _cancel = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + ".Cancel");
                try
                {
                    commit.SetValue(null, EventWaitHandle.OpenExisting(prefix + ".Commit"));
                    cancel.SetValue(null, EventWaitHandle.OpenExisting(prefix + ".Cancel"));
                }
                catch { UpdateLauncher.Dispose(); _cancel.Dispose(); throw; }
            }
            catch { _commit.Dispose(); throw; }
        }

        public void Dispose()
        {
            UpdateLauncher.Dispose();
            _commit.Dispose();
            _cancel.Dispose();
        }
    }
#endif
}
