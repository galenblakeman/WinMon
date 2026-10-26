using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

/// <summary>The choices the process list renders with. The window keeps them in <see cref="MonitorPreferences"/>.</summary>
internal sealed record ProcessListSettings(int TopCount, ProcessSort Sort, bool Grouped, bool AppsOnly, bool PinnedOnly)
{
    internal static ProcessListSettings From(MonitorPreferences preferences)
    {
        return new ProcessListSettings(preferences.TopProcessCount, preferences.ProcessSort, preferences.GroupProcesses,
            preferences.AppsOnly, preferences.PinnedOnly);
    }

    internal MonitorPreferences ApplyTo(MonitorPreferences preferences)
    {
        return preferences with
        {
            TopProcessCount = TopCount, ProcessSort = Sort, GroupProcesses = Grouped, AppsOnly = AppsOnly,
            PinnedOnly = PinnedOnly
        };
    }
}

/// <summary>Everything one refresh of the process list reads.</summary>
/// <param name="Samples">Retained history, oldest first.</param>
/// <param name="Visible">The live sample, or the inspected one while it is still retained.</param>
/// <param name="Selected">The inspected historical sample, or null for the live view.</param>
/// <param name="LatestFull">The newest sample before compaction, which live ranking and process actions use.</param>
/// <param name="LatestCompacted">The newest compacted sample, the one history ends with.</param>
internal sealed record ProcessListFrame(
    IReadOnlyList<MonitorSample> Samples,
    MonitorSample? Latest,
    MonitorSample? Visible,
    MonitorSample? Selected,
    MonitorSample? LatestFull,
    MonitorSample? LatestCompacted,
    bool HistoryMemoryLimited,
    SamplingError? Error,
    bool Wide,
    ProcessListSettings Settings);

/// <summary>Ranked, pinned and selected processes with their toolbar and action bar.</summary>
/// <remarks>
/// The window owns the settings and passes them in each <see cref="Update"/>. The view raises
/// <see cref="SettingsChanged"/> when a control changes one, and the window stores it and refreshes.
/// </remarks>
public partial class ProcessesView
{
    private readonly List<ProcessRow> _rows = [];
    private readonly List<ProcessRow> _pinnedRows = [];
    private ProcessListSettings _settings = ProcessListSettings.From(new MonitorPreferences());
    private ProcessListFrame? _frame;
    private bool _closing;

    public ProcessesView()
    {
        InitializeComponent();
        ProcessActions.ActionRequested += OnProcessAction;
        ProcessActionDrawer.CollapsedChanged += hidden => ActionsHiddenChanged?.Invoke(hidden);
        ProcessActions.EnablePriorityActions(CanSetProcessPriority, process => ProcessPriorityChanger.Read(process),
            SetProcessPriority);
        ProcessCountSelector.ItemsSource = TopCountChoices.Items;
        ProcessCountSelector.SelectedItem = TopCountChoices.Selection(_settings.TopCount);
        ProcessCountSelector.SelectionChanged += OnProcessCountChanged;
        // Item order matches ProcessSort so the selected index is the sort column.
        ProcessSortSelector.ItemsSource = SortNames();
        ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
        ProcessSortSelector.SelectionChanged += OnProcessSortChanged;
        ProcessSearchBox.TextChanged += OnProcessSearchChanged;
        AppsOnlyCheckBox.Checked += OnAppsOnlyChanged;
        AppsOnlyCheckBox.Unchecked += OnAppsOnlyChanged;
        PinnedOnlyCheckBox.Checked += OnPinnedOnlyChanged;
        PinnedOnlyCheckBox.Unchecked += OnPinnedOnlyChanged;
        IndividualChip.Checked += OnGroupingChanged;
        GroupedChip.Checked += OnGroupingChanged;
        // Column headings are shortcuts for the "processes by" dropdown, which stays the keyboard path.
        foreach (var (header, sort, _) in SortHeaders)
        {
            header.Cursor = Cursors.Hand;
            header.MouseEnter += (_, _) => header.TextDecorations = TextDecorations.Underline;
            header.MouseLeave += (_, _) => header.TextDecorations = null;
            header.MouseLeftButtonUp += (_, e) =>
            {
                ProcessSortSelector.SelectedIndex = (int)sort;
                e.Handled = true;
            };
        }

        UpdateSortHeaderToolTips();
        ProcessRows.Children.Add(OtherRow);
    }

