namespace WinMon.Monitoring;

/// <summary>On-demand analysis of a frozen set of retained observations.</summary>
public static class IncidentTimeline
{
    /// <summary>Find the latest highest valid observation, without bridging recording gaps.</summary>
    public static MonitorSample? Peak(IReadOnlyList<MonitorSample> samples, CaptureMetric metric)
    {
        MonitorSample? peak = null;
        var highest = double.NegativeInfinity;
        foreach (var sample in samples)
            if (!sample.IsGap && CaptureRules.Value(metric, sample) is { } value &&
                double.IsFinite(value) && value >= highest)
            {
                highest = value;
                peak = sample;
            }
        return peak;
    }

    /// <summary>Rank retained processes by the selected resource; network falls back to CPU.</summary>
    public static IReadOnlyList<ProcessUsage> Contributors(MonitorSample sample, CaptureMetric metric)
    {
        // shortcut: network ranks CPU until retained process traffic is joined into this view.
        return sample.Processes.OrderByDescending(process => metric switch
        {
            CaptureMetric.Memory => process.WorkingSetBytes,
            CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk => process.IoBytesPerSecond,
            CaptureMetric.Gpu => process.GpuPercent,
            _ => process.ObservedCpuPercent
        }).ThenBy(process => process.ProcessId).ToArray();
    }

    /// <summary>Compare complete adjacent observations by lifetime; null means changes cannot be inferred.</summary>
    public static (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)? Changes(
        MonitorSample? before, MonitorSample after)
    {
        if (before is null || before.IsGap || after.IsGap ||
            before.CompactedProcesses != 0 || after.CompactedProcesses != 0 ||
            before.UnavailableProcesses != 0 || after.UnavailableProcesses != 0 ||
            before.Processes.Any(p => p.CreationTicks <= 0) || after.Processes.Any(p => p.CreationTicks <= 0))
            return null;
        if (!Connected(before, after)) return null;
        var previous = before.Processes.Select(p => p.Identity).ToHashSet();
        var current = after.Processes.Select(p => p.Identity).ToHashSet();
        return (after.Processes.Where(p => !previous.Contains(p.Identity)).ToArray(),
            before.Processes.Where(p => !current.Contains(p.Identity)).ToArray());
    }

    /// <summary>Freeze lifecycle differences from existing uncompacted Usage inventories; missing keys mean unknown.</summary>
    public static IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>
        CaptureChanges(IReadOnlyList<MonitorSample> samples, UsageHistory usage)
    {
        var frames = usage.Export().ToDictionary(frame => frame.Key);
        var changes = new Dictionary<SampleKey, (IReadOnlyList<ProcessUsage>, IReadOnlyList<ProcessUsage>)>();
        var oldIds = new HashSet<ProcessLifetimeIdentity>();
        var newIds = new HashSet<ProcessLifetimeIdentity>();
        for (var i = 1; i < samples.Count; i++)
        {
            var before = samples[i - 1];
            var after = samples[i];
            if (before.IsGap || after.IsGap || before.UnavailableProcesses != 0 || after.UnavailableProcesses != 0 ||
                !Connected(before, after) ||
                !frames.TryGetValue(SampleKey.Of(before), out var previous) || !previous.Valid ||
                !frames.TryGetValue(SampleKey.Of(after), out var current) || !current.Valid ||
                previous.Observations.Any(p => p.Owner.Identity.StartToken <= 0) ||
                current.Observations.Any(p => p.Owner.Identity.StartToken <= 0)) continue;
            oldIds.Clear();
            newIds.Clear();
            foreach (var observation in previous.Observations) oldIds.Add(observation.Owner.Identity);
            foreach (var observation in current.Observations) newIds.Add(observation.Owner.Identity);
            changes[SampleKey.Of(after)] = (
                current.Observations.Where(p => !oldIds.Contains(p.Owner.Identity)).Select(p => Process(p.Owner)).ToArray(),
                previous.Observations.Where(p => !newIds.Contains(p.Owner.Identity)).Select(p => Process(p.Owner)).ToArray());
        }
        return changes;
    }

    private static ProcessUsage Process(UsageHistory.Owner owner) => new(owner.Identity.ProcessId,
        owner.Identity.StartToken, owner.Name, 0)
    {
        LifetimeDomain = owner.Identity.Domain, CpuAvailable = false
    };

    private static bool Connected(MonitorSample before, MonitorSample after)
    {
        var seconds = before.MonotonicTimestamp > 0 && after.MonotonicTimestamp > 0
            ? (after.MonotonicTimestamp - before.MonotonicTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency
            : (after.Timestamp - before.Timestamp).TotalSeconds;
        return seconds is > 0 and <= 5;
    }
}
