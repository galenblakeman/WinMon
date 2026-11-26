using System.Text.Json.Serialization;

namespace MonCore.Monitoring;

/// <summary>What an automatic capture rule watches; stored by name in preferences.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CaptureMetric>))]
public enum CaptureMetric
{
    Cpu,
    Memory,
    PrimaryDisk,
    SecondaryDisk,
    Network,
    Gpu,
    Process,
    ApplicationCpu, ApplicationMemory, ApplicationNetwork,
    ServiceCpu, ServiceMemory, ServiceNetwork,
    CpuTemperature, GpuTemperature, PrimaryDiskTemperature, SecondaryDiskTemperature, CpuPerformanceLimited, GpuThermalLimited
}

/// <summary>One automatic capture rule as the user set it.</summary>
/// <param name="Threshold">
/// Percent for machine resources and disk active time, Mbps for network, and percent of one logical processor for
/// <see cref="CaptureMetric.Process"/>.
/// </param>
/// <param name="Seconds">Consecutive valid samples the value must stay above the threshold.</param>
public sealed record CaptureRuleSetting(CaptureMetric Metric, bool Enabled, double Threshold, int Seconds)
{
    public string Target { get; init; } = "";
    [JsonIgnore]
    public bool IsTargeted => Metric is CaptureMetric.ApplicationCpu or CaptureMetric.ApplicationMemory or CaptureMetric.ApplicationNetwork or
        CaptureMetric.ServiceCpu or CaptureMetric.ServiceMemory or CaptureMetric.ServiceNetwork;
    [JsonIgnore]
    public bool IsService => Metric is CaptureMetric.ServiceCpu or CaptureMetric.ServiceMemory or CaptureMetric.ServiceNetwork;
    [JsonIgnore]
    public bool IsMemory => Metric is CaptureMetric.ApplicationMemory or CaptureMetric.ServiceMemory;
    [JsonIgnore]
    public bool IsNetwork => Metric is CaptureMetric.Network or CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceNetwork;
    [JsonIgnore]
    public bool IsProcessCpu => Metric is CaptureMetric.Process or CaptureMetric.ApplicationCpu or CaptureMetric.ServiceCpu;
    /// <summary>One rule per metric, in display order; CPU is the one enabled when the feature is turned on.</summary>
    public static CaptureRuleSetting[] Defaults =>
    [
        new(CaptureMetric.Cpu, true, 90, 10), new(CaptureMetric.Memory, false, 90, 30),
        new(CaptureMetric.PrimaryDisk, false, 95, 15), new(CaptureMetric.SecondaryDisk, false, 95, 15),
        new(CaptureMetric.Network, false, 50, 10), new(CaptureMetric.Gpu, false, 95, 10),
        new(CaptureMetric.Process, false, 90, 30),
        new(CaptureMetric.ApplicationCpu, false, 90, 30), new(CaptureMetric.ApplicationMemory, false, 1024, 30),
        new(CaptureMetric.ApplicationNetwork, false, 50, 10), new(CaptureMetric.ServiceCpu, false, 90, 30),
        new(CaptureMetric.ServiceMemory, false, 1024, 30), new(CaptureMetric.ServiceNetwork, false, 50, 10),
        new(CaptureMetric.CpuTemperature, false, 85, 30), new(CaptureMetric.GpuTemperature, false, 85, 30),
        new(CaptureMetric.PrimaryDiskTemperature, false, 60, 30), new(CaptureMetric.SecondaryDiskTemperature, false, 60, 30),
        new(CaptureMetric.CpuPerformanceLimited, false, 1, 10), new(CaptureMetric.GpuThermalLimited, false, 1, 10)
    ];

    /// <summary>The highest threshold a rule accepts; percent rules stop at 100.</summary>
    public static double MaximumThreshold(CaptureMetric metric)
    {
        return metric switch
        {
            CaptureMetric.CpuTemperature or CaptureMetric.GpuTemperature or CaptureMetric.PrimaryDiskTemperature or CaptureMetric.SecondaryDiskTemperature => 150,
            CaptureMetric.CpuPerformanceLimited or CaptureMetric.GpuThermalLimited => 1,
            CaptureMetric.Network or CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceNetwork => 100_000,
            CaptureMetric.Process or CaptureMetric.ApplicationCpu or CaptureMetric.ServiceCpu => 25_600,
            CaptureMetric.ApplicationMemory or CaptureMetric.ServiceMemory => 1_048_576,
            _ => 100
        };
    }

    /// <summary>A copy inside the ranges the settings accept.</summary>
    public CaptureRuleSetting Normalized()
    {
        var threshold = double.IsFinite(Threshold) ? Threshold : Defaults.First(d => d.Metric == Metric).Threshold;
        var target = (Target ?? "").Trim();
        return this with
        {
            Threshold = Math.Clamp(threshold, IsNetwork ? 0.1 : 1, MaximumThreshold(Metric)),
            Seconds = Math.Clamp(Seconds, 1, 300),
            Target = target.Length > 512 ? target[..512] : target
        };
    }
}

