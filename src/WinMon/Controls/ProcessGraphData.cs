using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process values and the shared sparkline scale for one refresh.</summary>
internal sealed record ProcessGraphData(IReadOnlyList<CpuSample> Samples, double?[] Values, double ScaleMaximum,
    ProcessSort Sort = ProcessSort.Cpu)
{
    internal static ProcessGraphData[] Build(IReadOnlyList<CpuSample> history, int seconds,
        IReadOnlyList<ProcessUsage> identities, bool grouped = false, ProcessSort sort = ProcessSort.Cpu)
    {
        if (grouped) return BuildGrouped(history, seconds, identities, sort);
        if (identities.Count == 0) return [];
        var end = history.Count > 0 ? CpuGraph.Moment(history[^1]) : 0;
        var visible = history.Where(s => CpuGraph.Moment(s) >= end - seconds).ToArray();
        var indices = identities.Select((p, i) => (Key: (p.ProcessId, p.CreationTicks), Index: i))
            .ToDictionary(p => p.Key, p => p.Index);
        var values = identities.Select(_ => new double?[visible.Length]).ToArray();
        var peaks = new double[identities.Count];
        for (var sampleIndex = 0; sampleIndex < visible.Length; sampleIndex++)
        {
            var sample = visible[sampleIndex];
            if (sample.IsGap) continue;
            var remaining = identities.Count;
            foreach (var process in sample.Processes)
            {
                if (!indices.TryGetValue((process.ProcessId, process.CreationTicks), out var index)) continue;
                // Match the former FirstOrDefault behavior even for duplicate imported entries.
                if (values[index][sampleIndex].HasValue) continue;
                values[index][sampleIndex] = Value(process, sort, false);
                if (values[index][sampleIndex] is { } value)
                    peaks[index] = Math.Max(peaks[index], value);
                if (--remaining == 0) break;
            }
            // Compaction establishes idle CPU/I/O, but cannot recover memory or unavailable GPU readings.
            if (sample.CompactedProcesses == 0 || sort is not (ProcessSort.Cpu or ProcessSort.Io)) continue;
            var sampleTime = sample.Timestamp.ToFileTime();
            for (var index = 0; index < identities.Count; index++)
                if (values[index][sampleIndex] is null && identities[index].CreationTicks <= sampleTime)
                    values[index][sampleIndex] = 0;
        }
        // One scale for every row so sparkline heights compare across processes.
        var scale = Scale(peaks.Max(), sort);
        return [.. identities.Select((_, i) => new ProcessGraphData(visible, values[i], scale, sort))];
    }

    /// <summary>Aggregate each executable group per sample, using its busiest member for GPU.</summary>
    private static ProcessGraphData[] BuildGrouped(IReadOnlyList<CpuSample> history, int seconds,
        IReadOnlyList<ProcessUsage> groups, ProcessSort sort)
    {
        if (groups.Count == 0) return [];
        var end = history.Count > 0 ? CpuGraph.Moment(history[^1]) : 0;
        var visible = history.Where(s => CpuGraph.Moment(s) >= end - seconds).ToArray();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Count; i++) indices.TryAdd(ProcessRanking.GroupKey(groups[i]), i);
        var values = groups.Select(_ => new double?[visible.Length]).ToArray();
        var peaks = new double[groups.Count];
        for (var sampleIndex = 0; sampleIndex < visible.Length; sampleIndex++)
        {
            var sample = visible[sampleIndex];
            if (sample.IsGap) continue;
            foreach (var process in sample.Processes)
            {
                if (!indices.TryGetValue(ProcessRanking.GroupKey(process), out var index) || Value(process, sort, true) is not { } value) continue;
                values[index][sampleIndex] = sort == ProcessSort.Gpu
                    ? Math.Max(values[index][sampleIndex] ?? 0, value)
                    : (values[index][sampleIndex] ?? 0) + value;
            }
            var sampleTime = sample.Timestamp.ToFileTime();
            for (var index = 0; index < groups.Count; index++)
            {
                if (values[index][sampleIndex] is { } total) peaks[index] = Math.Max(peaks[index], total);
                else if (sort is ProcessSort.Cpu or ProcessSort.Io && sample.CompactedProcesses > 0 && groups[index].CreationTicks <= sampleTime) values[index][sampleIndex] = 0;
            }
        }
        var scale = Scale(peaks.Max(), sort);
        return [.. groups.Select((_, i) => new ProcessGraphData(visible, values[i], scale, sort))];
    }

    private static double? Value(ProcessUsage process, ProcessSort sort, bool grouped)
    {
        double? value = sort switch
        {
            ProcessSort.Gpu => process.GpuPercent,
            ProcessSort.Memory => grouped ? process.PrivateBytes : process.WorkingSetBytes,
            ProcessSort.Io => process.IoBytesPerSecond,
            _ => process.CpuPercent
        };
        return value is { } measured && double.IsFinite(measured) ? Math.Max(0, measured) : null;
    }

    private static double Scale(double peak, ProcessSort sort) => sort is ProcessSort.Cpu or ProcessSort.Gpu
        ? Math.Clamp(Math.Ceiling(peak * 11) / 10, 0.1, 100)
        : Math.Max(1, peak * 1.1);
}
