using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ConnectionQueryTests
{
    private static readonly ProcessUsage Owner = new(42, 100, "worker", 0)
        { ImagePath = @"C:\apps\worker.exe" };

    private static readonly NetworkConnectionsFrame Endpoints = new(
        [new NetworkConnection(42, 100, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 5000))],
        NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private static ConnectionQueryRow Row(ProcessNetworkFrame network, params ProcessUsage[] owners) =>
        ConnectionQuery.Build(Endpoints, owners.Length == 0 ? [Owner] : owners, network, [], null,
            new ConnectionQueryOptions(NetworkEnabled: true)).Single();

    [TestCase(-1d, 20d)]
    [TestCase(10d, -1d)]
    [TestCase(double.NaN, 20d)]
    [TestCase(10d, double.NaN)]
    [TestCase(double.PositiveInfinity, 20d)]
    [TestCase(10d, double.PositiveInfinity)]
    [TestCase(null, 20d)]
    [TestCase(10d, null)]
    public void InvalidCurrentRateHidesBothDirectionsButPreservesTotals(double? receive, double? send)
    {
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new(42, 100, 123, 456, receive, send)]));

        Assert.Multiple(() =>
        {
            Assert.That(row.Network.HasTotals, Is.True);
            Assert.That(row.Network.ReceivedBytes, Is.EqualTo(123));
            Assert.That(row.Network.SentBytes, Is.EqualTo(456));
            Assert.That(row.Network.ReceiveBytesPerSecond, Is.Null);
            Assert.That(row.Network.SendBytesPerSecond, Is.Null);
        });
    }

    [TestCase(-1, 456)]
    [TestCase(123, -1)]
    public void NegativeCountersMakeTheSummaryUnavailable(long received, long sent)
    {
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new(42, 100, received, sent, 10, 20)]));

        Assert.That(row.Network, Is.EqualTo(ConnectionNetworkSummary.Unavailable));
    }

    [Test]
    public void DuplicateLifetimeReadingsAreCountedOnlyOnce()
    {
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new(42, 100, 123, 456, 10, 20), new(42, 100, 999, 999, 99, 99)]));

        Assert.Multiple(() =>
        {
            Assert.That(row.Members, Has.Count.EqualTo(1));
            Assert.That(row.Network.ReceivedBytes, Is.EqualTo(123));
            Assert.That(row.Network.SentBytes, Is.EqualTo(456));
            Assert.That(row.Network.ReceiveBytesPerSecond, Is.EqualTo(10));
            Assert.That(row.Network.SendBytesPerSecond, Is.EqualTo(20));
        });
    }

    [Test]
    public void ExecutableTotalsSaturateAndOverflowingRatesBecomeUnavailable()
    {
        var sibling = Owner with { ProcessId = 43, CreationTicks = 101 };
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new(42, 100, long.MaxValue - 1, 20, double.MaxValue, 10),
             new(43, 101, 10, long.MaxValue - 1, double.MaxValue, 20)]), Owner, sibling);

        Assert.Multiple(() =>
        {
            Assert.That(row.Network.HasTotals, Is.True);
            Assert.That(row.Network.ReceivedBytes, Is.EqualTo(long.MaxValue));
            Assert.That(row.Network.SentBytes, Is.EqualTo(long.MaxValue));
            Assert.That(row.Network.ReceiveBytesPerSecond, Is.Null);
            Assert.That(row.Network.SendBytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void MissingSiblingTrafficMarksKnownTotalsIncomplete()
    {
        var sibling = Owner with { ProcessId = 43, CreationTicks = 101 };
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.Provisional,
            [new(42, 100, 123, 456, 10, 20)]), Owner, sibling);

        Assert.Multiple(() =>
        {
            Assert.That(row.Members, Has.Count.EqualTo(2));
            Assert.That(row.Network.HasTotals, Is.True);
            Assert.That(row.Network.ReceivedBytes, Is.EqualTo(123));
            Assert.That(row.Network.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete));
        });
    }

    [TestCase(99L)]
    [TestCase(null)]
    public void EndpointWithoutMatchingLifetimeCannotBorrowReusedPidMetadata(long? creation)
    {
        var endpoints = Endpoints with { Connections = [Endpoints.Connections[0] with { ProcessCreationTicks = creation }] };
        var row = ConnectionQuery.Build(endpoints, [Owner], null, [], null).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.Null);
            Assert.That(row.Members, Is.Empty);
            Assert.That(row.Key, Is.EqualTo(creation is null ? "lifetime:42:?" : "lifetime:42:99"));
            Assert.That(row.Network, Is.EqualTo(ConnectionNetworkSummary.Disabled));
        });
    }

    [Test]
    public void EmptyCurrentLifetimeSetPreservesTotalsWithoutStaleRates()
    {
        var row = Row(new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new(42, 100, 123, 456, 10, 20)]) { CurrentLifetimes = new HashSet<ProcessLifetimeIdentity>() });

        Assert.Multiple(() =>
        {
            Assert.That(row.Network.HasTotals, Is.True);
            Assert.That(row.Network.ReceivedBytes, Is.EqualTo(123));
            Assert.That(row.Network.SentBytes, Is.EqualTo(456));
            Assert.That(row.Network.ReceiveBytesPerSecond, Is.Null);
            Assert.That(row.Network.SendBytesPerSecond, Is.Null);
        });
    }
}
