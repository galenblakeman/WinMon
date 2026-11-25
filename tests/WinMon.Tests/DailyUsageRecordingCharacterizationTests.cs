using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed partial class SessionEndCharacterizationTests
{
    private static readonly DateTimeOffset DailyStart = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
    // Invoke the existing sampling callback without adding a production-only fixture API.
    private static readonly MethodInfo DailyRecordMethod = typeof(MainWindow).GetMethod("RecordDailyUsage",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing daily recording callback");
    // Control only the tracked write; assertions observe archive data, persisted files and logging.
    private static readonly FieldInfo DailyWriteField = typeof(MainWindow).GetField("_usageArchiveWrite",
        BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing tracked archive write");

    private Action<MonitorSample> DailyRecorder(MainWindow? window = null)
    {
        var root = Path.GetFullPath(_temporaryFolder!) + Path.DirectorySeparatorChar;
        Assert.That(Path.GetFullPath(_store.UsageArchivePath), Does.StartWith(root).IgnoreCase);
        Assert.That(Path.GetFileName(_temporaryFolder), Does.StartWith("WinMon.SessionEnd."));
        return DailyRecordMethod.CreateDelegate<Action<MonitorSample>>(window ?? _window);
    }

    private Task DailyCheckpoint => (Task)DailyWriteField.GetValue(_window)!;

    private void InjectDailyCheckpoint(Task task)
    {
        Assert.That(DailyCheckpoint.IsCompleted, Is.True, "Never replace an outstanding real write");
        DailyWriteField.SetValue(_window, task);
    }

    private void LoadOwnedDailyArchive()
    {
        _ = DailyRecorder();
        Complete(_window.LoadUsageArchiveForFixtureAsync());
        Assert.That(_window.DailyUsageArchive, Is.Not.Null);
        _window.DailyUsageArchive = new UsageArchive(TimeZoneInfo.Utc);
    }

    private static MonitorSample DailySample(DateTimeOffset timestamp) => new(timestamp, 42, [], 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private double RecordedDailySeconds() => _window.DailyUsageArchive!.Export().Days.Sum(day => day.ObservedSeconds);
    private double PersistedDailySeconds() => Complete(UsageArchive.LoadAsync(_store.UsageArchivePath))
        .Export().Days.Sum(day => day.ObservedSeconds);

    [TestCase("diagnostic")]
    [TestCase("missing")]
    public void DailyUsageGuardsIgnoreNullSamplesAndPreserveOwnedFiles(string guard)
    {
        File.WriteAllText(_store.UsageArchivePath, "owned archive sentinel");
        if (guard == "diagnostic")
        {
            var diagnostic = CreateWindow(true);
            diagnostic.Loaded -= diagnostic.OnLoaded;
            try
            {
                diagnostic.DailyUsageArchive = new UsageArchive(TimeZoneInfo.Utc);
                Assert.DoesNotThrow(() => DailyRecorder(diagnostic)(null!));
                Assert.That(diagnostic.DailyUsageArchive.Export().Days, Is.Empty);
            }
            finally { CloseOwnedWindow(diagnostic); }
        }
        else Assert.DoesNotThrow(() => DailyRecorder()(null!));
        Assert.That(File.ReadAllText(_store.UsageArchivePath), Is.EqualTo("owned archive sentinel"));
        Assert.That(File.Exists(_store.UsageArchivePath + ".lock"), Is.False);
    }

    [Test]
    public void DailyUsageWithAnArchivePreservesTheNullSampleExceptionWithoutWriting()
    {
        LoadOwnedDailyArchive();
        Assert.Throws<NullReferenceException>(() => DailyRecorder()(null!));
        Assert.That(RecordedDailySeconds(), Is.Zero);
        Assert.That(File.Exists(_store.UsageArchivePath), Is.False);
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(1L)]
    [TestCase(-1_200_000_000L)]
    public void DailyUsageCheckpointUsesTheInclusiveMinuteBoundaryAndKeepsEarlierSamples(long offsetTicks)
    {
        LoadOwnedDailyArchive();
        var record = DailyRecorder();
        record(DailySample(DailyStart));
        Complete(DailyCheckpoint);
        Assert.That(PersistedDailySeconds(), Is.EqualTo(1));
        var before = File.ReadAllBytes(_store.UsageArchivePath);
        record(DailySample(DailyStart.AddMinutes(1).AddTicks(offsetTicks)));
        Complete(DailyCheckpoint);
        Assert.That(RecordedDailySeconds(), Is.EqualTo(2));
        if (offsetTicks < 0)
        {
            Assert.That(File.ReadAllBytes(_store.UsageArchivePath), Is.EqualTo(before));
            Assert.That(PersistedDailySeconds(), Is.EqualTo(1));
        }
        else Assert.That(PersistedDailySeconds(), Is.EqualTo(2));
    }

    [Test]
    public void DailyUsagePendingWriteStillRecordsAndDoesNotAdvanceTheCheckpointClock()
    {
        LoadOwnedDailyArchive();
        var record = DailyRecorder();
        record(DailySample(DailyStart));
        Complete(DailyCheckpoint);
        var before = File.ReadAllBytes(_store.UsageArchivePath);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        InjectDailyCheckpoint(pending.Task);
        try
        {
            record(DailySample(DailyStart.AddMinutes(2)));
            Assert.That(RecordedDailySeconds(), Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(_store.UsageArchivePath), Is.EqualTo(before));
            Assert.That(pending.Task.IsCompleted, Is.False);
            pending.SetResult(true);
            record(DailySample(DailyStart.AddMinutes(2).AddSeconds(1)));
            Complete(DailyCheckpoint);
            Assert.That(RecordedDailySeconds(), Is.EqualTo(3));
            Assert.That(PersistedDailySeconds(), Is.EqualTo(3));
        }
        finally
        {
            pending.TrySetResult(true);
            Complete(DailyCheckpoint);
        }
    }

    [Test]
    public void DailyUsageFaultedCheckpointLogsItsFailureAndTheNextSampleIsPersisted()
    {
        LoadOwnedDailyArchive();
        var failure = new IOException("owned checkpoint failure");
        InjectDailyCheckpoint(Task.FromException(failure));
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            DailyRecorder()(DailySample(DailyStart));
            Complete(DailyCheckpoint);
            listener.Flush();
            Assert.That(output.ToString(), Does.Contain("Usage archive checkpoint failed:")
                .And.Contain(nameof(IOException)).And.Contain("owned checkpoint failure"));
            Assert.That(RecordedDailySeconds(), Is.EqualTo(1));
            Assert.That(PersistedDailySeconds(), Is.EqualTo(1));
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DailyUsageInvalidObservationsKeepCheckpointSchedulingWithoutAddingUsage(bool gap)
    {
        LoadOwnedDailyArchive();
        var sample = DailySample(DailyStart) with { IsGap = gap, IntervalSeconds = gap ? 1 : 0 };
        DailyRecorder()(sample);
        Complete(DailyCheckpoint);
        Assert.That(File.Exists(_store.UsageArchivePath), Is.True);
        Assert.That(RecordedDailySeconds(), Is.Zero);
        Assert.That(PersistedDailySeconds(), Is.Zero);
    }
}
