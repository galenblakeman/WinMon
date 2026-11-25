using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class LedgerSnapshotCharacterizationTests
{
    private const string Owner = "OWNER";
    private const ProcessNetworkQuality P = ProcessNetworkQuality.Provisional;
    private const ProcessNetworkQuality W = ProcessNetworkQuality.WarmingUp;
    private const ProcessNetworkQuality I = ProcessNetworkQuality.Incomplete;
    private const ProcessNetworkQuality C = ProcessNetworkQuality.CapacityExceeded;
    private const ProcessNetworkQuality D = ProcessNetworkQuality.Discontinuity;

    [TestCase(false, false, 80, P | W)]
    [TestCase(false, false, 100, P)]
    [TestCase(true, false, 80, P | W | I | C)]
    [TestCase(true, false, 100, P | I | C)]
    [TestCase(false, true, 170, P | W | I | D)]
    [TestCase(false, true, 190, P | I | D)]
    [TestCase(true, true, 170, P | W | I | C | D)]
    [TestCase(true, true, 190, P | I | C | D)]
    public void QualityCombinesLifecycleStateAndKeepsCapacityAcrossClockReset(bool capacity, bool reset,
        long now, ProcessNetworkQuality expected)
    {
        var ledger = new NetworkTrafficLedger(10, 0, maximumLifetimes: 1);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 35, 40, true, 25), Is.True);
        if (capacity) Assert.That(ledger.Register(43, 200, Owner, 10), Is.False);
        var previousGeneration = Guid.Empty;
        if (reset)
        {
            previousGeneration = ledger.Snapshot(Owner, 100).Generation;
            _ = ledger.Snapshot(Owner, 90);
            Assert.That(ledger.Register(42, 200, Owner, 91), Is.True);
            Assert.That(ledger.Add(42, 95, 96, true, 25), Is.True);
        }
        var snapshot = ledger.Snapshot(Owner, now);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Quality, Is.EqualTo(expected));
            Assert.That(snapshot.FlowQuality, Is.EqualTo(expected));
            Assert.That(snapshot.Processes.Single().SentBytes, Is.EqualTo(25));
            Assert.That(snapshot.Flows, Is.Empty);
            Assert.That(snapshot.FlowTrafficSupported, Is.True);
            Assert.That(snapshot.Sequence, Is.EqualTo(reset ? 3 : 1));
            Assert.That(snapshot.Generation, Is.Not.EqualTo(previousGeneration));
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(
                (capacity ? ProcessNetworkIncompleteReason.CapacityExceeded : ProcessNetworkIncompleteReason.None) |
                (reset ? ProcessNetworkIncompleteReason.ClockDiscontinuity : ProcessNetworkIncompleteReason.None)));
        });
    }

    [TestCase(80, P | W | I)]
    [TestCase(100, P | I)]
    public void IncompleteCoverageRemainsAfterRatesRecover(long now, ProcessNetworkQuality expected)
    {
        var ledger = new NetworkTrafficLedger(10, 0);
        ledger.MarkIncomplete(10, ProcessNetworkIncompleteReason.QueueOverflow);
        var snapshot = ledger.Snapshot(Owner, now);
        Assert.That(snapshot.Quality, Is.EqualTo(expected));
        Assert.That(snapshot.FlowQuality, Is.EqualTo(expected));
        Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.QueueOverflow));
    }

    [TestCase(89, 0, 50, null)]
    [TestCase(90, 10, 60, 3d)]
    [TestCase(91, 10, 60, 3d)]
    public void WarmupRequiresAWindowEndStrictlyBeyondTheInitialFiveSeconds(long now, long start, long end, double? rate)
    {
        var ledger = new NetworkTrafficLedger(10, 7);
        Assert.That(ledger.Register(42, 100, Owner, 7), Is.True);
        Assert.That(ledger.Add(42, 25, 26, false, 15), Is.True);
        var snapshot = ledger.Snapshot(Owner, now);
        Assert.That((snapshot.WindowStartQpc, snapshot.WindowEndQpc), Is.EqualTo((start, end)));
        Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(rate));
        Assert.That(snapshot.Quality, Is.EqualTo(rate.HasValue ? P : P | W));
    }

    [TestCase(99, null)]
    [TestCase(100, 3d)]
    [TestCase(101, 3d)]
    public void LossRecoveryIncludesAWindowStartingExactlyAtTheLossObservation(long now, double? rate)
    {
        var ledger = new NetworkTrafficLedger(10, 0);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 25, 26, false, 15), Is.True);
        ledger.ReportLoss(3, 20);
        var snapshot = ledger.Snapshot(Owner, now);
        Assert.That(snapshot.EventsLost, Is.EqualTo(3));
        Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(rate));
        Assert.That(snapshot.Quality, Is.EqualTo(rate.HasValue ? P | I : P | I | W));
        Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.NativeLoss));
    }

    [TestCase(null, 90, "ownerSid")]
    [TestCase("", 90, "ownerSid")]
    [TestCase(" ", 90, "ownerSid")]
    [TestCase(Owner, -1, "nowQpc")]
    public void InvalidInputsDoNotAdvanceSequenceOrResetGeneration(string? owner, long now, string parameter)
    {
        var ledger = new NetworkTrafficLedger(10, 0);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 35, 40, true, 10), Is.True);
        var before = ledger.Snapshot(Owner, 100);
        var error = owner is null ? Assert.Throws<ArgumentNullException>(() => ledger.Snapshot(owner!, now))
            : now < 0 ? Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Snapshot(owner, now))
            : Assert.Throws<ArgumentException>(() => ledger.Snapshot(owner, now));
        Assert.That(error!.ParamName, Is.EqualTo(parameter));
        var after = ledger.Snapshot(Owner, 100);
        Assert.That(after.Generation, Is.EqualTo(before.Generation));
        Assert.That(after.Sequence, Is.EqualTo(before.Sequence + 1));
        Assert.That(after.Processes, Is.EqualTo(before.Processes));
        Assert.That(after.Quality, Is.EqualTo(before.Quality));
    }

    [Test]
    [SetCulture("tr-TR")]
    public void RowsAndFlowsUseLifetimeThenOrdinalEndpointOrderAndRemainFrozenAfterLaterPackets()
    {
        var ledger = new NetworkTrafficLedger(10, 0);
        Assert.That(ledger.Register(50, 500, Owner, 0), Is.True);
        Add(50, 35, TransportProtocol.Udp, "Z:1", "z:2");
        Assert.That(ledger.Register(7, 700, Owner, 0), Is.True);
        Add(7, 35, TransportProtocol.Udp, "A:1", "A:1");
        Add(7, 35, TransportProtocol.Tcp, "a:1", "z:9");
        Add(7, 35, TransportProtocol.Tcp, "A:1", "b:1");
        Add(7, 35, TransportProtocol.Tcp, "A:1", "B:1");
        ledger.End(7, 50);
        Assert.That(ledger.Register(7, 800, Owner, 50), Is.True);
        Add(7, 55, TransportProtocol.Tcp, "new:1", "new:2");
        Assert.That(ledger.Register(40, 400, "owner", 0), Is.True);
        Add(40, 35, TransportProtocol.Tcp, "foreign:1", "foreign:2");
        Assert.That(ledger.Register(99, 990, Owner, 0), Is.True);
        var first = ledger.Snapshot(Owner, 100);
        Assert.That(first.Processes.Select(row => (row.ProcessId, row.CreationTicks)),
            Is.EqualTo(new[] { (7, 700L), (7, 800L), (50, 500L) }));
        Assert.That(first.Flows.Select(row => (row.ProcessId, row.CreationTicks, row.Protocol, row.LocalEndpoint, row.RemoteEndpoint)),
            Is.EqualTo(new[] {
                (7, 700L, TransportProtocol.Tcp, "A:1", "B:1"),
                (7, 700L, TransportProtocol.Tcp, "A:1", "b:1"),
                (7, 700L, TransportProtocol.Tcp, "a:1", "z:9"),
                (7, 700L, TransportProtocol.Udp, "A:1", "A:1"),
                (7, 800L, TransportProtocol.Tcp, "new:1", "new:2"),
                (50, 500L, TransportProtocol.Udp, "Z:1", "z:2") }));
        Assert.That(ledger.Add(50, 55, 60, false, 9, TransportProtocol.Udp, "Z:1", "z:2"), Is.True);
        var second = ledger.Snapshot(Owner, 100);
        Assert.That(first.Processes.Last().ReceivedBytes, Is.EqualTo(1));
        Assert.That(first.Flows.Last().ReceivedBytes, Is.EqualTo(1));
        Assert.That(second.Processes.Last().ReceivedBytes, Is.EqualTo(10));
        Assert.That(second.Flows.Last().ReceivedBytes, Is.EqualTo(10));
        Assert.That(second.Generation, Is.EqualTo(first.Generation));
        Assert.That(second.Sequence, Is.EqualTo(first.Sequence + 1));
        return;
        void Add(int pid, long timestamp, TransportProtocol protocol, string local, string remote) =>
            Assert.That(ledger.Add(pid, timestamp, 60, false, 1, protocol, local, remote), Is.True);
    }
}
