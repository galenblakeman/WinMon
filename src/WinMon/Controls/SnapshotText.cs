using System.Globalization;
using System.Text;
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
        Row(Strings.CPU, Percent(sample.TotalCpuPercent));
        Row(Strings.Memory, r is { UsedMemoryBytes: { } used, TotalMemoryBytes: { } total }
            ? $"{Strings.Format(Strings.GiB, used / 1_073_741_824d, total / 1_073_741_824d)} ({Percent(r.MemoryPercent)})"
            : Percent(r.MemoryPercent));
        Row(Strings.GPU, Percent(r.GpuPercent));
        Disk(r.DiskName, r.DiskActivePercent, r.DiskBytesPerSecond);
        if (r.SecondaryDiskName.Length > 0)
            Disk(r.SecondaryDiskName, r.SecondaryDiskActivePercent, r.SecondaryDiskBytesPerSecond);
        Row(Strings.Network,
            $"{Clean(r.NetworkName)} · ↓ {Rate(r.ReceiveBytesPerSecond)} · ↑ {Rate(r.SendBytesPerSecond)}");

        text.AppendLine();
        text.AppendLine(Strings.Format(Strings.SnapshotExportProcesses,
            report.Grouped ? Strings.Grouped : Strings.Individual, report.Sort));
        text.AppendLine(Strings.SnapshotExportAverages);
        if (report.Grouped) text.AppendLine(Strings.IdleGroupMembersMayBeOmitted);
        if (report.Filter.Length > 0) Row(Strings.SearchNameOrPID, report.Filter);
        if (report.AppsOnly) text.AppendLine(Strings.ProcessKindApp);
        if (report.Coverage.Length > 0) text.AppendLine(report.Coverage);
        Row(Strings.ProcessColumn, "PID", $"{Strings.CPU} (%)", $"{Strings.GPU} (%)",
            $"{(report.Grouped ? Strings.Private : Strings.Memory)} (MiB)", Strings.IOMBS, Strings.Threads,
            Strings.Handles);
        for (var index = 0; index < report.Processes.Count; index++)
        {
            var p = report.Processes[index];
            var state = report.ProcessStates is { } states && index < states.Count ? states[index] : "";
            var name = !string.IsNullOrWhiteSpace(p.AppName) ? p.AppName :
                !string.IsNullOrWhiteSpace(p.ProductName) ? p.ProductName : p.Name;
            if (p.MemberCount > 1) name += $" ×{p.MemberCount}";
            if (state.Length > 0)
            {
                Row($"{name} · {state}", p.ProcessId.ToString(CultureInfo.CurrentCulture),
                    Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading,
                    Strings.MissingReading, Strings.MissingReading);
                continue;
            }

            Row(name,
                p.MemberIds.Count > 0
                    ? string.Join(", ", p.MemberIds)
                    : p.ProcessId.ToString(CultureInfo.CurrentCulture),
                Number(p.CpuPercent), Number(p.GpuPercent),
                Number((report.Grouped ? p.PrivateBytes : p.WorkingSetBytes) / 1_048_576d),
                Number(p.IoBytesPerSecond / 1_000_000d, "0.00"), Count(p.ThreadCount), Count(p.HandleCount));
        }

        if (report.Remainder is { } rest)
            Row(Strings.Format(report.Grouped ? Strings.OtherExecutables : Strings.OtherProcesses, rest.Count), "",
                Number(rest.CpuPercent), Strings.MissingReading, Strings.MissingReading,
                Number(rest.IoBytesPerSecond / 1_000_000d, "0.00"), Strings.MissingReading, Strings.MissingReading);
        return text.ToString().TrimEnd();

        void Row(params string[] cells)
        {
            text.AppendLine(string.Join('\t', cells.Select(Clean)));
        }

        void Disk(string name, double? active, double? bytes)
        {
            Row(name.Length == 0 ? Strings.Disk : Strings.Format(Strings.DiskLabel, name),
                $"{Percent(active)} · {(bytes is { } b ? Strings.Format(Strings.MBS, b / 1_000_000d) : Strings.MissingReading)}");
        }
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

    private static string Clean(string value)
    {
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}