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
    public static ProcessRanking Rank(IReadOnlyList<MonitorSample> history, int windowSeconds = DefaultWindowSeconds,
        ProcessSort sort = ProcessSort.Cpu, bool grouped = false, bool includeInactive = false)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        var window = new List<MonitorSample>(windowSeconds + 1);
        var first = WindowStart(history, windowSeconds);
        for (var i = history.Count - 1; i >= first; i--) window.Add(history[i]);
        if (window.Count == 0) return new ProcessRanking([], null, 0);

        // Reused on each thread: for ~1,000 processes, new collections would land on the large object heap on
        // every refresh. Cleared again on exit so they do not keep samples alive.
        var newest = _scratchNewest ??= [];
        var totals = _scratchTotals ??= [];
        var groups = grouped
            ? _scratchGroups ??= new Dictionary<string, List<Member>>(StringComparer.OrdinalIgnoreCase)
            : null;
        try
        {
            // Memory and counts are point-in-time readings: exclude processes that exited earlier in the window.
            foreach (var process in window[0].Processes) newest.Add((process.ProcessId, process.CreationTicks));
            // Newest first: the first entry seen for a process carries its latest name and memory.
            foreach (var sample in window)
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
                // Groups need every member, but only each group's busiest member becomes a row.
                if (groups is not null)
                {
                    var groupKey = GroupKey(latest);
                    if (!groups.TryGetValue(groupKey, out var members))
                    {
                        members = _spareMembers?.TryPop(out var spare) == true ? spare : [];
                        groups.Add(groupKey, members);
                    }

                    members.Add(new Member(latest, cpu, io, gpu, total.GpuLatest?.GpuId ?? "",
                        total.GpuLatest?.GpuEngine ?? "", newest.Contains(key)));
                    continue;
                }

                // Individually, skip processes the sort drops before copying them.
                if (!includeInactive && !Listed(sort, cpu, io, gpu, latest.WorkingSetBytes, newest.Contains(key),
                        latest.ThreadCount, latest.HandleCount)) continue;
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
            var ranked = (groups is not null
                    ? Group(groups).Where(p => includeInactive || Listed(sort, p.CpuPercent, p.IoBytesPerSecond,
                        p.GpuPercent, p.PrivateBytes, true, p.ThreadCount, p.HandleCount))
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
            return new ProcessRanking(ranked, window.Average(s => s.TotalCpuPercent!.Value), window.Count)
                { MeasuredCpuPercent = measuredCpu };
        }
        finally
        {
            newest.Clear();
            totals.Clear();
            if (groups is not null)
            {
                var spares = _spareMembers ??= [];
                foreach (var members in groups.Values)
                {
                    members.Clear();
                    spares.Push(members);
                }

                groups.Clear();
            }
        }
    }

    /// <summary>Average CPU of every measured process in the window, whatever the sort; zero with no measurable interval.</summary>
    public double MeasuredCpuPercent { get; init; }

    /// <summary>Shared interval selection for process and service rankings, including jitter and gap boundaries.</summary>
    public static int WindowStart(IReadOnlyList<MonitorSample> history, int windowSeconds = DefaultWindowSeconds)
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

    private record struct WindowTotals(
        ProcessUsage? Latest,
        double Cpu,
        double Io,
        int IoCount,
        double Gpu,
        int GpuCount,
        ProcessUsage? GpuLatest);

    /// <summary>Whether a process or group has a reading in the sort column; missing readings never become zero.</summary>
    private static bool Listed(ProcessSort sort, double cpu, double? io, double? gpu, ulong? memory, bool current,
        long? threads, long? handles)
    {
        return sort switch
        {
            ProcessSort.Gpu => gpu > 0,
            ProcessSort.Memory => memory > 0 && current,
            ProcessSort.Io => io > 0,
            ProcessSort.Threads => threads is not null && current,
            ProcessSort.Handles => handles is not null && current,
            _ => cpu > 0
        };
    }

    /// <summary>Executable identity for grouping: the full path, or the name when the path is unreadable.</summary>
    /// <remarks>A path always contains a backslash and a name never does, so the two kinds cannot collide.</remarks>
    public static string GroupKey(ProcessUsage process)
    {
        return process.ImagePath.Length > 0 ? process.ImagePath : process.Name;
    }

    [ThreadStatic] private static Dictionary<string, List<Member>>? _scratchGroups;
    [ThreadStatic] private static Stack<List<Member>>? _spareMembers;

    /// <summary>A process's window averages before grouping; <paramref name="Latest"/> is its newest observation.</summary>
    private readonly record struct Member(
        ProcessUsage Latest,
        double Cpu,
        double? Io,
        double? Gpu,
        string GpuId,
        string GpuEngine,
        bool Current);

    /// <summary>Combine window averages by executable. CPU and I/O add up; GPU members may use different
    /// engines, so a group reports its busiest member; memory sums the private bytes of current members.</summary>
    /// <remarks>Members keep window order, so ties pick the same busiest member as a stable maximum.</remarks>
    private static List<ProcessUsage> Group(Dictionary<string, List<Member>> groups)
    {
        var rows = new List<ProcessUsage>(groups.Count);
        foreach (var members in groups.Values)
        {
            var busiest = members[0];
            Member? gpu = null;
            double cpu = 0, io = 0;
            var hasIo = false;
            ulong workingSet = 0, privateBytes = 0;
            bool hasWorkingSet = false, hasPrivate = false, allThreads = true, allHandles = true;
            long threads = 0, handles = 0;
            var current = 0;
            // App-window state of all members and of current members: any app, and all background.
            bool anyApp = false, allBackground = true, anyCurrentApp = false, allCurrentBackground = true;
            var ids = new int[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var process = member.Latest;
                if (member.Cpu.CompareTo(busiest.Cpu) > 0) busiest = member;
                if (member.Gpu is { } percent && (gpu is null || percent.CompareTo(gpu.Value.Gpu!.Value) > 0))
                    gpu = member;
                cpu += member.Cpu;
                if (member.Io is { } rate)
                {
                    io += rate;
                    hasIo = true;
                }

                ids[i] = process.ProcessId;
                anyApp |= process.HasAppWindow == true;
                allBackground &= process.HasAppWindow == false;
                if (!member.Current) continue;
                current++;
                anyCurrentApp |= process.HasAppWindow == true;
                allCurrentBackground &= process.HasAppWindow == false;
                if (process.WorkingSetBytes is { } bytes)
                {
                    workingSet = checked(workingSet + bytes);
                    hasWorkingSet = true;
                }

                if (process.PrivateBytes is { } committed)
                {
                    privateBytes = checked(privateBytes + committed);
                    hasPrivate = true;
                }

                if (process.ThreadCount is { } count) threads = checked(threads + count);
                else allThreads = false;
                if (process.HandleCount is { } open) handles = checked(handles + open);
                else allHandles = false;
            }

            Array.Sort(ids);
            // A group is an app if any current member owns an app window, independent of which member is busiest.
            var (app, background) = current > 0 ? (anyCurrentApp, allCurrentBackground) : (anyApp, allBackground);
            rows.Add(busiest.Latest with
            {
                CpuPercent = cpu,
                IoBytesPerSecond = hasIo ? io : null,
                GpuPercent = gpu?.Gpu,
                GpuEngine = gpu?.GpuEngine ?? "",
                GpuId = gpu?.GpuId ?? "",
                WorkingSetBytes = hasWorkingSet ? workingSet : null,
                PrivateBytes = hasPrivate ? privateBytes : null,
                ThreadCount = current > 0 && allThreads ? threads : null,
                HandleCount = current > 0 && allHandles ? handles : null,
                MemberCount = members.Count,
                HasAppWindow = app ? true : background ? false : null,
                MemberIds = ids
            });
        }

        return rows;
    }

    /// <summary>Map an observation to monotonic seconds; support wall-time-only imported fixtures.</summary>
    public static double Moment(MonitorSample sample)
    {
        return sample.MonotonicTimestamp > 0
            ? sample.MonotonicTimestamp / (double)Stopwatch.Frequency
            : sample.Timestamp.UtcTicks / (double)TimeSpan.TicksPerSecond;
    }
}