using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

public partial class ServicesView : UserControl
{
    private readonly ObservableCollection<ServiceDisplayRow> _rows = [];
    private readonly HashSet<string> _expanded = [];
    private ServiceFrame? _frame;
    private IReadOnlyList<ServiceFrame> _history = [];
    private bool _historical, _busy, _ready, _refreshing;
    private string? _error;
    private bool? _wideToolbar;
    private bool? _wideActionBar;
    private ActionSelection? _actionSelection;
    private ServiceDisplayRow? _actionRow;
    private ServiceDisplayRow? _contextRow;
    private sealed record ActionSelection(string? ServiceName, string RowKey);
    internal event Action<ProcessUsage>? ProcessRequested;
    internal event Action<string>? CopyRequested;
    internal event Action<ServiceObservation, ProcessUsage?, string>? ActionRequested;

    public ServicesView()
    {
        InitializeComponent();
        foreach (var header in new[] { CpuHeader, MemoryHeader, IoHeader })
        {
            header.MouseEnter += (_, _) => header.TextDecorations = TextDecorations.Underline;
            header.MouseLeave += (_, _) => header.TextDecorations = null;
        }
        Rows.ItemsSource = _rows;
        Rows.ContextMenu = new ContextMenu();
        TopCount.ItemsSource = new[] { 5, 10, 15, 20, 50 };
        TopCount.SelectedItem = 20;
        RefreshLabels();
        _ready = true;
        Strings.Text.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => Strings.Text.PropertyChanged -= OnLanguageChanged;
        Loaded += (_, _) => { Strings.Text.PropertyChanged -= OnLanguageChanged; Strings.Text.PropertyChanged += OnLanguageChanged; ArrangeToolbar(); ArrangeActionBar(); };
        SizeChanged += (_, _) => { ArrangeToolbar(); ArrangeActionBar(); };
        ToolbarGrid.SizeChanged += (_, _) => ArrangeToolbar();
        ActionControls.SizeChanged += (_, _) => ArrangeActionBar();
        RefreshRows();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) { RefreshLabels(); RefreshRows(); ArrangeToolbar(); ArrangeActionBar(); }

    private void ArrangeActionBar()
    {
        if (!_ready || ActualWidth <= 0) return;
        ActionControls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = ActualWidth >= ActionControls.DesiredSize.Width + 264;
        if (_wideActionBar == wide) return;
        _wideActionBar = wide;
        ActionBarGrid.ColumnDefinitions[1].Width = wide ? GridLength.Auto : new GridLength(0);
        Grid.SetRow(ActionControls, wide ? 0 : 1);
        Grid.SetColumn(ActionControls, wide ? 1 : 0);
        ActionControls.Margin = wide ? new Thickness() : new Thickness(0, 10, 0, 0);
    }

    private void ArrangeToolbar()
    {
        if (!_ready || ActualWidth <= 0) return;
        RankingControls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ServiceFilters.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = ActualWidth >= RankingControls.DesiredSize.Width + ServiceFilters.DesiredSize.Width + 224;
        if (_wideToolbar == wide) return;
        _wideToolbar = wide;
        Grid.SetColumnSpan(RankingControls, wide ? 1 : 3);
        Grid.SetRow(SearchBorder, wide ? 0 : 1);
        Grid.SetColumn(SearchBorder, wide ? 1 : 0);
        Grid.SetColumnSpan(SearchBorder, wide ? 1 : 2);
        Grid.SetRow(ServiceFilters, wide ? 0 : 1);
        SearchBorder.Margin = wide ? new Thickness(12, 0, 12, 0) : new Thickness(0, 6, 12, 0);
        ServiceFilters.Margin = wide ? new Thickness() : new Thickness(0, 6, 0, 0);
    }
    private void RefreshLabels()
    {
        var status = StatusFilter.SelectedIndex;
        var sort = Sort.SelectedIndex;
        var ready = _ready;
        _ready = false;
        StatusFilter.ItemsSource = new[] { Strings.AllServiceStatuses, Strings.ServiceRunning, Strings.ServiceStopped };
        StatusFilter.SelectedIndex = Math.Max(0, status);
        Sort.ItemsSource = new[] { Strings.CPU, Strings.Memory, Strings.IO };
        Sort.SelectedIndex = Math.Clamp(sort, 0, 2);
        _ready = ready;
    }

