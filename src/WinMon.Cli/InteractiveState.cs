using WinMon.Monitoring;

namespace WinMon.Cli;

public sealed class TerminalTabState
{
    public int Sort { get; set; }
    public string Filter { get; set; } = "";
    public string Draft { get; set; } = "";
    public bool Editing { get; set; }
    public bool AppsOnly { get; set; }
    public bool Grouped { get; set; }
    public bool PinnedOnly { get; set; }
    public bool Workers { get; set; } = true;
    public int Status { get; set; }
    public int TrafficSeconds { get; set; } = 300;
    public int Top { get; set; }
    public int Offset { get; set; }
    public string? SelectedKey { get; set; }
    public bool Details { get; set; }
    public int DetailOffset { get; set; }
    public int HorizontalOffset { get; set; }
    public int ColumnOffset { get; set; }
    public bool RevealSort { get; set; } = true;
    public Dictionary<string, TerminalRow> Pins { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CliPin> SavedPins { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<TerminalRow> Rows { get; set; } = [];
}

public sealed record TerminalObservations(IReadOnlyList<ServiceFrame> Services, IReadOnlyList<AppUsage> Usage,
    DateTimeOffset UsageSince, long MachineBusyTicks, IReadOnlyList<ConnectionCapture> Connections, ProcessNetworkFrame? Network)
{
    public static TerminalObservations Empty { get; } = new([], [], DateTimeOffset.Now, 0, [], null);
}

/// <summary>Only the terminal owner thread reads or changes this state.</summary>
public sealed class InteractiveState
{
    private CapturedFrame? _latest, _frozen;
    private IReadOnlyList<MonitorSample> _latestSamples = [], _frozenSamples = [];
    private TerminalObservations _observations = TerminalObservations.Empty, _frozenObservations = TerminalObservations.Empty;
    private SamplingError? _latestError, _frozenError;
    private NetworkTrafficState _networkStatus, _frozenNetworkStatus;
    private readonly TerminalTabState[] _tabs = [new(), new(), new(), new()];
    public InteractiveState(ProcessSort sort, string filter)
    {
        _tabs[0].Sort = (int)sort;
        _tabs[0].Filter = _tabs[0].Draft = filter;
    }
    public TerminalTab Tab { get; private set; }
    public TerminalTabState Active => _tabs[(int)Tab];
    public CapturedFrame? DisplayedFrame => Paused ? _frozen : _latest;
    public IReadOnlyList<MonitorSample> DisplayedSamples => Paused ? _frozenSamples : _latestSamples;
    public TerminalObservations Observations => Paused ? _frozenObservations : _observations;
    public bool Paused { get; private set; }
    public bool EditingFilter => Active.Editing;
    public bool Quit { get; private set; }
    public bool Help { get; private set; }
    public bool NetworkEnabled { get; set; }
    public bool SettingsSaveFailed { get; set; }
    public NetworkTrafficState NetworkStatus { get => Paused ? _frozenNetworkStatus : _networkStatus; set => _networkStatus = value; }
    public NetworkTrafficState LiveNetworkStatus => _networkStatus;
    public ProcessSort Sort => (ProcessSort)_tabs[0].Sort;
    public string Filter => Active.Filter;
    public string Draft => Active.Draft;
    public string Query => EditingFilter ? Draft : Filter;
    public int Offset => Active.Offset;
    public SamplingError? Error => Paused ? _frozenError : _latestError;
    public TerminalRow? Selected => Active.Rows.FirstOrDefault(row => row.Key == Active.SelectedKey);

    public void Configure(int top, bool grouped, bool appsOnly, TerminalTab tab = TerminalTab.Processes)
    {
        foreach (var settings in _tabs) settings.Top = top;
        _tabs[0].Grouped = grouped;
        _tabs[0].AppsOnly = _tabs[2].AppsOnly = _tabs[3].AppsOnly = appsOnly;
        if (tab != TerminalTab.Processes)
        {
            _tabs[(int)tab].Filter = _tabs[(int)tab].Draft = _tabs[0].Filter;
            _tabs[0].Filter = _tabs[0].Draft = "";
        }
        Tab = tab;
    }

    public void Observe(MonitorSample? sample, SamplingError? error, IReadOnlyList<MonitorSample>? history = null)
    {
        _latestError = error;
        if (sample is null) return;
        _latest = new CapturedFrame(sample, Error: error);
        _latestSamples = history is { Count: > 0 } ? new SampleList(history, history.Count, sample) : [sample];
    }

    public void ObserveSession(MonitorSession session)
    {
        Observe(session.LatestFull, session.Error, session.History.Samples);
        _observations = new TerminalObservations(session.Services.Frames.ToArray(), session.Usage.Apps,
            session.Usage.Since, session.Usage.MachineBusyTicks, session.Connections.Captures, session.ProcessNetwork);
    }

    public void RestorePreferences(CliPreferences preferences)
    {
        preferences.Normalize();
        Tab = preferences.ActiveTab;
        NetworkEnabled = preferences.Network;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var source = preferences.Tabs[i];
            var target = _tabs[i];
            target.Sort = source.Sort; target.Top = source.Top; target.AppsOnly = source.AppsOnly;
            target.Grouped = source.Grouped; target.PinnedOnly = source.PinnedOnly; target.Workers = source.Workers;
            target.Status = source.Status; target.TrafficSeconds = source.TrafficSeconds;
            target.SavedPins.Clear();
            target.Pins.Clear();
            foreach (var pin in source.Pins) target.SavedPins[pin.Identity] = pin;
        }
    }

    public CliPreferences CapturePreferences() => new()
    {
        ActiveTab = Tab, Network = NetworkEnabled,
        Tabs = _tabs.Select(tab => new CliTabPreferences
        {
            Sort = tab.Sort, Top = tab.Top, AppsOnly = tab.AppsOnly, Grouped = tab.Grouped,
            PinnedOnly = tab.PinnedOnly, Workers = tab.Workers, Status = tab.Status,
            TrafficSeconds = tab.TrafficSeconds, Pins = tab.SavedPins.Values.ToArray()
        }).ToArray()
    };

    public SamplingOptions RetentionOptions()
    {
        var pins = new ProcessPins();
        foreach (var row in _tabs[0].Pins.Values)
            if (row.Process is { } process && !pins.Contains(process, row.Grouped)) pins.Toggle(process, row.Grouped);
        if (Tab == TerminalTab.Processes && Selected is { Process: { } selected } selectedRow && !pins.Contains(selected, selectedRow.Grouped))
            pins.Toggle(selected, selectedRow.Grouped);
        return new SamplingOptions(Retain: pins.RetentionSnapshot(), RetainAll: _tabs[0].Top == 0);
    }

    public TerminalTable Present(TerminalTable table, int pageSize)
    {
        var pins = Active.Pins;
        var visible = table.Rows.ToDictionary(row => row.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var saved in Active.SavedPins.Values)
        {
            var matching = table.Rows.Where(row => StringComparer.OrdinalIgnoreCase.Equals(row.PinIdentity, saved.Identity)).ToArray();
            var live = matching.Where(row => row.Process is null || row.Grouped ||
                DisplayedFrame?.Full.Processes.Any(process => process.Identity == row.Process.Identity) == true).ToArray();
            if (live.Length > 0) matching = live;
            if (matching.Length > 0)
            {
                foreach (var key in pins.Where(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Value.PinIdentity, saved.Identity))
                             .Select(pair => pair.Key).ToArray()) pins.Remove(key);
                foreach (var row in matching) pins[row.Key] = row;
            }
            else
            {
                foreach (var pinKey in pins.Where(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Value.PinIdentity, saved.Identity) &&
                             visible.TryGetValue(pair.Key, out var replacement) &&
                             !StringComparer.OrdinalIgnoreCase.Equals(replacement.PinIdentity, saved.Identity)).Select(pair => pair.Key).ToArray())
                    pins.Remove(pinKey);
                if (!pins.Values.Any(row => StringComparer.OrdinalIgnoreCase.Equals(row.PinIdentity, saved.Identity)))
                    pins["saved:" + saved.Identity] = new TerminalRow("saved:" + saved.Identity,
                        [saved.Name], WinMon.Resources.Strings.Unavailable) { PinIdentity = saved.Identity, Grouped = Active.Grouped };
            }
        }
        foreach (var key in pins.Keys.ToArray())
            if (visible.TryGetValue(key, out var row)) pins[key] = row;
        var pinned = pins.Keys.Where(visible.ContainsKey).Select(key => visible[key]);
        if (Query.Trim().Length == 0 && !Active.AppsOnly && (Tab != TerminalTab.Services || Active.Status == 0))
            pinned = pinned.Concat(pins.Values.Where(row => !visible.ContainsKey(row.Key) &&
                    (Tab != TerminalTab.Processes || row.Grouped == Active.Grouped ||
                     row.PinIdentity is { } identity && Active.SavedPins.ContainsKey(identity)))
                .Select(row => row with
                {
                    Cells = Enumerable.Range(0, table.Columns.Length)
                        .Select(index => index == 0 ? row.Cells[0] : WinMon.Resources.Strings.Unavailable).ToArray(),
                    Details = WinMon.Resources.Strings.Unavailable + "\n" + row.Cells[0]
                }));
        var unpinned = table.Rows.Where(row => !pins.ContainsKey(row.Key));
        if (Active.Top > 0) unpinned = unpinned.Take(Active.Top);
        var rows = pinned.Concat(Active.PinnedOnly ? [] : unpinned).ToArray();
        Active.Rows = rows;
        if (!rows.Any(row => row.Key == Active.SelectedKey)) Active.SelectedKey = rows.FirstOrDefault()?.Key;
        ClampOffset(rows.Length, pageSize);
        var selected = Array.FindIndex(rows, row => row.Key == Active.SelectedKey);
        if (selected < Active.Offset) Active.Offset = Math.Max(0, selected);
        if (selected >= Active.Offset + pageSize) Active.Offset = selected - pageSize + 1;
        return table with { Rows = rows };
    }

    public void ClampOffset(int count, int pageSize) => Active.Offset = Math.Clamp(Offset, 0, Math.Max(0, count - pageSize));

    public void Handle(ConsoleKeyInfo key, int pageSize)
    {
        var active = Active;
        if (EditingFilter)
        {
            if (key.Key == ConsoleKey.Enter) { active.Filter = Draft; active.Editing = false; }
            else if (key.Key == ConsoleKey.Escape) { active.Draft = Filter; active.Editing = false; }
            else if (key.Key == ConsoleKey.Backspace && Draft.Length > 0)
                active.Draft = Draft[..(Draft.Length - (Draft.Length >= 2 && char.IsSurrogatePair(Draft, Draft.Length - 2) ? 2 : 1))];
            else if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0' && Draft.Length < 128) active.Draft += key.KeyChar;
            active.Offset = 0;
            active.SelectedKey = null;
            return;
        }
        if (key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F4)
        {
            Tab = (TerminalTab)(key.Key - ConsoleKey.F1);
            Help = false;
            return;
        }
        if (key.KeyChar == '?') { Help = !Help; return; }
        if (key.Key == ConsoleKey.Q) { Quit = true; return; }
        if (key.Key == ConsoleKey.B)
        {
            NetworkEnabled = !NetworkEnabled;
            return;
        }
        if (key.Key == ConsoleKey.Escape)
        {
            if (Help) Help = false;
            else if (active.Details) active.Details = false;
            else { active.Filter = active.Draft = ""; active.Offset = 0; }
            return;
        }
        if (active.Details || Help)
        {
            if (key.Key == ConsoleKey.RightArrow) active.HorizontalOffset += 16;
            if (key.Key == ConsoleKey.LeftArrow) active.HorizontalOffset = Math.Max(0, active.HorizontalOffset - 16);
            var delta = key.Key switch { ConsoleKey.UpArrow => -1, ConsoleKey.DownArrow => 1,
                ConsoleKey.PageUp => -pageSize, ConsoleKey.PageDown => pageSize, _ => 0 };
            active.DetailOffset = Math.Max(0, active.DetailOffset + delta);
            if (key.Key == ConsoleKey.Home) active.DetailOffset = 0;
            return;
        }
        switch (key.Key)
        {
            case ConsoleKey.Spacebar:
                if (!Paused)
                {
                    _frozen = _latest;
                    var end = _latestSamples.Count > 0 ? ProcessRanking.Moment(_latestSamples[^1]) : 0;
                    _frozenSamples = _latestSamples.Where(sample =>
                        ProcessRanking.Moment(sample) >= end - TerminalCharts.HistorySeconds).ToArray();
                    _frozenObservations = _observations;
                    _frozenError = _latestError;
                    _frozenNetworkStatus = _networkStatus;
                }
                Paused = !Paused;
                break;
            case ConsoleKey.UpArrow: Move(-1, pageSize); break;
            case ConsoleKey.DownArrow: Move(1, pageSize); break;
            case ConsoleKey.PageUp: Move(-pageSize, pageSize); break;
            case ConsoleKey.PageDown: Move(pageSize, pageSize); break;
            case ConsoleKey.Home: active.SelectedKey = active.Rows.FirstOrDefault()?.Key; active.Offset = 0; break;
            case ConsoleKey.End: active.SelectedKey = active.Rows.LastOrDefault()?.Key; active.Offset = Math.Max(0, active.Rows.Count - pageSize); break;
            case ConsoleKey.RightArrow: active.ColumnOffset++; break;
            case ConsoleKey.LeftArrow: active.ColumnOffset = Math.Max(0, active.ColumnOffset - 1); break;
            case ConsoleKey.Enter: active.Details = Selected is not null; active.DetailOffset = 0; break;
            case ConsoleKey.P:
                if (Selected is { } row)
                {
                    if (row.PinIdentity is { } identity)
                    {
                        if (active.SavedPins.Remove(identity))
                            foreach (var pinKey in active.Pins.Where(pair => StringComparer.OrdinalIgnoreCase.Equals(pair.Value.PinIdentity, identity))
                                         .Select(pair => pair.Key).ToArray()) active.Pins.Remove(pinKey);
                        else
                        {
                            var name = SnapshotReport.SafeText(row.PinName ?? row.Cells[0]);
                            active.SavedPins[identity] = new CliPin(identity, name[..Math.Min(name.Length, 256)]);
                            active.Pins[row.Key] = row;
                        }
                    }
                    else if (!active.Pins.Remove(row.Key)) active.Pins[row.Key] = row;
                }
                break;
            case ConsoleKey.N: active.PinnedOnly = !active.PinnedOnly; active.Offset = 0; break;
            case ConsoleKey.G when Tab == TerminalTab.Processes: active.Grouped = !active.Grouped; active.SelectedKey = null; active.Offset = 0; break;
            case ConsoleKey.A when Tab != TerminalTab.Services: active.AppsOnly = !active.AppsOnly; active.SelectedKey = null; active.Offset = 0; break;
            case ConsoleKey.W when Tab == TerminalTab.Services: active.Workers = !active.Workers; break;
            case ConsoleKey.S when Tab == TerminalTab.Services: active.Status = (active.Status + 1) % 3; break;
            case ConsoleKey.R when Tab == TerminalTab.Connections:
                active.TrafficSeconds = active.TrafficSeconds switch { 0 => 60, 60 => 300, 300 => 1800, _ => 0 }; break;
            case ConsoleKey.T:
                active.Top = active.Top switch { 5 => 10, 10 => 15, 15 => 20, 20 => 50, 50 => 0, _ => 5 };
                if (Tab == TerminalTab.Services && active.Top != 0 && active.Sort == 3) active.Sort = 0;
                active.Offset = 0;
                break;
            default:
                if (key.KeyChar == '/') { active.Editing = true; active.Draft = Filter; active.Offset = 0; }
                else if (key.KeyChar is >= '1' and <= '6')
                {
                    var index = key.KeyChar - '1';
                    if (Tab == TerminalTab.Processes)
                        active.Sort = (int)(index switch { 0 => ProcessSort.Cpu, 1 => ProcessSort.Memory,
                            2 => ProcessSort.Gpu, 3 => ProcessSort.Io, 4 => ProcessSort.Threads, _ => ProcessSort.Handles });
                    else if (Tab == TerminalTab.Services && index <= 3 && (index != 3 || active.Top == 0) ||
                             Tab == TerminalTab.Usage && index <= 4 || Tab == TerminalTab.Connections) active.Sort = index;
                    active.Offset = 0;
                    active.SelectedKey = null;
                    active.RevealSort = true;
                }
                break;
        }
    }

    private void Move(int delta, int pageSize)
    {
        if (Active.Rows.Count == 0) { Active.Offset = Math.Max(0, Offset + delta); return; }
        var index = Active.Rows.ToList().FindIndex(row => row.Key == Active.SelectedKey);
        index = Math.Clamp(index + delta, 0, Active.Rows.Count - 1);
        Active.SelectedKey = Active.Rows[index].Key;
        Active.Offset = Math.Clamp(Offset, Math.Max(0, index - pageSize + 1), index);
    }
}
