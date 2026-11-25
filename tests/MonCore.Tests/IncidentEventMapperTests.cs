using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class IncidentEventMapperTests
{
    private static MonitorSample Sample(int wallSecond, int monotonicSecond, bool gap = false, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(wallSecond), 10, processes, 0, gap,
            (monotonicSecond + 1L) * Stopwatch.Frequency);

    [Test]
    public void Map_InterpolatesWithinAContinuousWallClockInterval()
    {
        var result = Events(new IncidentEvent(DateTimeOffset.UnixEpoch.AddSeconds(5), "Display", 4101, IncidentEventLevel.Warning, ""));
        var mapped = IncidentEventMapper.Map(result, [Sample(0, 0), Sample(10, 10)]).Single();
        Assert.That(mapped.Moment, Is.EqualTo(6).Within(.001));
        Assert.That(mapped.ObservationIndex, Is.Null);
        Assert.That(mapped.Approximate, Is.False);
    }

    [Test]
    public void Map_UsesNearestNonGapObservationAndMarksClockJumpsApproximate()
    {
        var result = Events(new IncidentEvent(DateTimeOffset.UnixEpoch.AddSeconds(11), "disk", 129, IncidentEventLevel.Warning, ""));
        var mapped = IncidentEventMapper.Map(result, [Sample(10, 0, true), Sample(11, 1), Sample(9, 2)]).Single();
        Assert.That(mapped.ObservationIndex, Is.EqualTo(1));
        Assert.That(mapped.Approximate, Is.True);
    }

    [Test]
    public void Map_RepeatedWallClockIntervalsAreMarkedAmbiguous()
    {
        var eventItem = new IncidentEvent(DateTimeOffset.UnixEpoch.AddSeconds(1), "x", 1, IncidentEventLevel.Information, "");
        var mapped = IncidentEventMapper.Map(Events(eventItem),
            [Sample(0, 0), Sample(1, 1), Sample(2, 2), Sample(0, 3), Sample(1, 4), Sample(2, 5)]).Single();
        Assert.That(mapped.Approximate, Is.True);
    }

    [Test]
    public void Map_LinksOnlyTheExactWindowsLifetimeIncludingRetainedOwners()
    {
        var eventItem = new IncidentEvent(DateTimeOffset.UnixEpoch, "Application Error", 1000, IncidentEventLevel.Error, "", 7, 123);
        var wrongDomain = new ProcessUsage(7, 123, "wrong", 0) { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var retained = new ProcessUsage(7, 123, "right", 0);
        var mapped = IncidentEventMapper.Map(Events(eventItem), [Sample(0, 0, false, wrongDomain)], [retained]).Single();
        Assert.That(mapped.LinkedProcess, Is.SameAs(retained));
    }

    [Test]
    public void Map_EventOutsideRetainedRangeIsApproximateAndNotSelectable()
    {
        var mapped = IncidentEventMapper.Map(Events(new IncidentEvent(DateTimeOffset.UnixEpoch.AddSeconds(9), "x", 1, IncidentEventLevel.Information, "")),
            [Sample(0, 0), Sample(1, 1)]).Single();
        Assert.That(mapped.Approximate, Is.True);
        Assert.That(mapped.ObservationIndex, Is.Null);
    }

    [TestCase(10, 1)]
    [TestCase(13, 1)]
    [TestCase(14, null)]
    public void Map_SkipsTheClosestGapAndSelectsOnlyWithinTwoSeconds(int eventSecond, int? expectedIndex)
    {
        var item = new IncidentEvent(DateTimeOffset.UnixEpoch.AddSeconds(eventSecond), "x", 1, IncidentEventLevel.Information, "");
        var mapped = IncidentEventMapper.Map(Events(item), [Sample(10, 0, true), Sample(11, 1)]).Single();
        Assert.That(mapped.ObservationIndex, Is.EqualTo(expectedIndex));
    }

    [Test]
    public void Map_DoesNotLinkAReusedPidOrANameMatch()
    {
        var item = new IncidentEvent(DateTimeOffset.UnixEpoch, "Application Error", 1000, IncidentEventLevel.Error, "same", 7, 123);
        var mapped = IncidentEventMapper.Map(Events(item), [Sample(0, 0, false,
            new ProcessUsage(7, 124, "same", 0), new ProcessUsage(8, 123, "same", 0))]).Single();
        Assert.That(mapped.LinkedProcess, Is.Null);
    }

    private static IncidentEventResult Events(params IncidentEvent[] events) =>
        new(events, [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.Complete)], false);
}
