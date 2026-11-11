using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed partial class DiagnosticTools
{
    [McpServerTool(Name = "sample_performance", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Collect bounded system or single-process trends. Internal collection stays at one second. Reports weighted averages and valid coverage; cannot observe sub-second peaks. A process lifetime is pinned and never follows PID reuse.")]
    public async Task<CallToolResult> SamplePerformance(
        [Description("Integer duration, 1–60 seconds")] int durationSeconds = 10,
        [Description("Output bucket seconds, 1–10; must divide duration")] int outputIntervalSeconds = 1,
        int? pid = null, string? expectedStartToken = null, string? expectedDomain = null,
        CancellationToken cancellationToken = default)
    {
        if (durationSeconds is < 1 or > 60 || outputIntervalSeconds is < 1 or > 10 ||
            durationSeconds % outputIntervalSeconds != 0 ||
            (pid is { } id ? !ValidIdentity(id, expectedStartToken, expectedDomain) :
                expectedStartToken is not null || expectedDomain is not null)) return Invalid();
        if (!await _samplingSlots.WaitAsync(0, cancellationToken))
            return Responses.Result(new DiagnosticResponse(1, owner.UtcNow, "busy", "unavailable", 0, true, 0, "busy", null));
        try
        {
            var initial = await owner.CurrentAsync(cancellationToken);
            if (initial is null || !owner.IsFresh(initial)) return Responses.Result(Responses.Envelope(owner, initial, null));
            var process = pid is null ? null : initial.Frame.Full.Processes.FirstOrDefault(p => p.ProcessId == pid);
            if (pid is not null && IdentityStatus(process, expectedStartToken, expectedDomain) is var state && state != "ok")
                return Responses.Result(Responses.Envelope(owner, initial, null, state));
            var identity = process?.Identity;
            var targets = PerformanceTargets.From(initial.Frame.Full.Resources);
            var targetsChanged = false;
            var collectionErrors = new HashSet<string>();
            var aggregate = new PerformanceAggregate(durationSeconds, outputIntervalSeconds);
            var start = owner.Now;
            var startedAt = owner.UtcNow;
            aggregate.Add(initial.Frame.Full with { IntervalSeconds = 0 }, null, 0, false);
            var sequence = initial.Sequence;
            var latest = initial;
            var status = "ok";
            var actualDuration = 0d;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds + 2), owner.Clock);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                var finished = false;
                while (!finished)
                {
                    await owner.WaitForChangeAsync(sequence, linked.Token);
                    foreach (var frame in owner.ReadAfter(sequence))
                    {
                        sequence = frame.Sequence;
                        latest = frame;
                        if (frame.Frame.Error is { } error) collectionErrors.Add(error.ToString());
                        var elapsed = owner.Seconds(start, frame.ObservedAt);
                        actualDuration = Math.Clamp(elapsed, 0, durationSeconds);
                        var sample = frame.Frame.Full;
                        var resources = sample.Resources;
                        var diskChanged = resources.DiskName != initial.Frame.Full.Resources.DiskName;
                        var networkChanged = resources.NetworkId != initial.Frame.Full.Resources.NetworkId;
                        var gpuChanged = resources.GpuId != initial.Frame.Full.Resources.GpuId;
                        targetsChanged |= diskChanged || networkChanged || gpuChanged;
                        var current = identity is null ? null : sample.Processes.FirstOrDefault(p => p.Identity == identity);
                        if (identity is not null && current is null && !sample.IsGap && owner.IsFresh(frame))
                        {
                            status = "notObserved";
                            finished = true;
                            break;
                        }
                        aggregate.Add(sample, current, elapsed, owner.IsFresh(frame));
                        if (elapsed >= durationSeconds) { finished = true; break; }
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                status = "incomplete";
                actualDuration = Math.Clamp(owner.Seconds(start, owner.Now), 0, durationSeconds);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var result = new PerformanceResult(startedAt, durationSeconds, actualDuration, outputIntervalSeconds,
                process is null ? null : ProcessIdentity.From(process), targets,
                PerformanceTargets.From(latest.Frame.Full.Resources), targetsChanged,
                aggregate.Summary(), aggregate.Trend(actualDuration), collectionErrors.Order().ToArray(),
                "Per-process network traffic is not collected; process I/O is not physical disk throughput.");
            return Responses.Result(Responses.Envelope(owner, latest, result, status,
                count: result.Trend.Length, allowHistorical: true));
        }
        finally { _samplingSlots.Release(); }
    }
}

internal sealed record MetricSummary(double? Minimum, double? Average, double? Maximum,
    int ValidSampleCount, double ValidSeconds, double Coverage, string Unit)
{
    public bool Available => ValidSampleCount > 0;
}
internal sealed record PerformanceBucket(double StartSeconds, double DurationSeconds,
    Dictionary<string, MetricSummary> Metrics);
internal sealed record PerformanceResult(DateTimeOffset StartedAtUtc, int RequestedDurationSeconds,
    double ActualDurationSeconds, int OutputIntervalSeconds, ProcessIdentity? Identity,
    PerformanceTargets StartingTargets, PerformanceTargets EndingTargets, bool TargetsChanged,
    Dictionary<string, MetricSummary> Metrics, PerformanceBucket[] Trend, string[] CollectionErrors, string Limitation);

internal sealed record PerformanceTargets(string DiskName, string NetworkId, string GpuId, bool MetadataTruncated)
{
    internal static PerformanceTargets From(ResourceSample resources) => new(Responses.Text(resources.DiskName),
        Responses.Text(resources.NetworkId), Responses.Text(resources.GpuId),
        resources.DiskName.Length > 512 || resources.NetworkId.Length > 512 || resources.GpuId.Length > 512);
}

internal sealed class PerformanceAggregate(int duration, int interval)
{
    private readonly Dictionary<string, Accumulator> _total = [];
    private readonly Dictionary<string, Accumulator>[] _buckets =
        Enumerable.Range(0, duration / interval).Select(_ => new Dictionary<string, Accumulator>()).ToArray();
    private double _previousEnd;

    internal void Add(MonitorSample sample, ProcessUsage? process, double end, bool fresh)
    {
        var start = Math.Max(_previousEnd, Math.Max(0, end - sample.IntervalSeconds));
        _previousEnd = Math.Max(_previousEnd, end);
        var finish = Math.Min(end, duration);
        var valid = fresh && !sample.IsGap && sample.IntervalSeconds is > 0 and <= 5;
        var r = sample.Resources;
        (string Name, double? Value, string Unit)[] values =
        [
            ("cpuPercent", sample.TotalCpuPercent, "percentOfMachineCapacity"),
            ("memoryUsedBytes", r.UsedMemoryBytes, "bytes"),
            ("memoryPercent", r.MemoryPercent, "percent"),
            ("gpuPercent", r.GpuPercent, "percentOfBusiestEngine"),
            ("diskActivePercent", r.DiskActivePercent, "percent"),
            ("diskReadBytesPerSecond", r.DiskReadBytesPerSecond, "bytesPerSecond"),
            ("diskWriteBytesPerSecond", r.DiskWriteBytesPerSecond, "bytesPerSecond"),
            ("receiveBytesPerSecond", r.ReceiveBytesPerSecond, "bytesPerSecond"),
            ("sendBytesPerSecond", r.SendBytesPerSecond, "bytesPerSecond"),
            ("processCpuPercent", process?.ObservedCpuPercent, "percentOfMachineCapacity"),
            ("processWorkingSetBytes", process?.WorkingSetBytes, "bytes"),
            ("processPrivateBytes", process?.PrivateBytes, "bytes"),
            ("processIoBytesPerSecond", process?.IoBytesPerSecond, "bytesPerSecond")
        ];
        foreach (var (name, value, unit) in values)
        {
            AddTo(_total, name, valid ? value : null, Math.Max(0, finish - start), unit);
            for (var i = 0; i < _buckets.Length; i++)
            {
                var overlap = Math.Max(0, Math.Min(finish, (i + 1) * interval) - Math.Max(start, i * interval));
                AddTo(_buckets[i], name, valid ? value : null, overlap, unit);
            }
        }
    }

    internal Dictionary<string, MetricSummary> Summary() => Summarize(_total, duration);
    internal PerformanceBucket[] Trend(double actualDuration) => _buckets
        .Select((bucket, i) => new PerformanceBucket(i * interval,
            Math.Clamp(actualDuration - i * interval, 0, interval),
            Summarize(bucket, Math.Clamp(actualDuration - i * interval, 0, interval))))
        .Where(bucket => bucket.DurationSeconds > 0).ToArray();

    private static void AddTo(Dictionary<string, Accumulator> target, string name, double? value, double seconds, string unit)
    {
        if (!target.TryGetValue(name, out var metric)) target.Add(name, metric = new Accumulator(unit));
        if (value is not { } number || !double.IsFinite(number) || seconds <= 0) return;
        metric.Count++;
        metric.Seconds += seconds;
        metric.Weighted += number * seconds;
        metric.Minimum = metric.Minimum is { } min ? Math.Min(min, number) : number;
        metric.Maximum = metric.Maximum is { } max ? Math.Max(max, number) : number;
    }

    private static Dictionary<string, MetricSummary> Summarize(Dictionary<string, Accumulator> metrics, double seconds)
        => metrics.ToDictionary(pair => pair.Key, pair => new MetricSummary(pair.Value.Minimum,
            pair.Value.Seconds > 0 ? pair.Value.Weighted / pair.Value.Seconds : null,
            pair.Value.Maximum, pair.Value.Count, pair.Value.Seconds,
            seconds > 0 ? Math.Clamp(pair.Value.Seconds / seconds, 0, 1) : 0, pair.Value.Unit));

    private sealed class Accumulator(string unit)
    {
        internal string Unit { get; } = unit;
        internal int Count;
        internal double Seconds;
        internal double Weighted;
        internal double? Minimum;
        internal double? Maximum;
    }
}
