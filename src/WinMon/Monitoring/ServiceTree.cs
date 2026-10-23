using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WinMon.Monitoring;

internal sealed record ServiceMetrics(double? Cpu, ulong? PrivateBytes, double? Io)
{
    internal static readonly ServiceMetrics None = new(null, null, null);

    internal static ServiceMetrics Sum(IReadOnlyList<ProcessUsage> processes)
    {
        if (processes.Count == 0) return None;
        double cpu = 0, io = 0;
        ulong memory = 0;
        bool hasMemory = true, hasIo = true;
        // Indexing avoids a boxed enumerator over the interface.
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            cpu += process.CpuPercent;
            if (process.PrivateBytes is { } bytes) memory += bytes;
            else hasMemory = false;
            if (process.IoBytesPerSecond is { } rate) io += rate;
            else hasIo = false;
        }

        return new ServiceMetrics(cpu, hasMemory ? memory : null, hasIo ? io : null);
    }

    // The same sums as a one-member list, without allocating one.
    internal static ServiceMetrics Of(ProcessUsage process)
    {
        return new ServiceMetrics(0d + process.CpuPercent, process.PrivateBytes,
            process.IoBytesPerSecond is { } io ? 0d + io : null);
    }
}

internal sealed record ServiceGroup(
    string Key,
    IReadOnlyList<ServiceObservation> Services,
    ProcessUsage? Host,
    IReadOnlyList<ProcessUsage> Processes)
{
    internal bool Shared => Services.Count > 1;

    internal ServiceMetrics Metrics(bool workers)
    {
        return workers ? ServiceMetrics.Sum(Processes)
            : Host is null ? ServiceMetrics.None : ServiceMetrics.Of(Host);
    }
}

internal sealed record ServiceFrame(CpuSample Sample, IReadOnlyList<ServiceGroup> Groups, bool Available = true)
{
    private static readonly ConditionalWeakTable<ServiceFrame, ServiceFrameIndex> Indexes = new();

    /// <summary>Group lookup and metrics for this frame, built on first use; frames never change after capture.</summary>
    internal ServiceFrameIndex Index => Indexes.GetValue(this, static frame => new ServiceFrameIndex(frame.Groups));
}

/// <summary>Per-frame lookups that every Services refresh needs for each retained frame.</summary>
internal sealed class ServiceFrameIndex
{
    private readonly IReadOnlyList<ServiceGroup> _groups;
    private readonly Dictionary<string, int> _positions;
    private ServiceMetrics[]? _host, _workers;
    private double? _hostPeak, _workersPeak;

    internal ServiceFrameIndex(IReadOnlyList<ServiceGroup> groups)
    {
        _groups = groups;
        _positions = new Dictionary<string, int>(groups.Count);
        for (var i = 0; i < groups.Count; i++) _positions.TryAdd(groups[i].Key, i);
    }

    /// <summary>Position of the group with this key in <see cref="ServiceFrame.Groups"/>, or -1.</summary>
    internal int IndexOf(string key)
    {
        return _positions.GetValueOrDefault(key, -1);
    }

    internal ServiceGroup? Find(string key)
    {
        return IndexOf(key) is var i and >= 0 ? _groups[i] : null;
    }

    internal ServiceMetrics Metrics(int position, bool workers)
    {
        ref var metrics = ref workers ? ref _workers : ref _host;
        if (metrics is not null) return metrics[position];
        metrics = new ServiceMetrics[_groups.Count];
        for (var i = 0; i < metrics.Length; i++) metrics[i] = _groups[i].Metrics(workers);
        return metrics[position];
    }

    /// <summary>Busiest group CPU in this frame, counting a missing reading as zero.</summary>
    internal double PeakCpu(bool workers)
    {
        ref var peak = ref workers ? ref _workersPeak : ref _hostPeak;
        if (peak is not null) return peak.Value;
        var busiest = 0d;
        for (var i = 0; i < _groups.Count; i++)
            if ((Metrics(i, workers).Cpu ?? 0) is var cpu && cpu > busiest)
                busiest = cpu;
        peak = busiest;
        return busiest;
    }
}

