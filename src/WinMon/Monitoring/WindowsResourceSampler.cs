using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Collect memory, system-volume activity, and one explicitly identified network link.</summary>
internal sealed class WindowsResourceSampler : IDisposable
{
    private readonly string diskName = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
    private SafePdhQuery? query;
    private IntPtr idleCounter, bytesCounter;
    private bool diskBaseline;
    private long lastDiskTimestamp;
    private NetworkObservation? previousNetwork;

    internal ResourceSample Sample(bool reset)
    {
        if (reset) Reset();
        var result = new ResourceSample { DiskName = diskName };
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 && memory.AvailablePhysical <= memory.TotalPhysical)
            result = result with { TotalMemoryBytes = memory.TotalPhysical, UsedMemoryBytes = memory.TotalPhysical - memory.AvailablePhysical };

        // PDH failures only make disk unavailable; they never stop memory, network or CPU.
        try { result = ReadDisk(result); }
        catch (Win32Exception ex) { Trace.TraceWarning("Disk counters unavailable: {0}", ex.Message); ResetDisk(); }
        try { result = ReadNetwork(result); }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
        {
            Trace.TraceWarning("Network counters unavailable: {0}", ex.Message);
            previousNetwork = null;
        }
        return result;
    }

    internal void Reset()
    {
        ResetDisk();
        previousNetwork = null;
    }

    private ResourceSample ReadDisk(ResourceSample result)
    {
        if (query is null)
        {
            Check(Native.PdhOpenQueryW(null, UIntPtr.Zero, out var opened));
            query = opened;
            Check(Native.PdhAddEnglishCounterW(query, $@"\LogicalDisk({diskName})\% Idle Time", UIntPtr.Zero, out idleCounter));
            Check(Native.PdhAddEnglishCounterW(query, $@"\LogicalDisk({diskName})\Disk Bytes/sec", UIntPtr.Zero, out bytesCounter));
        }
        Check(Native.PdhCollectQueryData(query));
        var now = Stopwatch.GetTimestamp();
        var interval = (now - lastDiskTimestamp) / (double)Stopwatch.Frequency;
        var valid = diskBaseline && interval is > 0 and <= 5;
        diskBaseline = true;
        lastDiskTimestamp = now;
        // Rate counters need two observations. Never join across sleep or pause.
        if (!valid) return result;
        var idle = ReadCounter(idleCounter);
        return result with
        {
            DiskActivePercent = idle is double value ? Math.Clamp(100 - value, 0, 100) : null,
            DiskBytesPerSecond = ReadCounter(bytesCounter)
        };
    }

    private ResourceSample ReadNetwork(ResourceSample result)
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        // Prefer the previously selected link while it remains connected. Never merge interfaces.
        var network = candidates.FirstOrDefault(n => n.Id == previousNetwork?.Id) ?? candidates
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            .ThenByDescending(n => n.Speed).FirstOrDefault();
        if (network is null) { previousNetwork = null; return result; }
        var stats = network.GetIPStatistics();
        var observation = new NetworkObservation(network.Id, network.Speed, Stopwatch.GetTimestamp(), stats.BytesReceived, stats.BytesSent);
        var prior = previousNetwork;
        previousNetwork = observation;
        result = result with { NetworkName = network.Name, NetworkId = network.Id, LinkBitsPerSecond = observation.Speed > 0 ? observation.Speed : null };
        return result with
        {
            ReceiveBytesPerSecond = NetworkRate(prior, observation, true),
            SendBytesPerSecond = NetworkRate(prior, observation, false)
        };
    }

    internal static double? NetworkRate(NetworkObservation? prior, NetworkObservation current, bool receive)
    {
        if (prior is null || prior.Id != current.Id || prior.Speed != current.Speed) return null;
        var before = receive ? prior.Received : prior.Sent;
        var after = receive ? current.Received : current.Sent;
        if (before < 0 || after < 0) return null;
        return ResourceRates.PerSecond((ulong)before, (ulong)after,
            (current.Timestamp - prior.Timestamp) / (double)Stopwatch.Frequency);
    }

    private static double? ReadCounter(IntPtr counter)
    {
        var code = Native.PdhGetFormattedCounterValue(counter, 0x200 | 0x1000, out _, out var value);
        return code == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) && value.Value >= 0 ? value.Value : null;
    }

    private static void Check(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status));
    }

    private void ResetDisk()
    {
        query?.Dispose();
        query = null;
        idleCounter = bytesCounter = IntPtr.Zero;
        diskBaseline = false;
    }

    public void Dispose() => Reset();

    internal sealed record NetworkObservation(string Id, long Speed, long Timestamp, long Received, long Sent);

    private sealed class SafePdhQuery : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafePdhQuery() : base(true) { }
        protected override bool ReleaseHandle() => Native.PdhCloseQuery(handle) == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct CounterValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafePdhQuery query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint PdhAddEnglishCounterW(SafePdhQuery query, string path, UIntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhCollectQueryData(SafePdhQuery query);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhCloseQuery(IntPtr query);
    }
}
