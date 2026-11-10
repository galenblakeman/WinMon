using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

public sealed record SnapshotProcess(int Pid, string Name, double? CpuPercent, ulong? WorkingSetBytes,
    ulong? PrivateBytes, double? IoBytesPerSecond, double? GpuPercent, long? Threads, long? Handles);

public sealed record SnapshotReport(int SchemaVersion, string Machine, DateTimeOffset Timestamp,
    double IntervalSeconds, bool IsGap, string? SamplingError, int UnavailableProcesses,
    double? CpuPercent, ulong? MemoryUsedBytes, ulong? MemoryTotalBytes, double? GpuPercent,
    string DiskName, double? DiskActivePercent, double? DiskReadBytesPerSecond, double? DiskWriteBytesPerSecond,
    string NetworkName, double? ReceiveBytesPerSecond, double? SendBytesPerSecond,
    string Sort, IReadOnlyList<SnapshotProcess> Processes)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static SnapshotReport Create(CapturedFrame frame, string machine, int top, ProcessSort sort, string filter)
        => Create(frame, machine, top, sort, filter, [frame.Full]);

    public static SnapshotReport Create(CapturedFrame frame, string machine, int top, ProcessSort sort, string filter,
        IReadOnlyList<MonitorSample> history)
    {
        var sample = frame.Full;
        var resources = sample.Resources;
        var ranked = ProcessRanking.Rank(history, sort: sort, includeInactive: true);
        var filtered = ProcessSearch.Filter(ranked.Processes, filter);
        var processes = (top == 0 ? filtered : filtered.Take(top))
            .Select(p => new SnapshotProcess(p.ProcessId, p.Name, p.ObservedCpuPercent, p.WorkingSetBytes,
                p.PrivateBytes, p.IoBytesPerSecond, p.GpuPercent, p.ThreadCount, p.HandleCount)).ToArray();
        return new SnapshotReport(1, machine, sample.Timestamp, sample.IntervalSeconds, sample.IsGap,
            frame.Error?.ToString(), sample.UnavailableProcesses, sample.TotalCpuPercent,
            resources.UsedMemoryBytes, resources.TotalMemoryBytes, resources.GpuPercent,
            resources.DiskName, resources.DiskActivePercent, resources.DiskReadBytesPerSecond,
            resources.DiskWriteBytesPerSecond, resources.NetworkName, resources.ReceiveBytesPerSecond,
            resources.SendBytesPerSecond, sort.ToString().ToLowerInvariant(), processes);
    }

    public void Render(IAnsiConsole console)
    {
        console.Write(new Rule(Markup.Escape($"{TerminalChrome.ProductName} | {SafeText(Machine)}")).RuleStyle("blue"));
        console.Write(new Text($"{Timestamp:O} | {IntervalSeconds:0.00} s\n"));
        var summary = new Table().Border(TableBorder.Simple).HideHeaders();
        summary.AddColumn("");
        summary.AddColumn("");
        summary.AddRow(new Text(Strings.CPU), new Text(Percent(CpuPercent)));
        summary.AddRow(new Text(Strings.Memory), new Text($"{Memory(MemoryUsedBytes)} / {Memory(MemoryTotalBytes)}"));
        summary.AddRow(new Text(Strings.GPU), new Text(Percent(GpuPercent)));
        summary.AddRow(new Text($"{Strings.Disk} {SafeText(DiskName)}"), new Text(Percent(DiskActivePercent)));
        summary.AddRow(new Text(Strings.ReadWriteIO), new Text($"{Rate(DiskReadBytesPerSecond)} / {Rate(DiskWriteBytesPerSecond)}"));
        summary.AddRow(new Text($"{Strings.Network} {SafeText(NetworkName)}"),
            new Text($"RX {NetworkRate(ReceiveBytesPerSecond)} / TX {NetworkRate(SendBytesPerSecond)}"));
        console.Write(summary);
        console.Write(BuildProcessTable());
        if (Processes.Count == 0) console.Write(new Text(Strings.CliNoMatches + "\n"));
        console.Write(new Text(Strings.CliMetrics + "\n"));
        if (IsGap || SamplingError is not null || UnavailableProcesses > 0)
            console.Write(new Text(string.Format(CultureInfo.CurrentCulture, Strings.CliIncomplete,
                SamplingError ?? Strings.Unavailable, UnavailableProcesses) + "\n"));
    }

    public Table BuildProcessTable()
    {
        var table = new Table().Border(TableBorder.Simple);
        foreach (var label in new[] { "PID", Strings.ProcessColumn, "CPU %", Strings.ProcessWorkingSetLabel, Strings.IO })
            table.AddColumn(new TableColumn(new Text(label)));
        if (Sort is "gpu" or "threads" or "handles")
            table.AddColumn(new TableColumn(new Text(Sort switch
            {
                "gpu" => Strings.GPU + " %", "threads" => Strings.Threads, _ => Strings.Handles
            })));
        foreach (var process in Processes)
        {
            var cells = new List<Text>
            {
                new(process.Pid.ToString(CultureInfo.InvariantCulture)), new Text(SafeText(process.Name)).Ellipsis(),
                new(Percent(process.CpuPercent)), new(Memory(process.WorkingSetBytes)), new(Rate(process.IoBytesPerSecond))
            };
            if (Sort is "gpu" or "threads" or "handles")
                cells.Add(new Text(Sort == "gpu" ? Percent(process.GpuPercent) :
                    (Sort == "threads" ? process.Threads : process.Handles)?.ToString(CultureInfo.CurrentCulture) ?? Strings.Unavailable));
            table.AddRow(cells.ToArray());
        }
        return table;
    }

    // Terminal control characters in process names and native error messages must never execute.
    public static string SafeText(string value) => string.Concat(value.Where(c => !char.IsControl(c) &&
        CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format));

    public static string Percent(double? value) => value is { } n ? $"{n:0.0}%" : Strings.Unavailable;
    public static string Memory(ulong? value) => value is { } n ? Controls.TableMetricText.Memory(n) : Strings.Unavailable;
    public static string Rate(double? value) => value is { } n ? Controls.TableMetricText.IoRate(n) : Strings.Unavailable;
    public static string NetworkRate(double? value) => value is { } n ? RateText.Format(n * 8) : Strings.Unavailable;
}
