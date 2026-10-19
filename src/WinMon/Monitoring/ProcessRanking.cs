using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>
/// Ranks processes by average CPU or GPU over a short window ending at one sample.
/// </summary>
/// <remarks>
/// Windows charges process CPU time in clock ticks (about 15.6 ms), so in any single one-second
/// interval most lightly active processes read exactly zero. Averaging a few intervals keeps the
/// ranked list stable, and a process stays listed while any interval in the window measured it.
/// </remarks>
/// <param name="Processes">Processes active in the selected metric, busiest first. Memory is the latest reading; I/O and GPU are averaged where readable.</param>
/// <param name="TotalCpuPercent">Average total machine CPU over the same intervals, or <see langword="null"/> with no measurable interval.</param>
/// <param name="IntervalCount">Number of measured intervals averaged.</param>
public sealed record ProcessRanking(IReadOnlyList<ProcessUsage> Processes, double? TotalCpuPercent, int IntervalCount)
{
    /// <summary>Default averaging window in seconds.</summary>
    public const int DefaultWindowSeconds = 5;

    // Consecutive samples further apart than this are a recording gap, matching the chart.
    private const double GapSeconds = 2.5;

    /// <summary>Rank processes over the window ending at the last of <paramref name="history"/>.</summary>
    /// <param name="history">Chronological samples; the last one is the live or selected sample.</param>
    /// <param name="windowSeconds">Averaging window; with one-second sampling it covers that many intervals.</param>
    /// <param name="sortByGpu">Rank by GPU instead of CPU; missing GPU readings do not become zero.</param>
    /// <returns>The ranking, empty when the last sample is a gap or has no CPU reading.</returns>
    public static ProcessRanking Rank(IReadOnlyList<CpuSample> history, int windowSeconds = DefaultWindowSeconds, bool sortByGpu = false)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        var window = new List<CpuSample>(windowSeconds + 1);
        if (history.Count > 0)
        {
            var end = Moment(history[^1]);
            // Each sample covers about one second ending at its timestamp, so half a second of slack
            // keeps timer jitter from adding or dropping an interval.
            var start = end - (windowSeconds - 0.5);
            for (var i = history.Count - 1; i >= 0; i--)
            {
                var sample = history[i];
                if (Moment(sample) < start || sample.IsGap || sample.TotalCpuPercent is null) break;
                if (window.Count > 0 && Moment(window[^1]) - Moment(sample) > GapSeconds) break;
                window.Add(sample);
            }
        }
        if (window.Count == 0) return new ProcessRanking([], null, 0);

        var totals = new Dictionary<(int, long), (ProcessUsage Latest, double Cpu, double Io, int IoCount, double Gpu, int GpuCount, ProcessUsage? GpuLatest)>();
        // Newest first: the first entry seen for a process carries its latest name and memory.
        foreach (var sample in window)
        {
            foreach (var process in sample.Processes)
            {
                var key = (process.ProcessId, process.CreationTicks);
                totals.TryGetValue(key, out var total);
                totals[key] = (total.Latest ?? process,
                    total.Cpu + process.CpuPercent,
                    total.Io + (process.IoBytesPerSecond ?? 0),
                    total.IoCount + (process.IoBytesPerSecond is null ? 0 : 1),
                    total.Gpu + (process.GpuPercent ?? 0),
                    total.GpuCount + (process.GpuPercent is null ? 0 : 1),
                    total.GpuLatest ?? (process.GpuPercent is not null ? process : null));
            }
        }
        var ranked = totals.Values
            .Select(t => t.Latest with
            {
                CpuPercent = t.Cpu / window.Count,
                IoBytesPerSecond = t.IoCount > 0 ? t.Io / t.IoCount : null,
                GpuPercent = t.GpuCount > 0 ? t.Gpu / t.GpuCount : null,
                GpuId = t.GpuLatest?.GpuId ?? "",
                GpuEngine = t.GpuLatest?.GpuEngine ?? ""
            })
            .Where(p => sortByGpu ? p.GpuPercent > 0 : p.CpuPercent > 0)
            .OrderByDescending(p => sortByGpu ? p.GpuPercent ?? 0 : p.CpuPercent)
            .ThenBy(p => p.ProcessId)
            .ToArray();
        return new ProcessRanking(ranked, window.Average(s => s.TotalCpuPercent!.Value), window.Count);
    }

    /// <summary>Map an observation to monotonic seconds; support wall-time-only imported fixtures.</summary>
    internal static double Moment(CpuSample sample) => sample.MonotonicTimestamp > 0
        ? sample.MonotonicTimestamp / (double)Stopwatch.Frequency
        : sample.Timestamp.UtcTicks / (double)TimeSpan.TicksPerSecond;
}
