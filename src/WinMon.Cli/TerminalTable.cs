namespace WinMon.Cli;

public enum TerminalTab { Processes, Services, Connections, Usage }

public sealed record TerminalRow
{
    private readonly Func<string> _createDetails;
    private readonly Func<string[]> _createCells;
    private string? _details;
    private string[]? _cells;
    public string Key { get; init; }
    // Deferred formatting is accessed only by the terminal owner, like the rest of the view state.
    public string[] Cells { get => _cells ??= _createCells(); init => _cells = value; }
    public string Details { get => _details ??= _createDetails(); init => _details = value; }
    public WinMon.Monitoring.ProcessUsage? Process { get; init; }
    public bool Grouped { get; init; }
    public string? PinIdentity { get; init; }
    public string? PinName { get; init; }

    public TerminalRow(string key, string[] cells, string details,
        WinMon.Monitoring.ProcessUsage? process = null, bool grouped = false)
        : this(key, () => cells, () => details, process, grouped) { }

    public TerminalRow(string key, string[] cells, Func<string> details,
        WinMon.Monitoring.ProcessUsage? process = null, bool grouped = false)
        : this(key, () => cells, details, process, grouped) { }

    public TerminalRow(string key, Func<string[]> cells, Func<string> details,
        WinMon.Monitoring.ProcessUsage? process = null, bool grouped = false)
    {
        Key = key;
        _createCells = cells;
        _createDetails = details;
        Process = process;
        Grouped = grouped;
    }
}

public sealed record TerminalTable(string[] Columns, IReadOnlyList<TerminalRow> Rows, string Note = "");
