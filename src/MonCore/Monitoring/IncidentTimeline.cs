namespace MonCore.Monitoring;

/// <summary>On-demand analysis of a frozen set of retained observations.</summary>
public static class IncidentTimeline
{
    public sealed record Reading(double? Average, int Observations);
    public sealed record ProcessComparison(ProcessUsage Process, Reading Before, Reading During, Reading After)
    {
        public double? Change => During.Average - Before.Average;
    }
    public sealed record Comparison(Reading Before, Reading During, Reading After,
        IReadOnlyList<ProcessComparison> Processes);

    /// <summary>Observed sample means in adjacent ten-second windows centered on the selected moment.
    /// Missing processes/readings are never substituted with zero; gaps are excluded, not interpolated.</summary>
    public static Comparison Compare(IReadOnlyList<MonitorSample> samples, MonitorSample selected, CaptureMetric metric) =>
        Compare(samples, selected, metric, null);

    public static Comparison Compare(IReadOnlyList<MonitorSample> samples, MonitorSample selected, CaptureMetric metric,
        IReadOnlyDictionary<SampleKey, ConnectionCapture>? traffic)
    {
        var windows = new List<MonitorSample>[] { [], [], [] };
        foreach (var sample in samples)
        {
            if (sample.IsGap) continue;
            var offset = sample.MonotonicTimestamp > 0 && selected.MonotonicTimestamp > 0
                ? (sample.MonotonicTimestamp - selected.MonotonicTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency
                : (sample.Timestamp - selected.Timestamp).TotalSeconds;
            if (offset is < -15 or >= 15) continue;
            windows[offset < -5 ? 0 : offset < 5 ? 1 : 2].Add(sample);
        }
        var resource = windows.Select(window => Mean(window.Select(sample => CaptureRules.Value(metric, sample)))).ToArray();
        var processes = windows.Select(window => ThermalMetrics.IsThermal(metric)
            ? new Dictionary<ProcessLifetimeIdentity, (ProcessUsage Process, Reading Reading)>() : ProcessMeans(window, metric, traffic)).ToArray();
        var rows = processes.SelectMany(window => window.Keys).Distinct().Select(identity =>
        {
            var owner = processes.Last(window => window.ContainsKey(identity))[identity].Process;
            var readings = processes.Select(window => window.TryGetValue(identity, out var value)
                ? value.Reading : new Reading(null, 0)).ToArray();
            return new ProcessComparison(owner, readings[0], readings[1], readings[2]);
        }).OrderByDescending(row => row.Change.HasValue)
            .ThenByDescending(row => Math.Abs(row.Change ?? 0))
            .ThenByDescending(row => row.During.Average)
            .ThenBy(row => row.Process.ProcessId).Take(10).ToArray();
        return new(resource[0], resource[1], resource[2], rows);
    }

    private static Dictionary<ProcessLifetimeIdentity, (ProcessUsage Process, Reading Reading)> ProcessMeans(
        IReadOnlyList<MonitorSample> window, CaptureMetric metric, IReadOnlyDictionary<SampleKey, ConnectionCapture>? traffic)
    {
        if (metric != CaptureMetric.Network)
            return window.SelectMany(sample => sample.Processes)
                .Where(process => process.CreationTicks > 0).GroupBy(process => process.Identity)
                .ToDictionary(group => group.Key, group => (group.Last(),
                    Mean(group.Select(process => ProcessValue(process, metric)))));
        return window.SelectMany(sample => ProcessValues(sample, metric,
                traffic?.GetValueOrDefault(SampleKey.Of(sample))))
            .Where(row => row.Process.CreationTicks > 0).GroupBy(row => row.Process.Identity)
            .ToDictionary(group => group.Key, group => (group.Last().Process, Mean(group.Select(row => row.Value))));
    }

    private static Reading Mean(IEnumerable<double?> readings)
    {
        var valid = readings.Where(value => value.HasValue && double.IsFinite(value.Value)).Select(value => value!.Value).ToArray();
        return new(valid.Length == 0 ? null : valid.Average(), valid.Length);
    }

    private static double? ProcessValue(ProcessUsage process, CaptureMetric metric) => metric switch
    {
        CaptureMetric.Memory => process.WorkingSetBytes,
        CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk or CaptureMetric.PrimaryDiskLatency or CaptureMetric.SecondaryDiskLatency => process.IoBytesPerSecond,
        CaptureMetric.Gpu => process.GpuPercent,
        _ => process.ObservedCpuPercent
    };

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

    public readonly record struct ProcessReading(ProcessUsage Process, double? Value);

    /// <summary>Join only the exact captured observation and Windows lifetime; absent traffic stays unknown.</summary>
    public static IReadOnlyList<ProcessReading> ProcessReadings(MonitorSample sample, CaptureMetric metric,
        ConnectionCapture? traffic = null) => ProcessValues(sample, metric, traffic)
        .OrderByDescending(row => row.Value).ThenBy(row => row.Process.ProcessId).ToArray();

    private static IEnumerable<ProcessReading> ProcessValues(MonitorSample sample, CaptureMetric metric,
        ConnectionCapture? traffic = null)
    {
        return ThermalMetrics.IsThermal(metric) ? [] : metric == CaptureMetric.Network ? NetworkProcessValues(sample, traffic) : ScalarProcessValues(sample, metric);
    }

    private static IEnumerable<ProcessReading> ScalarProcessValues(MonitorSample sample, CaptureMetric metric) =>
        sample.Processes.Select(process => new ProcessReading(process, ProcessValue(process, metric)));

    private static IEnumerable<ProcessReading> NetworkProcessValues(MonitorSample sample, ConnectionCapture? traffic)
    {
        if (traffic is not null && !SampleKey.Of(sample).Matches(traffic.Sample)) traffic = null;
        var frame = traffic?.Network;
        var readings = (frame?.Processes ?? []).ToDictionary(reading =>
            new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks, ProcessLifetimeIdentity.WindowsFileTime));
        // Incomplete session totals can coexist with recovered, validated window rates.
        var valid = !sample.IsGap && frame is not null &&
            (frame.Quality & ~(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete)) == ProcessNetworkQuality.None;
        var observed = sample.Processes.Concat(traffic?.Owners ?? []).ToArray();
        var known = observed.Select(process => process.Identity).ToHashSet();
        return observed.Concat((frame?.Owners ?? []).Where(process => !known.Contains(process.Identity))
                .Select(process => process with { CpuAvailable = false, WorkingSetBytes = null,
                    IoBytesPerSecond = null, GpuPercent = null }))
            .DistinctBy(process => process.Identity).Select(process =>
        {
            double? rate = null;
            if (valid && process.CreationTicks > 0 && readings.TryGetValue(process.Identity, out var reading) &&
                (frame!.CurrentLifetimes is null || frame.CurrentLifetimes.Contains(process.Identity)) &&
                reading.ReceiveBytesPerSecond is >= 0 and var receive &&
                reading.SendBytesPerSecond is >= 0 and var send && double.IsFinite(receive + send))
                rate = receive + send;
            return new ProcessReading(process, rate);
        });
    }

