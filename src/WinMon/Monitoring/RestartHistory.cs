using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace WinMon.Monitoring;

/// <summary>The monotonic clocks a restart file is stamped and checked with; injectable for tests.</summary>
/// <param name="Timestamp">Stopwatch ticks, which keep counting across process restarts on the same boot.</param>
/// <param name="TickCount">Milliseconds since boot, a second clock to cross-check the first.</param>
/// <param name="Frequency">Stopwatch ticks per second.</param>
internal sealed record RestartClock(Func<long> Timestamp, Func<long> TickCount, long Frequency)
{
    internal static RestartClock System { get; } =
        new(Stopwatch.GetTimestamp, () => Environment.TickCount64, Stopwatch.Frequency);

    internal double SecondsSince(long start)
    {
        return (Timestamp() - start) / (double)Frequency;
    }
}

/// <summary>What a restart file restored: compacted samples oldest first, and the usage ledger when it was valid.</summary>
/// <param name="LiveOffset">
/// Ticks to add to this process's live samples so they follow the restored ones. It is non-zero when the saving
/// process had itself rebased history after a reboot: its samples then sit ahead of the shared Stopwatch clock.
/// </param>
internal sealed record RestartContents(IReadOnlyList<MonitorSample> Samples, UsageLedgerState? Usage,
    long LiveOffset = 0);

/// <summary>
/// Saves history across an in-app update restart, in the update's transaction directory, and reads it back.
/// </summary>
/// <remarks>
/// The file is a header, a usage-ledger chunk, sample chunks newest first, and a fixed-size trailer. Every chunk is
/// framed as kind, length and CRC-32 of its Deflate-compressed payload, and carries its own strings, so each one
/// can be checked and read alone: a save stopped at its deadline or a damaged chunk only costs the older history.
/// Samples keep their monotonic timestamps, which stay valid because the update handshake guarantees the same
/// boot; see docs/design/UPDATE-RESTART-HISTORY.md.
/// </remarks>
internal static class RestartHistory
{
    internal const string FileName = "restart-history.bin";
    internal const string NormalFileName = "history.bin";
    private const string NormalToken = "normal-exit";
    internal const int FormatVersion = 1;
    internal const int ChunkSamples = 60;
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(1.5);
    internal const double StopChunksAfterSeconds = 1.2;
    internal const double MaxAgeSeconds = 300;
    internal const double ClockToleranceSeconds = 2;

    private const byte LedgerChunk = 1;
    private const byte SampleChunk = 2;
    private const int TrailerLength = 12;
    private const int FrameLength = 9;
    private static readonly byte[] HeaderMagic = [.. "WMRH"u8];
    private static readonly byte[] TrailerMagic = [.. "WMRT"u8];

    /// <summary>
    /// Write history newest first until <see cref="StopChunksAfterSeconds"/>, then move the file into place only
    /// if the whole save finished within <see cref="Budget"/>. Never throws.
    /// </summary>
    /// <param name="path">Final history file path.</param>
    /// <param name="token">Session token used to identify the saved history.</param>
    /// <param name="appVersion">Version that wrote the history.</param>
    /// <param name="samples">Samples to save, newest chunks first.</param>
    /// <param name="usage">Usage totals to restore with the samples.</param>
    /// <param name="clock">Clock used to enforce the save deadline.</param>
    /// <param name="createTemporary">Opens the temporary file; tests pass a slow stream.</param>
    /// <returns>Whether the final file now exists.</returns>
    internal static bool Save(string path, string token, string appVersion, IReadOnlyList<MonitorSample> samples,
        UsageLedgerState? usage, RestartClock clock, Func<string, Stream>? createTemporary = null,
        long maxBytes = long.MaxValue)
    {
        var start = clock.Timestamp();
        var directory = Path.GetDirectoryName(path) ?? ".";
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = (createTemporary ?? CreateTemporary)(temporary))
            {
                var header = Header(token, appVersion, clock);
                stream.Write(header);
                using var buffer = new MemoryStream();
                WriteChunk(stream, buffer, LedgerChunk, writer => WriteUsage(writer, usage));
                var chunks = 1;
                for (var end = samples.Count;
                     end > 0 && clock.SecondsSince(start) < StopChunksAfterSeconds;
                     end -= ChunkSamples)
                {
                    var first = Math.Max(0, end - ChunkSamples);
                    var last = end;
                    WriteChunk(stream, buffer, SampleChunk, writer => WriteSamples(writer, samples, first, last));
                    if (stream.Position > maxBytes) throw new InvalidDataException("History exceeds the file size limit.");
                    chunks++;
                }

                Span<byte> trailer = stackalloc byte[TrailerLength];
                TrailerMagic.CopyTo(trailer);
                BinaryPrimitives.WriteInt32LittleEndian(trailer[4..], chunks);
                BinaryPrimitives.WriteUInt32LittleEndian(trailer[8..], Crc32.Compute(header));
                stream.Write(trailer);
                if (stream.Position > maxBytes) throw new InvalidDataException("History exceeds the file size limit.");
                if (stream is FileStream file) file.Flush(true);
                else stream.Flush();
            }

