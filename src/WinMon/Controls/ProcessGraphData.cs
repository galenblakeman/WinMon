using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process values shared by sparkline scaling and drawing for one refresh.</summary>
internal sealed record ProcessGraphData(IReadOnlyList<CpuSample> Samples, double?[] Values, double ScaleMaximum)
{
    internal static ProcessGraphData[] Build(IReadOnlyList<CpuSample> history, int seconds,
        IReadOnlyList<ProcessUsage> identities)
    {
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
        }
        return identities.Select((_, i) => new ProcessGraphData(visible, values[i],
            Math.Clamp(Math.Ceiling(peaks[i] * 11) / 10, 0.1, 100))).ToArray();
    }
}
