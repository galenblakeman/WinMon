namespace WinMon.Monitoring;

/// <summary>Five-second CPU/I/O averages; memory and membership are from the selected latest frame.</summary>
internal sealed record ServiceRanking(
    IReadOnlyDictionary<string, ServiceMetrics> Groups,
    IReadOnlyDictionary<(int Pid, long Created), ServiceMetrics> Processes,
    int IntervalCount)
{
    internal static ServiceRanking Build(IReadOnlyList<ServiceFrame> history, bool workers)
    {
        if (history.Count == 0) return new ServiceRanking(new Dictionary<string, ServiceMetrics>(),
            new Dictionary<(int, long), ServiceMetrics>(), 0);
        // Missing service discovery breaks the service window even if CPU collection continued.
        var samples = history.Select(f => f.Available ? f.Sample : f.Sample with { IsGap = true }).ToArray();
        var first = ProcessRanking.WindowStart(samples);
        var count = history.Count - first;
        var groups = new Dictionary<string, Totals>();
        var processes = new Dictionary<(int, long), Totals>();
        for (var i = first; i < history.Count; i++)
        {
            foreach (var group in history[i].Groups)
            {
                groups.TryGetValue(group.Key, out var groupTotal);
                groups[group.Key] = groupTotal.Add(group.Metrics(workers));
                foreach (var process in group.Processes)
                {
                    var key = (process.ProcessId, process.CreationTicks);
                    processes.TryGetValue(key, out var processTotal);
                    processes[key] = processTotal.Add(new ServiceMetrics(process.CpuPercent, process.PrivateBytes, process.IoBytesPerSecond));
                }
            }
        }
        var latest = history[^1];
        return new ServiceRanking(
            latest.Groups.ToDictionary(g => g.Key, g => groups.GetValueOrDefault(g.Key).Average(count, g.Metrics(workers).PrivateBytes)),
            latest.Groups.SelectMany(g => g.Processes).ToDictionary(p => (p.ProcessId, p.CreationTicks),
                p => processes.GetValueOrDefault((p.ProcessId, p.CreationTicks)).Average(count, p.PrivateBytes)), count);
    }

    private readonly record struct Totals(double Cpu, double Io, bool HasCpu, bool HasIo)
    {
        internal Totals Add(ServiceMetrics metrics) => new(Cpu + (metrics.Cpu ?? 0), Io + (metrics.Io ?? 0),
            HasCpu || metrics.Cpu.HasValue, HasIo || metrics.Io.HasValue);
        // Match ProcessRanking: divide by every interval in the window; no readable I/O remains unavailable.
        internal ServiceMetrics Average(int count, ulong? memory) => new(count > 0 && HasCpu ? Cpu / count : null,
            memory, count > 0 && HasIo ? Io / count : null);
    }
}
