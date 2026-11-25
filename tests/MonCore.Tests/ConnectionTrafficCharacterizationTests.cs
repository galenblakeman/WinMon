using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ConnectionTrafficCharacterizationTests
{
    private static ProcessNetworkReading Reading(long received, long sent, int pid = 42, long created = 1) =>
        new(pid, created, received, sent, double.NaN, null);

    private static ConnectionCapture Capture(int second, params ProcessNetworkReading[] readings) =>
        new(new(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0, false, second * Stopwatch.Frequency),
            null, [], new(second, 1, ProcessNetworkQuality.None, readings));

    private static void AssertResult(ConnectionTrafficTotals result, double covered, bool available, bool incomplete,
        params (int Pid, long Created, long Received, long Sent)[] readings)
    {
        var expected = readings.ToDictionary(r => new ProcessLifetimeIdentity(r.Pid, r.Created,
            ProcessLifetimeIdentity.WindowsFileTime), r => (r.Received, r.Sent));
        Assert.Multiple(() =>
        {
            Assert.That(result.Processes, Is.EquivalentTo(expected));
            Assert.That(result.CoveredSeconds, Is.EqualTo(covered).Within(1e-10));
            Assert.That(result.Available, Is.EqualTo(available));
            Assert.That(result.Incomplete, Is.EqualTo(incomplete));
        });
    }

    [TestCase("end")]
    [TestCase("network")]
    [TestCase("gap")]
    [TestCase("unavailable")]
    [TestCase("warming")]
    public void Build_UnavailableEndShortCircuitsMalformedHistory(string reason)
    {
        var end = Capture(10);
        end = reason switch
        {
            "end" => null!,
            "network" => end with { Network = null, Sample = null! },
            "gap" => end with { Sample = end.Sample with { IsGap = true } },
            _ => end with { Network = end.Network! with { Processes = null!, Quality = reason == "warming"
                ? ProcessNetworkQuality.WarmingUp : ProcessNetworkQuality.Unavailable } }
        };
        AssertResult(ConnectionTrafficTotals.Build(null!, end, 10), 0, false, true);
    }

    [TestCase(ProcessNetworkQuality.None, true, false)]
    [TestCase(ProcessNetworkQuality.WarmingUp, false, true)]
    [TestCase(ProcessNetworkQuality.Provisional, true, false)]
    [TestCase(ProcessNetworkQuality.Incomplete, true, true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, true, true)]
    [TestCase(ProcessNetworkQuality.Unavailable, false, true)]
    [TestCase(ProcessNetworkQuality.Discontinuity, true, true)]
    [TestCase((ProcessNetworkQuality)64, true, false)]
    [TestCase(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete, false, true)]
    public void Build_EndQualityPreservesAvailabilityAndCompleteness(ProcessNetworkQuality quality, bool available, bool incomplete)
    {
        var end = Capture(10, Reading(100, 200));
        end = end with { Network = end.Network! with { Quality = quality } };
        var result = ConnectionTrafficTotals.Build([], end, 0);
        AssertResult(result, 0, available, incomplete, available ? [(42, 1, 100, 200)] : []);
    }

    [TestCase(ProcessNetworkQuality.None, true, false)]
    [TestCase(ProcessNetworkQuality.WarmingUp, true, false)]
    [TestCase(ProcessNetworkQuality.Provisional, true, false)]
    [TestCase(ProcessNetworkQuality.Incomplete, true, true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, true, true)]
    [TestCase(ProcessNetworkQuality.Unavailable, false, true)]
    [TestCase(ProcessNetworkQuality.Discontinuity, true, true)]
    [TestCase((ProcessNetworkQuality)64, true, false)]
    public void Build_HistoricalQualityKeepsItsDistinctBaselineRules(ProcessNetworkQuality quality, bool used, bool incomplete)
    {
        var first = Capture(1, Reading(10, 20));
        first = first with { Network = first.Network! with { Quality = quality } };
        var end = Capture(2, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), used ? 1 : 0, true, incomplete,
            (42, 1, used ? 90 : 0, used ? 180 : 0));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Build_WindowLowerBoundaryIsInclusiveToTheQpcTick(int offset)
    {
        var first = Capture(10, Reading(10, 20));
        first = first with { Sample = first.Sample with { MonotonicTimestamp = 10 * Stopwatch.Frequency + offset } };
        var end = Capture(20, Reading(100, 200));
        var result = ConnectionTrafficTotals.Build([first], end, 10);
        if (offset < 0) AssertResult(result, 0, false, true);
        else AssertResult(result, 20 - first.Sample.MonotonicTimestamp / (double)Stopwatch.Frequency,
            true, false, (42, 1, 90, 180));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Build_WindowUpperBoundaryIsInclusiveToTheQpcTick(int offset)
    {
        var first = Capture(20, Reading(10, 20));
        first = first with { Sample = first.Sample with { MonotonicTimestamp = 20 * Stopwatch.Frequency + offset } };
        var end = Capture(20, Reading(100, 200));
        var result = ConnectionTrafficTotals.Build([first], end, 10);
        if (offset > 0) AssertResult(result, 0, false, true);
        else AssertResult(result, 20 - first.Sample.MonotonicTimestamp / (double)Stopwatch.Frequency,
            true, true, (42, 1, 90, 180));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Build_CoverageAllowsExactlyOneSecondOfShortfall(int offset)
    {
        var first = Capture(11, Reading(10, 20));
        first = first with { Sample = first.Sample with { MonotonicTimestamp = 11 * Stopwatch.Frequency - offset } };
        var end = Capture(20, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first], end, 10),
            20 - first.Sample.MonotonicTimestamp / (double)Stopwatch.Frequency, true, offset < 0, (42, 1, 90, 180));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Build_OnlyForwardSpacingAboveTwoSecondsMarksIncomplete(int offset)
    {
        var first = Capture(5);
        var next = Capture(7);
        next = next with { Sample = next.Sample with { MonotonicTimestamp = 7 * Stopwatch.Frequency + offset } };
        var end = Capture(8, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first, next], end, 0), 3, true, offset > 0, (42, 1, 100, 200));
        AssertResult(ConnectionTrafficTotals.Build([next, first], end, 0),
            8 - next.Sample.MonotonicTimestamp / (double)Stopwatch.Frequency, true, false, (42, 1, 100, 200));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Build_UsesPositiveQpcOtherwiseTruncatedWallClockMilliseconds(int clock)
    {
        var first = Capture(10, Reading(10, 20));
        var end = Capture(20, Reading(100, 200));
        first = first with { Sample = first.Sample with { Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(1000).AddTicks(9999),
            MonotonicTimestamp = clock > 0 ? first.Sample.MonotonicTimestamp : clock } };
        end = end with { Sample = end.Sample with { Timestamp = DateTimeOffset.UnixEpoch.AddMilliseconds(11000).AddTicks(1),
            MonotonicTimestamp = clock > 0 ? end.Sample.MonotonicTimestamp : clock } };
        AssertResult(ConnectionTrafficTotals.Build([first], end, 10), 10, true, false, (42, 1, 90, 180));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Build_FirstEligibleHistoryEntryWinsWithoutSorting(bool reverse)
    {
        var late = Capture(8, Reading(80, 160));
        var early = Capture(7, Reading(70, 140));
        var end = Capture(10, Reading(100, 200));
        ConnectionCapture[] history = reverse ? [early, late, Capture(9)] : [late, early, Capture(9)];
        AssertResult(ConnectionTrafficTotals.Build(history, end, 3), reverse ? 3 : 2, true, false,
            (42, 1, reverse ? 30 : 20, reverse ? 60 : 40));
    }

    [TestCase("gap")]
    [TestCase("network")]
    [TestCase("unavailable")]
    public void Build_UnusableHistoryIsSkippedBeforeReadingItsProcessCollection(string reason)
    {
        var missing = Capture(1);
        missing = missing with { Sample = missing.Sample with { IsGap = reason == "gap" },
            Network = reason == "network" ? null : missing.Network! with { Processes = null!,
                Quality = reason == "unavailable" ? ProcessNetworkQuality.Unavailable : ProcessNetworkQuality.None } };
        var first = Capture(2, Reading(20, 40));
        var end = Capture(3, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([missing, first, end], end, 2), 1, true, true, (42, 1, 80, 160));
        AssertResult(ConnectionTrafficTotals.Build([missing], end, 2), 0, false, true);
        AssertResult(ConnectionTrafficTotals.Build([missing], end, 0), 0, true, true, (42, 1, 100, 200));
    }

    [Test]
    public void Build_OutOfWindowQualityDoesNotContaminateTheSelectedInterval()
    {
        var old = Capture(8) with { Network = null };
        var future = Capture(12) with { Network = null };
        var first = Capture(9, Reading(10, 20));
        var end = Capture(10, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([old, first, end, future], end, 1), 1, true, false, (42, 1, 90, 180));
    }

    [TestCase("gap")]
    [TestCase("network")]
    [TestCase("unavailable")]
    [TestCase("incomplete")]
    [TestCase("capacity")]
    [TestCase("discontinuity")]
    public void Build_ScansLaterQualityAfterSelectingACompleteBaseline(string reason)
    {
        var first = Capture(1, Reading(10, 20));
        var middle = Capture(2);
        middle = middle with { Sample = middle.Sample with { IsGap = reason == "gap" },
            Network = reason == "network" ? null : middle.Network! with { Quality = reason switch
            {
                "unavailable" => ProcessNetworkQuality.Unavailable,
                "incomplete" => ProcessNetworkQuality.Incomplete,
                "capacity" => ProcessNetworkQuality.CapacityExceeded,
                "discontinuity" => ProcessNetworkQuality.Discontinuity,
                _ => ProcessNetworkQuality.None
            } } };
        var end = Capture(3, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first, middle, end], end, 2), 2, true, true, (42, 1, 90, 180));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void Build_NonpositiveIntervalsUseSessionCountersWithoutABaseline(int seconds)
    {
        var first = Capture(1, Reading(90, 190));
        var end = Capture(2, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, seconds), 1, true, false, (42, 1, 100, 200));
        AssertResult(ConnectionTrafficTotals.Build([], end, seconds), 0, true, false, (42, 1, 100, 200));
    }

    [TestCase("baseline-received")]
    [TestCase("baseline-sent")]
    [TestCase("end-received")]
    [TestCase("end-sent")]
    [TestCase("received-reset")]
    [TestCase("sent-reset")]
    public void Build_NegativeOrDecreasingCountersRejectTheWholeLifetime(string invalid)
    {
        var first = Capture(1, Reading(invalid == "baseline-received" ? -1 : 10, invalid == "baseline-sent" ? -1 : 20));
        var end = Capture(2, Reading(invalid == "end-received" ? -1 : invalid == "received-reset" ? 9 : 100,
            invalid == "end-sent" ? -1 : invalid == "sent-reset" ? 19 : 200));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, false, true);
    }

    [TestCase(0L, 0L, 0L, 0L)]
    [TestCase(0L, 0L, long.MaxValue, long.MaxValue)]
    [TestCase(long.MaxValue, long.MaxValue, long.MaxValue, long.MaxValue)]
    public void Build_ZeroAndMaximumCountersRemainExact(long firstReceived, long firstSent, long received, long sent)
    {
        var first = Capture(1, Reading(firstReceived, firstSent));
        var end = Capture(2, Reading(received, sent));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, true, false,
            (42, 1, received - firstReceived, sent - firstSent));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public void Build_FirstBaselineDuplicateWinsEvenWhenItIsInvalid(bool invalidFirst, bool invalidLast)
    {
        var first = Capture(1, Reading(invalidFirst ? -1 : 10, 20), Reading(invalidLast ? -1 : 70, 80));
        var end = Capture(2, Reading(100, 200));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, !invalidFirst, invalidFirst,
            invalidFirst ? [] : [(42, 1, 90, 180)]);
    }

    [TestCase("valid")]
    [TestCase("invalid-first")]
    [TestCase("invalid-last")]
    public void Build_FirstAcceptedEndDuplicateWinsButInvalidDuplicatesMarkIncomplete(string order)
    {
        var first = Capture(1, Reading(10, 20));
        var a = Reading(order == "invalid-first" ? -1 : 100, 200);
        var b = Reading(order == "invalid-last" ? -1 : 300, 400);
        var end = Capture(2, a, b);
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, true, order != "valid",
            (42, 1, order == "invalid-first" ? 290 : 90, order == "invalid-first" ? 380 : 180));
    }

    [Test]
    public void Build_NewAndReusedLifetimesStartAtZeroAndMissingLifetimesMarkIncomplete()
    {
        var first = Capture(1, Reading(100, 200), Reading(50, 60, 43));
        var end = Capture(2, Reading(7, 8, created: 2), Reading(9, 10, 44));
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, true, true,
            (42, 2, 7, 8), (44, 1, 9, 10));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Build_EmptyEndIsAvailableEvenWhenBaselineLifetimesAreMissing(bool populatedBaseline)
    {
        var first = populatedBaseline ? Capture(1, Reading(10, 20)) : Capture(1);
        var end = Capture(2);
        AssertResult(ConnectionTrafficTotals.Build([first, end], end, 1), 1, true, populatedBaseline);
    }

    [TestCase("history")]
    [TestCase("capture")]
    [TestCase("sample")]
    [TestCase("end-sample")]
    [TestCase("baseline-processes")]
    [TestCase("baseline-reading")]
    [TestCase("end-processes")]
    [TestCase("end-reading")]
    public void Build_MalformedRequiredInputsKeepTheirNullReferenceExceptions(string invalid)
    {
        var first = Capture(1, Reading(10, 20));
        var end = Capture(2, Reading(100, 200));
        first = invalid switch
        {
            "capture" => null!,
            "sample" => first with { Sample = null! },
            "baseline-processes" => first with { Network = first.Network! with { Processes = null! } },
            "baseline-reading" => first with { Network = first.Network! with { Processes = [null!] } },
            _ => first
        };
        end = invalid switch
        {
            "end-sample" => end with { Sample = null! },
            "end-processes" => end with { Network = end.Network! with { Processes = null! } },
            "end-reading" => end with { Network = end.Network! with { Processes = [null!] } },
            _ => end
        };
        Assert.That(() => ConnectionTrafficTotals.Build(invalid == "history" ? null! : [first], end, 1),
            Throws.TypeOf<NullReferenceException>());
    }

    [Test]
    public void Build_ResultDoesNotChangeWhenInputsOrLaterResultsChange()
    {
        var readings = new List<ProcessNetworkReading> { Reading(100, 200) };
        var end = Capture(2) with { Network = new(2, 1, ProcessNetworkQuality.None, readings) };
        var result = ConnectionTrafficTotals.Build([], end, 0);
        readings[0] = Reading(999, 999);
        var later = ConnectionTrafficTotals.Build([], end, 0);
        AssertResult(result, 0, true, false, (42, 1, 100, 200));
        AssertResult(later, 0, true, false, (42, 1, 999, 999));
        Assert.That(result.Processes, Is.Not.SameAs(later.Processes));
    }
}