/// <summary>A rule that held for its duration.</summary>
/// <param name="Fired">The live, uncompacted sample that completed the duration.</param>
/// <param name="Peak">The run's highest sample, uncompacted.</param>
/// <param name="Run">The rule's value in each sample of the run, oldest first.</param>
/// <param name="Process">The process that triggered a <see cref="CaptureMetric.Process"/> rule.</param>
public sealed record CaptureFired(
    CaptureRuleSetting Rule,
    MonitorSample Fired,
    MonitorSample Peak,
    double PeakValue,
    IReadOnlyList<double> Run,
    ProcessUsage? Process = null);

/// <summary>
/// Decides when automatic capture rules fire, from live samples in order. No WPF and no file I/O.
/// </summary>
/// <remarks>
/// A rule fires after its value stays above the threshold for its number of consecutive valid samples. A gap or a
/// missing reading breaks the run without counting as below. After firing, a rule re-arms only once the value has
/// stayed below the threshold minus a margin for its duration (hysteresis), and fires again only after the cooldown,
/// so a value hovering at the threshold does not fire repeatedly. See docs/design/AUTO-CAPTURE.md.
/// </remarks>
public sealed class CaptureRules(IReadOnlyList<CaptureRuleSetting> rules, TimeSpan cooldown, int logicalProcessors)
{
    private readonly CaptureRuleSetting[] _rules = [.. rules.Where(rule => rule.Enabled).Select(r => r.Normalized())];
    private readonly Dictionary<CaptureMetric, Run> _runs = [];
    private readonly Dictionary<ProcessLifetimeIdentity, Run> _processRuns = [];
    private readonly Dictionary<CaptureMetric, double> _lastFired = [];
    private readonly List<ProcessLifetimeIdentity> _absent = [];

    /// <summary>Feed one live, uncompacted sample; returns the rules it completed.</summary>
    public IReadOnlyList<CaptureFired> Observe(MonitorSample full, ServiceFrame? services = null, ProcessNetworkFrame? network = null)
    {
        ArgumentNullException.ThrowIfNull(full);
        List<CaptureFired>? fired = null;
        var moment = ProcessRanking.Moment(full);
        var valid = full is { IsGap: false, TotalCpuPercent: not null };
        foreach (var rule in _rules)
        {
            if (rule.Metric == CaptureMetric.Process)
            {
                ObserveProcesses(rule, full, valid || (!full.IsGap && full.IndependentProcessReadings), moment,
                    ref fired);
                continue;
            }

            if (!_runs.TryGetValue(rule.Metric, out var run)) _runs[rule.Metric] = run = new Run();
            if (ThermalMetrics.IsThermal(rule.Metric))
            {
                var identity = ThermalMetrics.Identity(rule.Metric, full.Resources);
                if (run.Identity != identity || (run.Seen > 0 && (moment <= run.Seen || moment - run.Seen > 2.5)))
                { run.Break(); run.Below = 0; }
                run.Seen = moment;
                run.Identity = identity;
            }
            var value = rule.IsTargeted ? ScopedValue(rule, full, services, network) : Value(rule.Metric, full);
            if (Step(rule, run, (rule.IsTargeted || ThermalMetrics.IsThermal(rule.Metric) ? !full.IsGap : valid) ? value : null, full, moment) is { } result)
                (fired ??= []).Add(result);
        }

        return fired ?? (IReadOnlyList<CaptureFired>)[];
    }

    private double? ScopedValue(CaptureRuleSetting rule, MonitorSample full, ServiceFrame? services, ProcessNetworkFrame? network)
    {
        var processes = ScopedProcesses(rule, full, services);
        if (processes.Count == 0) return null;
        if (rule.IsMemory) return processes.All(p => p.PrivateBytes is not null)
            ? processes.Sum(p => (double)p.PrivateBytes!.Value) / (1024 * 1024) : null;
        if (!rule.IsNetwork) return processes.All(p => p.ObservedCpuPercent is not null)
            ? processes.Sum(p => p.CpuPercent) * logicalProcessors : null;
        return NetworkValue(processes, network);
    }

