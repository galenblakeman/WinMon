using MonCore.Monitoring;
using NUnit.Framework;
using static WinMon.NetworkService.Tests.CollectorRuntimeFixture;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorSnapshotTests
{
    [TestCase("source")]
    [TestCase("ledger")]
    [TestCase("failed")]
    public async Task UnavailableStateReturnsNoReadingsWithoutTouchingTheClock(string unavailable)
    {
        await using var f = new CollectorRuntimeFixture(source: unavailable != "source", ledger: unavailable != "ledger");
        if (unavailable == "failed") f.Collector.SignalFailure(ProcessNetworkFailure.TraceConsumer);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Generation, Is.EqualTo(Guid.Empty));
            Assert.That(snapshot.TimestampQpc, Is.Zero);
            Assert.That(snapshot.Quality, Is.EqualTo(ProcessNetworkQuality.Unavailable));
            Assert.That(snapshot.Failure, Is.EqualTo(unavailable == "failed" ? ProcessNetworkFailure.TraceConsumer : ProcessNetworkFailure.None));
            Assert.That(snapshot.Processes, Is.Empty);
            Assert.That(snapshot.Flows, Is.Empty);
            Assert.That(snapshot.FlowTrafficSupported, Is.False);
            Assert.That(f.Clock.Reads, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task MissingOwnerSidRetainsArgumentErrorsBeforeClockAccess(string? owner)
    {
        await using var f = new CollectorRuntimeFixture();
        var error = owner is null
            ? Assert.Throws<ArgumentNullException>(() => f.Collector.Snapshot(owner!))
            : Assert.Throws<ArgumentException>(() => f.Collector.Snapshot(owner));
        Assert.That(error!.ParamName, Is.EqualTo("ownerSid"));
        Assert.That(f.Clock.Reads, Is.Empty);
    }

    [Test]
    public async Task SnapshotPublishesPendingDropsAndCoverageWithoutBorrowingAnotherOwnersRows()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        Assert.That(f.State.Ledger.Add(42, Tick(8), Tick(10), false, 9), Is.True);
        f.State.Set("_coverageReasons", (int)ProcessNetworkIncompleteReason.QueueOverflow);
        f.State.Set("_droppedEvents", 3L);
        f.State.Set("_rateGapPending", 1);
        f.Clock.At(13);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.TimestampQpc, Is.EqualTo(Tick(13)));
            Assert.That(snapshot.DroppedEvents, Is.EqualTo(3));
            Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(9));
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.QueueOverflow));
            Assert.That(snapshot.Quality & ProcessNetworkQuality.Incomplete, Is.EqualTo(ProcessNetworkQuality.Incomplete));
            Assert.That(snapshot.FlowQuality & ProcessNetworkQuality.Incomplete, Is.EqualTo(ProcessNetworkQuality.Incomplete));
            Assert.That(f.State.RateGap, Is.Zero);
            Assert.That(f.Collector.Snapshot(Other).Processes, Is.Empty);
        });
    }

    [Test]
    public async Task UnscopedLinkLocalAmbiguityAffectsFlowQualityWithoutInvalidatingAggregateQuality()
    {
        await using var f = new CollectorRuntimeFixture();
        using var header = new EtwHeader(timestamp: 9000);
        f.Queue.Packet(header.Data, 11, false, TransportProtocol.Udp,
            System.Net.IPAddress.Parse("fe80::1"), 1, System.Net.IPAddress.Loopback, 2);
        Assert.That(f.Queue.Batch(), Is.False);
        f.Clock.At(13);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(11));
        Assert.That(snapshot.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
        Assert.That(snapshot.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete));
        Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        Assert.That(snapshot.Flows, Is.Empty);
    }

    [Test]
    public async Task FailureAfterTheInitialGuardKeepsRowsAndMarksTheMainQualityUnavailable()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        Assert.That(f.State.Ledger.Add(42, Tick(8), Tick(10), false, 9), Is.True);
        f.Clock.At(13);
        f.Clock.OnTimestamp = () => f.Collector.SignalFailure(ProcessNetworkFailure.TraceConsumer);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(9));
            Assert.That(snapshot.Failure, Is.EqualTo(ProcessNetworkFailure.TraceConsumer));
            Assert.That(snapshot.Quality & ProcessNetworkQuality.Unavailable, Is.EqualTo(ProcessNetworkQuality.Unavailable));
            Assert.That(snapshot.FlowQuality & ProcessNetworkQuality.Unavailable, Is.EqualTo(ProcessNetworkQuality.None));
            Assert.That(snapshot.FlowTrafficSupported, Is.True);
        });
    }
}