    /// <summary>A control changed a setting; the window stores it and refreshes.</summary>
    internal event Action<ProcessListSettings>? SettingsChanged;

    /// <summary>A process was pinned or unpinned; <see cref="SavedPins"/> holds the new list.</summary>
    internal event Action? PinsChanged;

    /// <summary><see cref="Retention"/> changed; publish it to the sampling thread.</summary>
    internal event Action? RetentionChanged;

    /// <summary>The search text or the selection changed, and the list needs a refresh.</summary>
    internal event Action? RefreshRequested;

    internal event Action<string>? CopyRequested;

    /// <summary>Pinned and selected processes that compacted history keeps even while idle.</summary>
    internal Func<ProcessUsage, bool>? Retention { get; private set; }

    internal SavedProcessPin[] SavedPins => Pins.Snapshot();
    internal IReadOnlyList<ProcessRow> Rows => _rows;
    internal IReadOnlyList<ProcessRow> PinnedRows => _pinnedRows;
    internal ProcessRow OtherRow { get; } = new() { Visibility = Visibility.Collapsed };

    internal ProcessPins Pins { get; } = new();

    internal ProcessSelection Selection { get; } = new();

    internal bool ActionsHidden
    {
        get => ProcessActionDrawer.Collapsed;
        set => ProcessActionDrawer.SetCollapsed(value);
    }

    internal event Action<bool>? ActionsHiddenChanged;

    /// <summary>Block count and sort changes while Settings saves the preferences file.</summary>
    internal bool RankingEnabled
    {
        set
        {
            ProcessCountSelector.IsEnabled = value;
            ProcessSortSelector.IsEnabled = value;
        }
    }

    /// <summary>Whether a toolbar dropdown is open, so Escape belongs to it.</summary>
    internal bool HasOpenDropDown => ProcessCountSelector.IsDropDownOpen || ProcessSortSelector.IsDropDownOpen;

    /// <summary>Show saved settings and pins. Controls raise their usual change events.</summary>
    internal void Load(ProcessListSettings settings, IEnumerable<SavedProcessPin> pins)
    {
        _settings = settings;
        Pins.Restore(pins);
        UpdateRetention();
        PinnedOnlyCheckBox.IsChecked = settings.PinnedOnly;
        ProcessSortSelector.SelectedIndex = (int)settings.Sort;
        GroupedChip.IsChecked = settings.Grouped;
        IndividualChip.IsChecked = !settings.Grouped;
        AppsOnlyCheckBox.IsChecked = settings.AppsOnly;
        ProcessCountSelector.SelectedItem = TopCountChoices.Selection(settings.TopCount);
    }

    /// <summary>Ignore control changes while the window shuts down.</summary>
    internal void Close()
    {
        _closing = true;
    }

    internal void ArrangeToolbar(bool inline, bool inlinePins)
    {
        Grid.SetRow(ProcessSearchBorder, inline ? 0 : 1);
        Grid.SetColumn(ProcessSearchBorder, inline ? 2 : 0);
        Grid.SetColumnSpan(ProcessSearchBorder, inline ? 1 : 4);
        ProcessSearchBorder.Margin = inline ? new Thickness(12, 0, 12, 0) : new Thickness(0, 6, 12, 0);
        Grid.SetRow(AppsOnlyCheckBox, inline ? 0 : 1);
        Grid.SetColumn(AppsOnlyCheckBox, inline ? 3 : 4);
        Grid.SetColumnSpan(AppsOnlyCheckBox, inline ? 1 : 2);
        AppsOnlyCheckBox.Margin = inline ? new Thickness(0, 0, 12, 0) : new Thickness(0, 6, 0, 0);
        Grid.SetColumn(ProcessRankingControls, 0);
        Grid.SetColumnSpan(ProcessRankingControls, 2);
        Grid.SetColumn(ProcessGroupingControls, inlinePins ? 5 : 4);
        Grid.SetColumnSpan(ProcessGroupingControls, inlinePins ? 1 : 2);
        var pinRow = inlinePins ? 0 : inline ? 1 : 2;
        Grid.SetRow(PinnedOnlyCheckBox, pinRow);
        Grid.SetColumn(PinnedOnlyCheckBox, 4);
        Grid.SetColumnSpan(PinnedOnlyCheckBox, inlinePins ? 1 : 2);
        PinnedOnlyCheckBox.Margin = inlinePins ? new Thickness(0, 0, 12, 0) : new Thickness(0, 6, 0, 0);
    }

