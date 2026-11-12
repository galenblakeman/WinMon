using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ConnectionQueryFlowTests
{
    [Test]
    public void UnknownFlowOwnerKeepsLifetimeTrafficWithoutInventingProcessMetadata()
    {
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 1, [], 0);
        var network = new ProcessNetworkFrame(100, 10, ProcessNetworkQuality.Provisional,
            [new(42, 100, 123, 456, 10, 20)])
        {
            FlowTrafficSupported = true,
            Flows = [new(42, 100, TransportProtocol.Udp, "127.0.0.1:4000", "127.0.0.1:5000", 123, 456, 10, 20)]
        };
        var capture = new ConnectionCapture(sample, null, [], network);

        var group = ConnectionQuery.Build(null, [], network, [capture], capture,
            new ConnectionQueryOptions(NetworkEnabled: true, TrafficSeconds: 0)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(group.Process, Is.Null);
            Assert.That(group.Members, Is.Empty);
            Assert.That(group.Flows, Has.Count.EqualTo(1));
            Assert.That(group.Network.ReceivedBytes, Is.EqualTo(123));
            Assert.That(group.Network.ReceiveBytesPerSecond, Is.EqualTo(10));
            Assert.That(group.Traffic.SentBytes, Is.EqualTo(456));
        });
    }
}
