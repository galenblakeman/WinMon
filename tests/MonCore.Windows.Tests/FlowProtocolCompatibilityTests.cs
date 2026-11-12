using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class FlowProtocolCompatibilityTests
{
    [Test]
    public void OlderServicePayloadDefaultsToUnsupportedEndpointTraffic()
    {
        var original = Snapshot();
        var olderPayload = JsonSerializer.Serialize(new
        {
            original.Generation, original.Sequence, original.TimestampQpc, original.Frequency,
            original.WindowStartQpc, original.WindowEndQpc, original.Quality, original.EventsLost,
            original.LateEvents, original.Processes, original.SubscriptionId
        });

        var received = JsonSerializer.Deserialize<ProcessNetworkSnapshot>(olderPayload)!;

        Assert.Multiple(() =>
        {
            Assert.That(received.FlowTrafficSupported, Is.False);
            Assert.That(received.Flows, Is.Empty);
            Assert.That(received.FlowQuality, Is.EqualTo(ProcessNetworkQuality.None));
            Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(received));
        });
    }

    [Test]
    public void OlderSnapshotReaderIgnoresAdditiveFlowFields()
    {
        var payload = JsonSerializer.Serialize(Snapshot());
        var older = JsonSerializer.Deserialize<OlderSnapshot>(payload)!;

        Assert.Multiple(() =>
        {
            Assert.That(older.Generation, Is.Not.EqualTo(Guid.Empty));
            Assert.That(older.Processes.Single().ReceivedBytes, Is.EqualTo(123));
            Assert.That(older.SubscriptionId, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public void CombinedFlowCoverageFlagsDoNotRejectSoundAppTraffic()
    {
        var snapshot = Snapshot() with
        {
            FlowQuality = ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete |
                          ProcessNetworkQuality.CapacityExceeded
        };

        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        Assert.That(snapshot.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
    }

    private static ProcessNetworkSnapshot Snapshot()
    {
        return new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 100, 10, 20, 70,
            ProcessNetworkQuality.Provisional, 0, 0, [new(42, 100, 123, 456, 10, 20)])
        {
            SubscriptionId = Guid.NewGuid(), FlowTrafficSupported = true,
            FlowQuality = ProcessNetworkQuality.Provisional,
            Flows = [new(42, 100, TransportProtocol.Tcp, "127.0.0.1:4000", "127.0.0.1:5000", 123, 456, 10, 20)]
        };
    }

    // The pre-extension snapshot contract; unknown JSON properties use the protocol's default serializer behavior.
    private sealed record OlderSnapshot(Guid Generation, IReadOnlyList<ProcessNetworkReading> Processes,
        Guid SubscriptionId);
}
