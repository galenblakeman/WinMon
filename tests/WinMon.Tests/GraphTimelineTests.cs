using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class GraphTimelineTests
{
    [TestCase(0, 0.1)]
    [TestCase(0.04, 0.1)]
    [TestCase(0.4, 0.5)]
    [TestCase(3, 3.3)]
    [TestCase(100, 100)]
    public void ProcessScaleMaximum_VisiblePeak_AddsHeadroomWithSafeBounds(double peak, double expected)
    {
        var process = new ProcessUsage(42, 100, "example", peak);
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 80, [process], 0);

        Assert.That(CpuGraph.ProcessScaleMaximum([sample], 120, process), Is.EqualTo(expected).Within(0.000001));
    }

    [Test]
    public void ProcessScaleMaximum_IgnoresExpiredSamplesGapsAndOtherProcessLifetimes()
    {
        var process = new ProcessUsage(42, 100, "example", 0.4);
        MonitorSample[] history =
        [
            Sample(1, process with { CpuPercent = 90 }),
            Sample(119, process with { CreationTicks = 99, CpuPercent = 80 }),
            Sample(120, process with { CpuPercent = 70 }, true),
            Sample(121, process)
        ];

        Assert.That(CpuGraph.ProcessScaleMaximum(history, 10, process), Is.EqualTo(0.5));
        Assert.That(CpuGraph.ProcessScaleMaximum([], 10, process), Is.EqualTo(0.1));
        return;

        MonitorSample Sample(int second, ProcessUsage usage, bool gap = false) =>
            new(DateTimeOffset.UtcNow, 80, [usage], 0, gap, Stopwatch.Frequency * second);
    }

    [Test]
    public void ProcessScaleMaximum_HistoricalWindow_ExcludesLaterSpike()
    {
        var process = new ProcessUsage(42, 100, "example", 0.4);
        var first = new MonitorSample(DateTimeOffset.UtcNow, 20, [process], 0, false, Stopwatch.Frequency);
        var spike = first with { Processes = [process with { CpuPercent = 50 }], MonotonicTimestamp = Stopwatch.Frequency * 2 };

        Assert.That(CpuGraph.ProcessScaleMaximum([first], 120, process), Is.EqualTo(0.5));
        Assert.That(CpuGraph.ProcessScaleMaximum([first, spike], 120, process), Is.EqualTo(55));
    }

    [TestCase(-3600)]
    [TestCase(3600)]
    public void Moment_WallClockJumps_PreservesOneSecondTimelineDistance(int wallClockJumpSeconds)
    {
        var stamp = DateTimeOffset.Parse("2026-10-19T12:00:00Z");
        var first = new MonitorSample(stamp, 25, [], 0, false, Stopwatch.Frequency * 10);
        var next = new MonitorSample(stamp.AddSeconds(wallClockJumpSeconds), 50, [], 0, false, Stopwatch.Frequency * 11);

        Assert.That(CpuGraph.Moment(next) - CpuGraph.Moment(first), Is.EqualTo(1).Within(0.000001));
    }
}