    internal void Update(ServiceFrame? frame, IReadOnlyList<ServiceFrame> history, bool historical)
    {
        _frame = frame;
        _history = history;
        _historical = historical;
        RefreshRows();
    }

    internal void SetBusy(bool busy, string? error = null) { _busy = busy; _error = error; RefreshRows(); }
    private void OnFilterChanged(object sender, RoutedEventArgs e) { if (_ready) { RefreshRows(); ArrangeToolbar(); } }
    private void OnRankHeader(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Sort.SelectedIndex = ReferenceEquals(sender, CpuHeader) ? 0 : ReferenceEquals(sender, MemoryHeader) ? 1 : 2;
        e.Handled = true;
    }

    private void UpdateRankHeaders()
    {
        (TextBlock Header, string Label)[] headers = [(CpuHeader, Strings.CpuColumn), (MemoryHeader, Strings.ServicePrivateMiB), (IoHeader, Strings.IOMBS)];
        for (var i = 0; i < headers.Length; i++)
        {
            var (header, label) = headers[i];
            var active = Sort.SelectedIndex == i;
            header.Text = active ? label + " ▾" : label;
            header.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            header.SetResourceReference(TextBlock.ForegroundProperty, active ? "Text" : "Muted");
            header.ToolTip = $"{Strings.ServiceSort} {label}";
        }
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ClearServiceSearchButton.Visibility = Search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshRows();
    }
    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        Search.Focus();
    }
    internal void FocusSearch() { Search.Focus(); Search.SelectAll(); }
    internal bool ClearSearch()
    {
        if (!Search.IsKeyboardFocusWithin || Search.Text.Length == 0) return false;
        Search.Clear();
        return true;
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _actionRow = Rows.SelectedItem as ServiceDisplayRow;
        _actionSelection = _actionRow is { } row ? new ActionSelection(row.Service?.Name, row.Key) : null;
        UpdateButtons();
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroll || scroll.ViewportWidth <= 0) return;
        // Match the row viewport, including the vertical scrollbar gutter, at every horizontal offset.
        HeaderScroll.Width = scroll.ViewportWidth;
        ColumnHeaders.Width = Math.Max(ColumnHeaders.MinWidth, scroll.ViewportWidth - ColumnHeaders.Margin.Left - ColumnHeaders.Margin.Right);
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
    }
    private void OnExpand(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ServiceDisplayRow row) return;
        if (!_expanded.Add(row.Key)) _expanded.Remove(row.Key);
        RefreshRows();
    }

    internal void ExpandAll()
    {
        if (_frame is null) return;
        foreach (var group in _frame.Groups) _expanded.Add(group.Key);
        RefreshRows();
    }

    internal void ExpandGroup(string key) { _expanded.Add(key); RefreshRows(); }

    private void RefreshRows()
    {
        if (!_ready) return;
        UpdateRankHeaders();
        _refreshing = true;
        try
        {
            var old = _rows.ToDictionary(r => r.Key);
            var desired = new List<ServiceDisplayRow>();
            var groups = _frame?.Groups ?? [];
            var ranking = ServiceRanking.Build(_history, Workers.IsChecked == true);
            var moments = _history.Select(f => f.Sample).ToArray();
            var historyGroups = _history.Select(f => f.Groups.ToDictionary(g => g.Key)).ToArray();
            // Expansion state is session-local and bounded to retained service lifetimes.
            var retained = _history.SelectMany(f => f.Groups).Select(g => g.Key).ToHashSet();
            _expanded.RemoveWhere(k => !retained.Contains(k));
            var query = Search.Text.Trim();
            var filtered = groups.Where(g => Matches(g, query) && (StatusFilter.SelectedIndex switch
            {
                1 => g.Services.Any(s => s.State == 4),
                2 => g.Services.Any(s => s.State == 1),
                _ => true
            }));
            var ordered = Sort.SelectedIndex switch
            {
                1 => filtered.OrderByDescending(g => RankedMetrics(g).PrivateBytes),
                2 => filtered.OrderByDescending(g => RankedMetrics(g).Io),
                _ => filtered.OrderByDescending(g => RankedMetrics(g).Cpu)
            };
            var scale = Math.Max(1, _history.SelectMany(f => f.Groups).Select(g => g.Metrics(Workers.IsChecked == true).Cpu ?? 0).DefaultIfEmpty().Max() * 1.1);
            foreach (var group in ordered.ThenBy(g => g.Services[0].DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Take(TopCount.SelectedItem is int limit ? limit : 20))
            {
                Add(group.Key, group, null, null, false, true);
                if (!_expanded.Contains(group.Key)) continue;
                if (group.Shared)
                    foreach (var service in group.Services.OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase))
                        Add(group.Key + ":service:" + service.Name, group, null, service, true, false);
                var processes = Workers.IsChecked == true ? group.Processes : group.Host is null ? [] : new[] { group.Host };
                foreach (var process in processes.OrderBy(p => p == group.Host ? 0 : 1)
                    .ThenByDescending(p => ranking.Processes.GetValueOrDefault((p.ProcessId, p.CreationTicks))?.Cpu))
                {
                    if (group.Shared && group.Processes.Count == 1 && process == group.Host) continue;
                    Add($"process:{process.ProcessId}:{process.CreationTicks}", group, process, null, true, false);
                }
            }
            if (!_rows.Select(r => r.Key).SequenceEqual(desired.Select(r => r.Key)))
            {
                _rows.Clear();
                foreach (var row in desired) _rows.Add(row);
            }
            RefreshActionSelection(groups);
            Rows.SelectedItem = _actionRow is null ? null : _rows.FirstOrDefault(r => r.Key == _actionRow.Key);
            Notice.Text = _error ?? (_busy ? Strings.ServiceActionInProgress : _frame is null ?
                (_historical ? Strings.ServiceHistoryExpired : Strings.ServicesCollecting) : !_frame.Available ? Strings.ServicesUnavailable :
                _rows.Count == 0 ? Strings.NoServicesMatch : string.Empty);
            Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            ServiceMetrics RankedMetrics(ServiceGroup group) => ranking.Groups.GetValueOrDefault(group.Key) ?? new(null, null, null);

            void Add(string key, ServiceGroup group, ProcessUsage? process, ServiceObservation? service, bool child, bool header)
            {
                if (!old.TryGetValue(key, out var row)) row = new ServiceDisplayRow(key);
                var metrics = service is not null && group.Shared ? new ServiceMetrics(null, null, null) :
                    process is null ? RankedMetrics(group) : ranking.Processes.GetValueOrDefault((process.ProcessId, process.CreationTicks)) ?? new(null, null, null);
                row.Update(group, process, service, child, header, _expanded.Contains(group.Key), Workers.IsChecked == true, metrics);
                if (service is null && (process is not null || group.Host is not null))
                {
                    var values = _history.Select((f, i) =>
                    {
                        if (!f.Available) return (double?)null;
                        var past = historyGroups[i].GetValueOrDefault(group.Key);
                        return process is null ? past?.Metrics(Workers.IsChecked == true).Cpu :
                            past?.Processes.FirstOrDefault(p => p.ProcessId == process.ProcessId && p.CreationTicks == process.CreationTicks)?.CpuPercent;
                    }).ToArray();
                    var identity = process ?? group.Host!;
                    row.Graph.SetProcessData(new ProcessGraphData(moments, values, scale), 120, identity);
                    row.Graph.Visibility = Visibility.Visible;
                }
                else row.Graph.Visibility = Visibility.Hidden;
                desired.Add(row);
            }
        }
        finally { _refreshing = false; UpdateButtons(); }
    }

    private static bool Matches(ServiceGroup group, string query) => query.Length == 0 ||
        group.Services.Any(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || s.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
        group.Processes.Any(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal));

    private void RefreshActionSelection(IReadOnlyList<ServiceGroup> groups)
    {
        var previous = _actionRow;
        _actionRow = null;
        if (_actionSelection is not { } selection || _frame?.Available != true) return;
        // Service selection follows its SCM name across status/host changes. Process and shared-host
        // selections remain bound to the original process lifetime encoded in their row key.
        foreach (var group in groups)
        {
            ServiceObservation? service = null;
            ProcessUsage? process = null;
            string key;
            bool header;
            if (selection.ServiceName is { } name)
            {
                service = group.Services.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
                if (service is null) continue;
                header = !group.Shared;
                key = header ? group.Key : group.Key + ":service:" + service.Name;
            }
            else if (selection.RowKey == group.Key)
            {
                header = true;
                key = group.Key;
            }
            else
            {
                process = group.Processes.FirstOrDefault(p => $"process:{p.ProcessId}:{p.CreationTicks}" == selection.RowKey);
                if (process is null) continue;
                header = false;
                key = selection.RowKey;
            }
            _actionRow = _rows.FirstOrDefault(r => r.Key == key);
            if (_actionRow is null)
            {
                _actionRow = previous?.Key == key ? previous : new ServiceDisplayRow(key);
                _actionRow.Update(group, process, header ? null : service, !header, header, _expanded.Contains(group.Key),
                    Workers.IsChecked == true, new ServiceMetrics(null, null, null));
            }
            _actionSelection = new ActionSelection(selection.ServiceName, key);
            return;
        }
        _actionSelection = null;
    }

    private void UpdateButtons()
    {
        var row = _actionRow;
        ViewProcess.IsEnabled = row?.Process is not null;
        CopyDetails.IsEnabled = row is not null;
        Start.IsEnabled = CanServiceAction(row?.Service, row?.Group.Host, "start");
        Stop.IsEnabled = CanServiceAction(row?.Service, row?.Group.Host, "stop");
        Restart.IsEnabled = Stop.IsEnabled;
        Summary.Text = row?.Name ?? Strings.SelectService;
        SelectionDetail.Text = row?.Detail ?? "";
        SelectionDetail.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        SelectionStatus.Text = row?.Status ?? "";
        SelectionStatusBadge.Visibility = SelectionStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var statusBrush = row?.IsRunning == true ? "Accent" : "Muted";
        SelectionStatus.SetResourceReference(TextBlock.ForegroundProperty, statusBrush);
        SelectionStatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, statusBrush);
        SelectionStatusBadge.SetResourceReference(Border.BorderBrushProperty, statusBrush);
        ActionAccent.SetResourceReference(Border.BackgroundProperty, row is null ? "Line" : "Accent");
        Stop.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
            (System.Windows.Media.Brush)Resources["ServiceStopOutline"];
    }

    private void OnViewProcess(object sender, RoutedEventArgs e)
    {
        if (_actionRow?.Process is { } process) ProcessRequested?.Invoke(process);
    }
    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        if (_actionRow is { } row && FormatDetails(row) is { } text) CopyRequested?.Invoke(text);
    }
    private string? FormatDetails(ServiceDisplayRow row)
    {
        if (_frame?.Available != true) return null;
        var workers = Workers.IsChecked == true;
        var ranking = ServiceRanking.Build(_history, workers);
        // Shared service children have no individually attributable metrics.
        var metrics = row.Service is not null && row.Group.Shared ? null : row.Key == row.Group.Key
            ? ranking.Groups.GetValueOrDefault(row.Group.Key)
            : row.Process is { } process ? ranking.Processes.GetValueOrDefault((process.ProcessId, process.CreationTicks)) : null;
        var text = new System.Text.StringBuilder();
        text.AppendLine(row.Name);
        text.AppendLine(row.Detail);
        if (row.Status.Length > 0) text.AppendLine($"{Strings.ServiceStatus}: {row.Status}");
        text.AppendLine($"{Strings.ProcessObservationLabel}: {_frame.Sample.Timestamp.ToLocalTime():G}");
        text.AppendLine($"{Strings.ProcessViewLabel}: {(_historical ? Strings.ProcessHistoricalView : Strings.LiveMode)}");
        text.AppendLine(workers ? Strings.ServiceWorkersIncluded : Strings.ServiceHostOnly);
        text.AppendLine($"{Strings.CPU}: {(metrics?.Cpu is { } cpu ? Strings.Format(Strings.PercentOneDecimal, cpu) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.ServicePrivateMiB}: {(metrics?.PrivateBytes is { } memory ? (memory / 1048576d).ToString("N0", CultureInfo.CurrentCulture) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.IOMBS}: {(metrics?.Io is { } io ? (io / 1000000d).ToString("N2", CultureInfo.CurrentCulture) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.Services}:");
        foreach (var service in row.Service is { } selected ? new[] { selected } : row.Group.Services)
            text.AppendLine($"  {service.DisplayName} ({service.Name}) · {ServiceDisplayRow.StateText(service.State)}");
        var processes = row.Key != row.Group.Key && row.Service is null && row.Process is { } child ? new[] { child } :
            workers ? row.Group.Processes : row.Group.Host is { } host ? new[] { host } : [];
        foreach (var member in processes)
            text.AppendLine($"  {member.Name} · {Strings.Format(Strings.PID, member.ProcessId)}");
        return text.ToString();
    }
    private void OnStart(object sender, RoutedEventArgs e) => Request("start");
    private void OnStop(object sender, RoutedEventArgs e) => Request("stop");
    private void OnRestart(object sender, RoutedEventArgs e) => Request("restart");
    private void Request(string action)
    {
        if (_actionRow is { Service: { } service } row) RequestService(service, row.Group.Host, action);
    }

    private bool CanServiceAction(ServiceObservation? service, ProcessUsage? host, string action) =>
        !_historical && !_busy && _frame?.Available == true && service is not null &&
        (action == "start" ? service.State == 1 : service.State is 4 or 7 && (service.ControlsAccepted & 1) != 0 && host is not null);

    private void RequestService(ServiceObservation service, ProcessUsage? host, string action)
    {
        if (!CanServiceAction(service, host, action)) return;
        // Popup targets are immutable. Reject changed status or a replaced host instead of redirecting.
        var group = _frame!.Groups.FirstOrDefault(g => g.Services.Any(s => string.Equals(s.Name, service.Name, StringComparison.OrdinalIgnoreCase)));
        var current = group?.Services.FirstOrDefault(s => string.Equals(s.Name, service.Name, StringComparison.OrdinalIgnoreCase));
        if (current is null || current.State != service.State || current.ProcessId != service.ProcessId ||
            group?.Host?.ProcessId != host?.ProcessId || group?.Host?.CreationTicks != host?.CreationTicks ||
            !CanServiceAction(current, group?.Host, action)) return;
        ActionRequested?.Invoke(service, host, action);
    }

    private void OnRowRightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _contextRow = null;
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Rows, source) is ListBoxItem item)
        { _contextRow = item.DataContext as ServiceDisplayRow; item.IsSelected = true; item.Focus(); }
    }

    private void OnServiceMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Rows, source) is ListBoxItem item
            ? item.DataContext as ServiceDisplayRow : e.CursorLeft < 0 ? Rows.SelectedItem as ServiceDisplayRow : _contextRow;
        if (row is null || CreateServiceMenu(row) is not { } menu) { e.Handled = true; return; }
        // A placeholder menu already exists, so WPF can use the replacement on the first invocation.
        Rows.ContextMenu = menu;
    }

    internal ContextMenu? CreateServiceMenu(ServiceDisplayRow row)
    {
        if (!_rows.Contains(row) || _frame?.Available != true) return null;
        Rows.SelectedItem = row;
        var process = row.Process;
        var service = row.Service;
        var host = row.Group.Host;
        var details = FormatDetails(row);
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "Raised");
        menu.SetResourceReference(ForegroundProperty, "Text");
        Add(Strings.ViewServiceProcess, ButtonIcon.Process, process is not null, () => { if (process is not null) ProcessRequested?.Invoke(process); });
        Add(Strings.CopyDetails, ButtonIcon.Copy, details is not null, () => { if (details is not null) CopyRequested?.Invoke(details); });
        menu.Items.Add(new Separator());
        Add(Strings.StartService, ButtonIcon.Start, CanServiceAction(service, host, "start"), () => RequestService(service!, host, "start"));
        Add(Strings.StopService, ButtonIcon.Stop, CanServiceAction(service, host, "stop"), () => RequestService(service!, host, "stop"));
        Add(Strings.RestartService, ButtonIcon.Live, CanServiceAction(service, host, "restart"), () => RequestService(service!, host, "restart"));
        return menu;

        void Add(string label, System.Windows.Media.Geometry icon, bool enabled, Action action)
        {
            var entry = ProcessRow.ActionItem(label, icon);
            entry.IsEnabled = enabled;
            entry.Click += (_, _) => { if (enabled) action(); };
            menu.Items.Add(entry);
        }
    }
}

