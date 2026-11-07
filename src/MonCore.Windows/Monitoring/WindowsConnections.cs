using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Reads ordinary-user TCP and UDP endpoint ownership through IP Helper tables.</summary>
public static class WindowsConnections
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidData = 13;
    private const int AddressFamilyIpv4 = 2;
    private const int AddressFamilyIpv6 = 23;
    private const int TcpTableOwnerPidAll = 5;
    private const int UdpTableOwnerPid = 1;
    private const int HeaderSize = sizeof(uint);
    private const int MaxTableBytes = 32 * 1024 * 1024;
    private const int Tcp4RowSize = 24;
    private const int Tcp6RowSize = 56;
    private const int Udp4RowSize = 12;
    private const int Udp6RowSize = 28;

    public static NetworkConnectionsFrame Capture()
    {
        // Read once to identify only the PIDs we must probe, avoiding a process-wide handle scan every second.
        var preliminary = new List<NetworkConnection>();
        _ = CaptureFamily(AddressFamilyIpv4, preliminary);
        _ = CaptureFamily(AddressFamilyIpv6, preliminary);
        var before = ProcessCreationTimes.Capture(preliminary.Select(connection => connection.ProcessId));
        var connections = new List<NetworkConnection>();
        var ipv4 = CaptureFamily(AddressFamilyIpv4, connections);
        var ipv6 = CaptureFamily(AddressFamilyIpv6, connections);
        var after = ProcessCreationTimes.Capture(connections.Select(connection => connection.ProcessId));
        foreach (var index in Enumerable.Range(0, connections.Count))
        {
            var connection = connections[index];
            if (before.TryGetValue(connection.ProcessId, out var started) &&
                after.TryGetValue(connection.ProcessId, out var ended) && started == ended)
                connections[index] = connection with { ProcessCreationTicks = started };
        }

        return new NetworkConnectionsFrame(connections, ipv4, ipv6);
    }

    private static NetworkAddressFamilyStatus CaptureFamily(int family, List<NetworkConnection> connections)
    {
        var captured = new List<NetworkConnection>();
        var tcp = ReadTable((IntPtr buffer, ref int size) => GetExtendedTcpTable(buffer, ref size, false, family,
            TcpTableOwnerPidAll, 0));
        if (!tcp.Success) return new NetworkAddressFamilyStatus(false, tcp.ErrorCode);
        var udp = ReadTable((IntPtr buffer, ref int size) => GetExtendedUdpTable(buffer, ref size, false, family,
            UdpTableOwnerPid, 0));
        if (!udp.Success) return new NetworkAddressFamilyStatus(false, udp.ErrorCode);
        if (family == AddressFamilyIpv4)
        {
            if (!IsWellFormed(tcp.Buffer!, Tcp4RowSize) || !IsWellFormed(udp.Buffer!, Udp4RowSize))
                return new NetworkAddressFamilyStatus(false, ErrorInvalidData);
            ParseTcp4(tcp.Buffer!, captured);
            ParseUdp4(udp.Buffer!, captured);
        }
        else
        {
            if (!IsWellFormed(tcp.Buffer!, Tcp6RowSize) || !IsWellFormed(udp.Buffer!, Udp6RowSize))
                return new NetworkAddressFamilyStatus(false, ErrorInvalidData);
            ParseTcp6(tcp.Buffer!, captured);
            ParseUdp6(udp.Buffer!, captured);
        }

        connections.AddRange(captured);
        return NetworkAddressFamilyStatus.Success;
    }

    private delegate uint TableReader(IntPtr buffer, ref int size);

    private static TableResult ReadTable(TableReader read)
    {
        var size = 0;
        var result = read(IntPtr.Zero, ref size);
        if (result != ErrorInsufficientBuffer || size is < HeaderSize or > MaxTableBytes)
            return new TableResult(null, (int)result);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(size);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var supplied = size;
                result = read(handle.AddrOfPinnedObject(), ref supplied);
                if (result == ErrorSuccess && supplied is >= HeaderSize and <= MaxTableBytes && supplied <= buffer.Length)
                    return new TableResult(buffer[..supplied], null);
                if (result == ErrorSuccess) return new TableResult(null, ErrorInvalidData);
                if (result != ErrorInsufficientBuffer || supplied is < HeaderSize or > MaxTableBytes)
                    return new TableResult(null, (int)result);
                size = supplied;
            }
            finally { handle.Free(); }
        }

        return new TableResult(null, ErrorInsufficientBuffer);
    }

    internal static void ParseTcp4(byte[] table, List<NetworkConnection> connections)
    {
        foreach (var offset in RowOffsets(table, Tcp4RowSize))
        {
            var remote = Ipv4Endpoint(table, offset + 12);
            var state = TcpStateFromTable(ReadUInt32(table, offset));
            connections.Add(new NetworkConnection(unchecked((int)ReadUInt32(table, offset + 20)), null,
                TransportProtocol.Tcp, Ipv4Endpoint(table, offset + 4), IsUnspecified(remote) ? null : remote,
                state));
        }
    }

    internal static void ParseUdp4(byte[] table, List<NetworkConnection> connections)
    {
        foreach (var offset in RowOffsets(table, Udp4RowSize))
            connections.Add(new NetworkConnection(unchecked((int)ReadUInt32(table, offset + 8)), null,
                TransportProtocol.Udp, Ipv4Endpoint(table, offset)));
    }

    internal static void ParseTcp6(byte[] table, List<NetworkConnection> connections)
    {
        foreach (var offset in RowOffsets(table, Tcp6RowSize))
        {
            var remote = Ipv6Endpoint(table, offset + 24, offset + 40, offset + 44);
            var state = TcpStateFromTable(ReadUInt32(table, offset + 48));
            connections.Add(new NetworkConnection(unchecked((int)ReadUInt32(table, offset + 52)), null,
                TransportProtocol.Tcp, Ipv6Endpoint(table, offset, offset + 16, offset + 20),
                IsUnspecified(remote) ? null : remote, state));
        }
    }

    internal static void ParseUdp6(byte[] table, List<NetworkConnection> connections)
    {
        foreach (var offset in RowOffsets(table, Udp6RowSize))
            connections.Add(new NetworkConnection(unchecked((int)ReadUInt32(table, offset + 24)), null,
                TransportProtocol.Udp, Ipv6Endpoint(table, offset, offset + 16, offset + 20)));
    }

    private static IEnumerable<int> RowOffsets(byte[] table, int rowSize)
    {
        if (table.Length < HeaderSize) yield break;
        var count = ReadUInt32(table, 0);
        var rows = Math.Min(count, (uint)((table.Length - HeaderSize) / rowSize));
        for (var index = 0; index < rows; index++) yield return HeaderSize + checked((int)index * rowSize);
    }

    internal static bool IsWellFormed(byte[] table, int rowSize) => table.Length >= HeaderSize &&
        ReadUInt32(table, 0) <= (uint)((table.Length - HeaderSize) / rowSize);

    private static TcpState? TcpStateFromTable(uint state) => state is >= 1 and <= 12 ? (TcpState)state : null;
    private static bool IsUnspecified(IPEndPoint endpoint) => endpoint.Port == 0 &&
        (endpoint.Address.Equals(IPAddress.Any) || endpoint.Address.Equals(IPAddress.IPv6Any));

    private static IPEndPoint Ipv4Endpoint(byte[] row, int offset) =>
        new(new IPAddress(row.AsSpan(offset, 4)), Port(ReadUInt32(row, offset + 4)));

    private static IPEndPoint Ipv6Endpoint(byte[] row, int addressOffset, int scopeOffset, int portOffset) =>
        new(new IPAddress(row.AsSpan(addressOffset, 16), ReadUInt32(row, scopeOffset)), Port(ReadUInt32(row, portOffset)));

    private static int Port(uint networkOrder) => (ushort)IPAddress.NetworkToHostOrder(unchecked((short)networkOrder));
    private static uint ReadUInt32(byte[] bytes, int offset) => MemoryMarshal.Read<uint>(bytes.AsSpan(offset, sizeof(uint)));
    private sealed record TableResult(byte[]? Buffer, int? ErrorCode) { public bool Success => Buffer is not null; }

    private static class ProcessCreationTimes
    {
        private const uint ProcessQueryLimitedInformation = 0x1000;

        internal static Dictionary<int, long> Capture(IEnumerable<int> processIds)
        {
            var result = new Dictionary<int, long>();
            foreach (var processId in processIds.Distinct())
            {
                try
                {
                    using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
                    if (!handle.IsInvalid && GetProcessTimes(handle, out var creation, out _, out _, out _))
                        result[processId] = creation;
                }
                catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception) { }
            }

            return result;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);
}
