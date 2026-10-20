using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Collect memory, system-volume activity, one network link, and busiest-engine GPU utilization.</summary>
internal sealed class WindowsResourceSampler : IDisposable
{
    private readonly WindowsGpuSampler _gpu = new();
    // Enumerating adapters costs far more than reading one adapter's counters, so the candidate list is
    // refreshed only when Windows reports a network change, or every 30 seconds for speed changes.
    private static readonly long CandidateLifetime = 30 * Stopwatch.Frequency;
    private NetworkInterface[]? _candidates;
    private long _candidatesRead;
    private int _networkChanged = 1;

    internal WindowsResourceSampler()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Volatile.Write(ref _networkChanged, 1);
    private readonly DiskCounter _primaryDisk = new();
    private readonly DiskCounter _secondaryDisk = new();
    private NetworkObservation? _previousNetwork;

    internal CpuSample Sample(CpuSample cpu, string? primaryDrive = null, string? secondaryDrive = null)
    {
        var reset = cpu.IsGap;
        if (reset) Reset();
        var result = new ResourceSample();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 && memory.AvailablePhysical <= memory.TotalPhysical)
            result = result with { TotalMemoryBytes = memory.TotalPhysical, UsedMemoryBytes = memory.TotalPhysical - memory.AvailablePhysical };

        // PDH failures only make disk unavailable; they never stop memory, network or CPU.
        var primary = _primaryDisk.Read(primaryDrive ?? Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'));
        var secondary = _secondaryDisk.Read(secondaryDrive);
        result = result with
        {
            DiskName = primary.Name, DiskActivePercent = primary.Active, DiskBytesPerSecond = primary.Bytes,
            SecondaryDiskName = secondary.Name, SecondaryDiskActivePercent = secondary.Active, SecondaryDiskBytesPerSecond = secondary.Bytes
        };
        try { result = ReadNetwork(result); }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
        {
            Trace.TraceWarning("Network counters unavailable: {0}", ex.Message);
            _previousNetwork = null;
            _candidates = null;
        }
        return _gpu.Sample(cpu with { Resources = result }, reset);
    }

    internal void Reset()
    {
        _gpu.Reset();
        _primaryDisk.Reset();
        _secondaryDisk.Reset();
        _previousNetwork = null;
    }

    // Each volume owns its query and baseline so a missing drive cannot invalidate the other.
    private sealed class DiskCounter
    {
        private string? _name;
        private SafePdhQuery? _query;
        private IntPtr _idleCounter, _bytesCounter;
        private bool _baseline;
        private long _timestamp;

        internal (string Name, double? Active, double? Bytes) Read(string? name)
        {
            if (_name != name) { Reset(); _name = name; }
            if (name is null) return ("", null, null);
            try
            {
                if (_query is null)
                {
                    Check(Native.PdhOpenQueryW(null, UIntPtr.Zero, out var opened));
                    _query = opened;
                    Check(Native.PdhAddEnglishCounterW(_query, $@"\LogicalDisk({name})\% Idle Time", UIntPtr.Zero, out _idleCounter));
                    Check(Native.PdhAddEnglishCounterW(_query, $@"\LogicalDisk({name})\Disk Bytes/sec", UIntPtr.Zero, out _bytesCounter));
                }
                Check(Native.PdhCollectQueryData(_query));
                var now = Stopwatch.GetTimestamp();
                var interval = (now - _timestamp) / (double)Stopwatch.Frequency;
                var valid = _baseline && interval is > 0 and <= 5;
                _timestamp = now;
                _baseline = true;
                if (!valid) return (name, null, null);
                var idle = ReadCounter(_idleCounter);
                var bytes = ReadCounter(_bytesCounter);
                // Reconnects need a fresh query and two observations, never a stale delta.
                if (idle is not null && bytes is not null) return (name, Math.Clamp(100 - idle.Value, 0, 100), bytes);
                Reset(); return (name, null, null);
            }
            catch (Win32Exception ex)
            {
                Trace.TraceWarning("Disk {0} counters unavailable: {1}", name, ex.Message);
                Reset();
                return (name, null, null);
            }
        }

        internal void Reset()
        {
            _query?.Dispose();
            _query = null;
            _idleCounter = _bytesCounter = IntPtr.Zero;
            _baseline = false;
        }
    }
    private ResourceSample ReadNetwork(ResourceSample result)
    {
        var now = Stopwatch.GetTimestamp();
        if (_candidates is null || Interlocked.Exchange(ref _networkChanged, 0) != 0 || now - _candidatesRead > CandidateLifetime)
        {
            _candidates = [.. NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n is { OperationalStatus: OperationalStatus.Up, NetworkInterfaceType: NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 })
                .OrderBy(n => n.Id, StringComparer.Ordinal)];
            _candidatesRead = now;
        }
        var candidates = _candidates;
        // Prefer the previously selected link while it remains connected. Never merge interfaces.
        var network = candidates.FirstOrDefault(n => n.Id == _previousNetwork?.Id) ?? candidates
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            .ThenByDescending(n => n.Speed).FirstOrDefault();
        if (network is null) { _previousNetwork = null; return result; }
        var stats = network.GetIPStatistics();
        var observation = new NetworkObservation(network.Id, network.Speed, Stopwatch.GetTimestamp(), stats.BytesReceived, stats.BytesSent);
        var prior = _previousNetwork;
        _previousNetwork = observation;
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

    public void Dispose()
    {
        // NetworkChange events are static and would otherwise keep this sampler alive.
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        Reset();
    }

    internal sealed record NetworkObservation(string Id, long Speed, long Timestamp, long Received, long Sent);

    // The P/Invoke marshaller constructs this handle for PdhOpenQueryW.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class SafePdhQuery() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
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
