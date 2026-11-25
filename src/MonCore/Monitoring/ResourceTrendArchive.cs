using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonCore.Monitoring;

/// <summary>Metrics retained as minute averages for the longer resource trend.</summary>
public enum ResourceTrendMetric
{
    Cpu,
    MemoryBytes,
    MemoryPercent,
    PrimaryDiskActive,
    PrimaryDiskRead,
    PrimaryDiskWrite,
    SecondaryDiskActive,
    SecondaryDiskRead,
    SecondaryDiskWrite,
    Receive,
    Send,
    Gpu
}

/// <summary>Why a minute cannot be connected to the preceding resource trend point.</summary>
public enum ResourceTrendBreak
{
    None,
    ResourceChanged,
    ClockChanged,
    CollectionRestart
}

/// <summary>One weighted value in a UTC minute bucket. An ambiguous value must not be plotted.</summary>
public sealed record ResourceTrendValue(double WeightedSum, double ObservedSeconds, double Minimum, double Maximum,
    string Device, ResourceTrendBreak Break)
{
    public string DeviceId { get; init; } = "";
    public string DeviceLabel { get; init; } = "";
    [JsonIgnore] public double? Average => Break is not (ResourceTrendBreak.ResourceChanged or ResourceTrendBreak.ClockChanged) && ObservedSeconds > 0
        ? WeightedSum / ObservedSeconds : null;
    [JsonIgnore] public bool IsBreak => Break != ResourceTrendBreak.None;
}

/// <summary>One UTC-aligned minute of independently available resource measurements.</summary>
public sealed record ResourceTrendBucket(DateTimeOffset StartUtc,
    IReadOnlyDictionary<ResourceTrendMetric, ResourceTrendValue> Values);

/// <summary>Serializable bounded state for <see cref="ResourceTrendArchive"/>.</summary>
public sealed record ResourceTrendArchiveState(int SchemaVersion, DateTimeOffset EndUtc, ResourceTrendBucket[] Buckets);

/// <summary>
/// Seven elapsed days of local, process-free resource observations. Samples are split at UTC minute boundaries,
/// so callers can render honest weighted minute averages without retaining per-process history.
/// </summary>
public sealed class ResourceTrendArchive
{
    public const int RetainedMinutes = 7 * 24 * 60 + 1;
    public const long MaximumFileBytes = 16 * 1024 * 1024;
    public const long MaximumDecodedBytes = 64 * 1024 * 1024;
    private const int MaximumDeviceLength = 256;
    private const double MaximumReading = 1e20;
    private readonly SortedDictionary<long, MutableBucket> _buckets = [];
    private readonly Dictionary<ResourceTrendMetric, string> _lastDevices = [];
    private readonly List<DeviceInfo> _devices = [new("", "")];
    private readonly Dictionary<string, int> _deviceIndexes = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastIntervalEnd;
    private DateTimeOffset? _latestUtc;
    private DateTimeOffset? _clockChangeUntil;
    private long _lastMonotonicTimestamp;
    private bool _breakNext;

    /// <summary>Starts a new trend segment after collection was stopped or its baseline was intentionally discarded.</summary>
    public void BreakContinuity()
    {
        _lastIntervalEnd = null;
        _lastMonotonicTimestamp = 0;
        _clockChangeUntil = null;
        _lastDevices.Clear();
        _breakNext = true;
    }

