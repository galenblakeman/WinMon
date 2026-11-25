using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class IncidentEventMappingCharacterizationTests
{
    private static MonitorSample Sample(double wall, double moment, bool gap = false) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(wall), 1, [], 0, gap,
            (long)Math.Round(moment * Stopwatch.Frequency));

    private static IncidentEvent Event(double wall) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(wall), "provider", 123, IncidentEventLevel.Warning, "message");

    private static IncidentEventResult Events(params IncidentEvent[] events) => new(events, [], false);

    private static void AssertMapping(IncidentEventMapping actual, IncidentEvent item, double moment,
        int? selected, bool approximate, ProcessUsage? owner = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Event, Is.SameAs(item));
            Assert.That(actual.Moment, Is.EqualTo(moment).Within(1e-9));
            Assert.That(actual.ObservationIndex, Is.EqualTo(selected));
            Assert.That(actual.Approximate, Is.EqualTo(approximate));
            Assert.That(actual.LinkedProcess, Is.SameAs(owner));
        });
    }

    [Test]
    public void EmptySamplesKeepEventsWithoutSelectionOrInventedChartTime()
    {
        var item = Event(10);
        AssertMapping(IncidentEventMapper.Map(Events(item), []).Single(), item, 0, null, true);
        Assert.That(IncidentEventMapper.Map(Events(), []), Is.Empty);
    }

    [TestCase(7.999999, null)]
    [TestCase(8d, 0)]
    [TestCase(8.000001, 0)]
    [TestCase(11.999999, 0)]
    [TestCase(12d, 0)]
    [TestCase(12.000001, null)]
    public void SingleObservationSelectionIncludesExactlyTwoSecondsOnEitherSide(double wall, int? selected)
    {
        var item = Event(wall);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(10, 100)]).Single(), item, 100, selected, true);
    }

    [TestCase(false, false, 0, false)]
    [TestCase(true, false, 1, true)]
    [TestCase(false, true, 0, true)]
    [TestCase(true, true, null, true)]
    public void EquidistantObservationsKeepFirstNonGapSelectionAndGapApproximation(
        bool leftGap, bool rightGap, int? selected, bool approximate)
    {
        var item = Event(2);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10, leftGap), Sample(4, 14, rightGap)]).Single(),
            item, 12, selected, approximate);
    }

    [Test]
    public void SingleGapAndAllGapOutsideRangeFallBackWithoutSelection()
    {
        var item = Event(20);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10, true)]).Single(), item, 10, null, true);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10, true), Sample(4, 14, true)]).Single(),
            item, 10, null, true);
    }

    [TestCase(-1d, 10d, 0)]
    [TestCase(11d, 20d, 1)]
    [TestCase(20d, 20d, null)]
    public void OutsideIntervalsUseNearestObservationMomentRatherThanExtrapolating(double wall, double moment, int? selected)
    {
        var item = Event(wall);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10), Sample(10, 20)]).Single(),
            item, moment, selected, true);
    }

    [Test]
    public void RepeatedZeroDurationWallTimesUseTheFirstNearestMoment()
    {
        var item = Event(5);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(5, 10), Sample(5, 20)]).Single(),
            item, 10, 0, true);
    }

    [Test]
    public void ShortestContainingIntervalDeterminesPlacementEvenWhenAnotherIsCloserInSequence()
    {
        var item = Event(5);
        AssertMapping(IncidentEventMapper.Map(Events(item),
            [Sample(0, 10), Sample(10, 20), Sample(4, 30), Sample(6, 32)]).Single(), item, 31, 2, true);
    }

    [TestCase(10d, false)]
    [TestCase(30d, true)]
    public void EqualLengthContainingIntervalsKeepFirstPlacementAndCompareEveryCandidate(double lastMoment, bool approximate)
    {
        var item = Event(5);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10), Sample(10, 20), Sample(0, lastMoment)]).Single(),
            item, 15, null, approximate);
    }

    [TestCase(0.999999, true)]
    [TestCase(1d, false)]
    [TestCase(1.000001, false)]
    [TestCase(2.999999, false)]
    [TestCase(3d, false)]
    [TestCase(3.000001, true)]
    public void ClockDivergenceMustExceedOneSecondToMakePlacementApproximate(double duration, bool approximate)
    {
        var item = Event(1);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10), Sample(2, 10 + duration)]).Single(),
            item, 10 + duration / 2, 0, approximate);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BackwardsWallOrMonotonicIntervalsStillInterpolateButAreApproximate(bool backwardsWall)
    {
        var item = Event(5);
        var samples = backwardsWall ? new[] { Sample(10, 10), Sample(0, 20) } : [Sample(0, 20), Sample(10, 10)];
        AssertMapping(IncidentEventMapper.Map(Events(item), samples).Single(), item, 15, null, true);
    }

    [TestCase(999, false)]
    [TestCase(1000, false)]
    [TestCase(1001, true)]
    public void CompetingPlacementsNeedMoreThanOneMillisecondDifference(int differenceMicroseconds, bool approximate)
    {
        var item = Event(0);
        AssertMapping(IncidentEventMapper.Map(Events(item),
            [Sample(0, 1), Sample(1, 2), Sample(0, 1 + differenceMicroseconds / 1_000_000d)]).Single(),
            item, 1, 0, approximate);
    }

    [Test]
    public void EventOrderAndContainingIntervalStateRemainIndependentAcrossEvents()
    {
        var events = new[] { Event(25), Event(1), Event(100), Event(15) };
        var samples = new[] { Sample(0, 10), Sample(10, 20), Sample(20, 30), Sample(30, 40) };
        var result = IncidentEventMapper.Map(Events(events), samples);

        Assert.That(result, Has.Count.EqualTo(4));
        AssertMapping(result[0], events[0], 35, null, false);
        AssertMapping(result[1], events[1], 11, 0, false);
        AssertMapping(result[2], events[2], 40, null, true);
        AssertMapping(result[3], events[3], 25, null, false);
    }

    [TestCase(0d, 10d, 0)]
    [TestCase(10d, 20d, 1)]
    public void IntervalEndpointsAndUtcOffsetsKeepExactPlacement(double wall, double moment, int selected)
    {
        var item = Event(wall) with { Timestamp = Event(wall).Timestamp.ToOffset(TimeSpan.FromHours(-4)) };
        var first = Sample(0, 10) with { Timestamp = Sample(0, 10).Timestamp.ToOffset(TimeSpan.FromHours(2)) };
        AssertMapping(IncidentEventMapper.Map(Events(item), [first, Sample(10, 20)]).Single(),
            item, moment, selected, false);
    }

    [Test]
    public void WallOnlyObservationsUseUtcTicksAsChartMoments()
    {
        var item = Event(1);
        var samples = new[] { Sample(0, 10) with { MonotonicTimestamp = 0 }, Sample(2, 12) with { MonotonicTimestamp = 0 } };
        AssertMapping(IncidentEventMapper.Map(Events(item), samples).Single(),
            item, DateTimeOffset.UnixEpoch.UtcTicks / (double)TimeSpan.TicksPerSecond + 1, 0, false);
    }

    [Test]
    public void ZeroMonotonicDurationKeepsFlatPlacementAtTheClockDivergenceBoundary()
    {
        var item = Event(.5);
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10), Sample(1, 10)]).Single(),
            item, 10, 0, false);
    }

    [TestCase(1L)]
    [TestCase(long.MaxValue)]
    public void FirstAndLargestPositiveLifetimeTokensCanLinkExactOwners(long token)
    {
        var owner = new ProcessUsage(42, token, "owner", 0);
        var item = Event(0) with { ProcessId = 42, CreationTicks = token };
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10) with { Processes = [owner] }]).Single(),
            item, 10, 0, true, owner);
    }

    [TestCase(null, null)]
    [TestCase(42, null)]
    [TestCase(42, 0L)]
    [TestCase(42, -1L)]
    [TestCase(null, 100L)]
    public void IncompleteEventIdentityDoesNotReadUnavailableOwnerInventories(int? pid, long? token)
    {
        var item = Event(0) with { ProcessId = pid, CreationTicks = token };
        AssertMapping(IncidentEventMapper.Map(Events(item), [Sample(0, 10) with { Processes = null! }], [null!]).Single(),
            item, 10, 0, true);
    }

    [Test]
    public void FirstExactWindowsOwnerWinsBeforeLaterSamplesAndExtraMetadata()
    {
        var first = new ProcessUsage(42, 100, "first", 0);
        var second = first with { Name = "second" };
        var extra = first with { Name = "extra" };
        var foreign = first with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var item = Event(0) with { ProcessId = 42, CreationTicks = 100 };
        var samples = new[] { Sample(0, 10) with { Processes = [foreign, first] }, Sample(1, 11) with { Processes = [second] } };

        AssertMapping(IncidentEventMapper.Map(Events(item), samples, [extra]).Single(), item, 10, 0, false, first);
    }

    [TestCase(null, null)]
    [TestCase(42, null)]
    [TestCase(42, 0L)]
    [TestCase(42, -1L)]
    [TestCase(null, 100L)]
    public void IncompleteIdentityCannotBorrowMetadataWhenAnotherEventBuildsTheOwnerIndex(int? pid, long? token)
    {
        var owner = new ProcessUsage(42, 100, "owner", 0);
        var incomplete = Event(0) with { ProcessId = pid, CreationTicks = token };
        var valid = Event(0) with { ProcessId = 42, CreationTicks = 100 };
        var result = IncidentEventMapper.Map(Events(incomplete, valid), [Sample(0, 10) with { Processes = [owner] }]);

        Assert.That(result, Has.Count.EqualTo(2));
        AssertMapping(result[0], incomplete, 10, 0, true);
        AssertMapping(result[1], valid, 10, 0, true, owner);
    }

    [Test]
    public void NullArgumentGuardsKeepTheirOrderAndNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<ArgumentNullException>(() => IncidentEventMapper.Map(null!, null!))!.ParamName, Is.EqualTo("result"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => IncidentEventMapper.Map(Events(), null!))!.ParamName, Is.EqualTo("samples"));
        });
    }

    [Test]
    public void MalformedRequiredCollectionsAndItemsKeepExistingFailures()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentNullException>(() => IncidentEventMapper.Map(new(null!, [], false), []));
            Assert.Throws<NullReferenceException>(() => IncidentEventMapper.Map(Events(new IncidentEvent[] { null! }), []));
            Assert.Throws<NullReferenceException>(() => IncidentEventMapper.Map(Events(Event(0)), [null!]));
            Assert.Throws<NullReferenceException>(() => IncidentEventMapper.Map(
                Events(Event(0) with { ProcessId = 42, CreationTicks = 100 }), [Sample(0, 10) with { Processes = null! }]));
        });
    }
}