internal sealed class ServiceDisplayRow(string key) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Status { get; private set; } = "";
    public bool IsRunning { get; private set; }
    public string Cpu { get; private set; } = "";
    public string Memory { get; private set; } = "";
    public string Io { get; private set; } = "";
    public string Icon { get; private set; } = "";
    public string ExpandGlyph { get; private set; } = "";
    public string ExpandLabel => Strings.Format(Strings.ExpandService, Name);
    public Visibility ExpandVisibility { get; private set; }
    public Thickness Indent { get; private set; }
    public CpuGraph Graph { get; } = new() { Height = 24, ShowGrid = false, IsHitTestVisible = false };
    internal ServiceGroup Group { get; private set; } = null!;
    internal ServiceObservation? Service { get; private set; }
    internal ProcessUsage? Process { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(ServiceGroup group, ProcessUsage? process, ServiceObservation? service, bool child, bool header, bool expanded, bool workers, ServiceMetrics metrics)
    {
        Group = group;
        Service = service ?? (header && !group.Shared ? group.Services[0] : null);
        Process = process ?? group.Host;
        Name = process?.Name ?? (header && group.Shared ? Strings.Format(Strings.SharedServiceHost, group.Host?.Name ?? "", group.Services[0].ProcessId) : (service ?? group.Services[0]).DisplayName);
        Detail = process is not null ? Strings.Format(Strings.ServiceProcessPid, process.ProcessId) : header ?
            group.Shared ? Strings.Format(Strings.SharedServiceCount, group.Services.Count) : group.Services[0].Name + " · " +
                (workers && group.Processes.Count > 1 ? Strings.Format(Strings.ServiceHostWorkers, group.Processes.Count - 1) : Strings.ServiceDedicatedHost) : service!.Name + " · " + Strings.IncludedInHostTotal;
        Status = process is not null ? "" : header && group.Shared ? "" : StateText((service ?? group.Services[0]).State);
        IsRunning = Status.Length > 0 && (service ?? group.Services[0]).State == 4;
        Icon = process is not null ? "\uE8A5" : "\uE713";
        Indent = new Thickness(child ? 30 : 0, 4, 8, 4);
        ExpandGlyph = expanded ? "⌄" : "›";
        ExpandVisibility = header && (group.Host is not null || group.Shared) ? Visibility.Visible : Visibility.Collapsed;
        Cpu = metrics.Cpu is { } cpu ? Strings.Format(Strings.PercentOneDecimal, cpu) : Strings.MissingReading;
        Memory = metrics.PrivateBytes is { } memory ? (memory / 1048576d).ToString("N0", CultureInfo.CurrentCulture) : Strings.MissingReading;
        Io = metrics.Io is { } io ? (io / 1000000d).ToString("N2", CultureInfo.CurrentCulture) : Strings.MissingReading;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal static string StateText(uint state) => state switch
    {
        1 => Strings.ServiceStopped, 2 => Strings.ServiceStarting, 3 => Strings.ServiceStopping, 4 => Strings.ServiceRunning,
        5 => Strings.ServiceContinuing, 6 => Strings.ServicePausing, 7 => Strings.ServicePaused, _ => Strings.Unavailable
    };
}