/// <summary>Observed ancestry, bounded to live lifetimes and current service hosts. Never groups by executable name.</summary>
/// <remarks>
/// Collections are reused between captures, and so are group keys, service arrays and unchanged groups, so the two
/// minutes of retained frames share them instead of each holding its own copies.
/// </remarks>
internal sealed class ServiceTree
{
    private Dictionary<(int Pid, long Created), (int Pid, long Created)> _owners = [];
    private Dictionary<(int Pid, long Created), (int Pid, long Created)> _nextOwners = [];
    private readonly Dictionary<int, ProcessUsage> _processes = [];
    private readonly Dictionary<int, List<ServiceObservation>> _servicesByPid = [];
    private readonly List<int> _hostPids = [];
    private readonly Dictionary<int, ProcessUsage> _hosts = [];
    private readonly Dictionary<(int, long), List<ProcessUsage>> _members = [];
    private readonly Stack<List<ServiceObservation>> _spareServiceLists = [];

    private readonly Stack<List<ProcessUsage>> _spareMemberLists = [];

    // The previous capture's groups: hosts by lifetime, pending hosts by PID, host-less services by observation.
    private Dictionary<(int, long), ServiceGroup> _hostGroups = [];
    private Dictionary<(int, long), ServiceGroup> _nextHostGroups = [];
    private Dictionary<int, ServiceGroup> _pendingGroups = [];
    private Dictionary<int, ServiceGroup> _nextPendingGroups = [];
    private Dictionary<ServiceObservation, ServiceGroup> _serviceGroups = new(ReferenceEqualityComparer.Instance);
    private Dictionary<ServiceObservation, ServiceGroup> _nextServiceGroups = new(ReferenceEqualityComparer.Instance);

    private static readonly Comparison<ProcessUsage> ByProcessId = (left, right) =>
        left.ProcessId.CompareTo(right.ProcessId);

    internal ServiceFrame Capture(CpuSample sample, ServiceCatalog? catalog)
    {
        var moment = sample with { Processes = [] };
        if (catalog is null || sample.IsGap)
        {
            _owners.Clear();
            return new ServiceFrame(moment, [], false);
        }

        Reset();
        // Add, not the indexer: a duplicate PID fails the pass rather than picking one entry.
        foreach (var process in sample.Processes) _processes.Add(process.ProcessId, process);
        foreach (var service in catalog.Services)
        {
            if (!service.HasHost) continue;
            if (!_servicesByPid.TryGetValue(service.ProcessId, out var list))
            {
                list = _spareServiceLists.TryPop(out var spare) ? spare : [];
                _servicesByPid.Add(service.ProcessId, list);
                _hostPids.Add(service.ProcessId);
            }

            list.Add(service);
        }

        foreach (var pid in _hostPids)
            if (_processes.TryGetValue(pid, out var host) && host.CreationTicks > 0 &&
                host.CreationTicks <= catalog.ObservedFileTime)
                _hosts.Add(pid, host);
        var next = _nextOwners;
        next.Clear();
        foreach (var process in _processes.Values)
        {
            // Each step moves to a strictly older parent lifetime, so the walk cannot revisit a process.
            var current = process;
            while (true)
            {
                if (_servicesByPid.ContainsKey(current.ProcessId))
                {
                    if (_hosts.TryGetValue(current.ProcessId, out var host))
                        next[(process.ProcessId, process.CreationTicks)] = (host.ProcessId, host.CreationTicks);
                    break;
                }

                if (_owners.TryGetValue((current.ProcessId, current.CreationTicks), out var owner) &&
                    _hosts.TryGetValue(owner.Pid, out var oldHost) && oldHost.CreationTicks == owner.Created)
                {
                    next[(process.ProcessId, process.CreationTicks)] = owner;
                    break;
                }

                if (!_processes.TryGetValue(current.ParentProcessId, out var parent) || parent.CreationTicks <= 0 ||
                    current.CreationTicks <= 0 || parent.CreationTicks >= current.CreationTicks) break;
                current = parent;
            }
        }

        (_owners, _nextOwners) = (next, _owners);
        foreach (var process in _processes.Values)
        {
            if (!next.TryGetValue((process.ProcessId, process.CreationTicks), out var owner)) continue;
            if (!_members.TryGetValue(owner, out var list))
            {
                list = _spareMemberLists.TryPop(out var spare) ? spare : [];
                _members.Add(owner, list);
            }

            list.Add(process);
        }

        var groups = new List<ServiceGroup>(catalog.Services.Count);
        _nextHostGroups.Clear();
        _nextPendingGroups.Clear();
        _nextServiceGroups.Clear();
        foreach (var pid in _hostPids)
        {
            var services = _servicesByPid[pid];
            ServiceGroup group;
            if (_hosts.TryGetValue(pid, out var host))
            {
                var lifetime = (pid, host.CreationTicks);
                var previous = _hostGroups.GetValueOrDefault(lifetime);
                ProcessUsage[] owned = [];
                if (_members.TryGetValue(lifetime, out var members))
                {
                    members.Sort(ByProcessId);
                    owned = [.. members];
                }

                group = new ServiceGroup(previous?.Key ?? $"host:{pid}:{host.CreationTicks}",
                    SameServices(previous, services) ? previous!.Services : [.. services], host, owned);
                _nextHostGroups[lifetime] = group;
            }
            else
            {
                var previous = _pendingGroups.GetValueOrDefault(pid);
                group = SameServices(previous, services)
                    ? previous!
                    : new ServiceGroup($"pending:{pid}", [.. services], null, []);
                _nextPendingGroups[pid] = group;
            }

            groups.Add(group);
        }

        foreach (var service in catalog.Services)
        {
            if (service.HasHost) continue;
            if (!_serviceGroups.TryGetValue(service, out var group))
                group = new ServiceGroup("service:" + service.Name, [service], null, []);
            _nextServiceGroups[service] = group;
            groups.Add(group);
        }

        (_hostGroups, _nextHostGroups) = (_nextHostGroups, _hostGroups);
        (_pendingGroups, _nextPendingGroups) = (_nextPendingGroups, _pendingGroups);
        (_serviceGroups, _nextServiceGroups) = (_nextServiceGroups, _serviceGroups);
        return new ServiceFrame(moment, groups);
    }

