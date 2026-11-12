using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkServiceClientTests
{
    [Test]
    public void Configuration_RequiresOwnProcessSystemAccountAndExactProtectedCommand()
    {
        var expected = $"\"{NetworkServiceProtocol.InstalledExecutable}\"";
        Assert.That(NetworkServiceIdentity.ConfigurationMatches(0x10, "LocalSystem", expected), Is.True);
        Assert.That(NetworkServiceIdentity.ConfigurationMatches(0x20, "LocalSystem", expected), Is.False);
        Assert.That(NetworkServiceIdentity.ConfigurationMatches(0x10, "OtherUser", expected), Is.False);
        Assert.That(NetworkServiceIdentity.ConfigurationMatches(0x10, "LocalSystem", expected + " --command"),
            Is.False);
        Assert.That(
            NetworkServiceIdentity.ConfigurationMatches(0x10, "LocalSystem", "\"C:\\Users\\Public\\service.exe\""),
            Is.False);
    }

    private static ProcessNetworkSnapshot Valid()
    {
        return new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 100, 10, 20, 70,
                ProcessNetworkQuality.Provisional, 0, 0, [new ProcessNetworkReading(42, 100, 500, 1000, 100, 200)])
            { SubscriptionId = Guid.NewGuid() };
    }

    [Test]
    public void ValidateSnapshot_AcceptsProvisionalAndUnavailableReadings()
    {
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(Valid()));
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(Valid() with
        {
            Generation = Guid.Empty, Sequence = 0, WindowStartQpc = 0, WindowEndQpc = 0,
            Quality = ProcessNetworkQuality.Unavailable, Processes = []
        }));
    }

    [Test]
    public void ValidateSnapshot_RequiresBoundedFailureOnlyOnUnavailableReadings()
    {
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(Valid() with
        {
            Quality = ProcessNetworkQuality.Unavailable, Failure = ProcessNetworkFailure.ClockDiscontinuity
        }));
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(Valid() with
        {
            Failure = ProcessNetworkFailure.ClockDiscontinuity
        }));
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(Valid() with
        {
            Quality = ProcessNetworkQuality.Unavailable, Failure = (ProcessNetworkFailure)999
        }));
    }

    [Test]
    public void ValidateSnapshot_RequiresKnownReasonsOnIncompleteReadings()
    {
        var incomplete = Valid() with
        {
            Quality = ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete,
            IncompleteReasons = ProcessNetworkIncompleteReason.OwnershipUnavailable |
                                ProcessNetworkIncompleteReason.NativeLoss
        };
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(incomplete));
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(incomplete with
        {
            IncompleteReasons = (ProcessNetworkIncompleteReason)256
        }));
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(incomplete with
        {
            Quality = ProcessNetworkQuality.Provisional
        }));
    }

    [Test]
    public void ValidateResponse_RejectsProtocolMismatchSeparatelyFromServiceRejection()
    {
        var mismatch = Assert.Throws<NetworkServiceProtocolMismatchException>(() =>
            NetworkServiceClient.ValidateResponse(new NetworkServiceResponse(NetworkServiceProtocol.Version + 1,
                "ok")));
        Assert.Multiple(() =>
        {
            Assert.That(mismatch!.ActualVersion, Is.EqualTo(NetworkServiceProtocol.Version + 1));
            Assert.That(mismatch.ExpectedVersion, Is.EqualTo(NetworkServiceProtocol.Version));
            Assert.That(
                () => NetworkServiceClient.ValidateResponse(new NetworkServiceResponse(NetworkServiceProtocol.Version,
                    "rejected")),
                Throws.TypeOf<InvalidOperationException>().And.Not.TypeOf<NetworkServiceProtocolMismatchException>());
        });
    }

    [Test]
    public void ValidateSnapshot_RejectsInvalidClockOrUnboundedRows()
    {
        foreach (var snapshot in new[]
                 {
                     Valid() with { SubscriptionId = Guid.Empty }, Valid() with { Frequency = 0 },
                     Valid() with { WindowStartQpc = -1 }, Valid() with { WindowEndQpc = 101 },
                     Valid() with { Generation = Guid.Empty }, Valid() with { EventsLost = -1 },
                     Valid() with { Processes = null! },
                     Valid() with { Processes = Enumerable.Repeat(Valid().Processes.Single(), 8193).ToArray() }
                 })
            Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(snapshot));
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(-1d)]
    public void ValidateSnapshot_RejectsInvalidTraffic(double rate)
    {
        var invalid = Valid() with { Processes = [new ProcessNetworkReading(42, 100, 100, 200, rate, null)] };
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(invalid));
    }

    [Test]
    public void ValidateSnapshot_AcceptsEndpointTrafficAndRejectsInvalidOrUnsupportedRows()
    {
        var flow = new ProcessNetworkFlowReading(42, 100, TransportProtocol.Tcp, "127.0.0.1:4000",
            "127.0.0.2:443", 500, 1000, 100, 200);
        var valid = Valid() with
        {
            FlowTrafficSupported = true, FlowQuality = ProcessNetworkQuality.Provisional, Flows = [flow]
        };

        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(valid));
        Assert.That(() => NetworkServiceClient.ValidateSnapshot(valid with { FlowTrafficSupported = false }),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NetworkServiceClient.ValidateSnapshot(valid with
            { Flows = [flow, flow] }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NetworkServiceClient.ValidateSnapshot(valid with
            { Flows = [flow with { LocalEndpoint = "bad\nendpoint" }] }), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => NetworkServiceClient.ValidateSnapshot(valid with
            { Flows = [flow with { RemoteEndpoint = "[::1]:443" }] }), Throws.TypeOf<InvalidDataException>());
    }
}
