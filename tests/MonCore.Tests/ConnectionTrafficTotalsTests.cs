using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ConnectionTrafficTotalsTests
{
    private static ConnectionCapture Capture(int second, params ProcessNetworkReading[] readings) =>
        new(new(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0, false, second * Stopwatch.Frequency),
            null, [], new(second, 1, ProcessNetworkQuality.None, readings));

    private static ProcessNetworkReading Reading(long received, long sent, long created = 1) =>
        new(42, created, received, sent, 100, 50);

    [Test]
    public void IntervalUsesCounterDifferencesAndIgnoresFutureCapturesAndDuplicateLifetimes()
    {
        var start = Capture(1, Reading(100, 200));
        var end = Capture(61, Reading(400, 700), Reading(400, 700), Reading(20, 30, 2));
        var totals = ConnectionTrafficTotals.Build([start, end, Capture(62, Reading(9999, 9999))], end, 60);
        Assert.Multiple(() =>
        {
            Assert.That(totals.Sum(), Is.EqualTo((320L, 530L)));
            Assert.That(totals.CoveredSeconds, Is.EqualTo(60));
            Assert.That(totals.Available, Is.True);
            // Sparse history is explicitly incomplete even when the endpoints cover the requested interval.
            Assert.That(totals.Incomplete, Is.True);
            Assert.That(totals.Sum([new(42, 1, ProcessLifetimeIdentity.WindowsFileTime)]), Is.EqualTo((300L, 500L)));
        });
    }

    [Test]
    public void ShortHistoryAndUnavailableGapsRemainExplicitAndCounterResetsAreNotNegative()
    {
        var start = Capture(1, Reading(100, 200));
        var gap = Capture(2) with { Network = null };
        var end = Capture(3, Reading(20, 30));
        var totals = ConnectionTrafficTotals.Build([start, gap, end], end, 300);
        Assert.Multiple(() =>
        {
            Assert.That(totals.Sum(), Is.EqualTo((0L, 0L)));
            Assert.That(totals.Processes, Is.Empty);
            Assert.That(totals.Incomplete, Is.True);
            Assert.That(totals.CoveredSeconds, Is.EqualTo(2));
            Assert.That(ConnectionTrafficTotals.Build([start], null, 300).Available, Is.False);
            Assert.That(ConnectionTrafficTotals.Build([], end, 300).Available, Is.False);
        });
    }

    [Test]
    public void SinceStartUsesSessionCountersAndSumsWithoutOverflow()
    {
        var end = Capture(3, Reading(long.MaxValue, 200), Reading(100, 300, 2));
        var totals = ConnectionTrafficTotals.Build([], end, 0);
        Assert.That(totals.Sum(), Is.EqualTo((long.MaxValue, 500L)));
    }

}
