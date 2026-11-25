using System.Net;
using System.Net.NetworkInformation;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ConnectionStateCharacterizationTests : WinMon.Tests.ConnectionStateCharacterizationTestsBase
{
    protected override void AssertState(TcpState? state, string expected)
    {
        var protocol = state is null ? TransportProtocol.Udp : TransportProtocol.Tcp;
        var connection = new NetworkConnection(42, 1, protocol,
            new IPEndPoint(IPAddress.Parse("203.0.113.24"), 443), null, state);
        var frame = new NetworkConnectionsFrame([connection],
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
        var owner = new ProcessUsage(42, 1, "Viewer", 0) { ImagePath = @"C:\apps\viewer.exe" };
        var table = ConnectionsTab.Build(frame, [owner], null, [], null, new ConnectionQueryOptions());
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        var protocolText = protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
        var expectedDetails = string.Join(Environment.NewLine,
            "Viewer", "PID: 42", $"{Strings.ProcessPathLabel}: C:\\apps\\viewer.exe",
            $"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}",
            $"42\t{protocolText}\t203.0.113.24:443\t{Strings.MissingReading}\t{expected}");
        Assert.That(table.Rows[0].Details, Is.EqualTo(expectedDetails));
        Assert.That(connection.State, Is.EqualTo(state));
    }
}
