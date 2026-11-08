using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkSetupBrokerTests
{
    [TestCase("service", true)]
    [TestCase("bad-phase", false)]
    public async Task ExpectAsync_AcceptsOnlyWhitelistedFailureMetadata(string failurePhase, bool accepted)
    {
        await using var pipe = new MemoryStream();
        await NetworkSetupWire.WriteAsync(pipe,
            new NetworkSetupReply(NetworkSetupWire.Version, "failed", failurePhase, 5),
            NetworkSetupWireJsonContext.Default.NetworkSetupReply, default);
        pipe.Position = 0;
        if (accepted)
        {
            var error = Assert.ThrowsAsync<NetworkSetupFailedException>(async () =>
                await NetworkSetupBroker.ExpectAsync(pipe, "ready", default));
            Assert.That(error!.Phase, Is.EqualTo("service"));
            Assert.That(error.ErrorCode, Is.EqualTo(5));
            Assert.That(error.Message, Does.Contain("service").And.Contain("5"));
        }
        else
        {
            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await NetworkSetupBroker.ExpectAsync(pipe, "ready", default));
        }
    }
}