    /// <summary>Add a live full sample. Gaps, invalid intervals and overlapping intervals are not inferred.</summary>
    public void Add(MonitorSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.IsGap || !double.IsFinite(sample.IntervalSeconds) || sample.IntervalSeconds is <= 0 or > 5) return;
        DateTimeOffset end;
        DateTimeOffset start;
        try { end = sample.Timestamp.ToUniversalTime(); start = end.AddSeconds(-sample.IntervalSeconds); }
        catch (ArgumentOutOfRangeException) { return; }
        if (!TryAdvanceClock(sample, start, end, out var clockChanged)) return;
        AccumulateInterval(sample, start, end, clockChanged);
        _breakNext = false;
        Prune(end);
    }

    private bool TryAdvanceClock(MonitorSample sample, DateTimeOffset start, DateTimeOffset end, out bool clockChanged)
    {
        clockChanged = false;
        var hasMonotonicInterval = _lastMonotonicTimestamp > 0 && sample.MonotonicTimestamp > 0;
        var elapsed = hasMonotonicInterval
            ? (sample.MonotonicTimestamp - _lastMonotonicTimestamp) / (double)Stopwatch.Frequency : 0;
        if (hasMonotonicInterval && (elapsed <= 0 || sample.IntervalSeconds > elapsed + 1d / Stopwatch.Frequency)) return false;
        clockChanged = ObserveClockChange(end, hasMonotonicInterval, elapsed);
        // Wall time and QPC are read separately, so normal timestamp jitter is not an overlapping capture.
        if (!hasMonotonicInterval && _lastIntervalEnd is { } prior && start < prior && !clockChanged) return false;
        _lastIntervalEnd = end;
        _latestUtc = end;
        _lastMonotonicTimestamp = sample.MonotonicTimestamp;
        if (_clockChangeUntil is { } recoveredUntil && end > recoveredUntil) _clockChangeUntil = null;
        if (clockChanged) _lastDevices.Clear();
        return true;
    }

    private bool ObserveClockChange(DateTimeOffset end, bool hasMonotonicInterval, double elapsed)
    {
        var clockChanged = _lastIntervalEnd is { } previous && hasMonotonicInterval &&
            Math.Abs((end - previous).TotalSeconds - elapsed) > 5;
        if (clockChanged && _lastIntervalEnd is { } beforeChange) _clockChangeUntil = beforeChange;
        if (_clockChangeUntil is { } changedUntil && end <= changedUntil) clockChanged = true;
        return clockChanged;
    }

    private void AccumulateInterval(MonitorSample sample, DateTimeOffset start, DateTimeOffset end, bool clockChanged)
    {
        var resources = sample.Resources;
        var values = Values(sample, resources);
        while (start < end)
        {
            var minute = new DateTimeOffset(start.UtcDateTime.AddTicks(-(start.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)));
            DateTimeOffset nextMinute;
            try { nextMinute = minute.AddMinutes(1); }
            catch (ArgumentOutOfRangeException) { nextMinute = end; }
            var until = nextMinute < end ? nextMinute : end;
            var seconds = (until - start).TotalSeconds;
            if (seconds <= 0) break;
            var key = minute.UtcTicks;
            if (!_buckets.TryGetValue(key, out var bucket)) _buckets.Add(key, bucket = new MutableBucket(minute));
            foreach (var value in values)
            {
                var trackDevice = TracksDevice(value.Metric, value.Value, value.DeviceId);
                var changed = trackDevice && _lastDevices.TryGetValue(value.Metric, out var priorDevice) &&
                    !StringComparer.Ordinal.Equals(priorDevice, value.DeviceId);
                bucket.Add(value.Metric, value.Value, InternDevice(value.DeviceId, value.DeviceLabel), seconds);
                if (changed) bucket.MarkMetricBreak(value.Metric, ResourceTrendBreak.ResourceChanged);
                if (trackDevice) _lastDevices[value.Metric] = value.DeviceId;
            }
            if (_breakNext) bucket.MarkBreak(ResourceTrendBreak.CollectionRestart);
            if (clockChanged) bucket.MarkBreak(ResourceTrendBreak.ClockChanged);
            start = until;
        }
    }

    // Device continuity deliberately remembers finite positive out-of-range readings that buckets reject.
    private static bool TracksDevice(ResourceTrendMetric metric, double? value, string deviceId) =>
        NeedsDevice(metric) && value is { } reading && double.IsFinite(reading) && reading >= 0 && !string.IsNullOrWhiteSpace(deviceId);

    private static bool ValidLiveReading(ResourceTrendMetric metric, double reading) =>
        double.IsFinite(reading) && reading is >= 0 and <= MaximumReading &&
        !(metric is ResourceTrendMetric.Cpu or ResourceTrendMetric.MemoryPercent or ResourceTrendMetric.PrimaryDiskActive or ResourceTrendMetric.SecondaryDiskActive or ResourceTrendMetric.Gpu && reading > 100);

    /// <summary>Returns an immutable, ordered snapshot safe to serialize or render off the sampling path.</summary>
    public ResourceTrendArchiveState Snapshot(DateTimeOffset? endUtc = null)
    {
        var end = (endUtc ?? _latestUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        Prune(end);
        return new ResourceTrendArchiveState(1, end, _buckets.Values.Where(bucket => bucket.StartUtc < end).Select(bucket => bucket.Freeze(_devices)).ToArray());
    }

    /// <summary>Gets retained UTC minute buckets intersecting the requested elapsed range.</summary>
    public IReadOnlyList<ResourceTrendBucket> GetBuckets(DateTimeOffset endUtc, TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        var end = endUtc.ToUniversalTime();
        Prune(end);
        var start = end - duration;
        return _buckets.Values.Where(bucket => bucket.StartUtc < end && bucket.StartUtc.AddMinutes(1) > start)
            .Select(bucket => bucket.Freeze(_devices)).ToArray();
    }

    public static ResourceTrendArchive Restore(ResourceTrendArchiveState state)
    {
        Validate(state);
        var archive = new ResourceTrendArchive();
        archive._latestUtc = state.EndUtc;
        foreach (var item in state.Buckets)
        {
            var bucket = new MutableBucket(item.StartUtc);
            foreach (var value in item.Values) bucket.Restore(value.Key, value.Value, archive.InternDevice(value.Value.DeviceId, value.Value.DeviceLabel));
            archive._buckets.Add(item.StartUtc.UtcTicks, bucket);
        }
        archive.BreakContinuity();
        return archive;
    }

    public static async Task<ResourceTrendArchive> LoadAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) return new ResourceTrendArchive();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Resource trend archive is too large.");
        await using var compressed = new GZipStream(stream, CompressionMode.Decompress);
        await using var decoded = new MemoryStream();
        var buffer = new byte[8192];
        for (;;)
        {
            var count = await compressed.ReadAsync(buffer, token);
            if (count == 0) break;
            if (decoded.Length > MaximumDecodedBytes - count) throw new InvalidDataException("Resource trend archive expands too large.");
            await decoded.WriteAsync(buffer.AsMemory(0, count), token);
        }
        decoded.Position = 0;
        try
        {
            return Restore(await JsonSerializer.DeserializeAsync<ResourceTrendArchiveState>(decoded, cancellationToken: token)
                ?? throw new InvalidDataException("Empty resource trend archive."));
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid resource trend archive.", ex); }
    }

    public static async Task SaveAsync(string path, ResourceTrendArchiveState state, CancellationToken token = default)
    {
        Validate(state);
        var json = JsonSerializer.SerializeToUtf8Bytes(state);
        if (json.LongLength > MaximumDecodedBytes) throw new InvalidDataException("Resource trend archive expands too large.");
        using var output = new MemoryStream();
        await using (var compressed = new GZipStream(output, CompressionLevel.SmallestSize, true)) await compressed.WriteAsync(json, token);
        var bytes = output.ToArray();
        if (bytes.LongLength > MaximumFileBytes) throw new InvalidDataException("Resource trend archive is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Prune(DateTimeOffset now)
    {
        var oldest = now.UtcDateTime.AddDays(-7).Ticks;
        while (_buckets.Count > 0 && _buckets.First().Key <= oldest - TimeSpan.TicksPerMinute) _buckets.Remove(_buckets.First().Key);
        while (_buckets.Count > 0 && _buckets.Last().Key >= now.UtcTicks + TimeSpan.TicksPerMinute) _buckets.Remove(_buckets.Last().Key);
        while (_buckets.Count > RetainedMinutes) _buckets.Remove(_buckets.First().Key);
    }

    private static IEnumerable<(ResourceTrendMetric Metric, double? Value, string DeviceId, string DeviceLabel)> Values(MonitorSample sample, ResourceSample resources)
    {
        yield return (ResourceTrendMetric.Cpu, sample.TotalCpuPercent, "", "");
        yield return (ResourceTrendMetric.MemoryBytes, resources.UsedMemoryBytes, "", "");
        yield return (ResourceTrendMetric.MemoryPercent, resources.MemoryPercent, "", "");
        foreach (var item in Disk(ResourceTrendMetric.PrimaryDiskActive, ResourceTrendMetric.PrimaryDiskRead, ResourceTrendMetric.PrimaryDiskWrite,
                     resources.DiskName, resources.DiskActivePercent, resources.DiskReadBytesPerSecond, resources.DiskWriteBytesPerSecond)) yield return item;
        foreach (var item in Disk(ResourceTrendMetric.SecondaryDiskActive, ResourceTrendMetric.SecondaryDiskRead, ResourceTrendMetric.SecondaryDiskWrite,
                     resources.SecondaryDiskName, resources.SecondaryDiskActivePercent, resources.SecondaryDiskReadBytesPerSecond, resources.SecondaryDiskWriteBytesPerSecond)) yield return item;
        yield return (ResourceTrendMetric.Receive, resources.ReceiveBytesPerSecond, resources.NetworkId, resources.NetworkName);
        yield return (ResourceTrendMetric.Send, resources.SendBytesPerSecond, resources.NetworkId, resources.NetworkName);
        yield return (ResourceTrendMetric.Gpu, resources.GpuPercent, resources.GpuId, resources.GpuName);
    }

    private static IEnumerable<(ResourceTrendMetric Metric, double? Value, string DeviceId, string DeviceLabel)> Disk(ResourceTrendMetric active, ResourceTrendMetric read,
        ResourceTrendMetric write, string device, double? activeValue, double? readValue, double? writeValue)
    {
        yield return (active, activeValue, device, device);
        yield return (read, readValue, device, device);
        yield return (write, writeValue, device, device);
    }

    private static bool NeedsDevice(ResourceTrendMetric metric) => metric is ResourceTrendMetric.PrimaryDiskActive or ResourceTrendMetric.PrimaryDiskRead or ResourceTrendMetric.PrimaryDiskWrite or
        ResourceTrendMetric.SecondaryDiskActive or ResourceTrendMetric.SecondaryDiskRead or ResourceTrendMetric.SecondaryDiskWrite or ResourceTrendMetric.Receive or ResourceTrendMetric.Send or ResourceTrendMetric.Gpu;

    private int InternDevice(string id, string label)
    {
        if (string.IsNullOrWhiteSpace(id)) return 0;
        id = id.Length > MaximumDeviceLength ? id[..MaximumDeviceLength] : id;
        label = label.Length > MaximumDeviceLength ? label[..MaximumDeviceLength] : label;
        var key = id + '\0' + label;
        if (_deviceIndexes.TryGetValue(key, out var index)) return index;
        if (_devices.Count >= 4096) return 0;
        index = _devices.Count;
        _devices.Add(new DeviceInfo(id, label));
        _deviceIndexes.Add(key, index);
        return index;
    }

    private static void Validate(ResourceTrendArchiveState state)
    {
        if (state.SchemaVersion != 1 || state.EndUtc.Offset != TimeSpan.Zero || state.Buckets is null || state.Buckets.Length > RetainedMinutes || state.Buckets.Any(b => b is null))
            throw new InvalidDataException("Invalid resource trend archive.");
        var prior = long.MinValue;
        foreach (var bucket in state.Buckets)
        {
            ValidateBucket(bucket, prior);
            prior = bucket.StartUtc.UtcTicks;
        }
    }

    private static void ValidateBucket(ResourceTrendBucket bucket, long prior)
    {
        if (bucket.StartUtc.Offset != TimeSpan.Zero || bucket.StartUtc.UtcTicks % TimeSpan.TicksPerMinute != 0 || bucket.StartUtc.UtcTicks <= prior ||
            bucket.Values is null || bucket.Values.Count > Enum.GetValues<ResourceTrendMetric>().Length)
            throw new InvalidDataException("Invalid resource trend bucket.");
        foreach (var pair in bucket.Values)
            ValidateValue(pair.Key, pair.Value);
    }

    private static void ValidateValue(ResourceTrendMetric metric, ResourceTrendValue value)
    {
        if (!Enum.IsDefined(metric) || value is null || !ValidDeviceNames(value) ||
            !ValidMeasurements(value) || !ValidRange(metric, value) || !Enum.IsDefined(value.Break))
            throw new InvalidDataException("Invalid resource trend value.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidDeviceNames(ResourceTrendValue value) =>
        value.Device is not null && value.DeviceId is not null && value.DeviceLabel is not null &&
        value.Device.Length <= MaximumDeviceLength && value.DeviceId.Length <= MaximumDeviceLength && value.DeviceLabel.Length <= MaximumDeviceLength;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidMeasurements(ResourceTrendValue value) =>
        double.IsFinite(value.WeightedSum) && double.IsFinite(value.ObservedSeconds) &&
        double.IsFinite(value.Minimum) && double.IsFinite(value.Maximum) &&
        value.ObservedSeconds is > 0 and <= 60 && value.WeightedSum >= 0 && value.Minimum >= 0 && value.Maximum >= value.Minimum;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidRange(ResourceTrendMetric metric, ResourceTrendValue value) =>
        !(value.WeightedSum > MaximumReading * 60 || value.Maximum > MaximumReading ||
            value.WeightedSum < value.Minimum * value.ObservedSeconds - 0.001 || value.WeightedSum > value.Maximum * value.ObservedSeconds + 0.001 ||
            (metric is ResourceTrendMetric.Cpu or ResourceTrendMetric.MemoryPercent or ResourceTrendMetric.PrimaryDiskActive or ResourceTrendMetric.SecondaryDiskActive or ResourceTrendMetric.Gpu && value.Maximum > 100));

    private sealed class MutableBucket(DateTimeOffset startUtc)
    {
        private readonly long _startTicks = startUtc.UtcTicks;
        public DateTimeOffset StartUtc => new(_startTicks, TimeSpan.Zero);
        private MetricValue[] _values = new MetricValue[Enum.GetValues<ResourceTrendMetric>().Length];
        private ushort _present;

        public void Add(ResourceTrendMetric metric, double? value, int device, double seconds)
        {
            if (value is not { } reading || !ValidLiveReading(metric, reading) || seconds <= 0 ||
                (NeedsDevice(metric) && device == 0)) return;
            ref var total = ref _values[(int)metric];
            seconds = Math.Min(seconds, 60 - total.Seconds);
            if (seconds <= 0) return;
            if (total.Seconds > 0 && total.Device != device) SetBreak(ref total, ResourceTrendBreak.ResourceChanged);
            total.WeightedSum += reading * seconds;
            total.Seconds += seconds;
            total.Device = device;
            _present |= (ushort)(1 << (int)metric);
        }

        public void Restore(ResourceTrendMetric metric, ResourceTrendValue value, int device)
        {
            _values[(int)metric] = new MetricValue { WeightedSum = value.WeightedSum, Seconds = value.ObservedSeconds, Device = device, Break = value.Break };
            _present |= (ushort)(1 << (int)metric);
        }

        public ResourceTrendBucket Freeze(IReadOnlyList<DeviceInfo> devices)
        {
            var values = new Dictionary<ResourceTrendMetric, ResourceTrendValue>();
            for (var index = 0; index < _values.Length; index++)
            {
                if ((_present & (1 << index)) == 0) continue;
                var value = _values[index];
                var average = value.WeightedSum / value.Seconds;
                var device = devices[value.Device];
                values.Add((ResourceTrendMetric)index, new ResourceTrendValue(value.WeightedSum, value.Seconds, average, average, device.Label, value.Break)
                { DeviceId = device.Id, DeviceLabel = device.Label });
            }
            return new ResourceTrendBucket(StartUtc, values);
        }

        public void MarkClockChanged() => MarkBreak(ResourceTrendBreak.ClockChanged);
        public void MarkBreak(ResourceTrendBreak reason)
        {
            for (var index = 0; index < _values.Length; index++) if ((_present & (1 << index)) != 0) SetBreak(ref _values[index], reason);
        }
        public void MarkMetricBreak(ResourceTrendMetric metric, ResourceTrendBreak reason)
        {
            if ((_present & (1 << (int)metric)) != 0) SetBreak(ref _values[(int)metric], reason);
        }

        private static void SetBreak(ref MetricValue value, ResourceTrendBreak reason)
        {
            // A restart must not turn mixed-device or ambiguous-clock totals into a usable average.
            if (reason == ResourceTrendBreak.ClockChanged || (value.Break != ResourceTrendBreak.ClockChanged &&
                                                              (reason == ResourceTrendBreak.ResourceChanged || value.Break == ResourceTrendBreak.None))) value.Break = reason;
        }
    }

    private struct MetricValue
    {
        public double WeightedSum;
        public double Seconds;
        public int Device;
        public ResourceTrendBreak Break;
    }

    private sealed record DeviceInfo(string Id, string Label);
}
