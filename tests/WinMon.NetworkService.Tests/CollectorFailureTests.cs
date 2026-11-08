using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class CollectorFailureTests
{
    private const string User = "S-1-5-21-1-2-3-1001";

    [Test]
    public async Task UnreadableProcessProbe_OnlyMarksIncompleteWhenTrafficWasObserved()
    {
        await using var collector = new EtwNetworkCollector([User]);
        Assert.That(collector.ResolveAndRegister(0, 1, 1, observedTraffic: false), Is.False);
        Assert.That(collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        Assert.That(collector.ResolveAndRegister(0, 1, 1, observedTraffic: true), Is.False);
        Assert.That(collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.OwnershipUnavailable));
    }

    [Test]
    public async Task FirstFailureSurvivesSubsequentShutdownAndContainsNoReadings()
    {
        await using var collector = new EtwNetworkCollector([User]);
        collector.SignalFailure(ProcessNetworkFailure.ClockDiscontinuity);
        collector.SignalFailure(ProcessNetworkFailure.Shutdown);
        Assert.Multiple(() =>
        {
            Assert.That(collector.Failed, Is.True);
            Assert.That(collector.Failure, Is.EqualTo(ProcessNetworkFailure.ClockDiscontinuity));
            Assert.That(collector.Snapshot(User).Failure, Is.EqualTo(ProcessNetworkFailure.ClockDiscontinuity));
            Assert.That(collector.Snapshot(User).Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
            Assert.That(collector.Snapshot(User).Processes, Is.Empty);
        });
    }

    [TestCase(ProcessNetworkFailure.None)]
    [TestCase((ProcessNetworkFailure)999)]
    public async Task InvalidFailureDoesNotChangeCollectorState(ProcessNetworkFailure failure)
    {
        await using var collector = new EtwNetworkCollector([User]);
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.SignalFailure(failure));
        Assert.That(collector.Failed, Is.False);
    }
}
