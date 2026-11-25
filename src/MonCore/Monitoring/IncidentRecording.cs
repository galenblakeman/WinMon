using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace WinMon.Monitoring;

/// <summary>One frozen History observation, without socket endpoints or live session state.</summary>
public sealed record IncidentObservation(MonitorSample Sample, ProcessNetworkFrame? Network,
    IReadOnlyList<ProcessUsage> Owners, IReadOnlyList<ProcessUsage>? Started, IReadOnlyList<ProcessUsage>? Stopped);

/// <summary>A bounded, versioned, explicitly saved investigation. Loading never restores a live session.</summary>
public sealed record IncidentRecording(int Version, long Frequency, IncidentObservation[] Observations,
    CaptureMetric Metric, int SelectedIndex)
{
    public const long MaximumBytes = 64L * 1024 * 1024;
    public const long MaximumDecodedBytes = 256L * 1024 * 1024;
    public const int CurrentVersion = 2;
    public IncidentEventResult WindowsEvents { get; init; } = IncidentEventResult.Unknown;

    public void Save(string path)
    {
        Validate();
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var compressed = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true))
                using (var bounded = new BoundedStream(compressed))
                    JsonSerializer.Serialize(bounded, this with { Version = CurrentVersion }, InvestigationHistory.Json);
                if (file.Length > MaximumBytes) throw new InvalidDataException("Incident exceeds 64 MiB.");
                file.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static IncidentRecording Load(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid incident size.");
        using var compressed = new GZipStream(file, CompressionMode.Decompress);
        using var bounded = new BoundedStream(compressed);
        var recording = JsonSerializer.Deserialize<IncidentRecording>(bounded, InvestigationHistory.Json)
            ?? throw new InvalidDataException("Missing incident recording.");
        // Version 1 has no event coverage, even if an unrelated extra JSON property uses that name.
        if (recording.Version == 1) recording = recording with { WindowsEvents = IncidentEventResult.Unknown };
        recording.Validate();
        // Files may be opened on another machine with a different Stopwatch frequency.
        var first = recording.Observations.FirstOrDefault()?.Sample.MonotonicTimestamp ?? 0;
        var normalized = recording with
        {
            Frequency = Stopwatch.Frequency,
            Observations = recording.Observations.Select(frame => frame with
            {
                Sample = frame.Sample with
                {
                    MonotonicTimestamp = first == 0 ? 0 : checked(Stopwatch.Frequency +
                        (long)((frame.Sample.MonotonicTimestamp - first) * (decimal)Stopwatch.Frequency / recording.Frequency))
                }
            }).ToArray()
        };
        normalized.Validate();
        return normalized;
    }

    private void Validate()
    {
        ValidateHeader();
        ValidateEvents();
        long entries = 0;
        MonitorSample? previous = null;
        foreach (var frame in Observations)
        {
            var sample = ValidateObservation(frame);
            ValidateDisplayNames(frame);
            entries += ObservationEntries(frame);
            if (entries > 2_000_000 || frame.Network?.Processes.Select(p => (p.ProcessId, p.CreationTicks)).Distinct().Count()
                != frame.Network?.Processes.Count)
                throw new InvalidDataException("Invalid incident traffic or observation limit.");
            if (previous is not null && ((sample.MonotonicTimestamp == 0) != (previous.MonotonicTimestamp == 0) ||
                (sample.MonotonicTimestamp > 0 ? sample.MonotonicTimestamp <= previous.MonotonicTimestamp : sample.Timestamp <= previous.Timestamp)))
                throw new InvalidDataException("Incident observations must be ordered.");
            previous = sample;
        }
        ValidateSpan();
    }

    private void ValidateHeader()
    {
        if (Version is < 1 or > CurrentVersion || Frequency is <= 0 or > 1_000_000_000_000 ||
            Metric is < CaptureMetric.Cpu or > CaptureMetric.Gpu ||
            Observations is null || Observations.Length > 7200 ||
            SelectedIndex < -1 || SelectedIndex >= Observations.Length ||
            Observations.Length > 0 && SelectedIndex < 0)
            throw new InvalidDataException("Invalid incident header or unsupported version.");
    }

    private static MonitorSample ValidateObservation(IncidentObservation frame)
    {
        if (frame?.Sample is not { Processes: not null, Resources: not null } sample ||
            frame.Owners is null || frame.Started is null != (frame.Stopped is null) ||
            !ValidSampleMetadata(sample) || !ValidObservationCollections(frame))
            throw new InvalidDataException("Invalid incident observation.");
        return sample;
    }

    private static bool ValidSampleMetadata(MonitorSample sample) =>
        sample.MonotonicTimestamp >= 0 && sample.UnavailableProcesses >= 0 && sample.CompactedProcesses >= 0 &&
        double.IsFinite(sample.IntervalSeconds) && sample.IntervalSeconds >= 0 &&
        !(sample.Resources.PagingReadsPerSecond is { } pagingReads &&
            (!double.IsFinite(pagingReads) || pagingReads < 0)) &&
        sample.LogicalProcessorPercents is not { Count: > 4096 };

    private static bool ValidObservationCollections(IncidentObservation frame)
    {
        var sample = frame.Sample;
        return InvestigationHistory.Valid(new InvestigationHistory.Frame(SampleKey.Of(sample), null, false,
                null, sample.Processes, frame.Network, null)) &&
            InvestigationHistory.Valid(new InvestigationHistory.Frame(SampleKey.Of(sample), null, false,
                null, frame.Owners, null, null)) &&
            InvestigationHistory.Valid(new InvestigationHistory.Frame(SampleKey.Of(sample), null, false,
                null, frame.Started, null, null)) &&
            InvestigationHistory.Valid(new InvestigationHistory.Frame(SampleKey.Of(sample), null, false,
                null, frame.Stopped, null, null));
    }

    private static void ValidateDisplayNames(IncidentObservation frame)
    {
        var sample = frame.Sample;
        if (sample.Resources.DiskName is not { Length: <= 1024 } ||
            sample.Resources.SecondaryDiskName is not { Length: <= 1024 } ||
            sample.Resources.NetworkName is not { Length: <= 1024 } ||
            sample.Resources.GpuName is not { Length: <= 1024 } ||
            sample.Processes.Concat(frame.Owners).Concat(frame.Network?.Owners ?? [])
                .Concat(frame.Started ?? []).Concat(frame.Stopped ?? []).Any(process => process.Name.Length > 1024))
            throw new InvalidDataException("Invalid incident display name.");
    }

    private static long ObservationEntries(IncidentObservation frame) =>
        frame.Sample.Processes.Count + frame.Owners.Count + (frame.Network?.Processes.Count ?? 0) +
        (frame.Network?.Owners.Count ?? 0) + (frame.Started?.Count ?? 0) + (frame.Stopped?.Count ?? 0);

    private void ValidateSpan()
    {
        if (Observations.Length > 1)
        {
            var first = Observations[0].Sample;
            var last = Observations[^1].Sample;
            var seconds = first.MonotonicTimestamp > 0
                ? (last.MonotonicTimestamp - first.MonotonicTimestamp) / (double)Frequency
                : (last.Timestamp - first.Timestamp).TotalSeconds;
            if (seconds > 86400) throw new InvalidDataException("Incident span exceeds one day.");
        }
    }

    private void ValidateEvents()
    {
        ValidateEventCoverage();
        foreach (var item in WindowsEvents.Events)
            ValidateEvent(item);
    }

    private void ValidateEventCoverage()
    {
        if (WindowsEvents is not { Events: not null, Coverage: not null } result ||
            result.Events.Count > 500 || result.Coverage.Count != 2 ||
            result.Coverage.Select(item => item?.Log).Distinct(StringComparer.Ordinal).Count() != 2 ||
            result.Coverage.Any(item => item is null || item.Log is not ("Application" or "System") ||
                !Enum.IsDefined(item.Status)))
            throw new InvalidDataException("Invalid incident event coverage.");
    }

    private static void ValidateEvent(IncidentEvent item)
    {
        if (item is null || item.Provider is not { Length: > 0 and <= 256 } ||
            item.Message is not { Length: <= 300 } || item.Message.IndexOfAny(['\r', '\n']) >= 0 ||
            item.EventId is < 0 or > 65535 || !Enum.IsDefined(item.Level))
            throw new InvalidDataException("Invalid incident event.");
        ValidateEventIdentity(item);
        ValidateCrashSignature(item.CrashSignature);
    }

    private static void ValidateEventIdentity(IncidentEvent item)
    {
        if (item.ProcessId.HasValue != item.CreationTicks.HasValue ||
            item.ProcessId is <= 0 || item.CreationTicks is <= 0)
            throw new InvalidDataException("Invalid incident event.");
    }

    private static void ValidateCrashSignature(CrashSignature? signature)
    {
        if (signature is null) return;
        if (!CrashSignatures.IsValid(signature))
            throw new InvalidDataException("Invalid incident event crash signature.");
    }

    // Bound decoded bytes as well as file size so a compressed input cannot exhaust memory.
    private sealed class BoundedStream(Stream inner) : Stream
    {
        private long _count;
        public override bool CanRead => inner.CanRead;
        public override bool CanWrite => inner.CanWrite;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _count; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, MaximumDecodedBytes - _count + 1)]);
            Count(read);
            return read;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Count(buffer.Length);
            inner.Write(buffer);
        }
        private void Count(int count)
        {
            _count += count;
            if (_count > MaximumDecodedBytes) throw new InvalidDataException("Incident exceeds 256 MiB decoded.");
        }
    }
}
