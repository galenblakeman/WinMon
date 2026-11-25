using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessGraphBoundaryCharacterizationTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);

    private static ProcessUsage Process(int id, double value = 3) =>
        new(id, At.AddMinutes(-1).ToFileTime(), "app", value, (ulong)value, value)
        {
            ImagePath = @"C:\fixture\app.exe", PrivateBytes = (ulong)value,
            ThreadCount = (long)value, HandleCount = (long)value, GpuPercent = value
        };

    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(At.AddSeconds(second), 20, processes, 0, false, Stopwatch.Frequency * (second + 1));

    [TestCase(-1, 0, false)]
    [TestCase(-1, 0, true)]
    [TestCase(0, 1, false)]
    [TestCase(0, 1, true)]
    [TestCase(1, 2, false)]
    [TestCase(1, 2, true)]
    public void TimeWindowKeepsTheExactLowerBoundaryAndExcludesEarlierSamples(
        int seconds, int count, bool grouped)
    {
        var process = Process(1);
        MonitorSample[] history = [Sample(0, process), Sample(1, process with { CpuPercent = 9 })];

        var data = ProcessGraphData.Build(history, seconds, [process], grouped)[0];

        Assert.That(data.Samples, Is.EqualTo(history.TakeLast(count)));
        Assert.That(data.Values, Is.EqualTo(new double?[] { 3, 9 }.TakeLast(count)));
        Assert.That(data.ScaleMaximum, Is.EqualTo(count == 0 ? 0.1 : 9.9));
    }

    [Test]
    public void EmptyRowsReturnBeforeReadingHistoryButNonemptyRowsKeepTheExistingNullContract(
        [Values(false, true)] bool grouped)
    {
        Assert.That(ProcessGraphData.Build(null!, 60, [], grouped), Is.Empty);
        Assert.Throws<NullReferenceException>(() => ProcessGraphData.Build(null!, 60, [Process(1)], grouped));
        Assert.Throws<NullReferenceException>(() => ProcessGraphData.Build([], 60, null!, grouped));
    }

    [Test]
    public void DuplicateIndividualLifetimesThrowWithoutBreakingTheNextValidCacheUse()
    {
        var process = Process(1);
        MonitorSample[] history = [Sample(0, process)];
        var reuse = new ProcessGraphData.Reuse();
        var original = ProcessGraphData.Build(history, 60, [process], reuse: reuse)[0];

        Assert.Throws<ArgumentException>(() => ProcessGraphData.Build(history, 60, [process, process], reuse: reuse));
        var next = ProcessGraphData.Build(history, 60, [process], reuse: reuse)[0];

        Assert.That(original.Values, Is.EqualTo(new double?[] { 3 }));
        Assert.That(next.Values, Is.EqualTo(new double?[] { 3 }));
        Assert.That(next.Values, Is.Not.SameAs(original.Values));
    }

    [TestCase(ProcessSort.Cpu)]
    [TestCase(ProcessSort.Memory)]
    [TestCase(ProcessSort.Threads)]
    public void IndividualRowsKeepTheFirstImportedUnavailableReadingEvenWhenADuplicateIsAvailable(ProcessSort sort)
    {
        var process = Process(1, 7);
        var missing = process with { CpuAvailable = false, WorkingSetBytes = null, ThreadCount = null };
        MonitorSample[] history = [Sample(0, missing, process)];
        var reuse = new ProcessGraphData.Reuse();

        var first = ProcessGraphData.Build(history, 60, [process], sort: sort, reuse: reuse)[0];
        var next = ProcessGraphData.Build([history[0], Sample(1, process)], 60, [process], sort: sort, reuse: reuse)[0];

        Assert.That(first.Values, Is.EqualTo(new double?[] { null }));
        Assert.That(next.Values, Is.EqualTo(new double?[] { null, 7 }));
    }

    [TestCase(ProcessSort.Cpu)]
    [TestCase(ProcessSort.Io)]
    public void DuplicateGroupKeysKeepTheFirstOwnerAndRecomputeIdleForEachRepresentative(ProcessSort sort)
    {
        var first = Process(1, 2);
        var second = Process(2, 3) with
        {
            ImagePath = @"c:\FIXTURE\APP.exe", CreationTicks = At.AddSeconds(2).ToFileTime()
        };
        MonitorSample[] history = [Sample(0, first, second), Sample(1) with { CompactedProcesses = 1 }];
        var reuse = new ProcessGraphData.Reuse();

        var original = ProcessGraphData.Build(history, 60, [first, second], true, sort, reuse);
        var reordered = ProcessGraphData.Build(history, 60, [second, first], true, sort, reuse);

        Assert.Multiple(() =>
        {
            Assert.That(original[0].Values, Is.EqualTo(new double?[] { 5, 0 }));
            Assert.That(original[1].Values, Is.EqualTo(new double?[] { null, null }));
            Assert.That(reordered[0].Values, Is.EqualTo(new double?[] { 5, null }));
            Assert.That(reordered[1].Values, Is.EqualTo(new double?[] { null, 0 }));
            Assert.That(reordered[0].ScaleMaximum, Is.EqualTo(5.5));
            Assert.That(reordered[1].ScaleMaximum, Is.EqualTo(5.5));
        });
    }

    [TestCase(ProcessSort.Cpu)]
    [TestCase(ProcessSort.Threads)]
    [TestCase(ProcessSort.Handles)]
    public void MixedCachedAndNewGroupsPreserveUnavailableCountsWithoutPoisoningTheNewRow(ProcessSort sort)
    {
        var alpha = Process(1);
        var unavailable = alpha with { ProcessId = 2, CpuAvailable = false, ThreadCount = null, HandleCount = null };
        var beta = Process(3, 2) with { Name = "beta", ImagePath = @"C:\fixture\beta.exe" };
        var first = Sample(0, alpha, unavailable, beta);
        var second = Sample(1, alpha with { CpuPercent = 4, ThreadCount = 4, HandleCount = 4 },
            beta with { CpuPercent = 6, ThreadCount = 6, HandleCount = 6 });
        var reuse = new ProcessGraphData.Reuse();
        var seeded = ProcessGraphData.Build([first], 60, [alpha], true, sort, reuse);

        var result = ProcessGraphData.Build([first, second], 60, [alpha, beta], true, sort, reuse);

        Assert.That(seeded[0].Values, Is.EqualTo(new double?[] { null }));
        Assert.That(result[0].Values, Is.EqualTo(new double?[] { null, 4 }));
        Assert.That(result[1].Values, Is.EqualTo(new double?[] { 2, 6 }));
        Assert.That(result[0].Samples, Is.SameAs(result[1].Samples));
    }

    [Test]
    public void CachedGroupIdleFillUsesTheCurrentRepresentativesStartBoundaryAndLifetimeDomain(
        [Values(ProcessSort.Cpu, ProcessSort.Io)] ProcessSort sort, [Values(-1, 0, 1)] int startOffset,
        [Values(ProcessLifetimeDomain.WindowsFileTime, ProcessLifetimeDomain.UnixMicroseconds)] ProcessLifetimeDomain domain)
    {
        var process = Process(1, 5);
        MonitorSample[] history = [Sample(0, process), Sample(1) with { CompactedProcesses = 1 }];
        var reuse = new ProcessGraphData.Reuse();
        var seeded = ProcessGraphData.Build(history, 60, [process], true, sort, reuse)[0];
        var representative = process with
        {
            ProcessId = 2, CreationTicks = At.AddSeconds(1).ToFileTime() + startOffset, LifetimeDomain = domain
        };

        var result = ProcessGraphData.Build(history, 60, [representative], true, sort, reuse)[0];

        Assert.That(seeded.Values, Is.EqualTo(new double?[] { 5, 0 }));
        Assert.That(result.Values, Is.EqualTo(new double?[] { 5,
            domain == ProcessLifetimeDomain.WindowsFileTime && startOffset <= 0 ? 0 : null }));
        Assert.That(seeded.Values, Is.EqualTo(new double?[] { 5, 0 }), "A later refresh must not mutate an earlier graph");
    }

    [Test]
    public void ReuseInvalidatesWhenMetricAndGroupingChangeTogether(
        [Values(ProcessSort.Gpu, ProcessSort.Memory)] ProcessSort nextSort, [Values(false, true)] bool wasGrouped)
    {
        var first = Process(1, 3) with { GpuPercent = 20, WorkingSetBytes = 7, PrivateBytes = 11 };
        var second = Process(2, 4) with { GpuPercent = 40, WorkingSetBytes = 9, PrivateBytes = 13 };
        MonitorSample[] history = [Sample(0, first, second)];
        var reuse = new ProcessGraphData.Reuse();
        ProcessGraphData.Build(history, 60, [first], wasGrouped, ProcessSort.Cpu, reuse);

        var result = ProcessGraphData.Build(history, 60, [first], !wasGrouped, nextSort, reuse)[0];

        var expected = nextSort == ProcessSort.Gpu ? wasGrouped ? 20 : 40 : wasGrouped ? 7 : 24;
        Assert.That(result.Values, Is.EqualTo(new double?[] { expected }));
        Assert.That(result.Sort, Is.EqualTo(nextSort));
        Assert.That(result.ScaleMaximum, Is.EqualTo(nextSort == ProcessSort.Gpu
            ? Math.Ceiling(expected * 11d) / 10 : expected * 1.1));
    }

    [Test]
    public void NonfiniteMetricsStayMissingIndividuallyAndKeepTheGroupedAggregationRules(
        [Values(ProcessSort.Cpu, ProcessSort.Gpu, ProcessSort.Io)] ProcessSort sort,
        [Values(false, true)] bool grouped, [Values(double.NaN, double.PositiveInfinity)] double value)
    {
        var unavailable = Process(1) with { CpuPercent = value, GpuPercent = value, IoBytesPerSecond = value };
        MonitorSample[] history = [Sample(0, unavailable, Process(2, 2))];

        var result = ProcessGraphData.Build(history, 60, [unavailable], grouped, sort)[0];

        Assert.That(result.Values, Is.EqualTo(new double?[] { grouped && sort != ProcessSort.Cpu ? 2 : null }));
    }

    [Test]
    public void NegativeFiniteMetricsClampBeforeGroupedAggregation(
        [Values(ProcessSort.Cpu, ProcessSort.Gpu, ProcessSort.Io)] ProcessSort sort,
        [Values(false, true)] bool grouped)
    {
        var negative = Process(1) with { CpuPercent = -3, GpuPercent = -3, IoBytesPerSecond = -3 };
        MonitorSample[] history = [Sample(0, negative, Process(2, 2))];

        var result = ProcessGraphData.Build(history, 60, [negative], grouped, sort)[0];

        Assert.That(result.Values, Is.EqualTo(new double?[] { grouped ? 2 : 0 }));
    }
}
