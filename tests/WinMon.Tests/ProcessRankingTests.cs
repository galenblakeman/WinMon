using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessRankingTests
{
    private static CpuSample Sample(double second, double total, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UtcNow, total, processes, 0, false, (long)(Stopwatch.Frequency * second));

    private static ProcessUsage Usage(int id, double cpu, ulong? memory = null, double? io = null) =>
        new(id, id * 10, $"p{id}", cpu, memory, io);

    [Test]
    public void Rank_KeepsAProcessListedWhileAnyIntervalInTheWindowMeasuredIt()
    {
        // p1 registers a tick only in the oldest interval of the window; single-interval ranking would drop it.
        CpuSample[] history =
        [
            Sample(1, 10, Usage(1, 5), Usage(2, 0)),
            Sample(2, 10, Usage(2, 1)),
            Sample(3, 10, Usage(2, 1)),
            Sample(4, 10, Usage(2, 1)),
            Sample(5, 10, Usage(1, 0), Usage(2, 0)),
        ];

        var ranking = ProcessRanking.Rank(history);

        Assert.Multiple(() =>
        {
            Assert.That(ranking.IntervalCount, Is.EqualTo(5));
            Assert.That(ranking.Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(ranking.Processes[0].CpuPercent, Is.EqualTo(1).Within(1e-9));
            Assert.That(ranking.Processes[1].CpuPercent, Is.EqualTo(0.6).Within(1e-9));
            Assert.That(ranking.TotalCpuPercent, Is.EqualTo(10).Within(1e-9));
        });
    }

    [Test]
    public void Rank_UsesOnlyTheLastFiveIntervalsDespiteTimerJitter()
    {
        var history = new[] { 1.0, 2.01, 2.98, 4.0, 5.02, 5.99 }
            .Select((second, i) => Sample(second, 10 * (i + 1), Usage(1, i == 0 ? 50 : 1))).ToArray();

        var ranking = ProcessRanking.Rank(history);

        Assert.Multiple(() =>
        {
            Assert.That(ranking.IntervalCount, Is.EqualTo(5));
            Assert.That(ranking.Processes.Single().CpuPercent, Is.EqualTo(1).Within(1e-9));
            Assert.That(ranking.TotalCpuPercent, Is.EqualTo(40).Within(1e-9));
        });
    }

    [Test]
    public void Rank_StopsAtGapsAndLongIntervals()
    {
        var gap = new CpuSample(DateTimeOffset.UtcNow, null, [], 0, true, Stopwatch.Frequency * 3);
        CpuSample[] afterGap = [Sample(1, 10, Usage(1, 9)), Sample(2, 10, Usage(1, 9)), gap, Sample(4, 10, Usage(2, 2)), Sample(5, 10, Usage(2, 2))];
        CpuSample[] afterDelay = [Sample(2, 10, Usage(1, 9)), Sample(4.6, 10, Usage(2, 2)), Sample(5.6, 10, Usage(2, 2))];

        Assert.Multiple(() =>
        {
            Assert.That(ProcessRanking.Rank(afterGap).Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 2 }));
            Assert.That(ProcessRanking.Rank(afterGap).IntervalCount, Is.EqualTo(2));
            Assert.That(ProcessRanking.Rank(afterDelay).IntervalCount, Is.EqualTo(2));
            Assert.That(ProcessRanking.Rank([.. afterGap, gap with { MonotonicTimestamp = Stopwatch.Frequency * 6 }]).Processes, Is.Empty);
            Assert.That(ProcessRanking.Rank([]).TotalCpuPercent, Is.Null);
        });
    }

    [Test]
    public void Rank_HistoricalEndExcludesLaterSamplesAndKeepsLatestMemoryWithAveragedIo()
    {
        CpuSample[] history =
        [
            Sample(1, 10, Usage(1, 2, memory: 100, io: 10)),
            Sample(2, 10, Usage(1, 2, memory: 200, io: null)),
            Sample(3, 10, Usage(1, 2, memory: 300, io: 30)),
            Sample(4, 90, Usage(1, 80, memory: 900, io: 900)),
        ];

        var selected = ProcessRanking.Rank(history.Take(3).ToArray()).Processes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(selected.CpuPercent, Is.EqualTo(2).Within(1e-9));
            Assert.That(selected.WorkingSetBytes, Is.EqualTo(300));
            Assert.That(selected.IoBytesPerSecond, Is.EqualTo(20).Within(1e-9));
        });
    }

    [Test]
    public void Rank_GpuUsesReadableIntervalsIncludingZero_AndCanShowCpuIdleProcesses()
    {
        CpuSample[] history = [
            Sample(1, 10, Usage(1, 0) with { GpuPercent = 60 }, Usage(2, 8) with { GpuPercent = 10 }),
            Sample(2, 10, Usage(1, 0) with { GpuPercent = null }, Usage(2, 8) with { GpuPercent = 10 }),
            Sample(3, 10, Usage(1, 0) with { GpuPercent = 0 }, Usage(2, 8) with { GpuPercent = 10 })];
        var gpu = ProcessRanking.Rank(history, sortByGpu: true);
        Assert.Multiple(() =>
        {
            Assert.That(gpu.Processes.Select(p => p.ProcessId), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(gpu.Processes[0].GpuPercent, Is.EqualTo(30));
            Assert.That(ProcessRanking.Rank(history).Processes.Single().ProcessId, Is.EqualTo(2));
        });
    }

    [Test]
    public void Rank_GpuSeparatesPidLifetimes_AndHonorsHistoricalEndAndGaps()
    {
        CpuSample[] history = [
            Sample(1, 10, new ProcessUsage(7, 1, "old", 0) with { GpuPercent = 80 }),
            Sample(2, 10, new ProcessUsage(7, 2, "new", 0) with { GpuPercent = 20 }),
            Sample(3, 10, new ProcessUsage(7, 2, "new", 0) with { GpuPercent = 100 })];
        var selected = ProcessRanking.Rank(history.Take(2).ToArray(), sortByGpu: true);
        Assert.Multiple(() =>
        {
            Assert.That(selected.Processes.Select(p => p.GpuPercent), Is.EqualTo(new double?[] { 80, 20 }));
            Assert.That(ProcessRanking.Rank([history[2] with { IsGap = true }], sortByGpu: true).Processes, Is.Empty);
            Assert.That(ProcessRanking.Rank([Sample(1, 10, Usage(1, 10))], sortByGpu: true).Processes, Is.Empty);
        });
    }

    [Test]
    public void Rank_SeparatesReusedProcessIdsByCreationTime()
    {
        CpuSample[] history =
        [
            Sample(1, 10, new ProcessUsage(7, 1, "old", 3)),
            Sample(2, 10, new ProcessUsage(7, 2, "new", 1)),
        ];

        var ranking = ProcessRanking.Rank(history);

        Assert.That(ranking.Processes.Select(p => (p.Name, p.CpuPercent)),
            Is.EqualTo(new[] { ("old", 1.5), ("new", 0.5) }));
    }
}