    private void Reset()
    {
        _processes.Clear();
        foreach (var list in _servicesByPid.Values)
        {
            list.Clear();
            _spareServiceLists.Push(list);
        }

        _servicesByPid.Clear();
        _hostPids.Clear();
        _hosts.Clear();
        foreach (var list in _members.Values)
        {
            list.Clear();
            _spareMemberLists.Push(list);
        }

        _members.Clear();
    }

    // Observations are reused while a service is unchanged, so reference equality finds an unchanged host.
    private static bool SameServices(ServiceGroup? previous, List<ServiceObservation> services)
    {
        if (previous is null || previous.Services.Count != services.Count) return false;
        // ReSharper disable once LoopCanBeConvertedToQuery
        for (var i = 0; i < services.Count; i++)
            if (!ReferenceEquals(previous.Services[i], services[i]))
                return false;
        return true;
    }
}

/// <summary>Keep complete service frames separately from compacted process history, with an absolute frame cap.</summary>
internal sealed class ServiceHistory
{
    internal const int Seconds = 120;
    internal const int MaximumObservations = 120_000;
    private readonly List<ServiceFrame> _frames = [];
    private int _observations;
    internal IReadOnlyList<ServiceFrame> Frames => _frames;

    internal void Add(ServiceFrame frame)
    {
        _frames.Add(frame);
        _observations += Cost(frame);
        while (_frames.Count > 1 && (Age(_frames[0].Sample, frame.Sample) > Seconds || _frames.Count > Seconds + 1 ||
                                     _observations > MaximumObservations))
        {
            _observations -= Cost(_frames[0]);
            _frames.RemoveAt(0);
        }
    }

    private static int Cost(ServiceFrame frame)
    {
        return frame.Groups.Sum(g => g.Processes.Count + g.Services.Count);
    }

    private static double Age(CpuSample oldest, CpuSample newest)
    {
        return oldest.MonotonicTimestamp > 0 && newest.MonotonicTimestamp > 0
            ? (newest.MonotonicTimestamp - oldest.MonotonicTimestamp) / (double)Stopwatch.Frequency
            : (newest.Timestamp - oldest.Timestamp).TotalSeconds;
    }

    internal ServiceFrame? Find(CpuSample? sample)
    {
        return sample is null
            ? _frames.LastOrDefault()
            : _frames.LastOrDefault(f =>
                f.Sample.MonotonicTimestamp == sample.MonotonicTimestamp && f.Sample.Timestamp == sample.Timestamp);
    }
}