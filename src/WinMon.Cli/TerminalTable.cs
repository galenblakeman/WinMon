using MonCore.Monitoring;

namespace WinMon.Cli;

public enum TerminalTab { Processes, Services, Connections, Usage, Hardware }

public sealed record TerminalRow
{
    private readonly Func<string> _createDetails;
    private readonly Func<string[]> _createCells;

    public string Key { get; init; }
    // Deferred formatting is accessed only by the terminal owner, like the rest of the view state.
    public string[] Cells
    {
        get => field ??= _createCells();
        init;
    }

    public string Details
    {
        get => field ??= _createDetails();
        init;
    }

    public ProcessUsage? Process { get; init; }
    public bool Grouped { get; init; }
    public string? PinIdentity { get; init; }
    public string? PinName { get; init; }

    public TerminalRow(string key, string[] cells, string details,
        ProcessUsage? process = null, bool grouped = false)
        : this(key, () => cells, () => details, process, grouped) { }

    public TerminalRow(string key, string[] cells, Func<string> details,
        ProcessUsage? process = null, bool grouped = false)
        : this(key, () => cells, details, process, grouped) { }

    public TerminalRow(string key, Func<string[]> cells, Func<string> details,
        ProcessUsage? process = null, bool grouped = false)
    {
        Key = key;
        _createCells = cells;
        _createDetails = details;
        Process = process;
        Grouped = grouped;
    }
}

public sealed record TerminalTable(string[] Columns, IReadOnlyList<TerminalRow> Rows, string Note = "");
