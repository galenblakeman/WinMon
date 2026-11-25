using System.Globalization;
using System.Text;
using MonCore.Monitoring;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>The selected sample and the exact ranked, filtered rows last presented by the window.</summary>
internal sealed record SnapshotReport(
    MonitorSample Sample,
    IReadOnlyList<ProcessUsage> Processes,
    bool Grouped,
    string Sort,
    string Filter,
    bool AppsOnly,
    string Coverage,
    SnapshotRemainder? Remainder)
{
    internal IReadOnlyList<string>? ProcessStates { get; init; }
    internal int TopCount { get; init; }
    internal bool TriggerIncludedOutsideTop { get; init; }
}

internal sealed record SnapshotRemainder(int Count, double CpuPercent, double? IoBytesPerSecond);

internal static class SnapshotText
{
    internal static string Format(SnapshotReport report)
    {
        var text = new StringBuilder();
        var sample = report.Sample;
        var r = sample.Resources;
        text.AppendLine(Strings.Format(Strings.ProductVersion, AppInfo.Version));
        text.AppendLine(Strings.Format(Strings.ExportSnapshotTime, sample.Timestamp));
        text.AppendLine(Strings.SnapshotExportIntervals);
        AppendRow(text, Strings.CPU, Percent(sample.TotalCpuPercent));
        AppendRow(text, Strings.Memory, r is { UsedMemoryBytes: { } used, TotalMemoryBytes: { } total }
            ? $"{Strings.Format(Strings.GiB, used / 1_073_741_824d, total / 1_073_741_824d)} ({Percent(r.MemoryPercent)})"
            : Percent(r.MemoryPercent));
        AppendRow(text, Strings.DetailSystemCache, Bytes(r.CachedBytes));
        AppendRow(text, Strings.DetailPagingReads, PerSecond(r.PagingReadsPerSecond));
        AppendRow(text, Strings.DetailStandby, Bytes(r.StandbyBytes));
        AppendRow(text, Strings.DetailModified, Bytes(r.ModifiedBytes));
        AppendRow(text, Strings.DetailFree, Bytes(r.FreeBytes));
        AppendRow(text, Strings.DetailCompressionStore, Bytes(r.CompressionStoreBytes));
        AppendRow(text, Strings.GPU, Percent(r.GpuPercent));
        AppendDisk(text, r.DiskName, r.DiskActivePercent, r.DiskBytesPerSecond);
        if (r.SecondaryDiskName.Length > 0)
            AppendDisk(text, r.SecondaryDiskName, r.SecondaryDiskActivePercent, r.SecondaryDiskBytesPerSecond);
        AppendRow(text, Strings.Network,
            $"{Clean(r.NetworkName)} · ↓ {Rate(r.ReceiveBytesPerSecond)} · ↑ {Rate(r.SendBytesPerSecond)}");

        text.AppendLine();
        text.AppendLine(Strings.Format(Strings.SnapshotExportProcesses,
            report.Grouped ? Strings.Grouped : Strings.Individual, report.Sort));
        text.AppendLine(Strings.SnapshotExportAverages);
        text.AppendLine(Strings.Format(Strings.CaptureReportProcessCount,
            report.TopCount == 0 ? Strings.AllEntries : report.TopCount.ToString(CultureInfo.CurrentCulture),
            report.Processes.Count));
        if (report.TriggerIncludedOutsideTop) text.AppendLine(Strings.CaptureReportExtraTrigger);
        if (report.Grouped) text.AppendLine(Strings.IdleGroupMembersMayBeOmitted);
        if (report.Filter.Length > 0) AppendRow(text, Strings.SearchNameOrPID, report.Filter);
        if (report.AppsOnly) text.AppendLine(Strings.ProcessKindApp);
        if (report.Coverage.Length > 0) text.AppendLine(report.Coverage);
        AppendProcessTable(text, report);
        return text.ToString().TrimEnd();
    }

    private static string Number(double? value, string format = "0.0")
    {
        return value?.ToString(format, CultureInfo.CurrentCulture) ?? Strings.MissingReading;
    }

    private static string Count(long? value)
    {
        return value?.ToString(CultureInfo.CurrentCulture) ?? Strings.MissingReading;
    }

    private static string Percent(double? value)
    {
        return value is { } v ? Strings.Format(Strings.PercentOneDecimal, v) : Strings.MissingReading;
    }

    private static string Rate(double? bytes)
    {
        return bytes is { } b ? RateText.Format(b * 8) : Strings.MissingReading;
    }

    private static string Bytes(ulong? bytes)
    {
        return bytes is { } value ? UsageText.Bytes(value) : Strings.MissingReading;
    }

    private static string PerSecond(double? value)
    {
        return value is { } rate ? rate.ToString("0.0", CultureInfo.CurrentCulture) + "/s" : Strings.MissingReading;
    }

    private static string Clean(string value)
    {
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    private static void AppendProcessTable(StringBuilder text, SnapshotReport report)
    {
        AppendRow(text, Strings.ProcessColumn, "PID", $"{Strings.CPU} (%)", $"{Strings.GPU} (%)",
            $"{(report.Grouped ? Strings.Private : Strings.Memory)} (MiB)", Strings.IOMBS, Strings.Threads,
            Strings.Handles, Strings.ProcessPathLabel);
        for (var index = 0; index < report.Processes.Count; index++)
        {
            var p = report.Processes[index];
            var state = report.ProcessStates is { } states && index < states.Count ? states[index] : "";
            var name = !string.IsNullOrWhiteSpace(p.AppName) ? p.AppName :
                !string.IsNullOrWhiteSpace(p.ProductName) ? p.ProductName : p.Name;
            if (p.MemberCount > 1) name += $" ×{p.MemberCount}";
            if (state.Length > 0)
            {
                AppendRow(text, $"{name} · {state}", p.ProcessId.ToString(CultureInfo.CurrentCulture),
                    Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading,
                    Strings.MissingReading, Strings.MissingReading,
                    p.ImagePath.Length > 0 ? p.ImagePath : Strings.ExecutablePathUnavailable);
                continue;
            }

            AppendRow(text, name,
                p.MemberIds.Count > 0
                    ? string.Join(", ", p.MemberIds)
                    : p.ProcessId.ToString(CultureInfo.CurrentCulture),
                Number(p.CpuPercent), Number(p.GpuPercent),
                Number((report.Grouped ? p.PrivateBytes : p.WorkingSetBytes) / 1_048_576d),
                Number(p.IoBytesPerSecond / 1_000_000d, "0.00"), Count(p.ThreadCount), Count(p.HandleCount),
                p.ImagePath.Length > 0 ? p.ImagePath : Strings.ExecutablePathUnavailable);
        }

        if (report.Remainder is { } rest)
            AppendRow(text, Strings.Format(report.Grouped ? Strings.OtherExecutables : Strings.OtherProcesses, rest.Count), "",
                Number(rest.CpuPercent), Strings.MissingReading, Strings.MissingReading,
                Number(rest.IoBytesPerSecond / 1_000_000d, "0.00"), Strings.MissingReading, Strings.MissingReading,
                Strings.MissingReading);
    }

    private static void AppendRow(StringBuilder text, params string[] cells)
    {
        text.AppendLine(string.Join('\t', cells.Select(Clean)));
    }

    private static void AppendDisk(StringBuilder text, string name, double? active, double? bytes)
    {
        AppendRow(text, name.Length == 0 ? Strings.Disk : Strings.Format(Strings.DiskLabel, name),
            $"{Percent(active)} · {(bytes is { } b ? Strings.Format(Strings.MBS, b / 1_000_000d) : Strings.MissingReading)}");
    }
}
