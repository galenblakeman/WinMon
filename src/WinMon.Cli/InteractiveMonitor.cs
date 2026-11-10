using System.Collections.Concurrent;
using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using WinMon.Monitoring;
using WinMon.Resources;
using static WinMon.Cli.SnapshotReport;

namespace WinMon.Cli;

public static class InteractiveMonitor
{
    public static int Run(IAnsiConsole console, Func<IFrameSource> createSource, int top,
        ProcessSort sort, string filter, CancellationToken token, bool grouped = false, bool appsOnly = false,
        TerminalTab tab = TerminalTab.Processes, bool network = false, int? initialSort = null,
        InteractiveState? initialState = null, CliSettingsStore? settingsStore = null, bool noSave = false)
    {
        var deliveries = new ConcurrentQueue<Action>();
        var state = initialState ?? new InteractiveState(sort, filter);
        if (initialState is null)
        {
            state.Configure(top, grouped, appsOnly, tab);
            if (initialSort is { } selectedSort) state.Active.Sort = selectedSort;
            state.Active.Filter = state.Active.Draft = filter;
            state.NetworkEnabled = network;
        }
        var preferences = settingsStore is not null && !noSave
            ? new CliSettingsSession(settingsStore, state, () => state.SettingsSaveFailed = true) : null;
        var retention = state.RetentionOptions();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var traffic = state.NetworkEnabled ? new NetworkTrafficClient() : null;
        var trafficTask = traffic?.Run(stop.Token) ?? Task.CompletedTask;
        SamplingOptions Options()
        {
            var sample = Volatile.Read(ref traffic)?.Sample;
            return Volatile.Read(ref retention) with
            { ProcessNetwork = sample?.Snapshot, ProcessNetworkSessionId = sample?.SessionId ?? Guid.Empty };
        }
        var session = new MonitorSession(createSource, Options, deliveries.Enqueue);
        session.History.Resize(1800);
        session.Recorded += () => state.ObserveSession(session);
        var sampling = session.Run(stop.Token);
        try
        {
            console.AlternateScreen(() => console.Live(new Text(Strings.CliWarmingUp))
                .AutoClear(true).Overflow(VerticalOverflow.Crop).Cropping(VerticalOverflowCropping.Bottom)
                .Start(context =>
                {
                    var size = (Width: 0, Height: 0);
                    var dirty = true;
                    while (!token.IsCancellationRequested && !state.Quit)
                    {
                        while (deliveries.TryDequeue(out var deliver)) { deliver(); if (!state.Paused) dirty = true; }
                        var currentSize = (console.Profile.Width, console.Profile.Height);
                        if (size != currentSize) { size = currentSize; dirty = true; }
                        if (traffic is not null && state.LiveNetworkStatus != traffic.Sample.State)
                        { state.NetworkStatus = traffic.Sample.State; if (!state.Paused) dirty = true; }
                        for (var i = 0; i < 32 && !state.Quit && console.Input.IsKeyAvailable(); i++)
                        {
                            if (console.Input.ReadKey(true) is { } key)
                                state.Handle(key, PageSize(state, size.Width, size.Height));
                            if (state.NetworkEnabled != (traffic is not null))
                            {
                                var previous = Interlocked.Exchange(ref traffic, null);
                                if (previous is not null)
                                {
                                    previous.DisposeAsync().AsTask().GetAwaiter().GetResult();
                                    trafficTask.GetAwaiter().GetResult();
                                }
                                if (state.NetworkEnabled)
                                {
                                    var next = new NetworkTrafficClient();
                                    trafficTask = next.Run(stop.Token);
                                    Volatile.Write(ref traffic, next);
                                }
                                else trafficTask = Task.CompletedTask;
                                state.NetworkStatus = NetworkTrafficState.NotStarted;
                            }
                            preferences?.Observe();
                            dirty = true;
                        }
                        var settingsFailed = state.SettingsSaveFailed;
                        preferences?.Flush();
                        if (settingsFailed != state.SettingsSaveFailed) dirty = true;
                        if (state.Quit) break;
                        if (dirty)
                        {
                            context.UpdateTarget(BuildView(state, state.Active.Top,
                                PageSize(state, size.Width, size.Height), size.Width, size.Height,
                                console.Profile.Capabilities.Unicode));
                            Volatile.Write(ref retention, state.RetentionOptions());
                            dirty = false;
                        }
                        token.WaitHandle.WaitOne(50);
                    }
                }));
        }
        finally
        {
            preferences?.Flush(force: true);
            session.Close();
            stop.Cancel();
            try { Task.WhenAll(sampling, trafficTask).GetAwaiter().GetResult(); }
            finally { if (traffic is not null) traffic.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        return token.IsCancellationRequested ? 130 : 0;
    }

    public static TerminalTable Query(InteractiveState state)
    {
        var settings = state.Active;
        var data = state.Observations;
        var capture = data.Connections.LastOrDefault();
        return state.Tab switch
        {
            TerminalTab.Services => ServicesTab.Build(data.Services, settings.Sort, state.Query, settings.Status, settings.Workers, 0),
            TerminalTab.Usage => UsageTab.Build(data.Usage, data.UsageSince, data.MachineBusyTicks,
                state.DisplayedFrame?.Full.Timestamp, settings.Sort, state.Query, settings.AppsOnly, 0),
            TerminalTab.Connections => ConnectionsTab.Build(capture?.Connections, capture?.Owners ?? [],
                capture?.Network, data.Connections, capture, new ConnectionQueryOptions((ConnectionSortMetric)settings.Sort,
                    settings.AppsOnly, state.Query, state.NetworkEnabled, settings.TrafficSeconds, Strings.ConnectionsUnknownProcess)),
            _ => ProcessesTab.Build(state.DisplayedSamples, state.Sort, state.Query, settings.Grouped,
                settings.AppsOnly, state.Query.Trim().Length > 0 || settings.Top == 0,
                settings.Pins.Values.ToArray(), state.Paused, settings.SavedPins.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase))
        };
    }

    public static IRenderable BuildView(InteractiveState state, int top, int pageSize, int width, int height) =>
        BuildView(state, top, pageSize, width, height, true);

    public static IRenderable BuildView(InteractiveState state, int top, int pageSize, int width, int height, bool unicode)
    {
        var separator = unicode ? " · " : " | ";
        string Mark(bool value) => unicode ? value ? "☑" : "☐" : value ? "[x]" : "[ ]";
        var header = new Paragraph(TerminalText.Fit($"{TerminalChrome.ProductName}{separator}{SafeText(Environment.MachineName)}{separator}", Math.Max(0, width - 12)), TerminalChrome.Accent)
            .Append(state.Paused ? Strings.CliPaused : Strings.CliLive,
                new Style(state.Paused ? TerminalChrome.Gold : TerminalChrome.Green, decoration: Decoration.Bold));
        var tabs = Tabs(state.Tab, width);
        var footer = TerminalChrome.Keys(Strings.CliNavigation, width);
        if (width < 80 || height < 20) return new Rows(header, tabs, new Text(Strings.CliTerminalSmall), footer);
        var settings = state.Active;
        if (state.Help || settings.Details)
        {
            var contents = state.Help ? Strings.CliFullHelp : state.Selected?.Details ?? Strings.ProcessNotRecorded;
            var lines = contents.Split('\n');
            var count = Math.Max(1, height - 5);
            settings.DetailOffset = Math.Clamp(settings.DetailOffset, 0, Math.Max(0, lines.Length - count));
            var details = string.Join('\n', lines.Skip(settings.DetailOffset).Take(count)
                .Select(line => string.Concat(SafeText(line.Replace("\t", "  ", StringComparison.Ordinal)).EnumerateRunes()
                    .Skip(settings.HorizontalOffset).Select(r => r.ToString()))));
            return new Rows(header, tabs, state.Help ? TerminalChrome.Keys(details, width) : Line(details, width), TerminalChrome.Keys(Strings.CliDetailsKeys, width));
        }
        if (state.DisplayedFrame is not { } frame)
            return new Rows(header, tabs, new Text(state.Error is null ? Strings.CliWarmingUp : Strings.ReadingsUnavailableRetryingEachSecond,
                new Style(state.Error is null ? TerminalChrome.Gold : TerminalChrome.Red)), footer);
        pageSize = Math.Min(pageSize, PageSize(state, width, height));
        var filterShown = FilterShown(state);
        settings.Top = top;
        var result = state.Present(Query(state), pageSize);
        var summary = TerminalCharts.Summary(frame.Full, state.DisplayedSamples, width, height, unicode);
        var pins = $"N {Strings.PinnedOnly} {Mark(settings.PinnedOnly)}{separator}P {Strings.Format(Strings.PinnedProcessCount, settings.Pins.Count)}";
        var controls = $"B {Strings.Network} {Mark(state.NetworkEnabled)}{separator}" +
            $"T {Strings.Top} {(settings.Top == 0 ? Strings.AllEntries : settings.Top)}{separator}" +
            (state.Tab == TerminalTab.Services
                ? "S " + (settings.Status switch { 1 => Strings.ServiceRunning, 2 => Strings.ServiceStopped, _ => Strings.AllServiceStatuses }) +
                    $"{separator}W {Strings.ServiceWorkers} {Mark(settings.Workers)}"
                : $"A {Strings.AppOnly} {Mark(settings.AppsOnly)}") + separator + pins;
        if (state.Tab == TerminalTab.Processes) controls += $"{separator}G {Strings.Grouped} {Mark(settings.Grouped)}";
        if (state.Tab == TerminalTab.Connections) controls += $"{separator}R {settings.TrafficSeconds}s";
        var table = BuildTable(state, result, pageSize, width, out var columnsHidden);
        var timeLabel = state.Tab == TerminalTab.Processes
            ? string.Format(CultureInfo.CurrentCulture, Strings.ProcessesAtSAverage, frame.Full.Timestamp, ProcessRanking.DefaultWindowSeconds, "")
            : frame.Full.Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        var status = Line(timeLabel + separator +
            string.Format(CultureInfo.CurrentCulture, Strings.CliWatchRows, result.Rows.Count == 0 ? 0 : state.Offset + 1,
                Math.Min(state.Offset + pageSize, result.Rows.Count), result.Rows.Count) +
            (columnsHidden ? separator + Strings.CliColumnsHint : ""), width);
        var warning = state.Error is not null ? Strings.ReadingsUnavailableRetryingEachSecond : frame.Full.IsGap ? Strings.CliWarmingUp : result.Note;
        if (result.Rows.Count == 0 && warning.Length == 0) warning = Strings.CliNoMatches;
        if (state.Tab == TerminalTab.Connections && state.NetworkEnabled)
            warning = TrafficNote(state) + (warning.Length > 0 ? " · " + warning : "");
        if (state.SettingsSaveFailed) warning = Strings.CliSettingsSaveFailed + " " + warning;
        var healthColor = state.SettingsSaveFailed || state.Error is not null || state.NetworkEnabled && state.Tab == TerminalTab.Connections &&
            state.NetworkStatus is NetworkTrafficState.Unavailable or NetworkTrafficState.UpdateRequired ? TerminalChrome.Red :
            state.NetworkEnabled && state.Tab == TerminalTab.Connections && state.NetworkStatus == NetworkTrafficState.Active
                && warning == TrafficNote(state) ? TerminalChrome.Green : TerminalChrome.Gold;
        var view = new List<IRenderable> { header, tabs, summary, TerminalChrome.Keys(controls, width) };
        if (filterShown)
            view.Add(Line(string.Format(CultureInfo.CurrentCulture,
                state.EditingFilter ? Strings.CliFilterEditing : Strings.CliWatchFilter, SafeText(state.Query)).Replace(" · ", separator, StringComparison.Ordinal), width));
        view.Add(table);
        // Blank lines keep the status and key hints at the bottom; the last terminal row stays free.
        var used = view.Count - 2 + TerminalCharts.SummaryHeight(width, height) + 4 +
            (result.Columns.Length == 0 ? 0 : Math.Clamp(result.Rows.Count - state.Offset, 0, pageSize)) +
            1 + warning.Split('\n').Length + Strings.CliNavigation.Split('\n').Length;
        for (; used < height - 1; used++) view.Add(new Text(" "));
        view.AddRange([status, Line(warning, width, new Style(healthColor)), footer]);
        return new Rows(view);
    }

    private static bool FilterShown(InteractiveState state) => state.EditingFilter || state.Query.Length > 0;

    private static int PageSize(InteractiveState state, int width, int height) =>
        Math.Max(1, TerminalCharts.PageSize(width, height, state.Tab) - (FilterShown(state) ? 1 : 0));

    // Lines are composed by hand so the selection fills the whole row and the first column takes the spare width.
    private static IRenderable BuildTable(InteractiveState state, TerminalTable result, int pageSize, int width, out bool columnsHidden)
    {
        columnsHidden = false;
        if (result.Columns.Length == 0) return new Rows(new Text(" "), new Text(" "), new Text(new string('─', 6)), new Text(" "));
        var slots = Math.Max(1, (width - 25) / 15);
        var sortColumn = SortColumn(state);
        var rows = result.Rows.Skip(state.Offset).Take(pageSize).ToArray();
        if (state.Active.RevealSort)
        {
            var column = sortColumn;
            if (column > state.Active.ColumnOffset + slots) state.Active.ColumnOffset = column - slots;
            if (column < state.Active.ColumnOffset + 1) state.Active.ColumnOffset = Math.Max(0, column - 1);
            state.Active.RevealSort = false;
        }
        state.Active.ColumnOffset = Math.Clamp(state.Active.ColumnOffset, 0, Math.Max(0, result.Columns.Length - 1 - slots));
        var indices = Enumerable.Range(1, result.Columns.Length - 1).Skip(state.Active.ColumnOffset).Take(slots).ToArray();
        columnsHidden = indices.Length < result.Columns.Length - 1;
        var nameWidth = Math.Clamp(width - 3 - 15 * indices.Length, 22, 40);
        bool Right(int index) => !(state.Tab == TerminalTab.Services && index == 1 || state.Tab == TerminalTab.Usage && index == 6);
        var keys = state.Tab switch { TerminalTab.Services => 4, TerminalTab.Usage => 5, _ => 6 };
        var header = new Paragraph("  ");
        foreach (var index in indices.Prepend(0))
        {
            // The digit is the key that sorts by this column.
            var key = Enumerable.Range(0, keys).Where(k => KeyColumn(state, k) == index)
                .Select(k => (k + 1).ToString(CultureInfo.InvariantCulture)).FirstOrDefault("");
            var arrow = index != sortColumn ? "" : index == 0 ? "↑" : "↓";
            var cell = Cell((arrow + key).Length > 0 ? $"{arrow}{key} {result.Columns[index]}" : result.Columns[index],
                index == 0 ? nameWidth : 13, index != 0 && Right(index));
            var start = cell.Length - cell.TrimStart().Length;
            var style = index == sortColumn ? TerminalChrome.Accent : Style.Plain;
            header.Append((index == 0 ? " " : "  ") + cell[..start]).Append(cell.Substring(start, arrow.Length), style)
                .Append(cell.Substring(start + arrow.Length, key.Length), TerminalChrome.Key)
                .Append(cell[(start + arrow.Length + key.Length)..], style);
        }
        var lines = new List<IRenderable> { new Text(" "), header, new Text(new string('─', 3 + nameWidth + 15 * indices.Length)) };
        foreach (var row in rows)
        {
            var selected = row.Key == state.Active.SelectedKey;
            var pinned = state.Active.Pins.ContainsKey(row.Key);
            var style = selected ? TerminalChrome.Selected : Style.Plain;
            var line = new Paragraph((selected ? ">" : " ") + (pinned ? "*" : " "),
                    pinned ? new Style(TerminalChrome.Gold, style.Background, style.Decoration) : style)
                .Append(" " + Cell(row.Cells[0], nameWidth, false), style);
            foreach (var index in indices)
            {
                var value = row.Cells.ElementAtOrDefault(index);
                line.Append("  " + Cell(value is null || value == Strings.Unavailable ? Strings.MissingReading : value, 13, Right(index)), style);
            }
            lines.Add(line);
        }
        lines.Add(new Text(" "));
        return new Rows(lines);
    }

    private static string Cell(string text, int width, bool right)
    {
        var fitted = TerminalText.Fit(text, width);
        var padding = new string(' ', Math.Max(0, width - new Segment(fitted).CellCount()));
        return right ? padding + fitted : fitted + padding;
    }

    private static Text Line(string value, int width, Style? style = null) => new(string.Join('\n', value.Split('\n').Select(line => TerminalText.Fit(line, width))), style);

    private static string TabName(TerminalTab tab) => tab switch { TerminalTab.Services => Strings.Services,
        TerminalTab.Usage => Strings.UsageTab, TerminalTab.Connections => Strings.ConnectionsTab, _ => Strings.ProcessesTab };
    private static Paragraph Tabs(TerminalTab active, int width)
    {
        var text = new Paragraph();
        var tabWidth = Math.Max(0, (width - 6) / 4);
        foreach (var tab in Enum.GetValues<TerminalTab>())
        {
            var label = $"{(tab == active ? "> " : "  ")}[F{(int)tab + 1}] {TabName(tab)}";
            var fitted = TerminalText.Fit(label, tabWidth);
            var style = tab == active ? TerminalChrome.Selected : TerminalChrome.Accent;
            var keyStart = Math.Min(2, fitted.Length);
            var keyEnd = Math.Min(6, fitted.Length);
            // The marker stays for no-colour terminals but sits outside the highlight.
            text.Append(fitted[..keyStart], TerminalChrome.Accent);
            text.Append(fitted[keyStart..keyEnd], tab == active ? style : TerminalChrome.Key);
            text.Append(fitted[keyEnd..], style);
            if (tab != TerminalTab.Usage) text.Append("  ");
        }
        return text;
    }

    private static int SortColumn(InteractiveState state) => KeyColumn(state, state.Tab switch
    {
        TerminalTab.Processes => state.Sort switch { ProcessSort.Cpu => 0, ProcessSort.Memory => 1,
            ProcessSort.Gpu => 2, ProcessSort.Io => 3, ProcessSort.Threads => 4, _ => 5 },
        TerminalTab.Connections when !state.NetworkEnabled && state.Active.Sort is not (2 or 3) => 2,
        _ => state.Active.Sort
    });

    // The column that sort key (zero-based) orders by, or -1 when the key has no column.
    private static int KeyColumn(InteractiveState state, int key) => state.Tab switch
    {
        TerminalTab.Processes => key + 2,
        TerminalTab.Services => key == 3 ? 0 : key + 2,
        TerminalTab.Usage => key switch { 0 => 1, 1 => 3, 2 => 4, 3 => 5, _ => 6 },
        _ => !state.NetworkEnabled ? key switch { 2 => 1, 3 => 2, _ => -1 } :
            key switch { 0 => 3, 1 => 4, 2 => 1, 3 => 2, 4 => 5, _ => 6 }
    };

    private static string TrafficNote(InteractiveState state) => !state.NetworkEnabled ? Strings.NetworkProcessMonitoringDisabled :
        state.NetworkStatus switch { NetworkTrafficState.Active => Strings.NetworkProcessMonitoringActive,
            NetworkTrafficState.Incomplete => Strings.NetworkProcessMonitoringIncomplete,
            NetworkTrafficState.Unavailable => Strings.CliNetworkSetupHint + " · " + Strings.NetworkProcessMonitoringUnavailable,
            NetworkTrafficState.UpdateRequired => Strings.CliNetworkSetupHint + " · " + Strings.NetworkProcessMonitoringUpdateRequired,
            _ => Strings.NetworkProcessMonitoringWarmingUp };
}
