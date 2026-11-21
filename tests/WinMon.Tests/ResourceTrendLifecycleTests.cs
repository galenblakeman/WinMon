using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), Category("Integration"), SetUICulture("en")]
public sealed class ResourceTrendLifecycleTests
{
    private string _directory = null!;
    private PreferenceStore _store = null!;
    private MainWindow _window = null!;
    private readonly List<object> _addedResources = [];

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.Trends-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key))
            {
                app.Resources[key] = new Style();
                _addedResources.Add(key);
            }
        if (!app.Resources.Contains("TitleBarButtonText"))
        {
            app.Resources["TitleBarButtonText"] = new DataTemplate();
            _addedResources.Add("TitleBarButtonText");
        }
        if (!app.Resources.Contains(typeof(Button)))
        {
            app.Resources[typeof(Button)] = new Style(typeof(Button));
            _addedResources.Add(typeof(Button));
        }
        _store = new PreferenceStore(Path.Combine(_directory, "settings.json"));
        _window = new MainWindow(false, preferenceStore: _store);
        _window.Loaded -= _window.OnLoaded;
    }

    [TearDown]
    public void TearDown()
    {
        _window?.CloseForFixture();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void LiveOnlyRecording_CheckpointReloadAndDisabledPreferencePreserveObservedTotals() => Run(async () =>
    {
        await _window.LoadResourceTrendsForFixtureAsync();
        var sample = Sample();
        _window.Session.Record(sample, sample);
        Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Is.Empty,
            "Restored/synthetic Record does not raise LiveSampleRecorded.");
        _window.RecordResourceTrendForFixture(sample);
        _window.Preferences = _window.Preferences with { KeepResourceTrends = false };
        _window.RecordResourceTrendForFixture(sample with { Timestamp = sample.Timestamp.AddSeconds(1) });
        await _window.SaveResourceTrendsForFixtureAsync();
        var restored = await ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].WeightedSum, Is.EqualTo(40));
            Assert.That(restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].ObservedSeconds, Is.EqualTo(1));
            Assert.That(_window.ResourceTrendNoticeForFixture, Is.Not.Null.And.Not.Empty);
        });
    });

    [Test]
    public void Clear_BeforeStartupLoad_WaitsAndCannotRestoreDeletedHistory() => Run(async () =>
    {
        var saved = new ResourceTrendArchive();
        saved.Add(Sample());
        await ResourceTrendArchive.SaveAsync(_store.ResourceTrendArchivePath, saved.Snapshot());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ResourceTrendArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        _window.ResourceTrendLoaderForFixture = (_, _) =>
        {
            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            return pending.Task;
        };
        var clear = _window.ClearResourceTrendsForFixtureAsync();
        var first = await Task.WhenAny(clear, entered.Task);
        var load = _window.LoadResourceTrendsForFixtureAsync();
        var fileRetainedWhileLoading = File.Exists(_store.ResourceTrendArchivePath);
        pending.SetResult(saved);
        await Task.WhenAll(clear, load);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(entered.Task), "Clear must join startup loading before deleting history.");
            Assert.That(fileRetainedWhileLoading, Is.True);
            Assert.That(reads, Is.EqualTo(1), "Startup and Clear must share one read and one writer lock.");
            Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Is.Empty);
            Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.False);
            Assert.That(_window.ResourceTrendNoticeForFixture, Is.Null);
        });
        _window.RecordResourceTrendForFixture(Sample() with { TotalCpuPercent = 75 });
        await _window.SaveResourceTrendsForFixtureAsync();
        var restored = await ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath);
        Assert.That(restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(75));
        Assert.That(restored.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].ObservedSeconds, Is.EqualTo(1));
    });

    [Test]
    public void Clear_DuringStartupLoad_SharesTheReadAndClearsItsResult() => Run(async () =>
    {
        var saved = new ResourceTrendArchive();
        saved.Add(Sample());
        await ResourceTrendArchive.SaveAsync(_store.ResourceTrendArchivePath, saved.Snapshot());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<ResourceTrendArchive>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        _window.ResourceTrendLoaderForFixture = (_, _) =>
        {
            Interlocked.Increment(ref reads);
            entered.TrySetResult();
            return pending.Task;
        };
        var load = _window.LoadResourceTrendsForFixtureAsync();
        await entered.Task;
        var clear = _window.ClearResourceTrendsForFixtureAsync();
        var sameLoad = _window.LoadResourceTrendsForFixtureAsync();
        var clearWaited = !clear.IsCompleted;
        pending.SetResult(saved);
        await Task.WhenAll(load, clear, sameLoad);
        Assert.Multiple(() =>
        {
            Assert.That(sameLoad, Is.SameAs(load));
            Assert.That(clearWaited, Is.True);
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Is.Empty);
            Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.False);
            Assert.That(_window.ResourceTrendNoticeForFixture, Is.Null);
        });
    });

    [Test]
    public void RejectedArchive_IsPreservedUntilExplicitClearThenSavingRecovers() => Run(async () =>
    {
        var rejected = Encoding.UTF8.GetBytes("{\"SchemaVersion\":999,\"Buckets\":[]}");
        await File.WriteAllBytesAsync(_store.ResourceTrendArchivePath, rejected);
        await _window.LoadResourceTrendsForFixtureAsync();
        _window.RecordResourceTrendForFixture(Sample());
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(_store.ResourceTrendArchivePath), Is.EqualTo(rejected));
            Assert.That(_window.ResourceTrendNoticeForFixture, Is.Not.Null);
            Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Has.Length.EqualTo(1));
        });
        await _window.ClearResourceTrendsForFixtureAsync();
        Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.False);
        _window.RecordResourceTrendForFixture(Sample());
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.That((await ResourceTrendArchive.LoadAsync(_store.ResourceTrendArchivePath)).Snapshot().Buckets, Has.Length.EqualTo(1));
        Assert.That(_window.ResourceTrendNoticeForFixture, Is.Null);
    });

    [Test]
    public void WriterContention_KeepsCurrentSessionReadingsWithoutReplacingArchive() => Run(async () =>
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample());
        await ResourceTrendArchive.SaveAsync(_store.ResourceTrendArchivePath, archive.Snapshot());
        var original = await File.ReadAllBytesAsync(_store.ResourceTrendArchivePath);
        using var otherWriter = new FileStream(_store.ResourceTrendArchivePath + ".lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        await _window.LoadResourceTrendsForFixtureAsync();
        Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Is.Empty);
        _window.RecordResourceTrendForFixture(Sample());
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.That(await File.ReadAllBytesAsync(_store.ResourceTrendArchivePath), Is.EqualTo(original));
        Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Has.Length.EqualTo(1));
        Assert.That(_window.ResourceTrendNoticeForFixture, Is.Not.Null);
    });

    [Test]
    public void Clear_WaitsForPendingCheckpointAndDoesNotResetUsage() => Run(async () =>
    {
        await _window.LoadResourceTrendsForFixtureAsync();
        _window.RecordResourceTrendForFixture(Sample());
        _window.Session.Usage.Add(Sample());
        var usageSince = _window.Session.Usage.Since;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _window.SetResourceTrendWriteForFixture(pending.Task);
        var clear = _window.ClearResourceTrendsForFixtureAsync();
        Assert.That(clear.IsCompleted, Is.False);
        await File.WriteAllTextAsync(_store.ResourceTrendArchivePath, "previous checkpoint");
        pending.SetResult();
        await clear;
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.False);
            Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Is.Empty);
            Assert.That(_window.Session.Usage.Since, Is.EqualTo(usageSince));
        });
    });

    [Test]
    public void FailedCheckpoint_RetainsMemoryAndReportsUnsavedReadings() => Run(async () =>
    {
        await _window.LoadResourceTrendsForFixtureAsync();
        _window.RecordResourceTrendForFixture(Sample());
        Directory.CreateDirectory(_store.ResourceTrendArchivePath);
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.Multiple(() =>
        {
            Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Has.Length.EqualTo(1));
            Assert.That(_window.ResourceTrendNoticeForFixture, Is.Not.Null);
        });
        Directory.Delete(_store.ResourceTrendArchivePath);
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.True);
        Assert.That(_window.ResourceTrendNoticeForFixture, Is.Null);
    });

    [Test]
    public void FailedClear_PreservesReadingsAndAllowsALaterCheckpoint() => Run(async () =>
    {
        await _window.LoadResourceTrendsForFixtureAsync();
        _window.RecordResourceTrendForFixture(Sample());
        Directory.CreateDirectory(_store.ResourceTrendArchivePath);
        Assert.That(async () => await _window.ClearResourceTrendsForFixtureAsync(), Throws.InstanceOf<IOException>()
            .Or.InstanceOf<UnauthorizedAccessException>());
        Assert.That(_window.ResourceTrendsForFixture.Snapshot().Buckets, Has.Length.EqualTo(1));
        Directory.Delete(_store.ResourceTrendArchivePath);
        await _window.SaveResourceTrendsForFixtureAsync();
        Assert.That(File.Exists(_store.ResourceTrendArchivePath), Is.True);
    });

    private static void Run(Func<Task> action)
    {
        var previousContext = SynchronizationContext.Current;
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
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
    private static MonitorSample Sample()
    {
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        return new MonitorSample(minute.AddMinutes(-1).AddSeconds(10), 40, [], 0) { IntervalSeconds = 1 };
    }
}
