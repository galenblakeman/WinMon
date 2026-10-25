using System.Globalization;
using System.Text;

namespace WinMon.Monitoring;

internal enum CsvExportKind { ResourceHistory, ProcessSamples, UsageTotals }

/// <summary>Write retained observations and session usage in stable, invariant, raw-unit CSV columns.</summary>
internal static class CsvExport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static IReadOnlyList<MonitorSample> SelectSamples(IReadOnlyList<MonitorSample> samples, int? rangeSeconds)
    {
        if (rangeSeconds is null || samples.Count == 0) return samples;
        var start = samples[^1].Timestamp - TimeSpan.FromSeconds(rangeSeconds.Value);
        return [.. samples.Where(sample => sample.Timestamp >= start)];
    }

    /// <summary>Write to a sibling temporary file and replace the chosen file only after a complete export.</summary>
    internal static void Write(string path, CsvExportKind kind, IReadOnlyList<MonitorSample> samples,
        IReadOnlyList<AppUsage> apps, DateTimeOffset usageSince, long machineBusyTicks, long notAttributedTicks)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true), 1 << 16))
            {
                switch (kind)
                {
                    case CsvExportKind.ResourceHistory: WriteResources(writer, samples); break;
                    case CsvExportKind.ProcessSamples: WriteProcesses(writer, samples); break;
                    case CsvExportKind.UsageTotals:
                        WriteUsage(writer, apps, usageSince, machineBusyTicks, notAttributedTicks); break;
                    default: throw new ArgumentOutOfRangeException(nameof(kind));
                }
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteResources(TextWriter writer, IReadOnlyList<MonitorSample> samples)
    {
        Row(writer, "timestamp_utc", "is_gap", "interval_seconds", "cpu_percent", "cpu_kernel_percent",
            "cpu_clock_mhz", "memory_used_bytes", "memory_total_bytes", "gpu_percent", "gpu_engine",
            "gpu_dedicated_used_bytes", "gpu_shared_used_bytes", "disk_name", "disk_active_percent",
            "disk_read_bytes_per_second", "disk_write_bytes_per_second", "secondary_disk_name",
            "secondary_disk_active_percent", "secondary_disk_read_bytes_per_second",
            "secondary_disk_write_bytes_per_second", "network_name", "network_receive_bytes_per_second",
            "network_send_bytes_per_second");
        foreach (var sample in samples)
        {
            var r = sample.Resources;
            Row(writer, Utc(sample.Timestamp), Bool(sample.IsGap), Number(sample.IntervalSeconds),
                Number(sample.TotalCpuPercent), Number(r.CpuKernelPercent), Number(r.CpuClockMhz),
                Number(r.UsedMemoryBytes), Number(r.TotalMemoryBytes), Number(r.GpuPercent), Safe(r.GpuEngine),
                Number(r.GpuMemoryUsedBytes), Number(r.GpuSharedMemoryUsedBytes), Safe(r.DiskName),
                Number(r.DiskActivePercent), Number(r.DiskReadBytesPerSecond), Number(r.DiskWriteBytesPerSecond),
                Safe(r.SecondaryDiskName), Number(r.SecondaryDiskActivePercent), Number(r.SecondaryDiskReadBytesPerSecond),
                Number(r.SecondaryDiskWriteBytesPerSecond), Safe(r.NetworkName), Number(r.ReceiveBytesPerSecond),
                Number(r.SendBytesPerSecond));
        }
    }

    private static void WriteProcesses(TextWriter writer, IReadOnlyList<MonitorSample> samples)
    {
        Row(writer, "timestamp_utc", "pid", "creation_ticks", "parent_pid", "name", "app_name",
            "image_path", "publisher", "cpu_percent", "gpu_percent", "working_set_bytes", "private_bytes",
            "io_bytes_per_second", "threads", "handles", "has_app_window", "sample_compacted_processes");
        foreach (var sample in samples)
            foreach (var process in sample.Processes)
                Row(writer, Utc(sample.Timestamp), Number(process.ProcessId), Number(process.CreationTicks),
                    Number(process.ParentProcessId), Safe(process.Name), Safe(process.AppName), Safe(process.ImagePath),
                    Safe(process.Publisher), Number(process.CpuPercent), Number(process.GpuPercent),
                    Number(process.WorkingSetBytes), Number(process.PrivateBytes), Number(process.IoBytesPerSecond),
                    Number(process.ThreadCount), Number(process.HandleCount), Bool(process.HasAppWindow),
                    Number(sample.CompactedProcesses));
    }

    private static void WriteUsage(TextWriter writer, IReadOnlyList<AppUsage> apps, DateTimeOffset since,
        long machineBusyTicks, long notAttributedTicks)
    {
        Row(writer, "since_utc", "key", "name", "app_name", "image_path", "publisher", "cpu_seconds",
            "share_percent", "io_bytes", "gpu_seconds", "process_lifetimes", "running_processes", "is_app",
            "last_active_utc", "is_not_attributed");
        foreach (var app in apps.OrderByDescending(app => app.CpuTicks))
            Row(writer, Utc(since), app.Key == UsageLedger.OtherAppsKey ? "other_apps" : Safe(app.Key),
                app.Key == UsageLedger.OtherAppsKey ? "Other apps" : Safe(app.Name),
                Safe(app.AppName), Safe(app.ImagePath),
                Safe(app.Publisher),
                Number(app.CpuTicks / (double)TimeSpan.TicksPerSecond), Share(app.CpuTicks, machineBusyTicks),
                Number(app.IoBytes), Number(app.GpuSeconds), Number(app.Lifetimes), Number(app.Running),
                Bool(app.IsApp), app.LastActive is { } active ? Utc(active.Timestamp) : null, "false");
        Row(writer, Utc(since), "not_attributed", null, null, null, null,
            Number(notAttributedTicks / (double)TimeSpan.TicksPerSecond),
            Share(notAttributedTicks, machineBusyTicks), null, null, null, null, null, null, "true");
    }

    private static string? Share(long part, long whole) => whole > 0 ? Number(100d * part / whole) : null;
    private static string Safe(string value) => value.TrimStart().StartsWith('=') ||
                                                value.TrimStart().StartsWith('+') ||
                                                value.TrimStart().StartsWith('-') ||
                                                value.TrimStart().StartsWith('@') ? "'" + value : value;
    private static string Utc(DateTimeOffset timestamp) => timestamp.UtcDateTime.ToString("O", Invariant);
    private static string Bool(bool value) => value ? "true" : "false";
    private static string? Bool(bool? value) => value is { } present ? Bool(present) : null;
    private static string Number(int value) => value.ToString(Invariant);
    private static string Number(long value) => value.ToString(Invariant);
    private static string? Number(long? value) => value?.ToString(Invariant);
    private static string? Number(ulong? value) => value?.ToString(Invariant);
    private static string? Number(double? value) => value?.ToString("G17", Invariant);

    private static void Row(TextWriter writer, params string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) writer.Write(',');
            var value = values[i] ?? "";
            if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) writer.Write(value);
            else
            {
                writer.Write('"');
                writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
        }

        writer.WriteLine();
    }
}
