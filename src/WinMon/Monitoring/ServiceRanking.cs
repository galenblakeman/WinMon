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
        // Reused on each thread and cleared on exit, like ProcessRanking's totals.
        var groups = _scratchGroups ??= [];
        var processes = _scratchProcesses ??= [];
        try
        {
            return Build(history, workers, first, count, groups, processes);
        }
        finally
        {
            groups.Clear();
            processes.Clear();
        }
    }

    [ThreadStatic] private static Dictionary<string, Totals>? _scratchGroups;
    [ThreadStatic] private static Dictionary<(int, long), Totals>? _scratchProcesses;

    private static ServiceRanking Build(IReadOnlyList<ServiceFrame> history, bool workers, int first, int count,
        Dictionary<string, Totals> groups, Dictionary<(int, long), Totals> processes)
    {
        for (var i = first; i < history.Count; i++)
        {
            var frame = history[i];
            for (var j = 0; j < frame.Groups.Count; j++)
            {
                var group = frame.Groups[j];
                groups.TryGetValue(group.Key, out var groupTotal);
                groups[group.Key] = groupTotal.Add(frame.Index.Metrics(j, workers));
                foreach (var process in group.Processes)
                {
                    var key = (process.ProcessId, process.CreationTicks);
                    processes.TryGetValue(key, out var processTotal);
                    processes[key] = processTotal.Add(process.CpuPercent, process.IoBytesPerSecond);
                }
            }
        }
        var latest = history[^1];
        // Add, like ToDictionary: a duplicate key is an error, not a silent choice.
        var groupMetrics = new Dictionary<string, ServiceMetrics>(latest.Groups.Count);
        var processMetrics = new Dictionary<(int, long), ServiceMetrics>();
        for (var j = 0; j < latest.Groups.Count; j++)
        {
            var group = latest.Groups[j];
            groupMetrics.Add(group.Key, groups.GetValueOrDefault(group.Key).Average(count, latest.Index.Metrics(j, workers).PrivateBytes));
            foreach (var process in group.Processes)
            {
                var key = (process.ProcessId, process.CreationTicks);
                processMetrics.Add(key, processes.GetValueOrDefault(key).Average(count, process.PrivateBytes));
            }
        }
        return new ServiceRanking(groupMetrics, processMetrics, count);
    }

    private readonly record struct Totals(double Cpu, double Io, bool HasCpu, bool HasIo)
    {
        internal Totals Add(ServiceMetrics metrics) => Add(metrics.Cpu, metrics.Io);
        internal Totals Add(double? cpu, double? io) => new(Cpu + (cpu ?? 0), Io + (io ?? 0),
            HasCpu || cpu.HasValue, HasIo || io.HasValue);
        // Match ProcessRanking: divide by every interval in the window; no readable I/O remains unavailable.
        internal ServiceMetrics Average(int count, ulong? memory) => new(count > 0 && HasCpu ? Cpu / count : null,
            memory, count > 0 && HasIo ? Io / count : null);
    }
}
