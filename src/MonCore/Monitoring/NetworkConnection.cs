using System.Net;
using System.Net.NetworkInformation;

namespace WinMon.Monitoring;

/// <summary>One endpoint Windows associated with a process. It contains no transfer counters.</summary>
public sealed record NetworkConnection(
    int ProcessId,
    long? ProcessCreationTicks,
    TransportProtocol Protocol,
    IPEndPoint LocalEndpoint,
    IPEndPoint? RemoteEndpoint = null,
    TcpState? State = null);

/// <summary>The transport protocol represented by a <see cref="NetworkConnection"/>.</summary>
public enum TransportProtocol { Tcp, Udp }

/// <summary>Result for one address family so callers can distinguish an empty table from an unavailable table.</summary>
public sealed record NetworkAddressFamilyStatus(bool Available, int? ErrorCode = null)
{
    public static NetworkAddressFamilyStatus Success { get; } = new(true);
}

/// <summary>Read-only process endpoint observations from the operating system.</summary>
public sealed record NetworkConnectionsFrame(
    IReadOnlyList<NetworkConnection> Connections,
    NetworkAddressFamilyStatus Ipv4,
    NetworkAddressFamilyStatus Ipv6)
{
    public bool Available => Ipv4.Available || Ipv6.Available;
    public bool PartiallyAvailable => Available && (!Ipv4.Available || !Ipv6.Available);
}
