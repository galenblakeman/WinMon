using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ProcessGroupingTests
{
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, processes, 0, false, second * Stopwatch.Frequency);

    private static ProcessUsage Usage(int id, double cpu) =>
        new(id, id * 10, "worker" + id, cpu) { ImagePath = @"C:\worker.exe" };

    private static ProcessUsage Group(params MonitorSample[] history) =>
        ProcessRanking.Rank(history, grouped: true, includeInactive: true).Processes.Single(p => p.ImagePath == @"C:\worker.exe");

    [Test]
    public void Group_HistoricalCpuRepresentativeKeepsCurrentTotalsAndIndependentGpuRepresentative()
    {
        var first = Usage(1, 5) with { WorkingSetBytes = 100, PrivateBytes = 40, ThreadCount = 3, HandleCount = 10,
            GpuPercent = 80, GpuId = "first", GpuEngine = "first-engine", IoBytesPerSecond = 10 };
        var second = Usage(2, 5) with { PrivateBytes = 50, ThreadCount = 4, HandleCount = 20,
            GpuPercent = 80, GpuId = "second", GpuEngine = "second-engine" };
        var exited = Usage(3, 20) with { WorkingSetBytes = 900, PrivateBytes = 900, ThreadCount = 900, HandleCount = 900,
            GpuPercent = 1, IoBytesPerSecond = 20, AppName = "exited-app" };
        var result = Group(Sample(1, first, second, exited), Sample(2, second, first));
        Assert.Multiple(() =>
        {
            Assert.That(result.ProcessId, Is.EqualTo(3));
            Assert.That(result.Name, Is.EqualTo("worker3"));
            Assert.That(result.AppName, Is.EqualTo("exited-app"));
            Assert.That(result.CpuPercent, Is.EqualTo(20));
            Assert.That(result.ObservedCpuPercent, Is.EqualTo(20));
            Assert.That(result.IoBytesPerSecond, Is.EqualTo(20));
            Assert.That(result.GpuPercent, Is.EqualTo(80));
            Assert.That(result.GpuId, Is.EqualTo("second"));
            Assert.That(result.GpuEngine, Is.EqualTo("second-engine"));
            Assert.That(result.WorkingSetBytes, Is.EqualTo(100));
            Assert.That(result.PrivateBytes, Is.EqualTo(90));
            Assert.That(result.ThreadCount, Is.EqualTo(7));
            Assert.That(result.HandleCount, Is.EqualTo(30));
            Assert.That(result.MemberCount, Is.EqualTo(3));
            Assert.That(result.MemberIds, Is.EqualTo([1, 2, 3]));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Group_EqualCpuAndGpuKeepTheFirstObservedRepresentative(bool reverse)
    {
        var first = Usage(1, 5) with { GpuPercent = 20, GpuId = "gpu1" };
        var second = Usage(2, 5) with { GpuPercent = 20, GpuId = "gpu2" };
        var result = Group(reverse ? Sample(1, second, first) : Sample(1, first, second));
        Assert.That(result.ProcessId, Is.EqualTo(reverse ? 2 : 1));
        Assert.That(result.GpuId, Is.EqualTo(reverse ? "gpu2" : "gpu1"));
        Assert.That(result.MemberIds, Is.EqualTo([1, 2]));
    }

    private static IEnumerable<TestCaseData> WindowStates()
    {
        (bool? First, bool? Second, bool? Expected)[] states =
        [
            (null, null, null), (null, false, null), (false, null, null), (false, false, false),
            (true, null, true), (null, true, true), (true, false, true), (false, true, true), (true, true, true)
        ];
        foreach (var state in states)
        foreach (var current in new[] { false, true })
            yield return new TestCaseData(state.First, state.Second, current, state.Expected);
    }

    [TestCaseSource(nameof(WindowStates))]
    public void Group_AppWindowUsesCurrentMembersOrFallsBackToHistoricalMembers(bool? first, bool? second, bool current, bool? expected)
    {
        var a = Usage(1, 2) with { HasAppWindow = first };
        var b = Usage(2, 3) with { HasAppWindow = second };
        var opposite = Usage(3, 20) with { HasAppWindow = expected != true };
        var other = Usage(99, 1) with { ImagePath = @"C:\other.exe", HasAppWindow = expected != true };
        var result = current
            ? Group(Sample(1, opposite), Sample(2, a, b))
            : Group(Sample(1, a, b), Sample(2, other));
        Assert.That(result.HasAppWindow, Is.EqualTo(expected));
        Assert.That(result.MemberCount, Is.EqualTo(current ? 3 : 2));
    }

    [TestCase("missing")]
    [TestCase("zero")]
    [TestCase("mixed")]
    public void Group_NullAndZeroMetricsKeepTheirDistinctAggregationRules(string mode)
    {
        var a = Usage(1, 3);
        var b = Usage(2, 99) with { CpuAvailable = false };
        if (mode != "missing")
            a = a with { WorkingSetBytes = 0, PrivateBytes = 0, ThreadCount = 0, HandleCount = 0,
                IoBytesPerSecond = 0, GpuPercent = 0 };
        if (mode == "zero")
            b = b with { WorkingSetBytes = 0, PrivateBytes = 0, ThreadCount = 0, HandleCount = 0,
                IoBytesPerSecond = 0, GpuPercent = 0 };
        var result = Group(Sample(1, a, b));
        Assert.Multiple(() =>
        {
            Assert.That(result.CpuPercent, Is.EqualTo(3));
            Assert.That(result.ObservedCpuPercent, Is.Null);
            Assert.That(result.WorkingSetBytes, Is.EqualTo(mode == "missing" ? (ulong?)null : 0));
            Assert.That(result.PrivateBytes, Is.EqualTo(mode == "missing" ? (ulong?)null : 0));
            Assert.That(result.IoBytesPerSecond, Is.EqualTo(mode == "missing" ? (double?)null : 0));
            Assert.That(result.GpuPercent, Is.EqualTo(mode == "missing" ? (double?)null : 0));
            Assert.That(result.ThreadCount, Is.EqualTo(mode == "zero" ? (long?)0 : null));
            Assert.That(result.HandleCount, Is.EqualTo(mode == "zero" ? (long?)0 : null));
        });
    }

    [TestCase("workingSet", false)]
    [TestCase("workingSet", true)]
    [TestCase("private", false)]
    [TestCase("private", true)]
    [TestCase("threads", false)]
    [TestCase("threads", true)]
    [TestCase("handles", false)]
    [TestCase("handles", true)]
    public void Group_CheckedSumsStillOverflowAfterAnUnknownReadingAndRecover(string metric, bool unknownFirst)
    {
        var maximum = Usage(1, 2);
        var one = Usage(2, 3);
        switch (metric)
        {
            case "workingSet": maximum = maximum with { WorkingSetBytes = ulong.MaxValue }; one = one with { WorkingSetBytes = 1 }; break;
            case "private": maximum = maximum with { PrivateBytes = ulong.MaxValue }; one = one with { PrivateBytes = 1 }; break;
            case "threads": maximum = maximum with { ThreadCount = long.MaxValue }; one = one with { ThreadCount = 1 }; break;
            case "handles": maximum = maximum with { HandleCount = long.MaxValue }; one = one with { HandleCount = 1 }; break;
        }
        var sample = unknownFirst ? Sample(1, Usage(3, 1), maximum, one) : Sample(1, maximum, one);
        Assert.That(() => Group(sample), Throws.TypeOf<OverflowException>());
        var healthy = Group(Sample(2, Usage(99, 4)));
        Assert.That(healthy.ProcessId, Is.EqualTo(99));
        Assert.That(healthy.MemberIds, Is.EqualTo([99]));
    }

    [Test]
    public void Group_ExitedLifetimesKeepDuplicatePidMembershipButNoCurrentReadings()
    {
        var old = Usage(7, 8) with { CreationTicks = 1, WorkingSetBytes = 100, PrivateBytes = 200,
            ThreadCount = 3, HandleCount = 4, HasAppWindow = false };
        var reused = old with { CreationTicks = 2, CpuPercent = 4 };
        var other = Usage(99, 1) with { ImagePath = @"C:\other.exe" };
        var result = Group(Sample(1, reused, old), Sample(2, other));
        Assert.Multiple(() =>
        {
            Assert.That(result.CreationTicks, Is.EqualTo(1));
            Assert.That(result.CpuPercent, Is.EqualTo(6));
            Assert.That(result.MemberCount, Is.EqualTo(2));
            Assert.That(result.MemberIds, Is.EqualTo([7, 7]));
            Assert.That(result.WorkingSetBytes, Is.Null);
            Assert.That(result.PrivateBytes, Is.Null);
            Assert.That(result.ThreadCount, Is.Null);
            Assert.That(result.HandleCount, Is.Null);
            Assert.That(result.HasAppWindow, Is.False);
        });
    }
}
