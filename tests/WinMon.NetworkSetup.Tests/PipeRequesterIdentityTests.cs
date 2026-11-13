using System.IO.Pipes;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
[Category("Integration")]
public sealed class PipeRequesterIdentityTests
{
    [Test]
    public async Task ConnectedServerIdentity_IsTheCurrentUserAndRemainsValid()
    {
        var name = "WinMon-identity-test-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(limit.Token);
        await client.ConnectAsync(limit.Token);
        await connected;

        using var identity = PipeRequesterIdentity.FromServer(client);
        using var current = WindowsIdentity.GetCurrent();
        Assert.That(identity.Sid, Is.EqualTo(current.User!.Value));
        Assert.DoesNotThrow(identity.VerifyStillSameProcess);
    }

    [Test]
    public void SetupPaths_ResolveOwnDirectoryButRejectUninstalledRemoval()
    {
        Assert.That(SetupPaths.RequireOwnDirectory(), Is.EqualTo(Path.GetDirectoryName(Environment.ProcessPath)));
        Assert.Throws<UnauthorizedAccessException>(SetupPaths.RequireInstalledHelper);
    }
}