    private static IReadOnlyList<ProcessUsage> ScopedProcesses(CaptureRuleSetting rule, MonitorSample full, ServiceFrame? services)
    {
        if (rule.Target.Length == 0) return [];
        if (rule.IsService)
        {
            if (services is not { Available: true } || !SampleKey.Of(full).Matches(services.Sample)) return [];
            var group = services.Groups.FirstOrDefault(g => g.Services.Any(s => string.Equals(s.Name, rule.Target, StringComparison.OrdinalIgnoreCase)));
            // Shared host counters cannot be assigned to one named service.
            if (group is null || group.Shared) return [];
            return group.Processes;
        }

        return full.Processes.Where(p => Path.IsPathFullyQualified(rule.Target)
            ? string.Equals(p.ImagePath, rule.Target, StringComparison.OrdinalIgnoreCase)
            : string.Equals(p.Name, rule.Target, StringComparison.OrdinalIgnoreCase) ||
              MemoryExtensions.Equals(Path.GetFileName(p.ImagePath.AsSpan()), rule.Target.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
              MemoryExtensions.Equals(Path.GetFileNameWithoutExtension(p.ImagePath.AsSpan()), rule.Target.AsSpan(), StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static double? NetworkValue(IReadOnlyList<ProcessUsage> processes, ProcessNetworkFrame? network)
    {
        if (network is null || (network.Quality & ~(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete)) != 0) return null;
        double receive = 0, send = 0;
        foreach (var process in processes)
        {
            if (process.LifetimeDomain != ProcessLifetimeDomain.WindowsFileTime) return null;
            var reading = network.Processes.FirstOrDefault(p => p.ProcessId == process.ProcessId && p.CreationTicks == process.CreationTicks);
            if (reading?.ReceiveBytesPerSecond is not { } rx || reading.SendBytesPerSecond is not { } tx) return null;
            receive += rx; send += tx;
        }
        return Math.Max(receive, send) * 8 / 1_000_000;
    }

    private void ObserveProcesses(CaptureRuleSetting rule, MonitorSample full, bool valid, double moment,
        ref List<CaptureFired>? fired)
    {
        if (!valid)
        {
            // A gap lists no processes; it breaks every run but is not evidence that any process exited.
            foreach (var run in _processRuns.Values) Step(rule, run, null, full, moment);
            return;
        }

        foreach (var process in full.Processes)
        {
            var key = process.Identity;
            if (!_processRuns.TryGetValue(key, out var run)) _processRuns[key] = run = new Run();
            run.Seen = moment;
            if (Step(rule, run, process.ObservedCpuPercent * logicalProcessors, full, moment) is { } result)
                (fired ??= []).Add(result with { Process = process });
        }

        // A lifetime missing from a valid sample has exited or become unreadable; a reused PID starts afresh.
        foreach (var (key, run) in _processRuns)
            // Seen is assigned from this exact sample's moment, so equality distinguishes current from stale runs.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (run.Seen != moment)
                _absent.Add(key);
        foreach (var key in _absent) _processRuns.Remove(key);
        _absent.Clear();
    }

    private CaptureFired? Step(CaptureRuleSetting rule, Run run, double? value, MonitorSample sample, double moment)
    {
        if (value is not { } current)
        {
            run.Break();
            run.Below = 0;
            return null;
        }

        if (ThermalMetrics.IsThermal(rule.Metric) ? current >= rule.Threshold : current > rule.Threshold)
        {
            run.Below = 0;
            if (!run.Armed) return null;
            run.Values.Add(current);
            // A run held back by the cooldown can last hours; the report needs only its recent values.
            if (run.Values.Count > 300) run.Values.RemoveAt(0);
            if (run.Peak is null || current > run.PeakValue)
            {
                run.Peak = sample;
                run.PeakValue = current;
            }

            if (run.Values.Count < rule.Seconds ||
                (_lastFired.TryGetValue(rule.Metric, out var last) && moment - last < cooldown.TotalSeconds))
                return null;
            var result = new CaptureFired(rule, sample, run.Peak, run.PeakValue, [.. run.Values]);
            _lastFired[rule.Metric] = moment;
            run.Armed = false;
            run.Break();
            return result;
        }

        run.Break();
        run.Below = current < rule.Threshold - Margin(rule) ? run.Below + 1 : 0;
        if (!run.Armed && run.Below >= rule.Seconds) run.Armed = true;
        return null;
    }

    /// <summary>How far below the threshold a value must fall to count toward re-arming.</summary>
    /// <remarks>Never more than half the threshold, so a low threshold, which Settings allows from 1, can still re-arm.</remarks>
    private static double Margin(CaptureRuleSetting rule)
    {
        return rule.Metric == CaptureMetric.Network ? rule.Threshold / 10 : Math.Min(5, rule.Threshold / 2);
    }

    /// <summary>The value a machine rule compares; null when the reading is unavailable.</summary>
    public static double? Value(CaptureMetric metric, MonitorSample sample)
    {
        var r = sample.Resources;
        if (ThermalMetrics.IsThermal(metric)) return sample.IsGap ? null : ThermalMetrics.Value(metric, r);
        return metric switch
        {
            CaptureMetric.Cpu => sample.TotalCpuPercent,
            CaptureMetric.Memory => r.MemoryPercent,
            CaptureMetric.PrimaryDisk => r.DiskActivePercent,
            CaptureMetric.SecondaryDisk => r.SecondaryDiskName.Length > 0 ? r.SecondaryDiskActivePercent : null,
            CaptureMetric.Network => r is { ReceiveBytesPerSecond: { } rx, SendBytesPerSecond: { } tx }
                ? Math.Max(rx, tx) * 8 / 1_000_000
                : null,
            CaptureMetric.Gpu => r.GpuPercent,
            _ => null
        };
    }

    private sealed class Run
    {
        public List<double> Values { get; } = [];
        public MonitorSample? Peak { get; set; }
        public double PeakValue { get; set; }
        public bool Armed { get; set; } = true;
        public int Below { get; set; }
        public double Seen { get; set; }
        public string Identity { get; set; } = "";

        public void Break()
        {
            Values.Clear();
            Peak = null;
            PeakValue = 0;
        }
    }
}