            if (clock.SecondsSince(start) > Budget.TotalSeconds)
            {
                Trace.TraceWarning("WinMon restart history missed its deadline and was discarded.");
                TryDelete(temporary);
                return false;
            }

            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon could not save restart history: {0}", ex);
            TryDelete(temporary);
            return false;
        }
    }

    /// <summary>Save the current retention window for an ordinary exit, without carrying Usage totals.</summary>
    internal static bool SaveNormal(string path, string appVersion, IReadOnlyList<MonitorSample> samples,
        RestartClock clock)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            return Save(path, NormalToken, appVersion, samples, null, clock, maxBytes: 64L * 1024 * 1024);
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon could not save normal history: {0}", ex);
            return false;
        }
    }

    /// <summary>Restore recent samples onto this process's clock, including after a reboot.</summary>
    /// <returns>Restored samples and the offset to apply to subsequent live samples.</returns>
    internal static (IReadOnlyList<MonitorSample> Samples, long LiveOffset)? ReadNormal(
        string path, RestartClock clock, DateTimeOffset now, int retentionSeconds)
    {
        DeleteTemporaries(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            if (stream.Length > 64L * 1024 * 1024) return null;
            var contents = Read(stream, NormalToken, clock, validateRestart: false);
            if (contents is null || contents.Samples.Count == 0) return null;
            var newest = contents.Samples[^1];
            var elapsed = (now - newest.Timestamp).TotalSeconds;
            if (elapsed < -2 || elapsed >= retentionSeconds || newest.MonotonicTimestamp <= 0) return null;
            var liveOffset = Math.Max(0L, checked((long)(retentionSeconds + 1) * clock.Frequency - clock.Timestamp()));
            var targetNewest = checked(clock.Timestamp() + liveOffset - (long)(elapsed * clock.Frequency));
            var shift = checked(targetNewest - newest.MonotonicTimestamp);
            var cutoff = now.AddSeconds(-retentionSeconds);
            var samples = contents.Samples.Where(s => s.Timestamp >= cutoff && s.Timestamp <= now.AddSeconds(2))
                .Select(s => s with { MonotonicTimestamp = checked(s.MonotonicTimestamp + shift) }).ToArray();
            if (samples.Length == 0 || samples[0].MonotonicTimestamp <= 0) return null;
            return (samples, liveOffset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException or ArgumentException)
        {
            Trace.TraceWarning("WinMon could not restore normal history: {0}", ex.Message);
            return null;
        }
        finally
        {
            TryDelete(path);
        }
    }

    internal static void DeleteNormal(string path)
    {
        DeleteTemporaries(path);
        TryDelete(path);
    }

    /// <summary>
    /// Remove temporary files left when a session-end save outlived its wait and the process ended mid-write.
    /// Saves run only at exit, and a temporary file still open for writing cannot be deleted.
    /// </summary>
    private static void DeleteTemporaries(string path)
    {
        try
        {
            foreach (var temporary in Directory.EnumerateFiles(Path.GetDirectoryName(path) ?? ".",
                         $".{Path.GetFileName(path)}.*.tmp"))
                TryDelete(temporary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon could not clean up history temporary files: {0}", ex.Message);
        }
    }

    /// <summary>
    /// Read a restart file within <see cref="Budget"/> and delete it, whatever the outcome. Never throws.
    /// </summary>
    /// <returns>The valid newest history, or null when the file is missing or fails the header or trailer checks.</returns>
    internal static RestartContents? Read(string path, string token, RestartClock clock)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            return Read(stream, token, clock);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon could not read restart history: {0}", ex);
            return null;
        }
        finally
        {
            TryDelete(path);
        }
    }

    internal static RestartContents? Read(Stream stream, string token, RestartClock clock,
        bool validateRestart = true)
    {
        var start = clock.Timestamp();
        if (!stream.CanSeek || stream.Length < HeaderMagic.Length + TrailerLength) return null;
        long headerLength, savedTimestamp;
        using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
        {
            if (!reader.ReadBytes(HeaderMagic.Length).AsSpan().SequenceEqual(HeaderMagic)) return null;
            if (reader.ReadInt32() != FormatVersion) return Reject("an unknown format version");
            _ = reader.ReadString();
            if (reader.ReadString() != token) return Reject("another update's token");
            var frequency = reader.ReadInt64();
            savedTimestamp = reader.ReadInt64();
            var savedTickCount = reader.ReadInt64();
            if (frequency != clock.Frequency) return Reject("a different timer frequency");
            var timerSeconds = (clock.Timestamp() - savedTimestamp) / (double)frequency;
            var tickSeconds = (clock.TickCount() - savedTickCount) / 1000d;
            if (validateRestart && (timerSeconds is < 0 or > MaxAgeSeconds || tickSeconds is < 0 or > MaxAgeSeconds ||
                Math.Abs(timerSeconds - tickSeconds) > ClockToleranceSeconds))
                return Reject("monotonic clocks that do not match a recent restart");
            headerLength = stream.Position;
        }

        var header = new byte[headerLength];
        stream.Position = 0;
        stream.ReadExactly(header);
        if (stream.Length < headerLength + TrailerLength) return null;
        Span<byte> trailer = stackalloc byte[TrailerLength];
        stream.Position = stream.Length - TrailerLength;
        stream.ReadExactly(trailer);
        if (!trailer[..4].SequenceEqual(TrailerMagic) ||
            BinaryPrimitives.ReadUInt32LittleEndian(trailer[8..]) != Crc32.Compute(header))
            return Reject("an unfinished save");
        var count = BinaryPrimitives.ReadInt32LittleEndian(trailer[4..]);

        var end = stream.Length - TrailerLength;
        stream.Position = headerLength;
        UsageLedgerState? usage = null;
        var chunks = new List<List<MonitorSample>>();
        Span<byte> frame = stackalloc byte[FrameLength];
        for (var i = 0; i < count && clock.SecondsSince(start) < Budget.TotalSeconds; i++)
        {
            if (end - stream.Position < FrameLength) break;
            stream.ReadExactly(frame);
            var kind = frame[0];
            var length = BinaryPrimitives.ReadInt32LittleEndian(frame[1..]);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(frame[5..]);
            if (length < 0 || length > end - stream.Position) break;
            var payload = new byte[length];
            stream.ReadExactly(payload);
            var valid = Crc32.Compute(payload) == crc;
            if (kind == LedgerChunk)
            {
                // A bad ledger costs only the Usage totals; the samples behind it are still read.
                if (valid) usage = TryParse(payload, ReadUsage);
                continue;
            }

            if (kind != SampleChunk || !valid || TryParse(payload, ReadSamples) is not { } samples) break;
            chunks.Add(samples);
        }

        chunks.Reverse();
        MonitorSample[] restored = [.. chunks.SelectMany(chunk => chunk)];
        // Same boot, so unshifted samples all predate the save; any lead over it is the saver's history offset.
        var liveOffset = restored.Length == 0 ? 0 : Math.Max(0, restored[^1].MonotonicTimestamp - savedTimestamp);
        return new RestartContents(restored, usage, liveOffset);
    }

    private static RestartContents? Reject(string reason)
    {
        Trace.TraceWarning("WinMon discarded restart history with {0}.", reason);
        return null;
    }

    private static Stream CreateTemporary(string path)
    {
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
    }

    private static byte[] Header(string token, string appVersion, RestartClock clock)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, true))
        {
            writer.Write(HeaderMagic);
            writer.Write(FormatVersion);
            writer.Write(appVersion);
            writer.Write(token);
            writer.Write(clock.Frequency);
            writer.Write(clock.Timestamp());
            writer.Write(clock.TickCount());
        }

        return buffer.ToArray();
    }

    private static void WriteChunk(Stream stream, MemoryStream buffer, byte kind, Action<BinaryWriter> body)
    {
        buffer.SetLength(0);
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(deflate, Encoding.UTF8, true))
        {
            body(writer);
        }

        var payload = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        Span<byte> frame = stackalloc byte[FrameLength];
        frame[0] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(frame[1..], payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame[5..], Crc32.Compute(payload));
        stream.Write(frame);
        stream.Write(payload);
    }

    private static T? TryParse<T>(byte[] payload, Func<BinaryReader, T> read) where T : class?
    {
        try
        {
            using var deflate = new DeflateStream(new MemoryStream(payload), CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate, Encoding.UTF8);
            return read(reader);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or
                                       FormatException or OverflowException or IndexOutOfRangeException)
        {
            Trace.TraceWarning("WinMon restart history chunk could not be parsed: {0}", ex.Message);
            return null;
        }
    }

    private static void WriteSamples(BinaryWriter writer, IReadOnlyList<MonitorSample> samples, int first, int end)
    {
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        var table = new List<string>();

        for (var i = first; i < end; i++)
            foreach (var process in samples[i].Processes)
            {
                Add(process.Name);
                Add(process.ImagePath);
                Add(process.AppName);
                Add(process.ProductName);
                Add(process.Publisher);
                Add(process.GpuEngine);
                Add(process.GpuId);
            }

        writer.Write(table.Count);
        foreach (var value in table) writer.Write(value);
        writer.Write(end - first);
        for (var i = first; i < end; i++) WriteSample(writer, samples[i], strings);
        return;

        void Add(string value)
        {
            if (strings.TryAdd(value, table.Count)) table.Add(value);
        }
    }

    private static List<MonitorSample> ReadSamples(BinaryReader reader)
    {
        var table = new string[Count(reader)];
        for (var i = 0; i < table.Length; i++) table[i] = reader.ReadString();
        var count = Count(reader);
        var samples = new List<MonitorSample>(count);
        for (var i = 0; i < count; i++) samples.Add(ReadSample(reader, table));
        return samples;
    }

    private static void WriteSample(BinaryWriter writer, MonitorSample sample, Dictionary<string, int> strings)
    {
        WriteTime(writer, sample.Timestamp);
        Write(writer, sample.TotalCpuPercent);
        writer.Write(sample.UnavailableProcesses);
        writer.Write(sample.IsGap);
        writer.Write(sample.MonotonicTimestamp);
        writer.Write(sample.CompactedProcesses);
        writer.Write(sample.IntervalSeconds);
        writer.Write(sample.IntervalCpuTicks);
        WriteResources(writer, sample.Resources);
        writer.Write(sample.Processes.Count);
        foreach (var process in sample.Processes)
        {
            writer.Write(process.ProcessId);
            writer.Write(process.CreationTicks);
            writer.Write(strings[process.Name]);
            writer.Write(process.CpuPercent);
            Write(writer, process.WorkingSetBytes);
            Write(writer, process.IoBytesPerSecond);
            writer.Write(process.ParentProcessId);
            Write(writer, process.ThreadCount);
            Write(writer, process.HandleCount);
            Write(writer, process.GpuPercent);
            writer.Write(strings[process.GpuEngine]);
            writer.Write(strings[process.GpuId]);
            Write(writer, process.PrivateBytes);
            writer.Write(strings[process.ImagePath]);
            writer.Write(strings[process.AppName]);
            writer.Write(strings[process.ProductName]);
            writer.Write(strings[process.Publisher]);
            byte hasAppWindow = process.HasAppWindow switch { null => 0, false => 1, true => 2 };
            writer.Write(hasAppWindow);
            writer.Write(process.HasHadAppWindow);
            writer.Write(process.MemberCount);
            writer.Write(process.MemberIds.Count);
            foreach (var id in process.MemberIds) writer.Write(id);
        }
    }

    private static MonitorSample ReadSample(BinaryReader reader, string[] table)
    {
        var timestamp = ReadTime(reader);
        var total = ReadDouble(reader);
        var unavailable = reader.ReadInt32();
        var gap = reader.ReadBoolean();
        var monotonic = reader.ReadInt64();
        var compacted = reader.ReadInt32();
        var intervalSeconds = reader.ReadDouble();
        var intervalCpuTicks = reader.ReadInt64();
        var resources = ReadResources(reader);
        var processes = new ProcessUsage[Count(reader)];
        for (var i = 0; i < processes.Length; i++)
        {
            var id = reader.ReadInt32();
            var creation = reader.ReadInt64();
            var name = table[reader.ReadInt32()];
            var cpu = reader.ReadDouble();
            var workingSet = ReadUInt64(reader);
            var io = ReadDouble(reader);
            processes[i] = new ProcessUsage(id, creation, name, cpu, workingSet, io)
            {
                ParentProcessId = reader.ReadInt32(),
                ThreadCount = ReadInt64(reader),
                HandleCount = ReadInt64(reader),
                GpuPercent = ReadDouble(reader),
                GpuEngine = table[reader.ReadInt32()],
                GpuId = table[reader.ReadInt32()],
                PrivateBytes = ReadUInt64(reader),
                ImagePath = table[reader.ReadInt32()],
                AppName = table[reader.ReadInt32()],
                ProductName = table[reader.ReadInt32()],
                Publisher = table[reader.ReadInt32()],
                HasAppWindow = reader.ReadByte() switch
                {
                    0 => null, 1 => false, 2 => true, _ => throw new InvalidDataException("Bad app-window flag.")
                },
                HasHadAppWindow = reader.ReadBoolean(),
                MemberCount = reader.ReadInt32(),
                MemberIds = ReadIds(reader)
            };
        }

        return new MonitorSample(timestamp, total, processes, unavailable, gap, monotonic)
        {
            Resources = resources,
            CompactedProcesses = compacted,
            IntervalSeconds = intervalSeconds,
            IntervalCpuTicks = intervalCpuTicks
        };
    }

    private static int[] ReadIds(BinaryReader reader)
    {
        var ids = new int[Count(reader)];
        for (var i = 0; i < ids.Length; i++) ids[i] = reader.ReadInt32();
        return ids;
    }

    // Field order is the format: adding a ResourceSample field means a new format version and fixture.
    private static void WriteResources(BinaryWriter writer, ResourceSample r)
    {
        Write(writer, r.CpuClockMhz);
        Write(writer, r.CpuKernelPercent);
        Write(writer, r.AvailableMemoryBytes);
        Write(writer, r.CommittedBytes);
        Write(writer, r.CommitLimitBytes);
        Write(writer, r.CachedBytes);
        Write(writer, r.PagedPoolBytes);
        Write(writer, r.NonPagedPoolBytes);
        Write(writer, r.ProcessCount);
        Write(writer, r.ThreadCount);
        Write(writer, r.HandleCount);
        Write(writer, r.GpuPercent);
        writer.Write(r.GpuId);
        writer.Write(r.GpuEngine);
        Write(writer, r.GpuMemoryUsedBytes);
        Write(writer, r.GpuMemoryTotalBytes);
        Write(writer, r.GpuSharedMemoryUsedBytes);
        writer.Write(r.GpuName);
        Write(writer, r.TotalMemoryBytes);
        Write(writer, r.UsedMemoryBytes);
        writer.Write(r.DiskName);
        Write(writer, r.DiskActivePercent);
        Write(writer, r.DiskBytesPerSecond);
        Write(writer, r.DiskReadBytesPerSecond);
        Write(writer, r.DiskWriteBytesPerSecond);
        Write(writer, r.DiskResponseSeconds);
        Write(writer, r.DiskQueueLength);
        Write(writer, r.DiskFreeBytes);
        Write(writer, r.DiskTotalBytes);
        writer.Write(r.SecondaryDiskName);
        Write(writer, r.SecondaryDiskActivePercent);
        Write(writer, r.SecondaryDiskBytesPerSecond);
        Write(writer, r.SecondaryDiskReadBytesPerSecond);
        Write(writer, r.SecondaryDiskWriteBytesPerSecond);
        Write(writer, r.SecondaryDiskResponseSeconds);
        Write(writer, r.SecondaryDiskQueueLength);
        Write(writer, r.SecondaryDiskFreeBytes);
        Write(writer, r.SecondaryDiskTotalBytes);
        writer.Write(r.NetworkName);
        writer.Write(r.NetworkId);
        Write(writer, r.LinkBitsPerSecond);
        Write(writer, r.ReceiveBytesPerSecond);
        Write(writer, r.SendBytesPerSecond);
        Write(writer, r.ReceivedBytesSinceStart);
        Write(writer, r.SentBytesSinceStart);
    }

    private static ResourceSample ReadResources(BinaryReader reader)
    {
        return new ResourceSample
        {
            CpuClockMhz = ReadDouble(reader),
            CpuKernelPercent = ReadDouble(reader),
            AvailableMemoryBytes = ReadUInt64(reader),
            CommittedBytes = ReadUInt64(reader),
            CommitLimitBytes = ReadUInt64(reader),
            CachedBytes = ReadUInt64(reader),
            PagedPoolBytes = ReadUInt64(reader),
            NonPagedPoolBytes = ReadUInt64(reader),
            ProcessCount = ReadUInt32(reader),
            ThreadCount = ReadUInt32(reader),
            HandleCount = ReadUInt32(reader),
            GpuPercent = ReadDouble(reader),
            GpuId = reader.ReadString(),
            GpuEngine = reader.ReadString(),
            GpuMemoryUsedBytes = ReadUInt64(reader),
            GpuMemoryTotalBytes = ReadUInt64(reader),
            GpuSharedMemoryUsedBytes = ReadUInt64(reader),
            GpuName = reader.ReadString(),
            TotalMemoryBytes = ReadUInt64(reader),
            UsedMemoryBytes = ReadUInt64(reader),
            DiskName = reader.ReadString(),
            DiskActivePercent = ReadDouble(reader),
            DiskBytesPerSecond = ReadDouble(reader),
            DiskReadBytesPerSecond = ReadDouble(reader),
            DiskWriteBytesPerSecond = ReadDouble(reader),
            DiskResponseSeconds = ReadDouble(reader),
            DiskQueueLength = ReadDouble(reader),
            DiskFreeBytes = ReadUInt64(reader),
            DiskTotalBytes = ReadUInt64(reader),
            SecondaryDiskName = reader.ReadString(),
            SecondaryDiskActivePercent = ReadDouble(reader),
            SecondaryDiskBytesPerSecond = ReadDouble(reader),
            SecondaryDiskReadBytesPerSecond = ReadDouble(reader),
            SecondaryDiskWriteBytesPerSecond = ReadDouble(reader),
            SecondaryDiskResponseSeconds = ReadDouble(reader),
            SecondaryDiskQueueLength = ReadDouble(reader),
            SecondaryDiskFreeBytes = ReadUInt64(reader),
            SecondaryDiskTotalBytes = ReadUInt64(reader),
            NetworkName = reader.ReadString(),
            NetworkId = reader.ReadString(),
            LinkBitsPerSecond = ReadInt64(reader),
            ReceiveBytesPerSecond = ReadDouble(reader),
            SendBytesPerSecond = ReadDouble(reader),
            ReceivedBytesSinceStart = ReadUInt64(reader),
            SentBytesSinceStart = ReadUInt64(reader)
        };
    }

    private static void WriteUsage(BinaryWriter writer, UsageLedgerState? usage)
    {
        writer.Write(usage is not null);
        if (usage is null) return;
        WriteTime(writer, usage.Since);
        writer.Write(usage.MachineBusyTicks);
        writer.Write(usage.AttributedTicks);
        writer.Write(usage.Apps.Count);
        foreach (var app in usage.Apps)
        {
            writer.Write(app.Key);
            writer.Write(app.Name);
            writer.Write(app.ImagePath);
            writer.Write(app.AppName);
            writer.Write(app.Publisher);
            writer.Write(app.CpuTicks);
            writer.Write(app.IoBytes);
            Write(writer, app.GpuSeconds);
            writer.Write(app.IsApp);
            writer.Write(app.LastActive is not null);
            if (app.LastActive is { } key)
            {
                writer.Write(key.MonotonicTimestamp);
                WriteTime(writer, key.Timestamp);
            }

            writer.Write(app.Lifetimes);
        }

        writer.Write(usage.Lifetimes.Count);
        foreach (var lifetime in usage.Lifetimes)
        {
            writer.Write(lifetime.ProcessId);
            writer.Write(lifetime.CreationTicks);
            writer.Write(lifetime.App);
            writer.Write(lifetime.Absences);
        }
    }

    private static UsageLedgerState? ReadUsage(BinaryReader reader)
    {
        if (!reader.ReadBoolean()) return null;
        var since = ReadTime(reader);
        var busy = reader.ReadDouble();
        var attributed = reader.ReadDouble();
        var apps = new UsageAppState[Count(reader)];
        for (var i = 0; i < apps.Length; i++)
        {
            var key = reader.ReadString();
            var name = reader.ReadString();
            var imagePath = reader.ReadString();
            var appName = reader.ReadString();
            var publisher = reader.ReadString();
            var cpu = reader.ReadDouble();
            var io = reader.ReadDouble();
            var gpu = ReadDouble(reader);
            var isApp = reader.ReadBoolean();
            SampleKey? lastActive = reader.ReadBoolean() ? new SampleKey(reader.ReadInt64(), ReadTime(reader)) : null;
            apps[i] = new UsageAppState(key, name, imagePath, appName, publisher, cpu, io, gpu, isApp, lastActive,
                reader.ReadInt32());
        }

        var lifetimes = new UsageLifetimeState[Count(reader)];
        for (var i = 0; i < lifetimes.Length; i++)
        {
            lifetimes[i] = new UsageLifetimeState(reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt32(),
                reader.ReadInt32());
            if ((uint)lifetimes[i].App >= (uint)apps.Length)
                throw new InvalidDataException("A lifetime refers to a missing app.");
        }

        return new UsageLedgerState(since, busy, attributed, apps, lifetimes);
    }

    // A corrupt count must fail the chunk rather than allocate gigabytes.
    private static int Count(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        return count is >= 0 and <= 10_000_000 ? count : throw new InvalidDataException("Bad count.");
    }

    private static void WriteTime(BinaryWriter writer, DateTimeOffset time)
    {
        writer.Write(time.UtcTicks);
        writer.Write((short)time.Offset.TotalMinutes);
    }

    private static DateTimeOffset ReadTime(BinaryReader reader)
    {
        var utc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
        return utc.ToOffset(TimeSpan.FromMinutes(reader.ReadInt16()));
    }

    private static void Write(BinaryWriter writer, double? value)
    {
        writer.Write(value.HasValue);
        if (value is { } number) writer.Write(number);
    }

    private static void Write(BinaryWriter writer, ulong? value)
    {
        writer.Write(value.HasValue);
        if (value is { } number) writer.Write(number);
    }

    private static void Write(BinaryWriter writer, long? value)
    {
        writer.Write(value.HasValue);
        if (value is { } number) writer.Write(number);
    }

    private static void Write(BinaryWriter writer, uint? value)
    {
        writer.Write(value.HasValue);
        if (value is { } number) writer.Write(number);
    }

    private static double? ReadDouble(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadDouble() : null;
    }

    private static ulong? ReadUInt64(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadUInt64() : null;
    }

    private static long? ReadInt64(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadInt64() : null;
    }

    private static uint? ReadUInt32(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadUInt32() : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon could not delete {0}: {1}", path, ex.Message);
        }
    }

    /// <summary>CRC-32 (IEEE 802.3), so the format needs no package beyond the runtime.</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table =
        [
            .. Enumerable.Range(0, 256).Select(n =>
            {
                var c = (uint)n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                return c;
            })
        ];

        internal static uint Compute(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }
}
