using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ProcessRankingTests
{
    private static MonitorSample Sample(double second, double total, params ProcessUsage[] processes)
    {
        return new MonitorSample(DateTimeOffset.UtcNow, total, processes, 0, false,
            (long)(Stopwatch.Frequency * second));
    }

    private static ProcessUsage Usage(int id, double cpu, ulong? memory = null, double? io = null)
    {
        return new ProcessUsage(id, id * 10, $"p{id}", cpu, memory, io);
    }

    [Test]
    public void Rank_KeepsAProcessListedWhileAnyIntervalInTheWindowMeasuredIt()
    {
        // p1 registers a tick only in the oldest interval of the window; single-interval ranking would drop it.
        MonitorSample[] history =
        [
            Sample(1, 10, Usage(1, 5), Usage(2, 0)),
            Sample(2, 10, Usage(2, 1)),
            Sample(3, 10, Usage(2, 1)),
            Sample(4, 10, Usage(2, 1)),
            Sample(5, 10, Usage(1, 0), Usage(2, 0))
        ];

        var ranking = ProcessRanking.Rank(history);

        Assert.Multiple(() =>
        {
            Assert.That(ranking.IntervalCount, Is.EqualTo(5));
            Assert.That(ranking.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2]));
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
        var gap = new MonitorSample(DateTimeOffset.UtcNow, null, [], 0, true, Stopwatch.Frequency * 3);
        MonitorSample[] afterGap =
        [
            Sample(1, 10, Usage(1, 9)), Sample(2, 10, Usage(1, 9)), gap, Sample(4, 10, Usage(2, 2)),
            Sample(5, 10, Usage(2, 2))
        ];
        MonitorSample[] afterDelay =
            [Sample(2, 10, Usage(1, 9)), Sample(4.6, 10, Usage(2, 2)), Sample(5.6, 10, Usage(2, 2))];

        Assert.Multiple(() =>
        {
            Assert.That(ProcessRanking.Rank(afterGap).Processes.Select(p => p.ProcessId), Is.EqualTo([2]));
            Assert.That(ProcessRanking.Rank(afterGap).IntervalCount, Is.EqualTo(2));
            Assert.That(ProcessRanking.Rank(afterDelay).IntervalCount, Is.EqualTo(2));
            Assert.That(
                ProcessRanking.Rank([.. afterGap, gap with { MonotonicTimestamp = Stopwatch.Frequency * 6 }]).Processes,
                Is.Empty);
            Assert.That(ProcessRanking.Rank([]).TotalCpuPercent, Is.Null);
        });
    }

    [Test]
    public void Rank_HistoricalEndExcludesLaterSamplesAndKeepsLatestMemoryWithWindowAveragedIo()
    {
        MonitorSample[] history =
        [
            Sample(1, 10, Usage(1, 2, 100, 10)),
            Sample(2, 10, Usage(1, 2, 200, null)),
            Sample(3, 10, Usage(1, 2, 300, 30)),
            Sample(4, 90, Usage(1, 80, 900, 900))
        ];

        var selected = ProcessRanking.Rank([.. history.Take(3)]).Processes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(selected.CpuPercent, Is.EqualTo(2).Within(1e-9));
            Assert.That(selected.WorkingSetBytes, Is.EqualTo(300));
            Assert.That(selected.IoBytesPerSecond, Is.EqualTo(40 / 3d).Within(1e-9));
        });
    }

    [Test]
    public void Rank_GpuUsesReadableIntervalsIncludingZero_AndCanShowCpuIdleProcesses()
    {
        MonitorSample[] history =
        [
            Sample(1, 10, Usage(1, 0) with { GpuPercent = 60 }, Usage(2, 8) with { GpuPercent = 10 }),
            Sample(2, 10, Usage(1, 0) with { GpuPercent = null }, Usage(2, 8) with { GpuPercent = 10 }),
            Sample(3, 10, Usage(1, 0) with { GpuPercent = 0 }, Usage(2, 8) with { GpuPercent = 10 })
        ];
        var gpu = ProcessRanking.Rank(history, sort: ProcessSort.Gpu);
        Assert.Multiple(() =>
        {
            Assert.That(gpu.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2]));
            Assert.That(gpu.Processes[0].GpuPercent, Is.EqualTo(30));
            Assert.That(ProcessRanking.Rank(history).Processes.Single().ProcessId, Is.EqualTo(2));
        });
    }

    [Test]
    public void Rank_GpuSeparatesPidLifetimes_AndHonorsHistoricalEndAndGaps()
    {
        MonitorSample[] history =
        [
            Sample(1, 10, new ProcessUsage(7, 1, "old", 0) with { GpuPercent = 80 }),
            Sample(2, 10, new ProcessUsage(7, 2, "new", 0) with { GpuPercent = 20 }),
            Sample(3, 10, new ProcessUsage(7, 2, "new", 0) with { GpuPercent = 100 })
        ];
        var selected = ProcessRanking.Rank([.. history.Take(2)], sort: ProcessSort.Gpu);
        Assert.Multiple(() =>
        {
            Assert.That(selected.Processes.Select(p => p.GpuPercent), Is.EqualTo(new double?[] { 80, 20 }));
            Assert.That(ProcessRanking.Rank([history[2] with { IsGap = true }], sort: ProcessSort.Gpu).Processes,
                Is.Empty);
            Assert.That(ProcessRanking.Rank([Sample(1, 10, Usage(1, 10))], sort: ProcessSort.Gpu).Processes, Is.Empty);
        });
    }

    [Test]
    public void Rank_SeparatesReusedProcessIdsByCreationTime()
    {
        MonitorSample[] history =
        [
            Sample(1, 10, new ProcessUsage(7, 1, "old", 3)),
            Sample(2, 10, new ProcessUsage(7, 2, "new", 1))
        ];

        var ranking = ProcessRanking.Rank(history);

        Assert.That(ranking.Processes.Select(p => (p.Name, p.CpuPercent)),
            Is.EqualTo([("old", 1.5), ("new", 0.5)]));
    }

    [Test]
    public void Rank_MemoryUsesLatestWorkingSetOfProcessesStillPresent()
    {
        // p1 was largest but exited before the newest sample; p3 has no readable memory.
        MonitorSample[] history =
        [
            Sample(1, 10, Usage(1, 0, 900), Usage(2, 0, 100), Usage(3, 0)),
            Sample(2, 10, Usage(2, 0, 300), Usage(3, 0), Usage(4, 0, 200))
        ];

        var ranking = ProcessRanking.Rank(history, sort: ProcessSort.Memory);

        Assert.That(ranking.Processes.Select(p => (p.ProcessId, p.WorkingSetBytes)),
            Is.EqualTo(new (int, ulong?)[] { (2, 300), (4, 200) }));
    }

    [Test]
    public void Rank_IoAveragesOverTheWindowAndIncludesCpuIdleProcesses()
    {
        MonitorSample[] history =
        [
            Sample(1, 10, Usage(1, 0, io: 1_000), Usage(2, 5, io: 0)),
            Sample(2, 10, Usage(1, 0, io: null), Usage(2, 5, io: 0)),
            Sample(3, 10, Usage(1, 0, io: 3_000), Usage(2, 5, io: 500))
        ];

        var ranking = ProcessRanking.Rank(history, sort: ProcessSort.Io);

        Assert.That(ranking.Processes.Select(p => (p.ProcessId, p.IoBytesPerSecond)),
            Is.EqualTo(new (int, double?)[] { (1, 4_000 / 3d), (2, 500 / 3d) }));
    }

    [Test]
    public void Rank_MeasuredCpuCoversEveryProcess_WhateverTheSortOrGrouping()
    {
        MonitorSample[] history =
        [
            Sample(1, 20, Usage(1, 6, 100), Usage(2, 0, io: 50), Usage(3, 3) with { GpuPercent = 10 }),
            Sample(2, 20, Usage(1, 2, 100), Usage(2, 4, io: 0), Usage(3, 0) with { GpuPercent = 0 })
        ];
        var cpuSum = ProcessRanking.Rank(history).Processes.Sum(p => p.CpuPercent);

        Assert.Multiple(() =>
        {
            Assert.That(cpuSum, Is.EqualTo(7.5).Within(1e-9));
            foreach (var sort in Enum.GetValues<ProcessSort>())
            foreach (var grouped in new[] { false, true })
                Assert.That(ProcessRanking.Rank(history, sort: sort, grouped: grouped).MeasuredCpuPercent,
                    Is.EqualTo(cpuSum).Within(1e-9),
                    $"{sort}, grouped {grouped}");
            Assert.That(ProcessRanking.Rank([]).MeasuredCpuPercent, Is.Zero);
        });
    }

    [Test]
    public void Rank_ConsecutiveCallsShareNoState()
    {
        MonitorSample[] busy = [Sample(1, 10, Usage(1, 5), Usage(2, 3))];
        MonitorSample[] quiet = [Sample(1, 10, Usage(3, 1))];

        var first = ProcessRanking.Rank(busy);
        var second = ProcessRanking.Rank(quiet);

        Assert.Multiple(() =>
        {
            Assert.That(first.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2]));
            Assert.That(second.Processes.Select(p => p.ProcessId), Is.EqualTo([3]));
            Assert.That(ProcessRanking.Rank(busy).Processes, Is.EqualTo(first.Processes));
        });
    }

    private static IEnumerable<TestCaseData> SortModes()
    {
        foreach (var sort in Enum.GetValues<ProcessSort>())
        foreach (var grouped in new[] { false, true })
        foreach (var includeInactive in new[] { false, true })
            yield return new TestCaseData(sort, grouped, includeInactive);
    }

    [TestCaseSource(nameof(SortModes))]
    public void Rank_SortTiesKeepMissingZeroAndUnavailableReadingsDistinct(ProcessSort sort, bool grouped, bool includeInactive)
    {
        var first = Usage(1, 1, 20, 2) with
            { ImagePath = @"C:\one.exe", PrivateBytes = 20, GpuPercent = 2, ThreadCount = 0, HandleCount = 0 };
        var second = first with { ProcessId = 2, CreationTicks = 20, ImagePath = @"C:\two.exe" };
        var missing = Usage(3, 99) with { ImagePath = @"C:\missing.exe", CpuAvailable = false };
        var zero = Usage(4, 0, 0, 0) with
            { ImagePath = @"C:\zero.exe", PrivateBytes = 0, GpuPercent = 0, ThreadCount = 0, HandleCount = 0 };
        var history = new[] { Sample(1, 40, second, missing, zero, first) };
        var result = ProcessRanking.Rank(history, sort: sort, grouped: grouped, includeInactive: includeInactive);
        int[] expected = includeInactive
            ? sort == ProcessSort.Cpu ? [1, 2, 4, 3] : [1, 2, 3, 4]
            : sort is ProcessSort.Threads or ProcessSort.Handles ? [1, 2, 4] : [1, 2];
        Assert.That(result.Processes.Select(p => p.ProcessId), Is.EqualTo(expected));
        Assert.That(result.MeasuredCpuPercent, Is.EqualTo(2));
        Assert.That(result.TotalCpuPercent, Is.EqualTo(40));
        Assert.That(result.IntervalCount, Is.EqualTo(1));
        if (includeInactive)
        {
            Assert.That(result.Processes.Single(p => p.ProcessId == 3).ObservedCpuPercent, Is.Null);
            Assert.That(result.Processes.Single(p => p.ProcessId == 4).ObservedCpuPercent, Is.Zero);
        }
    }

    [Test]
    public void Rank_LatestMetadataKeepsLastReadableGpuAndMetricSpecificDenominators()
    {
        var older = Usage(1, 10, 100, 20) with { Name = "old", GpuPercent = 40, GpuId = "gpu-old", GpuEngine = "engine-old" };
        var latest = older with { Name = "new", CpuPercent = 900, CpuAvailable = false, WorkingSetBytes = 300,
            IoBytesPerSecond = null, GpuPercent = null, GpuId = "gpu-new", GpuEngine = "engine-new" };
        var exited = Usage(2, 6, 200, 8) with { GpuPercent = 20 };
        MonitorSample[] history = [Sample(1, 40, older, exited), Sample(2, 60, latest)];
        var result = ProcessRanking.Rank(history, sort: ProcessSort.Gpu);
        var row = result.Processes[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2]));
            Assert.That(row.Name, Is.EqualTo("new"));
            Assert.That(row.WorkingSetBytes, Is.EqualTo(300));
            Assert.That(row.ObservedCpuPercent, Is.EqualTo(10));
            Assert.That(row.IoBytesPerSecond, Is.EqualTo(10));
            Assert.That(row.GpuPercent, Is.EqualTo(40));
            Assert.That(row.GpuId, Is.EqualTo("gpu-old"));
            Assert.That(row.GpuEngine, Is.EqualTo("engine-old"));
            Assert.That(result.TotalCpuPercent, Is.EqualTo(50));
            Assert.That(result.MeasuredCpuPercent, Is.EqualTo(13));
            Assert.That(ProcessRanking.Rank(history, sort: ProcessSort.Memory).Processes.Select(p => p.ProcessId), Is.EqualTo([1]));
        });
    }

    [Test]
    public void Rank_PolicySwitchesDoNotReuseKeysOrMutateReturnedGroups()
    {
        var first = Usage(1, 3) with { ImagePath = @"C:\App.exe" };
        var second = Usage(2, 4) with { ImagePath = @"C:\app.exe" };
        var history = new[] { Sample(1, 20, first, second) };
        var windows = ProcessRanking.Rank(history, grouped: true);
        var exact = ProcessRanking.Rank(history, grouped: true, identityPolicy: ProcessIdentityPolicy.ExactPaths);
        var mergedPolicy = new ProcessIdentityPolicy("merge", StringComparer.Ordinal, _ => "all");
        var merged = ProcessRanking.Rank(history, grouped: true, identityPolicy: mergedPolicy);
        var exactAgain = ProcessRanking.Rank(history, grouped: true, identityPolicy: ProcessIdentityPolicy.ExactPaths);
        Assert.Multiple(() =>
        {
            Assert.That(windows.Processes.Single().CpuPercent, Is.EqualTo(7));
            Assert.That(windows.Processes.Single().MemberIds, Is.EqualTo([1, 2]));
            Assert.That(exact.Processes.Select(p => p.ProcessId), Is.EqualTo([2, 1]));
            Assert.That(merged.Processes.Single().MemberIds, Is.EqualTo([1, 2]));
            Assert.That(exactAgain.Processes.Select(p => p.ProcessId), Is.EqualTo([2, 1]));
            Assert.That(ProcessRanking.Rank(history, grouped: true).Processes.Single().MemberIds, Is.EqualTo([1, 2]));
        });
    }

    [TestCase("accumulation")]
    [TestCase("key")]
    [TestCase("overflow")]
    public void Rank_FailureClearsScratchBeforeTheNextCall(string failure)
    {
        var first = Usage(1, 3, ulong.MaxValue) with { ImagePath = @"C:\shared.exe" };
        var second = Usage(2, 4, 1) with { ImagePath = first.ImagePath };
        var visits = 0;
        var policy = new ProcessIdentityPolicy("throwing", StringComparer.Ordinal, process =>
            ++visits == 2 ? throw new InvalidOperationException("fixture") : process.ImagePath);
        if (failure == "accumulation")
            Assert.That(() => ProcessRanking.Rank([Sample(1, 20, null!), Sample(2, 20, first)], grouped: true),
                Throws.TypeOf<NullReferenceException>());
        else if (failure == "key")
            Assert.That(() => ProcessRanking.Rank([Sample(1, 20, first, second)], grouped: true, identityPolicy: policy),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("fixture"));
        else
            Assert.That(() => ProcessRanking.Rank([Sample(1, 20, first, second)], grouped: true),
                Throws.TypeOf<OverflowException>());
        var recovered = ProcessRanking.Rank([Sample(1, 20, Usage(99, 3))], grouped: true);
        ProcessRanking.Rank([Sample(1, 20, Usage(100, 4))], grouped: true);
        Assert.That(recovered.Processes.Single().ProcessId, Is.EqualTo(99));
        Assert.That(recovered.Processes.Single().MemberIds, Is.EqualTo([99]));
        Assert.That(recovered.MeasuredCpuPercent, Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Rank_ArgumentValidationPrecedesWindowConstruction(int seconds)
    {
        Assert.That(() => ProcessRanking.Rank(null!, seconds), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("history"));
        Assert.That(() => ProcessRanking.Rank([], seconds),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("windowSeconds"));
    }

    [Test]
    public void Rank_OverflowedWindowCapacityPreservesTheExistingException()
    {
        Assert.That(() => ProcessRanking.Rank([], int.MaxValue),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("capacity"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Rank_UnknownSortKeepsTheCpuFallback(bool grouped)
    {
        var result = ProcessRanking.Rank([Sample(1, 20, Usage(2, 4), Usage(1, 4), Usage(3, 0))],
            sort: (ProcessSort)999, grouped: grouped);
        Assert.That(result.Processes.Select(p => p.ProcessId), Is.EqualTo([1, 2]));
        Assert.That(result.MeasuredCpuPercent, Is.EqualTo(8));
    }

    [Test]
    public void Rank_UngroupedDoesNotInvokeTheExecutablePolicy()
    {
        var policy = new ProcessIdentityPolicy("unused", StringComparer.Ordinal,
            _ => throw new InvalidOperationException("must not run"));
        var result = ProcessRanking.Rank([Sample(1, 20, Usage(1, 3))], identityPolicy: policy);
        Assert.That(result.Processes.Single().ProcessId, Is.EqualTo(1));
    }

    [Test]
    public async Task Rank_ThreadScratchIsIsolatedDuringConcurrentGrouping()
    {
        using var rendezvous = new Barrier(2);
        Task<int[]> Run(int id) => Task.Factory.StartNew(() =>
        {
            var first = true;
            var policy = new ProcessIdentityPolicy("thread", StringComparer.Ordinal, process =>
            {
                if (first)
                {
                    first = false;
                    Assert.That(rendezvous.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
                }
                return process.Name;
            });
            return ProcessRanking.Rank([Sample(1, 20, Usage(id, 3))], grouped: true, identityPolicy: policy)
                .Processes.Select(p => p.ProcessId).ToArray();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var results = await Task.WhenAll(Run(10), Run(20));
        Assert.That(results[0], Is.EqualTo([10]));
        Assert.That(results[1], Is.EqualTo([20]));
    }
}