    /// <summary>Rank and draw the frame. Returns the snapshot report while a retained historical sample is shown.</summary>
    internal SnapshotReport? Update(ProcessListFrame frame)
    {
        _frame = frame;
        var (topCount, processSort, grouped, appsOnly, _) = _settings = frame.Settings;
        var (samples, latest, visible, selected) = (frame.Samples, frame.Latest, frame.Visible, frame.Selected);
        var historyMetric = processSort switch
        {
            ProcessSort.Gpu => grouped ? Strings.BusiestMemberGPU : Strings.GPU,
            ProcessSort.Memory => grouped ? Strings.SummedPrivateMemory : Strings.MemoryLowercase,
            ProcessSort.Io => grouped ? Strings.SummedIO : Strings.IO,
            ProcessSort.Threads => Strings.Threads,
            ProcessSort.Handles => Strings.Handles,
            _ => grouped ? Strings.SummedCPU : Strings.CPU
        };
        ProcessHistoryHeader.ToolTip = Strings.Format(Strings.HistoryOnOneScaleSharedByEveryRowHoverA, historyMetric);
        SelectionLabel.Text = selected is not null
            ? visible is null
                ? Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindowReturn
                : Strings.Format(Strings.ProcessesAtSAverage, visible.Timestamp, ProcessRanking.DefaultWindowSeconds,
                    grouped ? Strings.IdleGroupMembersMayBeOmitted : "")
            : string.Empty;
        SelectionLabel.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        var selectedIndex = -1;
        if (selected is not null)
            for (var i = 0; i < samples.Count; i++)
                if (ReferenceEquals(samples[i], selected))
                {
                    selectedIndex = i;
                    break;
                }

        // Views over the history snapshot rather than copies of it, which run to 3,600 samples.
        var rowSamples = selected is null ? samples : new SampleList(samples, selectedIndex + 1);
        if (selected is null && frame.LatestFull is not null && rowSamples.Count > 0 &&
            ReferenceEquals(rowSamples[^1], frame.LatestCompacted))
            rowSamples = new SampleList(samples, samples.Count, frame.LatestFull);
        // Rank on a short average ending at the live or selected sample; single intervals are tick-quantized.
        var query = ProcessSearchBox.Text.Trim();
        var ranking = ProcessRanking.Rank(rowSamples, sort: processSort, grouped: grouped,
            includeInactive: query.Length > 0 || topCount == 0);
        // Pins and the selection need inactive processes too. A search already ranked them; otherwise rank once.
        var inclusive = query.Length > 0 || topCount == 0 ? ranking : null;
        UpdateSortHeaders();
        var active = ProcessSearch.Filter(ranking.Processes, query, appsOnly).ToArray();
        var pinned = ResolvePinnedRows(rowSamples, grouped, query, appsOnly, Inclusive);
        var unpinned = active.Where(p => !Pins.Contains(p, grouped)).ToArray();
        var pinnedOnly = PinnedOnlyCheckBox.IsChecked == true;
        var top = pinnedOnly ? [] : TopCountChoices.Limit(unpinned, topCount).ToArray();
        var rest = pinnedOnly ? [] : unpinned.Skip(top.Length).ToArray();
        var displayed = pinned.Select(p => p.Usage).Concat(top).ToArray();
        var processData = ProcessGraphData.Build(rowSamples, MonitorPreferences.ShortRangeSeconds, displayed, grouped,
            processSort);
        UpdatePinnedRows(pinned, processData, rowSamples, grouped);
        ArrangeProcessRows(top, grouped);
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Visibility = i < top.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < top.Length)
                _rows[i].Update(top[i], rowSamples, MonitorPreferences.ShortRangeSeconds, frame.Wide,
                    processData[pinned.Length + i], grouped);
        }

        RefreshProcessActions(rowSamples, grouped, Inclusive);
        OtherRow.Visibility = rest.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rest.Length > 0)
            // Memory ranking also covers idle processes compacted out of the sample.
            OtherRow.ShowRemainder(
                rest.Length +
                (query.Length == 0 && !appsOnly && processSort == ProcessSort.Memory && !grouped
                    ? visible?.CompactedProcesses ?? 0
                    : 0),
                rest.Sum(p => p.CpuPercent),
                rest.Any(p => p.IoBytesPerSecond is not null) ? rest.Sum(p => p.IoBytesPerSecond ?? 0) : null, grouped);
        EmptyLabel.Visibility = displayed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = visible is null && selected is not null
            ? Strings.ReturnToLiveToInspectCurrentActivity
            : latest is null || visible?.TotalCpuPercent is null
                ? Strings.CollectingABaselineOrRecoveringAfterARecordingGap
                : appsOnly
                    ? Strings.NoAppsMatchCurrentView
                    : query.Length > 0
                        ? Strings.NoRankedProcessesMatchYourSearch
                        : processSort switch
                        {
                            ProcessSort.Gpu => Strings.NoMeasurableGPUActivityAmongAccessibleProcessesInThisWindow,
                            ProcessSort.Memory => Strings.NoMemoryReadingsAmongAccessibleProcesses,
                            ProcessSort.Io => Strings.NoMeasurableIOAmongAccessibleProcessesInThisWindow,
                            ProcessSort.Threads or ProcessSort.Handles => Strings.NoProcessCountReadings,
                            _ => Strings.NoMeasurableActiveProcessesInThisWindow
                        };
        if (pinnedOnly && displayed.Length == 0) EmptyLabel.Text = Strings.NoPinnedProcessesMatch;
        CoverageLabel.Text = visible is null
            ? ""
            : Coverage(visible.UnavailableProcesses, ranking.TotalCpuPercent, ranking.MeasuredCpuPercent);
        if (frame.HistoryMemoryLimited)
            CoverageLabel.Text += (CoverageLabel.Text.Length > 0 ? " · " : "") + Strings.HistoryShortenedByMemoryLimit;
        CoverageLabel.ToolTip = CoverageLabel.Text + Strings.UnattributedCPUIsTotalCPUMinusAllMeasuredProcessesKernel;
        if (selected is null || visible is null) return null;
        return new SnapshotReport(visible, displayed, grouped,
                SortHeaders.First(h => h.Sort == processSort).Text, query, appsOnly,
                CoverageLabel.Text,
                rest.Length == 0
                    ? null
                    : new SnapshotRemainder(
                        rest.Length +
                        (query.Length == 0 && !appsOnly && processSort == ProcessSort.Memory &&
                         !grouped
                            ? visible.CompactedProcesses
                            : 0),
                        rest.Sum(p => p.CpuPercent),
                        rest.Any(p => p.IoBytesPerSecond is not null)
                            ? rest.Sum(p => p.IoBytesPerSecond ?? 0)
                            : null))
            { ProcessStates = [.. pinned.Select(p => ProcessRow.StateText(p.State)), .. top.Select(_ => "")] };

        ProcessRanking Inclusive()
        {
            return inclusive ??= ProcessRanking.Rank(rowSamples, sort: processSort,
                grouped: grouped, includeInactive: true);
        }
    }

    /// <summary>Show one process from another view: every process individually, filtered to its PID.</summary>
    internal void ShowProcess(ProcessUsage process)
    {
        AppsOnlyCheckBox.IsChecked = false;
        PinnedOnlyCheckBox.IsChecked = false;
        IndividualChip.IsChecked = true;
        ProcessSearchBox.Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Show one executable group from another view: grouped, unfiltered, with the group selected.</summary>
    /// <remarks>Grouped selection matches by executable, so the representative needs only its name and path.</remarks>
    internal void ShowGroup(ProcessUsage representative)
    {
        AppsOnlyCheckBox.IsChecked = false;
        PinnedOnlyCheckBox.IsChecked = false;
        GroupedChip.IsChecked = true;
        ProcessSearchBox.Text = "";
        SelectProcess(representative, true);
    }

    internal void FocusSearch()
    {
        ProcessSearchBox.Focus();
        ProcessSearchBox.SelectAll();
    }

    /// <summary>Clear the search when it has focus and text, as Escape does.</summary>
    internal bool ClearFocusedSearch()
    {
        if (!ProcessSearchBox.IsKeyboardFocusWithin || ProcessSearchBox.Text.Length == 0) return false;
        ProcessSearchBox.Clear();
        return true;
    }

    /// <summary>Relabel the toolbar; ranking, history, selection and scroll position stay intact.</summary>
    internal void ApplyLanguage(ProcessListSettings settings)
    {
        ProcessCountSelector.SelectionChanged -= OnProcessCountChanged;
        try
        {
            ProcessCountSelector.ItemsSource = TopCountChoices.Items;
            ProcessCountSelector.SelectedItem = TopCountChoices.Selection(settings.TopCount);
        }
        finally
        {
            ProcessCountSelector.SelectionChanged += OnProcessCountChanged;
        }

        ProcessSortSelector.SelectionChanged -= OnProcessSortChanged;
        try
        {
            ProcessSortSelector.ItemsSource = SortNames();
            ProcessSortSelector.SelectedIndex = (int)settings.Sort;
        }
        finally
        {
            ProcessSortSelector.SelectionChanged += OnProcessSortChanged;
        }

        UpdateSortHeaderToolTips();
    }

    internal void ToggleProcessPin(ProcessUsage process, bool grouped)
    {
        Pins.Toggle(process, grouped);
        UpdateRetention();
        PinsChanged?.Invoke();
    }

    internal void SelectProcess(ProcessUsage process, bool grouped)
    {
        Selection.Select(process, grouped);
        UpdateRetention();
        RefreshRequested?.Invoke();
    }

    internal static bool[] LongestIncreasingRun(int[] values)
    {
        // Patience sorting: tails[k] is the index ending the best run of length k + 1.
        var tails = new List<int>();
        var previous = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            int low = 0, high = tails.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (values[tails[middle]] < values[i]) low = middle + 1;
                else high = middle;
            }

            previous[i] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count) tails.Add(i);
            else tails[low] = i;
        }

        var result = new bool[values.Length];
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i]) result[i] = true;
        return result;
    }

    internal void UpdateRetention()
    {
        var pins = Pins.RetentionSnapshot();
        var selected = Selection.RetentionSnapshot();
        Retention = pins is null && selected is null
            ? null
            : process => pins?.Invoke(process) == true || selected?.Invoke(process) == true;
        RetentionChanged?.Invoke();
    }

    private static string[] SortNames()
    {
        return [Strings.CPU, Strings.GPU, Strings.Memory, Strings.IO, Strings.Threads, Strings.Handles];
    }

    /// <summary>Summarize CPU that the measured processes do not cover, without inventing a process for it.</summary>
    private static string Coverage(int unavailableProcesses, double? totalCpu, double measuredCpu)
    {
        var unread = unavailableProcesses > 0
            ? Strings.Format(Strings.ProcessesNeedAdministratorAccess, unavailableProcesses)
            : "";
        if (totalCpu is not { } total) return unread;
        var unattributed = Strings.Format(Strings.Unattributed, Math.Max(0, total - measuredCpu));
        return unread.Length > 0 ? $"{unattributed} · {unread}" : unattributed;
    }

    private void Change(ProcessListSettings settings)
    {
        _settings = settings;
        SettingsChanged?.Invoke(settings);
    }

    private void OnProcessSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_closing) return;
        var hasText = ProcessSearchBox.Text.Length > 0;
        ProcessSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearProcessSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ProcessScroll.ScrollToTop();
        RefreshRequested?.Invoke();
    }

    internal void OnClearProcessSearch(object sender, RoutedEventArgs e)
    {
        ProcessSearchBox.Clear();
        ProcessSearchBox.Focus();
    }

    private void OnAppsOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        ProcessScroll.ScrollToTop();
        Change(_settings with { AppsOnly = AppsOnlyCheckBox.IsChecked == true });
    }

    private void OnPinnedOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        Change(_settings with { PinnedOnly = PinnedOnlyCheckBox.IsChecked == true });
    }

    private void OnProcessCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!TopCountChoices.TryGetCount(ProcessCountSelector.SelectedItem, out var count) || _closing) return;
        ProcessScroll.ScrollToTop();
        Change(_settings with { TopCount = count });
    }

    private void OnGroupingChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        ProcessScroll.ScrollToTop();
        Change(_settings with { Grouped = GroupedChip.IsChecked == true });
    }

    private void OnProcessSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProcessSortSelector.SelectedIndex < 0 || _closing) return;
        ProcessScroll.ScrollToTop();
        Change(_settings with { Sort = (ProcessSort)ProcessSortSelector.SelectedIndex });
    }

    /// <summary>Mark the column that orders the rows; the others stay muted.</summary>
    private (TextBlock Header, ProcessSort Sort, string Text)[] SortHeaders =>
    [
        (CpuHeader, ProcessSort.Cpu, Strings.CPU), (GpuHeader, ProcessSort.Gpu, Strings.GPU),
        (MemoryHeader, ProcessSort.Memory, Strings.Memory), (IoHeader, ProcessSort.Io, Strings.IO),
        (ThreadsHeader, ProcessSort.Threads, Strings.Threads), (HandlesHeader, ProcessSort.Handles, Strings.Handles)
    ];

    private void UpdateSortHeaders()
    {
        foreach (var (header, sort, name) in SortHeaders)
        {
            var active = sort == _settings.Sort;
            header.Text = active ? $"{name} ▾" : name;
            header.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            // Each new resource reference is resolved again, so set one only when the heading changes state.
            if (header.Tag is bool shown && shown == active) continue;
            header.Tag = active;
            header.SetResourceReference(TextBlock.ForegroundProperty, active ? "Text" : "Muted");
        }
    }

    private void UpdateSortHeaderToolTips()
    {
        foreach (var (header, sort, text) in SortHeaders)
        {
            var description = sort switch
            {
                ProcessSort.Gpu => Strings.BusiestGPUEnginePerProcessAveragedOverReadableIntervalsIn,
                ProcessSort.Memory => _settings.Grouped
                    ? Strings.PrivateBytesSummedAcrossThisExecutableSCurrentProcesses
                    : Strings.ResidentWorkingSetIncludingSharedPages,
                ProcessSort.Io => Strings.ProcessReadWriteIOIncludesFileNetworkAndDevice,
                ProcessSort.Threads => Strings.ProcessThreadsExplanation,
                ProcessSort.Handles => Strings.ProcessHandlesExplanation,
                _ => Strings.AverageOverTheLast5SecondsWindowsCountsProcessCPU
            };
            header.ToolTip = Strings.Format(Strings.ClickToRankProcessesBy, description, text);
        }
    }

    private PinnedProcess[] ResolvePinnedRows(IReadOnlyList<MonitorSample> samples, bool grouped, string query,
        bool appsOnly,
        Func<ProcessRanking> inclusive)
    {
        if (Pins.Count(grouped) == 0) return [];
        return
        [
            .. Pins.Resolve(samples.LastOrDefault(), inclusive().Processes, grouped,
                    _frame?.Selected is not null)
                .Where(p => ProcessSearch.Filter([p.Usage], query, appsOnly).Any())
        ];
    }

    private void UpdatePinnedRows(PinnedProcess[] pinned, ProcessGraphData[] data, IReadOnlyList<MonitorSample> samples,
        bool grouped)
    {
        while (_pinnedRows.Count < pinned.Length)
        {
            var row = new ProcessRow();
            EnableRowActions(row);
            _pinnedRows.Add(row);
            PinnedProcessRows.Children.Add(row);
        }

        for (var i = 0; i < _pinnedRows.Count; i++)
        {
            _pinnedRows[i].Visibility = i < pinned.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < pinned.Length)
                _pinnedRows[i].Update(pinned[i].Usage, samples,
                    MonitorPreferences.ShortRangeSeconds, _frame!.Wide, data[i], grouped, pinned[i].State);
        }

        var pinCount = Pins.Count(grouped);
        PinnedCountLabel.Text = Strings.Format(Strings.PinnedProcessCount, pinCount);
        PinSummary.Visibility = pinCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        PinnedCountLabel.ToolTip = Strings.PinnedProcessesExplanation;
        RankedDivider.Visibility = pinned.Length > 0 && PinnedOnlyCheckBox.IsChecked != true
            ? Visibility.Visible
            : Visibility.Collapsed;
        RankedDividerText.Text = Strings.Format(Strings.RankedByMetric,
            SortHeaders.First(h => h.Sort == _settings.Sort).Text);
    }

    private void EnableRowActions(ProcessRow row)
    {
        row.EnableProcessActions(CanEndProcess, EndProcess);
        row.EnablePriorityActions(CanSetProcessPriority, process => ProcessPriorityChanger.Read(process),
            SetProcessPriority);
        row.ProcessSelected += SelectProcess;
        row.EnableInspectionActions(
            snapshot => new ProcessPropertiesWindow(snapshot, Copy) { Owner = Window.GetWindow(this) }.ShowDialog(),
            snapshot => Copy(snapshot.Format()), OpenProcessLocation,
            process => new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = Window.GetWindow(this) }
                .ShowDialog(),
            CanDumpProcess, () => _frame?.Selected is not null);
        row.EnablePinning(Pins.Contains, ToggleProcessPin);
    }

    private void EnsureProcessRows(int count)
    {
        while (_rows.Count < count)
        {
            var row = new ProcessRow { Visibility = Visibility.Collapsed };
            EnableRowActions(row);
            _rows.Add(row);
            ProcessRows.Children.Insert(ProcessRows.Children.IndexOf(OtherRow), row);
        }

        // Release the extra row/graph controls when leaving All or narrowing the results.
        while (_rows.Count > Math.Max(50, count))
        {
            ProcessRows.Children.Remove(_rows[^1]);
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    // Keep each process on the row that last showed it and move rows to the new ranking. Rebinding rows in place
    // renamed almost every row (and its pin button) for screen readers, and moved keyboard focus to another process,
    // whenever the ranking reordered.
    private void ArrangeProcessRows(IReadOnlyList<ProcessUsage> top, bool grouped)
    {
        EnsureProcessRows(top.Count);
        var byIdentity = new Dictionary<(int, long, string?), ProcessRow>();
        foreach (var row in _rows)
            if (row.Identity is { } identity)
                byIdentity.TryAdd(identity, row);
        var arranged = new ProcessRow?[_rows.Count];
        var claimed = new HashSet<ProcessRow>();
        for (var i = 0; i < top.Count; i++)
            if (byIdentity.Remove(ProcessRow.IdentityOf(top[i], grouped), out var row))
            {
                arranged[i] = row;
                claimed.Add(row);
            }

        using (var spare = _rows.Where(row => !claimed.Contains(row)).GetEnumerator())
        {
            for (var i = 0; i < arranged.Length; i++)
                if (arranged[i] is null && spare.MoveNext())
                    arranged[i] = spare.Current;
        }

        var order = arranged.Select(row => row!).ToArray();
        if (!order.SequenceEqual(_rows)) MoveProcessRows(order);
        _rows.Clear();
        _rows.AddRange(order);
    }

    // Moves only the rows outside the longest run already in the new order, so most rows keep their visuals.
    private void MoveProcessRows(ProcessRow[] order)
    {
        var current = new Dictionary<ProcessRow, int>(_rows.Count);
        for (var i = 0; i < _rows.Count; i++) current[_rows[i]] = i;
        var staying = LongestIncreasingRun([.. order.Select(row => current[row])]);
        var focused = Keyboard.FocusedElement as DependencyObject;
        IInputElement? refocus = null;
        for (var i = 0; i < order.Length; i++)
        {
            if (staying[i]) continue;
            if (focused is not null && (order[i] == focused || order[i].IsAncestorOf(focused)))
                refocus = focused as IInputElement;
            ProcessRows.Children.Remove(order[i]);
        }

        var start = ProcessRows.Children.IndexOf(OtherRow) - staying.Count(stays => stays);
        for (var i = 0; i < order.Length; i++)
            if (!staying[i])
                ProcessRows.Children.Insert(start + i, order[i]);
        if (refocus is not null && !ReferenceEquals(Keyboard.FocusedElement, refocus)) Keyboard.Focus(refocus);
    }

    private void RefreshProcessActions(IReadOnlyList<MonitorSample> samples, bool grouped,
        Func<ProcessRanking> inclusive)
    {
        var historical = _frame?.Selected is not null;
        var error = _frame?.Error;
        var hadSelection = Selection.HasSelection;
        var target = Selection.Resolve(samples, grouped, historical, inclusive);
        if (hadSelection && !Selection.HasSelection) UpdateRetention();
        var snapshot = target is null
            ? null
            : new ProcessDetailsSnapshot(target.Usage, grouped, samples.LastOrDefault()?.Timestamp,
                historical, target.State == PinnedProcessState.Present && error is null,
                error is not null ? Strings.Unavailable : ProcessRow.StateText(target.State));
        ProcessActions.Update(snapshot,
            snapshot?.Available == true && CanEndProcess(snapshot.Process),
            snapshot?.Available == true && CanDumpProcess(snapshot.Process));
        ProcessActionDrawer.Summarize(ProcessActions.Title, ProcessActions.Detail, snapshot is not null);
        foreach (var row in _rows.Concat(_pinnedRows)) row.SetSelected(row.Represents(Selection.Contains));
    }

    private void OnProcessAction(string action, ProcessDetailsSnapshot snapshot)
    {
        if (_closing) return;
        var process = snapshot.Process;
        switch (action)
        {
            case "properties":
                new ProcessPropertiesWindow(snapshot, Copy) { Owner = Window.GetWindow(this) }.ShowDialog(); break;
            case "copy": Copy(snapshot.Format()); break;
            case "location": OpenProcessLocation(process); break;
            case "end" when snapshot is { Available: true, Historical: false } &&
                            snapshot.Grouped == _settings.Grouped:
                EndProcess(process); break;
            case "dump" when snapshot is { Available: true, Historical: false } &&
                             snapshot.Grouped == _settings.Grouped && CanDumpProcess(process):
                new ProcessDumpDialog(process, () => CanDumpProcess(process)) { Owner = Window.GetWindow(this) }
                    .ShowDialog();
                break;
        }
    }

    private void Copy(string text)
    {
        CopyRequested?.Invoke(text);
    }

    private ProcessUsage[] CaptureEndTargets(ProcessUsage process)
    {
        return ProcessTerminator.CaptureTargets(process,
            !_closing && _frame?.Selected is null, _settings.Grouped, _frame?.LatestFull?.Processes ?? []);
    }

    private bool CanEndProcess(ProcessUsage process)
    {
        return CaptureEndTargets(process).Length > 0;
    }

    private void EndProcess(ProcessUsage process)
    {
        var targets = CaptureEndTargets(process);
        if (targets.Length == 0) return;
        var grouped = _settings.Grouped;
        var message = grouped
            ? Strings.Format(Strings.EndGroupConfirmation, targets.Length, process.Name) + "\n\n" +
              string.Join("\n", targets.Select(p => $"{p.Name} (PID {p.ProcessId})"))
            : Strings.Format(Strings.EndPIDUnsavedWorkMayBeLostOnlyThisProcess, process.Name, process.ProcessId);
        if (new ProcessActionDialog(message, true) { Owner = Window.GetWindow(this) }.ShowDialog() != true ||
            _closing || _frame?.Selected is not null || grouped != _settings.Grouped) return;
        var errors = ProcessTerminator.EndAll(targets);
        if (errors.Count > 0)
            new ProcessActionDialog(string.Join("\n\n", errors), false) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private bool CanSetProcessPriority(ProcessUsage process)
    {
        return ProcessPriorityChanger.IsEligible(process,
            !_closing && _frame?.Selected is null && _frame?.Error is null, _settings.Grouped);
    }

    private void SetProcessPriority(ProcessUsage process, ProcessPriorityClass priority)
    {
        if (!CanSetProcessPriority(process)) return;
        if (ProcessPriorityChanger.Set(process, priority) is { } error)
            MessageDialog.ShowNotice(Window.GetWindow(this), error, Strings.SetPriority);
    }

    private bool CanDumpProcess(ProcessUsage process)
    {
        return ProcessDumpWriter.IsEligible(process,
            !_closing && _frame?.Selected is null, _settings.Grouped);
    }

    private void OpenProcessLocation(ProcessUsage process)
    {
        try
        {
            if (!File.Exists(process.ImagePath)) throw new IOException(Strings.ProcessFileMissing);
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe"))
                { UseShellExecute = false, Arguments = $"/select,\"{process.ImagePath}\"" };
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            MessageDialog.ShowNotice(Window.GetWindow(this), ex.Message, Strings.OpenFileLocation);
        }
    }
}
