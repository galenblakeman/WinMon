using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Collect memory, system-volume activity, one network link, and busiest-engine GPU utilization.</summary>
internal sealed class WindowsResourceSampler : IDisposable
{
    private readonly WindowsGpuSampler _gpu = new();

    private readonly WindowsCpuClock _cpuClock = new();

    // Enumerating adapters costs far more than reading one adapter's counters, so the candidate list is
    // refreshed only when Windows reports a network change, or every 30 seconds for speed changes.
    private static readonly long CandidateLifetime = 30 * Stopwatch.Frequency;
    private NetworkInterface[]? _candidates;
    private NetworkInterfaceChoice[]? _networkChoices;
    private long _candidatesRead;
    private int _networkChanged = 1;

    public WindowsResourceSampler()
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        Volatile.Write(ref _networkChanged, 1);
    }

    private readonly DiskCounter _primaryDisk = new();
    private readonly DiskCounter _secondaryDisk = new();
    private NetworkObservation? _previousNetwork;
    private NetworkObservation? _networkStart;
    private string? _networkInterfaceId;

    public MonitorSample Sample(MonitorSample cpu, string? primaryDrive = null, string? secondaryDrive = null,
        string? networkInterfaceId = null)
    {
        var reset = cpu.IsGap;
        if (reset) Reset();
        var result = new ResourceSample
        {
            CpuClockMhz = _cpuClock.ReadMhz(), CpuKernelPercent = cpu.Resources.CpuKernelPercent
        };
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 &&
            memory.AvailablePhysical <= memory.TotalPhysical)
            result = result with
            {
                TotalMemoryBytes = memory.TotalPhysical,
                UsedMemoryBytes = memory.TotalPhysical - memory.AvailablePhysical
            };
        var performance = new PerformanceInfo { Size = (uint)Marshal.SizeOf<PerformanceInfo>() };
        if (Native.GetPerformanceInfo(ref performance, performance.Size))
            result = result with
            {
                AvailableMemoryBytes = performance.PhysicalAvailablePages.ToUInt64() * performance.PageSize.ToUInt64(),
                CommittedBytes = performance.CommitTotalPages.ToUInt64() * performance.PageSize.ToUInt64(),
                CommitLimitBytes = performance.CommitLimitPages.ToUInt64() * performance.PageSize.ToUInt64(),
                CachedBytes = performance.SystemCachePages.ToUInt64() * performance.PageSize.ToUInt64(),
                PagedPoolBytes = performance.KernelPagedPages.ToUInt64() * performance.PageSize.ToUInt64(),
                NonPagedPoolBytes = performance.KernelNonPagedPages.ToUInt64() * performance.PageSize.ToUInt64(),
                ProcessCount = performance.ProcessCount, ThreadCount = performance.ThreadCount,
                HandleCount = performance.HandleCount
            };

        // PDH failures only make disk unavailable; they never stop memory, network or CPU.
        var primary = _primaryDisk.Read(primaryDrive ?? Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'));
        var secondary = _secondaryDisk.Read(secondaryDrive);
        result = result with
        {
            DiskName = primary.Name, DiskActivePercent = primary.Active, DiskBytesPerSecond = primary.Bytes,
            DiskReadBytesPerSecond = primary.Read, DiskWriteBytesPerSecond = primary.Write,
            DiskResponseSeconds = primary.Response, DiskQueueLength = primary.Queue,
            DiskFreeBytes = primary.Free, DiskTotalBytes = primary.Total,
            SecondaryDiskName = secondary.Name, SecondaryDiskActivePercent = secondary.Active,
            SecondaryDiskBytesPerSecond = secondary.Bytes,
            SecondaryDiskReadBytesPerSecond = secondary.Read,
            SecondaryDiskWriteBytesPerSecond = secondary.Write,
            SecondaryDiskResponseSeconds = secondary.Response,
            SecondaryDiskQueueLength = secondary.Queue,
            SecondaryDiskFreeBytes = secondary.Free, SecondaryDiskTotalBytes = secondary.Total
        };
        try
        {
            result = ReadNetwork(result, networkInterfaceId);
        }
        catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
        {
            Trace.TraceWarning("Network counters unavailable: {0}", ex.Message);
            _previousNetwork = null;
            _networkStart = null;
            _candidates = null;
            _networkChoices = null;
        }

        return _gpu.Sample(cpu with { Resources = result }, reset);
    }

    public void Reset()
    {
        _gpu.Reset();
        _primaryDisk.Reset();
        _secondaryDisk.Reset();
        _previousNetwork = null;
        // A gap invalidates rates, not totals: the adapter's counters keep counting through it.
    }

    // Each volume owns its query and baseline so a missing drive cannot invalidate the other.
    private sealed class DiskCounter
    {
        private string? _name;
        private SafePdhQuery? _query;
        private IntPtr _idleCounter, _bytesCounter, _readCounter, _writeCounter, _responseCounter, _queueCounter;
        private bool _baseline;
        private long _timestamp;
        private Task<(ulong Free, ulong Total)?>? _spaceTask;
        private (ulong Free, ulong Total)? _space;
        private long _spaceRead;

        public (string Name, double? Active, double? Bytes, double? Read, double? Write,
            double? Response, double? Queue, ulong? Free, ulong? Total) Read(string? name)
        {
            if (_name != name)
            {
                Reset();
                _name = name;
            }

            if (name is null) return ("", null, null, null, null, null, null, null, null);
            RefreshSpace(name);
            try
            {
                if (_query is null)
                {
                    Check(Native.PdhOpenQueryW(null, UIntPtr.Zero, out var opened));
                    _query = opened;
                    Check(Native.PdhAddEnglishCounterW(_query, $@"\LogicalDisk({name})\% Idle Time", UIntPtr.Zero,
                        out _idleCounter));
                    Check(Native.PdhAddEnglishCounterW(_query, $@"\LogicalDisk({name})\Disk Bytes/sec", UIntPtr.Zero,
                        out _bytesCounter));
                    _readCounter = AddOptionalCounter(_query, $@"\LogicalDisk({name})\Disk Read Bytes/sec");
                    _writeCounter = AddOptionalCounter(_query, $@"\LogicalDisk({name})\Disk Write Bytes/sec");
                    _responseCounter = AddOptionalCounter(_query, $@"\LogicalDisk({name})\Avg. Disk sec/Transfer");
                    _queueCounter = AddOptionalCounter(_query, $@"\LogicalDisk({name})\Current Disk Queue Length");
                }

                Check(Native.PdhCollectQueryData(_query));
                var now = Stopwatch.GetTimestamp();
                var interval = (now - _timestamp) / (double)Stopwatch.Frequency;
                var valid = _baseline && interval is > 0 and <= 5;
                _timestamp = now;
                _baseline = true;
                if (!valid) return (name, null, null, null, null, null, null, _space?.Free, _space?.Total);
                var idle = ReadCounter(_idleCounter);
                var bytes = ReadCounter(_bytesCounter);
                var read = ReadCounter(_readCounter);
                var write = ReadCounter(_writeCounter);
                var response = ReadCounter(_responseCounter);
                var queue = ReadCounter(_queueCounter);
                // Reconnects need a fresh query and two observations, never a stale delta.
                if (idle is not null && bytes is not null)
                    return (name, Math.Clamp(100 - idle.Value, 0, 100), bytes, read, write, response, queue,
                        _space?.Free, _space?.Total);
                ResetQuery();
                return (name, null, null, null, null, null, null, _space?.Free, _space?.Total);
            }
            catch (Win32Exception ex)
            {
                Trace.TraceWarning("Disk {0} counters unavailable: {1}", name, ex.Message);
                ResetQuery();
                return (name, null, null, null, null, null, null, _space?.Free, _space?.Total);
            }
        }

        private void RefreshSpace(string name)
        {
            if (_spaceTask is { IsCompleted: true })
            {
                _space = _spaceTask.Result;
                _spaceTask = null;
            }

            var now = Stopwatch.GetTimestamp();
            if (_spaceTask is not null || (_spaceRead != 0 && now - _spaceRead < 30 * Stopwatch.Frequency)) return;
            _spaceRead = now;
            _spaceTask = Task.Run(() =>
            {
                try
                {
                    var drive = new DriveInfo(name + "\\");
                    return drive.IsReady
                        ? (drive.AvailableFreeSpace >= 0 ? (ulong)drive.AvailableFreeSpace : 0,
                            drive.TotalSize >= 0 ? (ulong)drive.TotalSize : 0)
                        : ((ulong, ulong)?)null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                               or NotSupportedException or SecurityException)
                {
                    return null;
                }
            });
        }

        public void Reset()
        {
            ResetQuery();
            _spaceTask = null;
            _space = null;
            _spaceRead = 0;
        }

        private void ResetQuery()
        {
            _query?.Dispose();
            _query = null;
            _idleCounter = _bytesCounter =
                _readCounter = _writeCounter = _responseCounter = _queueCounter = IntPtr.Zero;
            _baseline = false;
        }
    }

    private ResourceSample ReadNetwork(ResourceSample result, string? networkInterfaceId)
    {
        if (!string.Equals(_networkInterfaceId, networkInterfaceId, StringComparison.OrdinalIgnoreCase))
        {
            _networkInterfaceId = networkInterfaceId;
            _previousNetwork = null;
            _networkStart = null;
        }

        var now = Stopwatch.GetTimestamp();
        if (_candidates is null || Interlocked.Exchange(ref _networkChanged, 0) != 0 ||
            now - _candidatesRead > CandidateLifetime)
        {
            _candidates =
            [
                .. NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n is
                    {
                        OperationalStatus: OperationalStatus.Up,
                        NetworkInterfaceType: NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211
                    })
                    .OrderBy(n => n.Id, StringComparer.Ordinal)
            ];
            _networkChoices =
            [
                .. _candidates.Select(network => new NetworkInterfaceChoice(network.Id,
                    network.GetIPProperties().GatewayAddresses.Count > 0, network.Speed))
            ];
            _candidatesRead = now;
        }

        var selectedId = SelectNetworkId(_networkChoices ?? [], _previousNetwork?.Id, networkInterfaceId);
        var network = selectedId is null ? null : _candidates!.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, selectedId, StringComparison.OrdinalIgnoreCase));
        if (network is null)
        {
            _previousNetwork = null;
            _networkStart = null;
            return result;
        }

        var stats = network.GetIPStatistics();
        var observation = new NetworkObservation(network.Id, network.Speed, Stopwatch.GetTimestamp(),
            stats.BytesReceived, stats.BytesSent);
        var prior = _previousNetwork;
        _previousNetwork = observation;
        var totals = NetworkTotals(_networkStart, observation);
        _networkStart = totals.Start;
        result = result with
        {
            NetworkName = network.Name, NetworkId = network.Id,
            LinkBitsPerSecond = observation.Speed > 0 ? observation.Speed : null,
            ReceivedBytesSinceStart = totals.Received,
            SentBytesSinceStart = totals.Sent
        };
        return result with
        {
            ReceiveBytesPerSecond = NetworkRate(prior, observation, true),
            SendBytesPerSecond = NetworkRate(prior, observation, false)
        };
    }

    /// <summary>Choose one connected adapter without ever merging interfaces or falling back from a manual choice.</summary>
    internal static string? SelectNetworkId(IReadOnlyList<NetworkInterfaceChoice> candidates, string? previousId,
        string? networkInterfaceId)
    {
        if (networkInterfaceId is not null)
            return candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, networkInterfaceId, StringComparison.OrdinalIgnoreCase))?.Id;
        if (previousId is not null)
            return candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, previousId, StringComparison.OrdinalIgnoreCase))?.Id;
        return candidates.OrderByDescending(candidate => candidate.HasGateway).ThenByDescending(candidate => candidate.Speed)
            .Select(candidate => candidate.Id).FirstOrDefault();
    }

    public static double? NetworkRate(NetworkObservation? prior, NetworkObservation current, bool receive)
    {
        if (prior is null || prior.Id != current.Id || prior.Speed != current.Speed) return null;
        var before = receive ? prior.Received : prior.Sent;
        var after = receive ? current.Received : current.Sent;
        if (before < 0 || after < 0) return null;
        return ResourceRates.PerSecond((ulong)before, (ulong)after,
            (current.Timestamp - prior.Timestamp) / (double)Stopwatch.Frequency);
    }

    public static (ulong? Received, ulong? Sent, NetworkObservation Start) NetworkTotals(
        NetworkObservation? start, NetworkObservation current)
    {
        // Unlike a rate, a total survives a link-speed change, which Wi-Fi makes often.
        if (start is null || start.Id != current.Id ||
            start.Received < 0 || start.Sent < 0 ||
            current.Received < start.Received || current.Sent < start.Sent)
            start = current;
        return (current.Received >= 0 && start.Received >= 0
                ? (ulong)(current.Received - start.Received)
                : null,
            current.Sent >= 0 && start.Sent >= 0
                ? (ulong)(current.Sent - start.Sent)
                : null,
            start);
    }

    private static double? ReadCounter(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        var code = Native.PdhGetFormattedCounterValue(counter, 0x200 | 0x1000, out _, out var value);
        return code == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;
    }

    private static IntPtr AddOptionalCounter(SafePdhQuery query, string path)
    {
        var status = Native.PdhAddEnglishCounterW(query, path, UIntPtr.Zero, out var counter);
        if (status == 0) return counter;
        Trace.TraceWarning("Optional disk counter unavailable ({0}): 0x{1:X8}", path, status);
        return IntPtr.Zero;
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

    public sealed record NetworkObservation(string Id, long Speed, long Timestamp, long Received, long Sent);

    internal sealed record NetworkInterfaceChoice(string Id, bool HasGateway, long Speed);

    // The P/Invoke marshaller constructs this handle for PdhOpenQueryW.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class SafePdhQuery() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            return Native.PdhCloseQuery(handle) == 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;

        public ulong TotalPhysical,
            AvailablePhysical,
            TotalPageFile,
            AvailablePageFile,
            TotalVirtual,
            AvailableVirtual,
            AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInfo
    {
        public uint Size;
        public UIntPtr CommitTotalPages, CommitLimitPages, CommitPeakPages;
        public UIntPtr PhysicalTotalPages, PhysicalAvailablePages, SystemCachePages;
        public UIntPtr KernelTotalPages, KernelPagedPages, KernelNonPagedPages;
        public UIntPtr PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
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
        public static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(ref PerformanceInfo info, uint size);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafePdhQuery query);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint PdhAddEnglishCounterW(SafePdhQuery query, string path, UIntPtr userData,
            out IntPtr counter);

        [DllImport("pdh.dll", ExactSpelling = true)]
        public static extern uint PdhCollectQueryData(SafePdhQuery query);

        [DllImport("pdh.dll", ExactSpelling = true)]
        public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
            out CounterValue value);

        [DllImport("pdh.dll", ExactSpelling = true)]
        public static extern uint PdhCloseQuery(IntPtr query);
    }
}
