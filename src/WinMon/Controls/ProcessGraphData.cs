using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process values and the shared sparkline scale for one refresh.</summary>
internal sealed record ProcessGraphData(IReadOnlyList<CpuSample> Samples, double?[] Values, double ScaleMaximum)
{
    internal static ProcessGraphData[] Build(IReadOnlyList<CpuSample> history, int seconds,
        IReadOnlyList<ProcessUsage> identities, bool grouped = false)
    {
        if (grouped) return BuildGrouped(history, seconds, identities);
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
                values[index][sampleIndex] = process.CpuPercent;
                if (double.IsFinite(process.CpuPercent))
                    peaks[index] = Math.Max(peaks[index], Math.Clamp(process.CpuPercent, 0, 100));
                if (--remaining == 0) break;
            }
            // Idle processes are compacted out of retained samples; one created before the sample was idle, so zero.
            if (sample.CompactedProcesses == 0) continue;
            var sampleTime = sample.Timestamp.ToFileTime();
            for (var index = 0; index < identities.Count; index++)
                if (values[index][sampleIndex] is null && identities[index].CreationTicks <= sampleTime)
                    values[index][sampleIndex] = 0;
        }
        // One scale for every row so sparkline heights compare across processes.
        var scale = Math.Clamp(Math.Ceiling(peaks.Max() * 11) / 10, 0.1, 100);
        return [.. identities.Select((_, i) => new ProcessGraphData(visible, values[i], scale))];
    }

    /// <summary>Sum each executable group's members per sample; a sample with no member present is a gap,
    /// unless it was compacted after the group's representative started, which makes it idle.</summary>
    private static ProcessGraphData[] BuildGrouped(IReadOnlyList<CpuSample> history, int seconds, IReadOnlyList<ProcessUsage> groups)
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
                if (!indices.TryGetValue(ProcessRanking.GroupKey(process), out var index) || !double.IsFinite(process.CpuPercent)) continue;
                values[index][sampleIndex] = (values[index][sampleIndex] ?? 0) + process.CpuPercent;
            }
            var sampleTime = sample.Timestamp.ToFileTime();
            for (var index = 0; index < groups.Count; index++)
            {
                if (values[index][sampleIndex] is { } total) peaks[index] = Math.Max(peaks[index], Math.Clamp(total, 0, 100));
                else if (sample.CompactedProcesses > 0 && groups[index].CreationTicks <= sampleTime) values[index][sampleIndex] = 0;
            }
        }
        var scale = Math.Clamp(Math.Ceiling(peaks.Max() * 11) / 10, 0.1, 100);
        return [.. groups.Select((_, i) => new ProcessGraphData(visible, values[i], scale))];
    }
}
