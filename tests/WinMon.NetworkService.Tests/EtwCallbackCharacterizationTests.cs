using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class EtwCallbackCharacterizationTests
{
    private static readonly IPAddress Source = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress Destination = IPAddress.Parse("198.51.100.2");

    [Test]
    public void SyntheticHeaderUsesConfiguredTraceClock()
    {
        using var header = new EtwHeader();
        Assert.Multiple(() =>
        {
            Assert.That(header.Data.Version, Is.EqualTo(1));
            Assert.That(header.Data.ProcessID, Is.EqualTo(42));
            Assert.That(header.Data.TimeStampRelativeMSec, Is.EqualTo(2000));
        });
    }

    [TestCase(true, 0, 42, 1)]
    [TestCase(false, 0, 42, 1)]
    [TestCase(true, 1, 0, 1)]
    [TestCase(false, 1, 0, 1)]
    [TestCase(true, 1, -2, 1)]
    [TestCase(false, 1, int.MinValue, 1)]
    [TestCase(true, 1, 42, -1)]
    [TestCase(false, 1, 42, int.MinValue)]
    [TestCase(true, 0, 42, 0)]
    [TestCase(false, 1, 0, 0)]
    public async Task InvalidHeadersRejectBeforeReadingAddressesOrClock(bool endpoint, byte version, int pid, int bytes)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(version, pid, clock: false);
        if (endpoint) fixture.Packet(header.Data, bytes, true, TransportProtocol.Tcp, null!, 1, null!, 2);
        else fixture.Aggregate(header.Data, bytes, false);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Count, Is.Zero);
            Assert.That(fixture.Dropped, Is.Zero);
            Assert.That(fixture.Ambiguous, Is.Zero);
            Assert.That(fixture.RateGap, Is.EqualTo(1));
            Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.InvalidEvent));
            Assert.That(fixture.Collector.Failed, Is.False);
        });
    }

    [TestCase(-1, 2, 1)]
    [TestCase(65536, 2, 1)]
    [TestCase(int.MinValue, 2, 1)]
    [TestCase(int.MaxValue, 2, 1)]
    [TestCase(1, -1, 1)]
    [TestCase(1, 65536, 1)]
    [TestCase(1, int.MinValue, 1)]
    [TestCase(1, int.MaxValue, 1)]
    [TestCase(-1, 2, 0)]
    [TestCase(1, 65536, 0)]
    public async Task InvalidPortsMarkTheRateGapEvenForZeroBytes(int sourcePort, int destinationPort, int bytes)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(clock: false);
        fixture.Packet(header.Data, bytes, false, TransportProtocol.Udp, null!, sourcePort, null!, destinationPort);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Count, Is.Zero);
            Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.InvalidEvent));
            Assert.That(fixture.RateGap, Is.EqualTo(1));
            Assert.That(fixture.Ambiguous, Is.Zero);
            Assert.That(fixture.Dropped, Is.Zero);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ValidZeroBytesIgnoreAddressesClockAndFlowAmbiguity(bool endpoint)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(clock: false);
        if (endpoint) fixture.Packet(header.Data, 0, true, TransportProtocol.Tcp, null!, 0, null!, 65535);
        else fixture.Aggregate(header.Data, 0, true);
        AssertClean(fixture);
    }

    [TestCase(true, TransportProtocol.Tcp, "192.0.2.1", "198.51.100.2", 0, 65535, 1)]
    [TestCase(false, TransportProtocol.Tcp, "192.0.2.1", "198.51.100.2", 65535, 0, 1)]
    [TestCase(true, TransportProtocol.Udp, "192.0.2.1", "198.51.100.2", 53, 49152, 2)]
    [TestCase(false, TransportProtocol.Udp, "192.0.2.1", "198.51.100.2", 53, 49152, 255)]
    [TestCase(true, TransportProtocol.Tcp, "2001:db8::1", "2001:db8::2", 80, 443, 1)]
    [TestCase(false, TransportProtocol.Tcp, "2001:db8::1", "2001:db8::2", 80, 443, 1)]
    [TestCase(true, TransportProtocol.Udp, "fe80::1%7", "fe80::2%9", 0, 65535, 1)]
    [TestCase(false, TransportProtocol.Udp, "fe80::1%7", "fe80::2%9", 65535, 0, 1)]
    [TestCase(true, (TransportProtocol)255, "192.0.2.1", "2001:db8::2", 1, 2, 1)]
    [TestCase(false, (TransportProtocol)255, "::ffff:192.0.2.1", "198.51.100.2", 1, 2, 1)]
    public async Task ValidPacketsKeepValuesAndNormalizeLocalRemoteDirection(bool send, TransportProtocol protocol,
        string sourceText, string destinationText, int sourcePort, int destinationPort, byte version)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(version, int.MaxValue);
        var source = IPAddress.Parse(sourceText);
        var destination = IPAddress.Parse(destinationText);
        fixture.Packet(header.Data, int.MaxValue, send, protocol, source, sourcePort, destination, destinationPort);
        var packet = fixture.Take();
        Assert.Multiple(() =>
        {
            Assert.That(packet, Is.EqualTo(new QueuedPacket("Network", int.MaxValue, EtwCallbackFixture.TwoSeconds,
                int.MaxValue, send, protocol, send ? source : destination, send ? sourcePort : destinationPort,
                send ? destination : source, send ? destinationPort : sourcePort)));
            Assert.That(packet!.Local, Is.SameAs(send ? source : destination));
            Assert.That(packet.Remote, Is.SameAs(send ? destination : source));
        });
        AssertClean(fixture);
    }

    [TestCase(true, "fe80::1", "192.0.2.2")]
    [TestCase(false, "fe80::1", "192.0.2.2")]
    [TestCase(true, "192.0.2.1", "fe80::2")]
    [TestCase(false, "192.0.2.1", "fe80::2")]
    [TestCase(true, "fe80::1%7", "fe80::2")]
    [TestCase(false, "fe80::1", "fe80::2%7")]
    [TestCase(true, "fe80::1", "fe80::2")]
    public async Task UnscopedLinkLocalPacketsKeepOnlyAggregateBytesWithoutARateGap(bool send, string source, string destination)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader();
        fixture.Packet(header.Data, 123, send, TransportProtocol.Udp, IPAddress.Parse(source), 1, IPAddress.Parse(destination), 2);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Take(), Is.EqualTo(new QueuedPacket("Network", 42, EtwCallbackFixture.TwoSeconds,
                123, send, null, null, 0, null, 0)));
            Assert.That(fixture.Count, Is.Zero);
            Assert.That(fixture.Ambiguous, Is.EqualTo(1));
            Assert.That(fixture.RateGap, Is.Zero);
            Assert.That(fixture.Dropped, Is.Zero);
            Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MissingAddressesKeepNullReferenceErrorsWithoutChangingQuality(bool missingSource)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(clock: false);
        Assert.Throws<NullReferenceException>(() => fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp,
            missingSource ? null! : Source, 1, missingSource ? Destination : null!, 2));
        AssertClean(fixture);
    }

    [Test]
    public async Task UnscopedSourceShortCircuitsMissingDestination()
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader();
        fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp, IPAddress.Parse("fe80::1"), 1, null!, 2);
        Assert.That(fixture.Take(), Is.EqualTo(new QueuedPacket("Network", 42, EtwCallbackFixture.TwoSeconds,
            1, true, null, null, 0, null, 0)));
        Assert.That(fixture.Ambiguous, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MissingEventThrowsBeforeValidationAndQualityChanges(bool endpoint)
    {
        await using var fixture = new EtwCallbackFixture();
        if (endpoint)
            Assert.Throws<NullReferenceException>(() => fixture.Packet(null!, -1, true, TransportProtocol.Tcp, Source, -1, Destination, -1));
        else Assert.Throws<NullReferenceException>(() => fixture.Aggregate(null!, -1, false));
        AssertClean(fixture);
    }

    [TestCase(999, 0)]
    [TestCase(1000, 0)]
    [TestCase(1001, 1)]
    public async Task AggregatePacketsClampNonpositiveRelativeTimeAndKeepDirection(long timestamp, int milliseconds)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader(timestamp: timestamp);
        fixture.Aggregate(header.Data, 7, false);
        Assert.That(fixture.Take(), Is.EqualTo(new QueuedPacket("Network", 42,
            17 + (long)Math.Round(milliseconds / 1000d * Stopwatch.Frequency), 7, false, null, null, 0, null, 0)));
        AssertClean(fixture);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TimestampOverflowKeepsPriorFlowAmbiguityButDoesNotEnqueue(bool linkLocal)
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader();
        fixture.Offset = long.MaxValue;
        Assert.Throws<OverflowException>(() => fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp,
            linkLocal ? IPAddress.Parse("fe80::1") : Source, 1, Destination, 2));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Count, Is.Zero);
            Assert.That(fixture.Ambiguous, Is.EqualTo(linkLocal ? 1 : 0));
            Assert.That(fixture.RateGap, Is.Zero);
            Assert.That(fixture.Dropped, Is.Zero);
            Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [Test]
    public async Task QueueCapacityRejectsEachExcessPacketAndAcceptsAgainAfterOneRead()
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader();
        for (var i = 0; i < 262_144; i++) fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp, Source, 1, Destination, 2);
        Assert.That(fixture.Count, Is.EqualTo(262_144));
        Assert.That(fixture.Dropped, Is.Zero);
        fixture.Packet(header.Data, 0, true, TransportProtocol.Tcp, null!, 1, null!, 2);
        Assert.That(fixture.Dropped, Is.Zero);
        fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp, Source, 1, Destination, 2);
        fixture.Aggregate(header.Data, 2, false);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Count, Is.EqualTo(262_144));
            Assert.That(fixture.Dropped, Is.EqualTo(2));
            Assert.That(fixture.RateGap, Is.EqualTo(1));
            Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.QueueOverflow));
            Assert.That(fixture.Take(), Is.EqualTo(new QueuedPacket("Network", 42, EtwCallbackFixture.TwoSeconds,
                1, true, TransportProtocol.Tcp, Source, 1, Destination, 2)));
        });
        fixture.Aggregate(header.Data, 3, false);
        Assert.That(fixture.Count, Is.EqualTo(262_144));
        Assert.That(fixture.Dropped, Is.EqualTo(2));
    }

    [Test]
    public async Task CallbackAfterDisposalReportsRejectedWritesAndKeepsOtherQualityReasons()
    {
        await using var fixture = new EtwCallbackFixture();
        using var invalid = new EtwHeader(version: 0);
        using var header = new EtwHeader();
        fixture.Aggregate(invalid.Data, 1, true);
        await fixture.Collector.DisposeAsync();
        fixture.Packet(header.Data, 1, true, TransportProtocol.Tcp, Source, 1, Destination, 2);
        fixture.Aggregate(header.Data, 2, false);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Count, Is.Zero);
            Assert.That(fixture.Dropped, Is.EqualTo(2));
            Assert.That(fixture.RateGap, Is.EqualTo(1));
            Assert.That(fixture.Collector.CoverageReasons,
                Is.EqualTo(ProcessNetworkIncompleteReason.InvalidEvent | ProcessNetworkIncompleteReason.QueueOverflow));
            Assert.That(fixture.Collector.Failed, Is.False);
        });
    }

    [Test]
    public async Task ConcurrentCallbacksKeepEveryPacketAndBothDirections()
    {
        await using var fixture = new EtwCallbackFixture();
        using var header = new EtwHeader();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(writer => Task.Run(() =>
        {
            for (var i = 0; i < 256; i++) fixture.Packet(header.Data, writer + 1, writer % 2 == 0,
                TransportProtocol.Tcp, Source, 1, Destination, 2);
        })));
        Assert.That(fixture.Count, Is.EqualTo(1024));
        var packets = Enumerable.Range(0, 1024).Select(_ => fixture.Take()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(packets.Sum(packet => packet.Bytes), Is.EqualTo(2560));
            Assert.That(packets.Count(packet => packet.Send), Is.EqualTo(512));
            Assert.That(packets.All(packet => packet.Pid == 42 && packet.Qpc == EtwCallbackFixture.TwoSeconds), Is.True);
            Assert.That(packets.GroupBy(packet => packet.Bytes).Select(group => group.Count()), Is.All.EqualTo(256));
            Assert.That(packets.Select(packet => packet.Bytes).Distinct(), Is.EquivalentTo(new[] { 1, 2, 3, 4 }));
        });
        AssertClean(fixture);
    }

    private static void AssertClean(EtwCallbackFixture fixture) => Assert.Multiple(() =>
    {
        Assert.That(fixture.Count, Is.Zero);
        Assert.That(fixture.Dropped, Is.Zero);
        Assert.That(fixture.RateGap, Is.Zero);
        Assert.That(fixture.Ambiguous, Is.Zero);
        Assert.That(fixture.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        Assert.That(fixture.Collector.Failed, Is.False);
    });
}
