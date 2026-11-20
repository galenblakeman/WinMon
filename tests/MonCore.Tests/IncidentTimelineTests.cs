using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class IncidentTimelineTests
{
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0,
            MonotonicTimestamp: (second + 1) * Stopwatch.Frequency);

    [Test]
    public void Compare_UsesThreeDisjointTenSecondWindowsAndRanksAbsoluteChanges()
    {
        var samples = Enumerable.Range(0, 31).Select(second => Sample(second,
            new ProcessUsage(1, 1, "increase", second < 10 ? 10 : second < 20 ? 70 : 20),
            new ProcessUsage(2, 2, "decrease", second < 10 ? 90 : 10)) with
            { TotalCpuPercent = second < 10 ? 20 : second < 20 ? 80 : 30 }).ToArray();
        var result = IncidentTimeline.Compare(samples, samples[15], CaptureMetric.Cpu);
        Assert.That(result.Before, Is.EqualTo(new IncidentTimeline.Reading(20, 10)));
        Assert.That(result.During, Is.EqualTo(new IncidentTimeline.Reading(80, 10)));
        Assert.That(result.After, Is.EqualTo(new IncidentTimeline.Reading(30, 10)));
        Assert.That(result.Processes.Select(row => row.Process.Name), Is.EqualTo(new[] { "decrease", "increase" }));
        Assert.That(result.Processes.Select(row => row.Change), Is.EqualTo(new[] { -80d, 60d }));
        Assert.That(result.Processes[1].After.Average, Is.EqualTo(20));
    }

    [Test]
    public void Compare_ExcludesGapsAndNonFiniteValuesAndKeepsMissingPeriodsUnknown()
    {
        var selected = Sample(15, new ProcessUsage(1, 1, "unknown", 999) { CpuAvailable = false }) with { TotalCpuPercent = null };
        MonitorSample[] samples = [Sample(0) with { TotalCpuPercent = 40 },
            Sample(1) with { IsGap = true, TotalCpuPercent = 100 },
            Sample(2) with { TotalCpuPercent = double.NaN },
            Sample(3) with { TotalCpuPercent = double.PositiveInfinity }, selected];
        var result = IncidentTimeline.Compare(samples, selected, CaptureMetric.Cpu);
        Assert.That(result.Before, Is.EqualTo(new IncidentTimeline.Reading(40, 1)));
        Assert.That(result.During, Is.EqualTo(new IncidentTimeline.Reading(null, 0)));
        Assert.That(result.After, Is.EqualTo(new IncidentTimeline.Reading(null, 0)));
        Assert.That(result.Processes[0].During.Average, Is.Null);
        Assert.That(result.Processes[0].Change, Is.Null);
        Assert.That(IncidentTimeline.Compare([], selected, CaptureMetric.Cpu).Processes, Is.Empty);
    }

    [Test]
    public void Compare_DoesNotMergeReusedPidsOrInventZeroForMissingCompactedProcesses()
    {
        var before = Sample(0, new ProcessUsage(1, 1, "old", 40));
        var selected = Sample(15, new ProcessUsage(1, 2, "new", 80));
        var after = Sample(20) with { CompactedProcesses = 1 };
        var result = IncidentTimeline.Compare([before, selected, after], selected, CaptureMetric.Cpu);
        Assert.That(result.Processes, Has.Count.EqualTo(2));
        Assert.That(result.Processes.All(row => row.Change is null && row.After.Average is null), Is.True);
        Assert.That(result.Processes.Single(row => row.Process.Name == "old").During.Average, Is.Null);
        Assert.That(result.Processes.Single(row => row.Process.Name == "new").Before.Average, Is.Null);
    }

    [TestCase(CaptureMetric.Memory, 500)]
    [TestCase(CaptureMetric.PrimaryDisk, 300)]
    [TestCase(CaptureMetric.SecondaryDisk, 300)]
    [TestCase(CaptureMetric.Gpu, 20)]
    [TestCase(CaptureMetric.Network, 10)]
    public void Compare_UsesSelectedProcessMetricAndExistingNetworkCpuFallback(CaptureMetric metric, double expected)
    {
        var selected = Sample(15, new ProcessUsage(1, 1, "app", 10, 500, 300) { GpuPercent = 20 });
        var result = IncidentTimeline.Compare([selected], selected, metric);
        Assert.That(result.Processes[0].During.Average, Is.EqualTo(expected));
        Assert.That(result.Processes[0].During.Observations, Is.EqualTo(1));
    }

    [Test]
    public void Compare_UsesMonotonicWindowsAcrossWallClockChangesAndFallsBackForFixtures()
    {
        var selected = Sample(15) with { TotalCpuPercent = 80 };
        var before = Sample(0) with { Timestamp = selected.Timestamp.AddHours(1), TotalCpuPercent = 20 };
        var result = IncidentTimeline.Compare([before, selected], selected, CaptureMetric.Cpu);
        Assert.That(result.Before.Average, Is.EqualTo(20));
        Assert.That(result.During.Average, Is.EqualTo(80));
        var wallTime = selected with { MonotonicTimestamp = 0 };
        Assert.That(IncidentTimeline.Compare([wallTime], wallTime, CaptureMetric.Cpu).During.Average, Is.EqualTo(80));
    }

    [Test]
    public void Compare_LimitsRowsAndSeparatesClockDomainsAndUnknownLifetimes()
    {
        var owners = Enumerable.Range(1, 12).Select(pid => new ProcessUsage(pid, pid, "app", pid)).ToArray();
        var before = Sample(0, owners);
        var selected = Sample(15, owners.Select(p => p with { CpuPercent = p.CpuPercent * 2 }).ToArray());
        var result = IncidentTimeline.Compare([before, selected], selected, CaptureMetric.Cpu);
        Assert.That(result.Processes.Select(row => row.Process.ProcessId), Is.EqualTo(Enumerable.Range(3, 10).Reverse()));
        var otherDomain = owners[0] with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        selected = Sample(15, otherDomain, new ProcessUsage(13, 0, "unknown", 100));
        result = IncidentTimeline.Compare([Sample(0, owners[0]), selected], selected, CaptureMetric.Cpu);
        Assert.That(result.Processes, Has.Count.EqualTo(2));
        Assert.That(result.Processes.All(row => row.Change is null), Is.True);
    }

    [Test]
    public void Peak_IgnoresGapsAndUnavailableValuesAndSelectsLatestTie()
    {
        var first = Sample(0) with { TotalCpuPercent = 90 };
        var latest = Sample(2) with { TotalCpuPercent = 90 };
        MonitorSample[] samples = [first, Sample(1) with { IsGap = true, TotalCpuPercent = 100 }, latest,
            Sample(3) with { TotalCpuPercent = null }, Sample(4) with { TotalCpuPercent = double.NaN }];
        Assert.That(IncidentTimeline.Peak(samples, CaptureMetric.Cpu), Is.SameAs(latest));
        Assert.That(IncidentTimeline.Peak(samples, CaptureMetric.Gpu), Is.Null);
        Assert.That(IncidentTimeline.Peak([], CaptureMetric.Cpu), Is.Null);
    }

    [Test]
    public void Peak_UsesSelectedResourceInsteadOfCpu()
    {
        var cpu = Sample(0) with { TotalCpuPercent = 99, Resources = new ResourceSample { DiskActivePercent = 1 } };
        var disk = Sample(1) with { Resources = new ResourceSample { DiskActivePercent = 98 } };
        Assert.That(IncidentTimeline.Peak([cpu, disk], CaptureMetric.PrimaryDisk), Is.SameAs(disk));
    }

    [TestCase(CaptureMetric.Cpu, 1)]
    [TestCase(CaptureMetric.Memory, 2)]
    [TestCase(CaptureMetric.PrimaryDisk, 3)]
    [TestCase(CaptureMetric.SecondaryDisk, 3)]
    [TestCase(CaptureMetric.Gpu, 4)]
    [TestCase(CaptureMetric.Network, 1)]
    public void Contributors_RanksSelectedResourceWithUnknownValuesLast(CaptureMetric metric, int expected)
    {
        var sample = Sample(0, new(1, 1, "cpu", 80), new(2, 2, "memory", 1, 900),
            new(3, 3, "io", 2, IoBytesPerSecond: 1000), new(4, 4, "gpu", 3) { GpuPercent = 90 },
            new(5, 5, "unknown", 100) { CpuAvailable = false });
        var result = IncidentTimeline.Contributors(sample, metric);
        Assert.That(result[0].ProcessId, Is.EqualTo(expected));
        Assert.That(result[^1].ProcessId, Is.EqualTo(5));
        Assert.That(sample.Processes[0].ProcessId, Is.EqualTo(1));
    }

    [Test]
    public void Changes_PidReuseIsAStopAndStartWhileSurvivingLifetimesAreExcluded()
    {
        ProcessUsage old = new(1, 1, "old", 0);
        ProcessUsage survivor = new(2, 2, "survivor", 0);
        ProcessUsage replacement = new(1, 3, "new", 0);
        var result = IncidentTimeline.Changes(Sample(0, old, survivor), Sample(1, replacement, survivor));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Started, Is.EqualTo(new[] { replacement }));
        Assert.That(result.Value.Stopped, Is.EqualTo(new[] { old }));
    }

    [TestCase("first")]
    [TestCase("before-gap")]
    [TestCase("after-gap")]
    [TestCase("before-compacted")]
    [TestCase("after-compacted")]
    [TestCase("before-unavailable")]
    [TestCase("after-unavailable")]
    [TestCase("unknown-lifetime")]
    [TestCase("elapsed-gap")]
    [TestCase("reverse-time")]
    public void Changes_IncompleteOrDisconnectedObservationsDoNotInventEvents(string condition)
    {
        MonitorSample? before = Sample(0, new ProcessUsage(1, 1, "old", 0));
        var after = Sample(1, new ProcessUsage(2, 2, "new", 0));
        switch (condition)
        {
            case "first": before = null; break;
            case "before-gap": before = before with { IsGap = true }; break;
            case "after-gap": after = after with { IsGap = true }; break;
            case "before-compacted": before = before with { CompactedProcesses = 1 }; break;
            case "after-compacted": after = after with { CompactedProcesses = 1 }; break;
            case "before-unavailable": before = before with { UnavailableProcesses = 1 }; break;
            case "after-unavailable": after = after with { UnavailableProcesses = 1 }; break;
            case "unknown-lifetime": after = Sample(1, new ProcessUsage(2, 0, "unknown", 0)); break;
            case "elapsed-gap": after = Sample(6); break;
            case "reverse-time": after = Sample(0); break;
        }
        Assert.That(IncidentTimeline.Changes(before, after), Is.Null);
    }

    [Test]
    public void CaptureChanges_UsesFullInventoriesAndFreezesEventsAfterUsageExpires()
    {
        var usage = new UsageHistory(2);
        ProcessUsage idle = new(1, 1, "idle", 0);
        ProcessUsage added = new(2, 2, "new", 1);
        var before = Sample(0, idle);
        var after = Sample(1, added);
        usage.Add(before);
        usage.Add(after);
        var compacted = before with { Processes = [], CompactedProcesses = 1 };
        var changes = IncidentTimeline.CaptureChanges([compacted, after], usage);
        usage.Add(Sample(10));
        var result = changes[SampleKey.Of(after)];
        Assert.That(result.Started.Select(p => p.Identity), Is.EqualTo(new[] { added.Identity }));
        Assert.That(result.Stopped.Select(p => p.Name), Is.EqualTo(new[] { "idle" }));
    }

    [TestCase("missing")]
    [TestCase("invalid")]
    [TestCase("inaccessible")]
    [TestCase("gap")]
    public void CaptureChanges_DoesNotTurnMissingOrIncompleteInventoriesIntoEvents(string condition)
    {
        var usage = new UsageHistory();
        var before = Sample(0, new ProcessUsage(1, 1, "old", 0));
        var after = Sample(1, new ProcessUsage(2, 2, "new", 0));
        if (condition == "invalid") before = before with { TotalCpuPercent = null };
        if (condition == "inaccessible") after = after with { UnavailableProcesses = 1 };
        if (condition == "gap") after = after with { IsGap = true };
        if (condition != "missing") usage.Add(before);
        usage.Add(after);
        Assert.That(IncidentTimeline.CaptureChanges([before, after], usage), Is.Empty);
    }

    [Test]
    public void Changes_EqualPidAndTokenInDifferentClockDomainsAreDifferentLifetimes()
    {
        ProcessUsage windows = new(1, 100, "windows", 0);
        var unix = windows with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var result = IncidentTimeline.Changes(Sample(0, windows), Sample(1, unix));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Started, Is.EqualTo(new[] { unix }));
        Assert.That(result.Value.Stopped, Is.EqualTo(new[] { windows }));
    }

    [Test]
    public void Changes_FiveSecondBoundaryAndEmptyCompleteObservationAreValid()
    {
        ProcessUsage process = new(1, 1, "old", 0);
        var result = IncidentTimeline.Changes(Sample(0, process), Sample(5));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Value.Stopped, Is.EqualTo(new[] { process }));
        Assert.That(result.Value.Started, Is.Empty);
    }
}
