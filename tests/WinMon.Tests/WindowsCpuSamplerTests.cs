using NUnit.Framework;
using System.ComponentModel;
using System.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsCpuSamplerTests
{
    [Test]
    public void Sample_CarriesExecutableAppNamesIntoSearchableRows()
    {
        var path = typeof(WindowsCpuSampler).Assembly.Location;
        var info = FileVersionInfo.GetVersionInfo(path);
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, [new ProcessCpuTimes(10, 100, "unrelated", 50, ImagePath: path)]),
            Snapshot(2, 2_000, 200, [new ProcessCpuTimes(10, 100, "unrelated", 50, ImagePath: path)])));
        _ = sampler.Sample();
        var sample = sampler.Sample();
        var rows = ProcessRanking.Rank([sample], includeInactive: true).Processes;
        Assert.Multiple(() =>
        {
            Assert.That(sample.Processes.Single().AppName, Is.EqualTo(info.FileDescription?.Trim()));
            Assert.That(sample.Processes.Single().ProductName, Is.EqualTo(info.ProductName?.Trim() ?? ""));
            Assert.That(ProcessSearch.Filter(rows, info.FileDescription!), Is.EqualTo(rows));
            Assert.That(rows.Single().CpuPercent, Is.Zero);
        });
    }

    [Test]
    public void Sample_FirstObservation_ReturnsGapWithoutPercentages()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(Snapshot(1, 100, 50, [])));

        var sample = sampler.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(sample.IsGap, Is.True);
            Assert.That(sample.TotalCpuPercent, Is.Null);
            Assert.That(sample.Processes, Is.Empty);
        });
    }

    [Test]
    public void Sample_ValidDeltas_NormalizesTotalAndProcessUsageToMachineCapacity()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 2_000, 600, [new ProcessCpuTimes(10, 100, "alpha", 200)]),
            Snapshot(2, 3_000, 700, [new ProcessCpuTimes(10, 100, "alpha", 450)])));

        _ = sampler.Sample();
        var sample = sampler.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(sample.IsGap, Is.False);
            Assert.That(sample.TotalCpuPercent, Is.EqualTo(90d));
            Assert.That(sample.Processes, Has.Count.EqualTo(1));
            Assert.That(sample.Processes[0], Is.EqualTo(new ProcessUsage(10, 100, "alpha", 25d)));
        });
    }

    [Test]
    public void Sample_ReusedProcessId_DoesNotAttributePriorLifetimeCpu()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, [new ProcessCpuTimes(22, 100, "old", 500)]),
            Snapshot(2, 2_000, 200, [new ProcessCpuTimes(22, 200, "new", 900)])));

        _ = sampler.Sample();
        var sample = sampler.Sample();

        Assert.That(sample.Processes, Is.Empty);
    }

    [Test]
    public void Sample_ProcessCounterRegression_OmitsUnreliableProcess()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, [new ProcessCpuTimes(22, 100, "alpha", 500)]),
            Snapshot(2, 2_000, 200, [new ProcessCpuTimes(22, 100, "alpha", 400)])));

        _ = sampler.Sample();
        var sample = sampler.Sample();

        Assert.That(sample.Processes, Is.Empty);
    }

    [Test]
    public void Sample_LongMonotonicDelay_ReturnsGapAndResetsProcessBaselines()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, [new ProcessCpuTimes(22, 100, "alpha", 50)]),
            Snapshot(7, 7_000, 700, [new ProcessCpuTimes(22, 100, "alpha", 500)]),
            Snapshot(8, 8_000, 800, [new ProcessCpuTimes(22, 100, "alpha", 600)])));

        _ = sampler.Sample();
        var gap = sampler.Sample();
        var resumed = sampler.Sample();

        Assert.Multiple(() =>
        {
            Assert.That(gap.IsGap, Is.True);
            Assert.That(gap.TotalCpuPercent, Is.Null);
            Assert.That(resumed.IsGap, Is.False);
            Assert.That(resumed.Processes[0].CpuPercent, Is.EqualTo(10d));
        });
    }

    [Test]
    public void Reset_NextSample_ReturnsGap()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, []),
            Snapshot(2, 2_000, 200, []),
            Snapshot(3, 3_000, 300, [])));

        _ = sampler.Sample();
        _ = sampler.Sample();
        sampler.Reset();
        var sample = sampler.Sample();

        Assert.That(sample.IsGap, Is.True);
        Assert.That(sample.TotalCpuPercent, Is.Null);
    }

    [Test]
    public void Sample_ProviderFails_PropagatesFailure()
    {
        using var sampler = new WindowsCpuSampler(new ThrowingSnapshotProvider());

        Assert.That(() => sampler.Sample(), Throws.TypeOf<Win32Exception>());
    }

    [Test]
    public void Sample_ProcessesAreOrderedByCpuThenNameThenProcessId()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100,
            [
                new ProcessCpuTimes(2, 1, "Beta", 100),
                new ProcessCpuTimes(1, 1, "alpha", 100),
                new ProcessCpuTimes(3, 1, "alpha", 100),
            ]),
            Snapshot(2, 2_000, 200,
            [
                new ProcessCpuTimes(2, 1, "Beta", 300),
                new ProcessCpuTimes(1, 1, "alpha", 300),
                new ProcessCpuTimes(3, 1, "alpha", 300),
            ])));

        _ = sampler.Sample();
        var sample = sampler.Sample();

        Assert.That(sample.Processes.Select(process => process.ProcessId), Is.EqualTo([1, 3, 2]));
    }

    [Test]
    public void Sample_ObservesUnavailableProcessCount()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(Snapshot(1, 1_000, 100, [], unavailableProcesses: 4)));

        var sample = sampler.Sample();

        Assert.That(sample.UnavailableProcesses, Is.EqualTo(4));
    }

    [Test]
    public void Sample_Disposed_RejectsFurtherCollection()
    {
        var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider());
        sampler.Dispose();

        Assert.That(() => sampler.Sample(), Throws.TypeOf<ObjectDisposedException>());
        Assert.That(() => sampler.Reset(), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Sample_ProcessExitsAndAnotherStarts_DoesNotInventUsage()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, [new ProcessCpuTimes(22, 100, "exited", 500)]),
            Snapshot(2, 2_000, 200, [new ProcessCpuTimes(33, 200, "new", 900)])));
        _ = sampler.Sample();

        Assert.That(sampler.Sample().Processes, Is.Empty);
    }

    [Test]
    public void Sample_EqualSystemCounters_ReturnsUnavailableGap()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 100, []), Snapshot(2, 1_000, 100, [])));
        _ = sampler.Sample();
        var result = sampler.Sample();

        Assert.That(result.IsGap, Is.True);
        Assert.That(result.TotalCpuPercent, Is.Null);
    }

    [Test]
    public void Sample_IdleCounterRegresses_ReturnsGapInsteadOfFabricatedSaturation()
    {
        using var sampler = new WindowsCpuSampler(new SequenceSnapshotProvider(
            Snapshot(1, 1_000, 200, []), Snapshot(2, 2_000, 100, [])));
        _ = sampler.Sample();
        var result = sampler.Sample();

        Assert.That(result.IsGap, Is.True);
        Assert.That(result.TotalCpuPercent, Is.Null);
    }

    private static CpuSnapshot Snapshot(
        long seconds,
        long totalTicks,
        long idleTicks,
        IReadOnlyList<ProcessCpuTimes> processes,
        int unavailableProcesses = 0)
    {
        return new CpuSnapshot(
            DateTimeOffset.UnixEpoch.AddSeconds(seconds),
            seconds * Stopwatch.Frequency,
            new SystemCpuTimes(idleTicks, totalTicks, 0),
            processes,
            unavailableProcesses);
    }

    private sealed class SequenceSnapshotProvider(params CpuSnapshot[] snapshots) : ICpuSnapshotProvider
    {
        private readonly Queue<CpuSnapshot> snapshots = new(snapshots);

        public CpuSnapshot Capture() => snapshots.Dequeue();
    }

    private sealed class ThrowingSnapshotProvider : ICpuSnapshotProvider
    {
        public CpuSnapshot Capture() => throw new Win32Exception();
    }
}




