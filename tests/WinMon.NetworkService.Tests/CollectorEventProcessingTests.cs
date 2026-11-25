using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;
using static WinMon.Tests.CollectorRuntimeFixture;

namespace WinMon.Tests;

[TestFixture]
public sealed class CollectorEventProcessingTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task StartAndNetworkEventsDistinguishUnknownOwnershipFromUnobservedStartup(bool start)
    {
        await using var f = new CollectorRuntimeFixture();
        f.Native.OwnerRead = _ => null;
        if (start) f.Queue.Lifecycle(true, 42, Tick(8));
        else f.Queue.Network(42, Tick(8), 9, false);
        Assert.That(f.Queue.Batch(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Native.Reads, Is.EqualTo(new[] { "owner:42" }));
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(Tick(11), true, 0)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(start ? ProcessNetworkIncompleteReason.None : ProcessNetworkIncompleteReason.OwnershipUnavailable));
            Assert.That(f.State.RateGap, Is.Zero);
            Assert.That(f.Collector.Snapshot(Owner).Processes, Is.Empty);
        });
    }

    [Test]
    public async Task StartRegistersWithTheTimestampCapturedBeforeNativeOwnershipIo()
    {
        await using var f = new CollectorRuntimeFixture();
        f.Native.OnOwner = () => f.Clock.At(11);
        f.Queue.Lifecycle(true, 42, Tick(8));
        Assert.That(f.Queue.Batch(), Is.False);
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(10))));
        Assert.That(f.Clock.Now, Is.EqualTo(Tick(11)));
        Assert.That(f.Native.Reads, Is.EqualTo(new[] { "owner:42" }));
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task StopOnlyEndsAKnownLifetimeAfterItsCreation(long delta, bool ended)
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Native.Reads.Clear();
        f.Queue.Lifecycle(false, 42, Tick(1) + delta);
        Assert.That(f.Queue.Batch(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.State.Cached(42), Is.EqualTo(ended ? null : new CachedOwner(Birth(1), Tick(1), Tick(3))));
            Assert.That(f.State.OwnerCount, Is.EqualTo(ended ? 0 : 1));
            Assert.That(f.Native.Reads, Is.Empty);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(true, TransportProtocol.Tcp, "192.0.2.1", "198.51.100.2", "192.0.2.1:123", "198.51.100.2:456")]
    [TestCase(false, TransportProtocol.Tcp, "192.0.2.1", "198.51.100.2", "198.51.100.2:456", "192.0.2.1:123")]
    [TestCase(true, TransportProtocol.Udp, "fe80::1%7", "2001:db8::2", "[fe80::1%7]:123", "[2001:db8::2]:456")]
    [TestCase(false, TransportProtocol.Udp, "fe80::1%7", "2001:db8::2", "[2001:db8::2]:456", "[fe80::1%7]:123")]
    public async Task AcceptedPacketsReachOwnerAndFlowAccountingWithCorrectDirectionAndEndpointFormatting(bool send,
        TransportProtocol protocol, string source, string destination, string local, string remote)
    {
        await using var f = new CollectorRuntimeFixture();
        using var header = new EtwHeader(timestamp: 9000);
        f.Queue.Packet(header.Data, 100, send, protocol, IPAddress.Parse(source), 123, IPAddress.Parse(destination), 456);
        Assert.That(f.Queue.Batch(), Is.False);
        f.Clock.At(13);
        var snapshot = f.Collector.Snapshot(Owner);
        var row = snapshot.Processes.Single();
        var flow = snapshot.Flows.Single();
        Assert.Multiple(() =>
        {
            Assert.That((row.ProcessId, row.CreationTicks, row.ReceivedBytes, row.SentBytes),
                Is.EqualTo((42, Birth(1), send ? 0L : 100L, send ? 100L : 0L)));
            Assert.That((row.ReceiveBytesPerSecond, row.SendBytesPerSecond), Is.EqualTo((send ? 0d : 20d, send ? 20d : 0d)));
            Assert.That((flow.LocalEndpoint, flow.RemoteEndpoint, flow.Protocol), Is.EqualTo((local, remote, protocol)));
            Assert.That((flow.ReceivedBytes, flow.SentBytes), Is.EqualTo((send ? 0L : 100L, send ? 100L : 0L)));
            Assert.That(snapshot.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(snapshot.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(snapshot.TimestampQpc, Is.EqualTo(Tick(13)));
            Assert.That(snapshot.LateEvents, Is.Zero);
            Assert.That(f.Collector.Snapshot(Other).Processes, Is.Empty);
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task MissingEndpointPartsStillContributeAggregateBytesWithoutInventingAFlow(bool local, bool remote)
    {
        await using var f = new CollectorRuntimeFixture();
        f.Queue.Network(42, Tick(8), 17, false, TransportProtocol.Tcp,
            local ? IPAddress.Loopback : null, 1, remote ? IPAddress.IPv6Loopback : null, 2);
        Assert.That(f.Queue.Batch(), Is.False);
        f.Clock.At(13);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(17));
        Assert.That(snapshot.Flows, Is.Empty);
        Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [Test]
    public async Task ProcessingDrainsPendingQualityBeforeAddingTheNextPacket()
    {
        await using var f = new CollectorRuntimeFixture();
        using var invalid = new EtwHeader(version: 0, clock: false);
        f.Queue.Aggregate(invalid.Data, 1, false);
        Assert.That(f.State.RateGap, Is.EqualTo(1));
        f.Queue.Network(42, Tick(8), 9, false);
        Assert.That(f.Queue.Batch(), Is.False);
        f.Clock.At(13);
        var snapshot = f.Collector.Snapshot(Owner);
        Assert.Multiple(() =>
        {
            Assert.That(f.State.RateGap, Is.Zero);
            Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(9));
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.InvalidEvent));
            Assert.That(snapshot.Quality & ProcessNetworkQuality.Incomplete, Is.EqualTo(ProcessNetworkQuality.Incomplete));
        });
    }

    [Test]
    public async Task NetworkArrivalTimeIsCapturedBeforeNativeOwnershipIo()
    {
        await using var f = new CollectorRuntimeFixture();
        f.Native.OnOwner = () => f.Clock.At(50);
        f.Queue.Network(42, Tick(8), 23, true);
        Assert.That(f.Queue.Batch(), Is.False);
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(10))));
        Assert.That(f.State.Ledger.Snapshot(Owner, Tick(13)).Processes.Single().SentBytes, Is.EqualTo(23));
    }

    [Test]
    public void NewClockOverloadRejectsNullOrAnotherTimestampDomain()
    {
        var probe = new CollectorRuntimeFixture.Probe();
        Assert.That(Assert.Throws<ArgumentNullException>(() => _ = new EtwNetworkCollector([Owner], probe, null!))!.ParamName,
            Is.EqualTo("timeProvider"));
        Assert.That(Assert.Throws<ArgumentException>(() => _ = new EtwNetworkCollector([Owner], probe, new OtherDomain()))!.ParamName,
            Is.EqualTo("timeProvider"));
    }

    private sealed class OtherDomain : TimeProvider { public override long TimestampFrequency => Stopwatch.Frequency + 1; }
}