    public static IReadOnlyList<ProcessUsage> Contributors(MonitorSample sample, CaptureMetric metric) =>
        Contributors(sample, metric, null);

    public static IReadOnlyList<ProcessUsage> Contributors(MonitorSample sample, CaptureMetric metric,
        ConnectionCapture? traffic) => metric == CaptureMetric.Network
        ? ProcessReadings(sample, metric, traffic).Select(row => row.Process).ToArray()
        : sample.Processes.OrderByDescending(process => metric switch
        {
            CaptureMetric.Memory => process.WorkingSetBytes,
            CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk or CaptureMetric.PrimaryDiskLatency or CaptureMetric.SecondaryDiskLatency => process.IoBytesPerSecond,
            CaptureMetric.Gpu => process.GpuPercent,
            _ => process.ObservedCpuPercent
        }).ThenBy(process => process.ProcessId).ToArray();

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
            if (!TryLifecycleInventories(before, after, frames, out var previous, out var current)) continue;
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

    private static bool TryLifecycleInventories(MonitorSample before, MonitorSample after,
        Dictionary<SampleKey, UsageHistory.Frame> frames, out UsageHistory.Frame previous, out UsageHistory.Frame current)
    {
        previous = null!;
        current = null!;
        if (before.IsGap || after.IsGap || before.UnavailableProcesses != 0 || after.UnavailableProcesses != 0 ||
            !Connected(before, after) ||
            !frames.TryGetValue(SampleKey.Of(before), out var previousFrame) || !previousFrame.Valid ||
            !frames.TryGetValue(SampleKey.Of(after), out var currentFrame) || !currentFrame.Valid ||
            previousFrame.Observations.Any(p => p.Owner.Identity.StartToken <= 0) ||
            currentFrame.Observations.Any(p => p.Owner.Identity.StartToken <= 0)) return false;
        previous = previousFrame;
        current = currentFrame;
        return true;
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
