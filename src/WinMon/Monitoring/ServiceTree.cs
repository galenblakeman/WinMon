using System.Diagnostics;

namespace WinMon.Monitoring;

internal sealed record ServiceMetrics(double? Cpu, ulong? PrivateBytes, double? Io)
{
    internal static ServiceMetrics Sum(IReadOnlyList<ProcessUsage> processes) => processes.Count == 0
        ? new(null, null, null)
        : new(processes.Sum(p => p.CpuPercent),
            processes.All(p => p.PrivateBytes.HasValue) ? processes.Aggregate(0UL, (sum, p) => sum + p.PrivateBytes!.Value) : null,
            processes.All(p => p.IoBytesPerSecond.HasValue) ? processes.Sum(p => p.IoBytesPerSecond!.Value) : null);
}

internal sealed record ServiceGroup(string Key, IReadOnlyList<ServiceObservation> Services, ProcessUsage? Host,
    IReadOnlyList<ProcessUsage> Processes)
{
    internal bool Shared => Services.Count > 1;
    internal ServiceMetrics Metrics(bool workers) => ServiceMetrics.Sum(workers ? Processes : Host is null ? [] : [Host]);
}

internal sealed record ServiceFrame(CpuSample Sample, IReadOnlyList<ServiceGroup> Groups, bool Available = true);

/// <summary>Observed ancestry, bounded to live lifetimes and current service hosts. Never groups by executable name.</summary>
internal sealed class ServiceTree
{
    private Dictionary<(int Pid, long Created), (int Pid, long Created)> _owners = [];

    internal ServiceFrame Capture(CpuSample sample, ServiceCatalog? catalog)
    {
        var moment = sample with { Processes = [] };
        if (catalog is null || sample.IsGap)
        {
            _owners.Clear();
            return new ServiceFrame(moment, [], false);
        }
        var processes = sample.Processes.ToDictionary(p => p.ProcessId);
        var servicesByPid = catalog.Services.Where(s => s.HasHost).GroupBy(s => s.ProcessId).ToDictionary(g => g.Key, g => g.ToArray());
        var hosts = servicesByPid.Keys.Where(pid => processes.TryGetValue(pid, out var p) && p.CreationTicks > 0 &&
            p.CreationTicks <= catalog.ObservedFileTime).ToDictionary(pid => pid, pid => processes[pid]);
        var next = new Dictionary<(int, long), (int, long)>();
        foreach (var process in processes.Values)
        {
            var visited = new HashSet<int>();
            var current = process;
            while (visited.Add(current.ProcessId))
            {
                if (servicesByPid.ContainsKey(current.ProcessId))
                {
                    if (hosts.TryGetValue(current.ProcessId, out var host))
                        next[(process.ProcessId, process.CreationTicks)] = (host.ProcessId, host.CreationTicks);
                    break;
                }
                if (_owners.TryGetValue((current.ProcessId, current.CreationTicks), out var owner) &&
                    hosts.TryGetValue(owner.Pid, out var oldHost) && oldHost.CreationTicks == owner.Created)
                {
                    next[(process.ProcessId, process.CreationTicks)] = owner;
                    break;
                }
                if (!processes.TryGetValue(current.ParentProcessId, out var parent) || parent.CreationTicks <= 0 ||
                    current.CreationTicks <= 0 || parent.CreationTicks >= current.CreationTicks) break;
                current = parent;
            }
        }
        _owners = next;
        var groups = new List<ServiceGroup>();
        var members = processes.Values.Where(p => next.ContainsKey((p.ProcessId, p.CreationTicks)))
            .GroupBy(p => next[(p.ProcessId, p.CreationTicks)]).ToDictionary(g => g.Key, g => g.OrderBy(p => p.ProcessId).ToArray());
        foreach (var pair in servicesByPid)
        {
            hosts.TryGetValue(pair.Key, out var host);
            var key = host is null ? $"pending:{pair.Key}" : $"host:{pair.Key}:{host.CreationTicks}";
            groups.Add(new ServiceGroup(key, pair.Value, host,
                host is not null && members.TryGetValue((host.ProcessId, host.CreationTicks), out var owned) ? owned : []));
        }
        foreach (var service in catalog.Services.Where(s => !s.HasHost))
            groups.Add(new ServiceGroup("service:" + service.Name, [service], null, []));
        return new ServiceFrame(moment, groups);
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
        while (_frames.Count > 1 && (Age(_frames[0].Sample, frame.Sample) > Seconds || _frames.Count > Seconds + 1 || _observations > MaximumObservations))
        {
            _observations -= Cost(_frames[0]);
            _frames.RemoveAt(0);
        }
    }
    private static int Cost(ServiceFrame frame) => frame.Groups.Sum(g => g.Processes.Count + g.Services.Count);
    private static double Age(CpuSample oldest, CpuSample newest) => oldest.MonotonicTimestamp > 0 && newest.MonotonicTimestamp > 0
        ? (newest.MonotonicTimestamp - oldest.MonotonicTimestamp) / (double)Stopwatch.Frequency
        : (newest.Timestamp - oldest.Timestamp).TotalSeconds;
    internal ServiceFrame? Find(CpuSample? sample) => sample is null ? _frames.LastOrDefault() :
        _frames.LastOrDefault(f => f.Sample.MonotonicTimestamp == sample.MonotonicTimestamp && f.Sample.Timestamp == sample.Timestamp);
}
