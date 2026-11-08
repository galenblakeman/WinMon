using System.ComponentModel;
using NUnit.Framework;
using WinMon.Monitoring;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class NetworkSetupPipeTests
{
    [Test]
    public async Task LocalDataOnlyClientIsIdentifiedAndPipeNameCannotBeReclaimed()
    {
        var name = "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N");
        await using var server = NetworkSetupPipe.Create(name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepted = server.WaitForConnectionAsync(timeout.Token);
        await using var client = await NetworkServiceClient.ConnectProtectedPipeAsync(name, timeout.Token);
        await accepted;
        Assert.That(NetworkSetupPipe.ClientProcessId(server), Is.EqualTo(Environment.ProcessId));
        Assert.That(() => NetworkSetupPipe.Create(name), Throws.TypeOf<Win32Exception>());
        await NetworkSetupWire.WriteAsync(client, new NetworkSetupRequest(1, "nonce", "install"),
            NetworkSetupWireJsonContext.Default.NetworkSetupRequest, timeout.Token);
        var request = await NetworkSetupWire.ReadAsync(server, NetworkSetupWireJsonContext.Default.NetworkSetupRequest,
            timeout.Token);
        Assert.That(request, Is.EqualTo(new NetworkSetupRequest(1, "nonce", "install")));
    }

    [Test]
    public void SetupWireRejectsOversizedFramesBeforeReadingPayload()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(NetworkSetupWire.MaximumMessageBytes + 1));
        Assert.ThrowsAsync<InvalidDataException>(async () => await NetworkSetupWire.ReadAsync(stream,
            NetworkSetupWireJsonContext.Default.NetworkSetupRequest, default));
    }
}