using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsConnectionsTests
{
    [Test]
    public void ParseTcp4_MapsOwnerEndpointsAndState()
    {
        var table = new byte[4 + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), (uint)TcpState.Established);
        new byte[] { 192, 0, 2, 3 }.CopyTo(table, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12), 0xBB01);
        new byte[] { 203, 0, 113, 9 }.CopyTo(table, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(20), 0x5000);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 42);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseTcp4(table, connections);

        Assert.That(connections, Has.Count.EqualTo(1));
        var connection = connections.Single();
        Assert.Multiple(() =>
        {
            Assert.That(connection.ProcessId, Is.EqualTo(42));
            Assert.That(connection.ProcessCreationTicks, Is.Null);
            Assert.That(connection.Protocol, Is.EqualTo(TransportProtocol.Tcp));
            Assert.That(connection.LocalEndpoint, Is.EqualTo(new IPEndPoint(IPAddress.Parse("192.0.2.3"), 443)));
            Assert.That(connection.RemoteEndpoint, Is.EqualTo(new IPEndPoint(IPAddress.Parse("203.0.113.9"), 80)));
            Assert.That(connection.State, Is.EqualTo(TcpState.Established));
        });
    }

    [Test]
    public void ParseUdp4_LeavesRemoteAndStateUnavailable()
    {
        var table = new byte[4 + 12];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        new byte[] { 0, 0, 0, 0 }.CopyTo(table, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(8), 0xE914);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12), 99);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseUdp4(table, connections);

        Assert.That(connections.Single(), Is.EqualTo(new NetworkConnection(99, null, TransportProtocol.Udp,
            new IPEndPoint(IPAddress.Any, 5353))));
    }

    [Test]
    public void ParseTcp6_PreservesScopePortAndHidesListenerRemoteEndpoint()
    {
        var table = new byte[4 + 56];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        new byte[] { 0xfe, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }.CopyTo(table, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(20), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 0x5000);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(52), (uint)TcpState.Listen);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(56), 123);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseTcp6(table, connections);

        Assert.Multiple(() =>
        {
            Assert.That(connections.Single().ProcessId, Is.EqualTo(123));
            Assert.That(connections.Single().LocalEndpoint, Is.EqualTo(new IPEndPoint(IPAddress.Parse("fe80::1%7"), 80)));
            Assert.That(connections.Single().RemoteEndpoint, Is.Null);
            Assert.That(connections.Single().State, Is.EqualTo(TcpState.Listen));
        });
    }

    [Test]
    public void IsWellFormed_RejectsClaimedRowsOutsideReturnedBytes()
    {
        var table = new byte[4 + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 2);

        Assert.That(WindowsConnections.IsWellFormed(table, 24), Is.False);
    }

    [Test]
    public void ParseTcp4_HidesListenerRemoteEndpoint()
    {
        var table = new byte[4 + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), 0);
        new byte[] { 127, 0, 0, 1 }.CopyTo(table, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12), 0x901F);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 10);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseTcp4(table, connections);

        Assert.Multiple(() =>
        {
            Assert.That(connections.Single().RemoteEndpoint, Is.Null);
            Assert.That(connections.Single().State, Is.Null);
        });
    }

    [Test]
    public void ParseTcp4_PreservesReportedPeerIndependentlyOfState()
    {
        var table = new byte[4 + 24];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), (uint)TcpState.Listen);
        new byte[] { 127, 0, 0, 1 }.CopyTo(table, 8);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(12), 0x901F);
        new byte[] { 127, 0, 0, 1 }.CopyTo(table, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(20), 0x911F);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 10);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseTcp4(table, connections);

        Assert.That(connections.Single().RemoteEndpoint,
            Is.EqualTo(new IPEndPoint(IPAddress.Loopback, 8081)));
    }

    [Test]
    public void ParseUdp6_PreservesScopeAndLeavesRemoteUnavailable()
    {
        var table = new byte[4 + 28];
        BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        new byte[] { 0xfe, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2 }.CopyTo(table, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(20), 9);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 0xE914);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(28), 15);

        var connections = new List<NetworkConnection>();
        WindowsConnections.ParseUdp6(table, connections);

        Assert.That(connections.Single(), Is.EqualTo(new NetworkConnection(15, null, TransportProtocol.Udp,
            new IPEndPoint(IPAddress.Parse("fe80::2%9"), 5353))));
    }
}
