using System.Runtime.CompilerServices;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process values and the shared sparkline scale for one refresh.</summary>
internal sealed record ProcessGraphData(
    IReadOnlyList<MonitorSample> Samples,
    double?[] Values,
    double ScaleMaximum,
    ProcessSort Sort = ProcessSort.Cpu)
{
    /// <summary>
    ///     The previous refresh's rows. Each refresh moves the window by about one sample, and a row's value for a
    ///     sample depends only on that sample, so rows seen last time copy the values of samples both windows share.
    /// </summary>
    internal sealed class Reuse
    {
        internal MonitorSample[] Visible = [];
        internal ProcessSort Sort;
        internal bool Grouped;
        internal readonly Dictionary<ProcessLifetimeIdentity, double?[]> Individual = [];
        // A group's idle fill depends on its representative's start, which changes under the same key, so groups
        // also keep which samples observed them and redo the fill each refresh.
        internal readonly Dictionary<string, (double?[] Values, bool[] Observed)> Groups =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal static ProcessGraphData[] Build(IReadOnlyList<MonitorSample> history, int seconds,
        IReadOnlyList<ProcessUsage> identities, bool grouped = false, ProcessSort sort = ProcessSort.Cpu,
        Reuse? reuse = null)
    {
        if (grouped) return BuildGrouped(history, seconds, identities, sort, reuse);
        if (identities.Count == 0) return [];
        var end = history.Count > 0 ? ProcessRanking.Moment(history[^1]) : 0;
        var visible = history.Where(s => ProcessRanking.Moment(s) >= end - seconds).ToArray();
        var indices = identities.Select((p, i) => (Key: p.Identity, Index: i))
            .ToDictionary(p => p.Key, p => p.Index);
        var shared = Shared(reuse, visible, sort, false, out var shift);
        var values = new double?[identities.Count][];
        var cached = new bool[identities.Count];
        var uncached = identities.Count;
        for (var i = 0; i < identities.Count; i++)
        {
            values[i] = new double?[visible.Length];
            if (shared == 0 || !reuse!.Individual.TryGetValue(identities[i].Identity, out var previous)) continue;
            Array.Copy(previous, shift, values[i], 0, shared);
            cached[i] = true;
            uncached--;
        }

        var observed = new bool[identities.Count];
        for (var sampleIndex = uncached == 0 ? shared : 0; sampleIndex < visible.Length; sampleIndex++)
        {
            var sample = visible[sampleIndex];
            if (sample.IsGap) continue;
            var copied = sampleIndex < shared;
            Array.Clear(observed);
            var remaining = copied ? uncached : identities.Count;
            var processes = sample.Processes;
            for (var p = 0; p < processes.Count; p++)
            {
                var process = processes[p];
                if (!indices.TryGetValue(process.Identity, out var index) || copied && cached[index]) continue;
                // Keep the first imported entry, including an explicitly unavailable reading.
                if (observed[index]) continue;
                observed[index] = true;
                values[index][sampleIndex] = Value(process, sort, false);
                if (--remaining == 0) break;
            }

            // Compaction establishes idle CPU/I/O, but cannot recover memory or unavailable GPU readings.
            if (sample.CompactedProcesses == 0 || sort is not (ProcessSort.Cpu or ProcessSort.Io)) continue;
            var sampleTime = FileTime(sample);
            for (var index = 0; index < identities.Count; index++)
                if (!observed[index] && !(copied && cached[index]) && values[index][sampleIndex] is null &&
                    identities[index].LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime &&
                    identities[index].CreationTicks <= sampleTime)
                    values[index][sampleIndex] = 0;
        }

        if (reuse is not null)
        {
            Remember(reuse, visible, sort, false);
            for (var i = 0; i < identities.Count; i++) reuse.Individual[identities[i].Identity] = values[i];
        }

        // One scale for every row so sparkline heights compare across processes.
        var scale = Scale(Peak(values), sort);
        return [.. values.Select(rowValues => new ProcessGraphData(visible, rowValues, scale, sort))];
    }

    /// <summary>Aggregate each executable group per sample, using its busiest member for GPU.</summary>
    private static ProcessGraphData[] BuildGrouped(IReadOnlyList<MonitorSample> history, int seconds,
        IReadOnlyList<ProcessUsage> groups, ProcessSort sort, Reuse? reuse)
    {
        if (groups.Count == 0) return [];
        var end = history.Count > 0 ? ProcessRanking.Moment(history[^1]) : 0;
        var visible = history.Where(s => ProcessRanking.Moment(s) >= end - seconds).ToArray();
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Count; i++) indices.TryAdd(ProcessRanking.GroupKey(groups[i]), i);
        var shared = Shared(reuse, visible, sort, true, out var shift);
        var values = new double?[groups.Count][];
        var observed = new bool[groups.Count][];
        var cached = new bool[groups.Count];
        var uncached = groups.Count;
        for (var i = 0; i < groups.Count; i++)
        {
            values[i] = new double?[visible.Length];
            observed[i] = new bool[visible.Length];
            // A duplicate key never receives values, so only the row that owns the key may copy them.
            var key = ProcessRanking.GroupKey(groups[i]);
            if (shared == 0 || indices[key] != i || !reuse!.Groups.TryGetValue(key, out var previous)) continue;
            Array.Copy(previous.Observed, shift, observed[i], 0, shared);
            for (var s = 0; s < shared; s++)
                if (observed[i][s]) values[i][s] = previous.Values[shift + s];
            cached[i] = true;
            uncached--;
        }

        var missingCounts = sort is ProcessSort.Cpu or ProcessSort.Threads or ProcessSort.Handles
            ? new bool[groups.Count] : null;
        for (var sampleIndex = uncached == 0 ? shared : 0; sampleIndex < visible.Length; sampleIndex++)
        {
            var sample = visible[sampleIndex];
            if (sample.IsGap) continue;
            var copied = sampleIndex < shared;
            if (missingCounts is not null) Array.Clear(missingCounts);
            var processes = sample.Processes;
            for (var p = 0; p < processes.Count; p++)
            {
                var process = processes[p];
                if (!indices.TryGetValue(ProcessRanking.GroupKey(process), out var index) ||
                    copied && cached[index]) continue;
                observed[index][sampleIndex] = true;
                if (Value(process, sort, true) is not { } value)
                {
                    missingCounts?[index] = true;
                    continue;
                }

                values[index][sampleIndex] = sort == ProcessSort.Gpu
                    ? Math.Max(values[index][sampleIndex] ?? 0, value)
                    : (values[index][sampleIndex] ?? 0) + value;
            }

            if (missingCounts is null) continue;
            for (var index = 0; index < groups.Count; index++)
                if (missingCounts[index] && !(copied && cached[index])) values[index][sampleIndex] = null;
        }

        // Compaction establishes idle CPU/I/O for groups absent from a sample that started before it.
        if (sort is ProcessSort.Cpu or ProcessSort.Io)
            for (var sampleIndex = 0; sampleIndex < visible.Length; sampleIndex++)
            {
                var sample = visible[sampleIndex];
                if (sample.IsGap || sample.CompactedProcesses == 0) continue;
                var sampleTime = FileTime(sample);
                for (var index = 0; index < groups.Count; index++)
                    if (!observed[index][sampleIndex] &&
                        groups[index].LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime &&
                        groups[index].CreationTicks <= sampleTime) values[index][sampleIndex] = 0;
            }

        if (reuse is not null)
        {
            Remember(reuse, visible, sort, true);
            foreach (var (key, index) in indices) reuse.Groups[key] = (values[index], observed[index]);
        }

        var scale = Scale(Peak(values), sort);
        return [.. values.Select(rowValues => new ProcessGraphData(visible, rowValues, scale, sort))];
    }

    /// <summary>How many leading samples of <paramref name="visible" /> the previous window also held, from <paramref name="shift" />.</summary>
    private static int Shared(Reuse? reuse, MonitorSample[] visible, ProcessSort sort, bool grouped, out int shift)
    {
        shift = 0;
        if (reuse is null || reuse.Sort != sort || reuse.Grouped != grouped || visible.Length == 0) return 0;
        var previous = reuse.Visible;
        // References, not record equality: the newest sample is replaced by its compacted copy next refresh.
        while (shift < previous.Length && !ReferenceEquals(previous[shift], visible[0])) shift++;
        var count = 0;
        while (shift + count < previous.Length && count < visible.Length &&
               ReferenceEquals(previous[shift + count], visible[count])) count++;
        return count;
    }

    private static void Remember(Reuse reuse, MonitorSample[] visible, ProcessSort sort, bool grouped)
    {
        reuse.Visible = visible;
        reuse.Sort = sort;
        reuse.Grouped = grouped;
        reuse.Individual.Clear();
        reuse.Groups.Clear();
    }

    private static double Peak(double?[][] values)
    {
        var peak = 0d;
        foreach (var row in values)
        foreach (var value in row)
            if (value > peak) peak = value.Value;
        return peak;
    }

    // DateTimeOffset.ToFileTime makes a leap-second-aware system call on current Windows, and every refresh
    // revisits the same samples. Samples are immutable, so each is converted once.
    private static readonly ConditionalWeakTable<MonitorSample, object> FileTimes = new();

    private static long FileTime(MonitorSample sample)
    {
        return (long)FileTimes.GetValue(sample, static s => s.Timestamp.ToFileTime());
    }

    private static double? Value(ProcessUsage process, ProcessSort sort, bool grouped)
    {
        var value = sort switch
        {
            ProcessSort.Gpu => process.GpuPercent,
            ProcessSort.Memory => grouped ? process.PrivateBytes : process.WorkingSetBytes,
            ProcessSort.Io => process.IoBytesPerSecond,
            ProcessSort.Threads => process.ThreadCount,
            ProcessSort.Handles => process.HandleCount,
            _ => process.ObservedCpuPercent
        };
        return value is { } measured && double.IsFinite(measured) ? Math.Max(0, measured) : null;
    }

    private static double Scale(double peak, ProcessSort sort)
    {
        return sort is ProcessSort.Cpu or ProcessSort.Gpu
            ? Math.Clamp(Math.Ceiling(peak * 11) / 10, 0.1, 100)
            : Math.Max(1, peak * 1.1);
    }
}
