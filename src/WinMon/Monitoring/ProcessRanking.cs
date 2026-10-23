using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Column that orders the process list.</summary>
public enum ProcessSort
{
    /// <summary>Five-second average share of total machine CPU.</summary>
    Cpu,
    /// <summary>Busiest GPU engine, averaged over readable intervals.</summary>
    Gpu,
    /// <summary>Latest resident working set; only processes present in the newest sample.</summary>
    Memory,
    /// <summary>Five-second average read/write I/O.</summary>
    Io,
    /// <summary>Latest thread count of processes present in the newest sample.</summary>
    Threads,
    /// <summary>Latest open handle count of processes present in the newest sample.</summary>
    Handles
}

/// <summary>
/// Ranks processes by CPU, GPU, memory, I/O, threads or handles over a short window ending at one sample.
/// </summary>
/// <remarks>
/// Windows charges process CPU time in clock ticks (about 15.6 ms), so in any single one-second
/// interval most lightly active processes read exactly zero. Averaging a few intervals keeps the
/// ranked list stable, and a process stays listed while any interval in the window measured it.
/// </remarks>
/// <param name="Processes">Processes active in the selected metric, busiest first. Memory is the latest reading; I/O is averaged over the window and GPU over readable intervals.</param>
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
    /// <param name="sort">Ranking column; missing GPU, memory or I/O readings never become zero.</param>
    /// <param name="grouped">Combine processes of the same executable into one row each.</param>
    /// <param name="includeInactive">Include observed processes even without activity or a reading in the sort column, for search.</param>
    /// <returns>The ranking, empty when the last sample is a gap or has no CPU reading.</returns>
    public static ProcessRanking Rank(IReadOnlyList<CpuSample> history, int windowSeconds = DefaultWindowSeconds,
        ProcessSort sort = ProcessSort.Cpu, bool grouped = false, bool includeInactive = false)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        var window = new List<CpuSample>(windowSeconds + 1);
        var first = WindowStart(history, windowSeconds);
        for (var i = history.Count - 1; i >= first; i--) window.Add(history[i]);
        if (window.Count == 0) return new ProcessRanking([], null, 0);

        // Reused on each thread: for ~1,000 processes, new collections would land on the large object heap on
        // every refresh. Cleared again on exit so they do not keep samples alive.
        var newest = _scratchNewest ??= [];
        var totals = _scratchTotals ??= [];
        try
        {
            // Memory and counts are point-in-time readings: exclude processes that exited earlier in the window.
            foreach (var process in window[0].Processes) newest.Add((process.ProcessId, process.CreationTicks));
            // Newest first: the first entry seen for a process carries its latest name and memory.
            foreach (var sample in window)
            {
                foreach (var process in sample.Processes)
                {
                    var key = (process.ProcessId, process.CreationTicks);
                    totals.TryGetValue(key, out var total);
                    totals[key] = new WindowTotals(total.Latest ?? process,
                        total.Cpu + process.CpuPercent,
                        total.Io + (process.IoBytesPerSecond ?? 0),
                        total.IoCount + (process.IoBytesPerSecond is null ? 0 : 1),
                        total.Gpu + (process.GpuPercent ?? 0),
                        total.GpuCount + (process.GpuPercent is null ? 0 : 1),
                        total.GpuLatest ?? (process.GpuPercent is not null ? process : null));
                }
            }
            var measuredCpu = 0d;
            var individual = new List<ProcessUsage>();
            foreach (var (key, total) in totals)
            {
                var latest = total.Latest!;
                var cpu = total.Cpu / window.Count;
                measuredCpu += cpu;
                // Like CPU, intervals without an entry are idle (compacted) and count as zero.
                double? io = total.IoCount > 0 ? total.Io / window.Count : null;
                double? gpu = total.GpuCount > 0 ? total.Gpu / total.GpuCount : null;
                // Groups need every member. Individually, skip processes the sort drops before copying them.
                if (!includeInactive && !grouped && !Listed(sort, cpu, io, gpu, latest.WorkingSetBytes, newest.Contains(key), latest.ThreadCount, latest.HandleCount)) continue;
                individual.Add(latest with
                {
                    CpuPercent = cpu,
                    IoBytesPerSecond = io,
                    GpuPercent = gpu,
                    GpuId = total.GpuLatest?.GpuId ?? "",
                    GpuEngine = total.GpuLatest?.GpuEngine ?? ""
                });
            }
            // Groups sum private bytes (working sets share pages); a grouped row is current if any member is.
            var ranked = (grouped
                    ? Group(individual, newest).Where(p => includeInactive || Listed(sort, p.CpuPercent, p.IoBytesPerSecond, p.GpuPercent, p.PrivateBytes, true, p.ThreadCount, p.HandleCount))
                    : individual)
                .OrderByDescending(p => sort switch
                {
                    ProcessSort.Gpu => p.GpuPercent ?? 0,
                    ProcessSort.Memory => (grouped ? p.PrivateBytes : p.WorkingSetBytes) ?? 0,
                    ProcessSort.Io => p.IoBytesPerSecond ?? 0,
                    ProcessSort.Threads => p.ThreadCount ?? 0,
                    ProcessSort.Handles => p.HandleCount ?? 0,
                    _ => p.CpuPercent
                })
                .ThenBy(p => p.ProcessId)
                .ToArray();
            return new ProcessRanking(ranked, window.Average(s => s.TotalCpuPercent!.Value), window.Count) { MeasuredCpuPercent = measuredCpu };
        }
        finally
        {
            newest.Clear();
            totals.Clear();
        }
    }

    /// <summary>Average CPU of every measured process in the window, whatever the sort; zero with no measurable interval.</summary>
    public double MeasuredCpuPercent { get; init; }

    /// <summary>Shared interval selection for process and service rankings, including jitter and gap boundaries.</summary>
    internal static int WindowStart(IReadOnlyList<CpuSample> history, int windowSeconds = DefaultWindowSeconds)
    {
        var first = history.Count;
        if (first == 0) return first;
        // Half a second of slack keeps timer jitter from adding or dropping an interval.
        var start = Moment(history[^1]) - (windowSeconds - 0.5);
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var sample = history[i];
            if (Moment(sample) < start || sample.IsGap || sample.TotalCpuPercent is null) break;
            if (i < history.Count - 1 && Moment(history[i + 1]) - Moment(sample) > GapSeconds) break;
            first = i;
        }
        return first;
    }

    [ThreadStatic] private static HashSet<(int, long)>? _scratchNewest;
    [ThreadStatic] private static Dictionary<(int, long), WindowTotals>? _scratchTotals;

    private record struct WindowTotals(ProcessUsage? Latest, double Cpu, double Io, int IoCount, double Gpu, int GpuCount, ProcessUsage? GpuLatest);

    /// <summary>Whether a process or group has a reading in the sort column; missing readings never become zero.</summary>
    private static bool Listed(ProcessSort sort, double cpu, double? io, double? gpu, ulong? memory, bool current,
        long? threads, long? handles) => sort switch
    {
        ProcessSort.Gpu => gpu > 0,
        ProcessSort.Memory => memory > 0 && current,
        ProcessSort.Io => io > 0,
        ProcessSort.Threads => threads is not null && current,
        ProcessSort.Handles => handles is not null && current,
        _ => cpu > 0
    };

    /// <summary>Executable identity for grouping: the full path, or the name when the path is unreadable.</summary>
    /// <remarks>A path always contains a backslash and a name never does, so the two kinds cannot collide.</remarks>
    internal static string GroupKey(ProcessUsage process) => process.ImagePath.Length > 0 ? process.ImagePath : process.Name;

    /// <summary>Combine window averages by executable. CPU and I/O add up; GPU members may use different
    /// engines, so a group reports its busiest member; memory sums the private bytes of current members.</summary>
    private static IEnumerable<ProcessUsage> Group(IEnumerable<ProcessUsage> processes, HashSet<(int, long)> newest)
    {
        return from members in processes.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase) select members.ToArray() into all let busiest = all.MaxBy(p => p.CpuPercent)! let gpu = all.Where(p => p.GpuPercent is not null).MaxBy(p => p.GpuPercent) let current = all.Where(p => newest.Contains((p.ProcessId, p.CreationTicks))).ToArray() select busiest with
        {
            CpuPercent = all.Sum(p => p.CpuPercent),
            IoBytesPerSecond = all.Any(p => p.IoBytesPerSecond is not null) ? all.Sum(p => p.IoBytesPerSecond ?? 0) : null,
            GpuPercent = gpu?.GpuPercent,
            GpuEngine = gpu?.GpuEngine ?? "",
            GpuId = gpu?.GpuId ?? "",
            WorkingSetBytes = current.Any(p => p.WorkingSetBytes is not null) ? (ulong)current.Sum(p => (decimal)(p.WorkingSetBytes ?? 0)) : null,
            PrivateBytes = current.Any(p => p.PrivateBytes is not null) ? (ulong)current.Sum(p => (decimal)(p.PrivateBytes ?? 0)) : null,
            ThreadCount = current.Length > 0 && current.All(p => p.ThreadCount is not null) ? current.Sum(p => p.ThreadCount!.Value) : null,
            HandleCount = current.Length > 0 && current.All(p => p.HandleCount is not null) ? current.Sum(p => p.HandleCount!.Value) : null,
            MemberCount = all.Length,
            HasAppWindow = AppWindowState(current.Length > 0 ? current : all),
            MemberIds = [.. all.Select(p => p.ProcessId).Order()]
        };
    }

    // A group is an app if any current member owns an app window, independent of which member is busiest.
    private static bool? AppWindowState(ProcessUsage[] members) => members.Any(p => p.HasAppWindow == true)
        ? true : members.All(p => p.HasAppWindow == false) ? false : null;

    /// <summary>Map an observation to monotonic seconds; support wall-time-only imported fixtures.</summary>
    internal static double Moment(CpuSample sample) => sample.MonotonicTimestamp > 0
        ? sample.MonotonicTimestamp / (double)Stopwatch.Frequency
        : sample.Timestamp.UtcTicks / (double)TimeSpan.TicksPerSecond;
}
