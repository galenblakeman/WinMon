using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinMon.Monitoring;

namespace WinMon.Controls;

public partial class ConnectionsView
{
    private readonly ObservableCollection<ConnectionDisplayRow> _rows = [];
    private readonly Dictionary<string, ConnectionDisplayRow> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expanded = [];
    private NetworkConnectionsFrame? _frame;
    private IReadOnlyList<ProcessUsage> _processes = [];
    private bool _historical;
    private bool _refreshing;
    private string? _selectedKey;

    internal event Action<string>? CopyRequested;
    internal event Action<ProcessUsage>? ProcessRequested;
    internal event Action<IReadOnlyList<ProcessUsage>>? GroupRequested;
    internal event Action<bool>? GroupedChanged;
    internal event Action<bool>? ActionsHiddenChanged;
    internal bool Grouped
    {
        get => GroupedChip.IsChecked == true;
        set
        {
            if (Grouped == value) return;
            if (value) GroupedChip.IsChecked = true;
            else IndividualChip.IsChecked = true;
        }
    }
    internal bool ActionsHidden
    {
        get => ActionsDrawer.Collapsed;
        set => ActionsDrawer.SetCollapsed(value);
    }
    internal IReadOnlyList<ConnectionDisplayRow> DisplayedRows => _rows;
    internal ProcessUsage? SelectedProcessForFixture => (Rows.SelectedItem as ConnectionDisplayRow)?.Process;
    internal string? SelectedDetailsForFixture => Rows.SelectedItem is ConnectionDisplayRow row ? Details(row) : null;

    public ConnectionsView()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
        Rows.PreviewKeyDown += OnRowKeyDown;
        ActionsDrawer.CollapsedChanged += hidden => ActionsHiddenChanged?.Invoke(hidden);
        Protocol.ItemsSource = new[] { Strings.AllEntries, "TCP", "UDP" };
        Protocol.SelectedIndex = 0;
        Strings.Text.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => Strings.Text.PropertyChanged -= OnLanguageChanged;
        Loaded += (_, _) =>
        {
            Strings.Text.PropertyChanged -= OnLanguageChanged;
            Strings.Text.PropertyChanged += OnLanguageChanged;
        };
        RefreshRows();
    }

    internal void Update(NetworkConnectionsFrame? frame, IReadOnlyList<ProcessUsage> processes, bool historical)
    {
        _frame = frame;
        _processes = processes;
        _historical = historical;
        RefreshRows();
    }

    internal void FocusSearch()
    {
        Search.Focus();
        Search.SelectAll();
    }

    internal bool HasOpenDropDown => Protocol.IsDropDownOpen;
    internal bool ClearSearch()
    {
        if (!Search.IsKeyboardFocusWithin || Search.Text.Length == 0) return false;
        Search.Clear();
        return true;
    }
    internal void SelectConnectionForFixture(int processId, long creationTicks, int connectionIndex = 0)
    {
        var entry = Entries().FirstOrDefault(entry => entry.Connection.ProcessId == processId &&
            entry.Connection.ProcessCreationTicks == creationTicks);
        if (entry is null) return;
        _expanded.Add(GroupKey(entry));
        RefreshRows();
        Rows.SelectedItem = _rows.Where(row => row.Connection is { } connection &&
                connection.ProcessId == processId && connection.ProcessCreationTicks == creationTicks)
            .ElementAtOrDefault(connectionIndex);
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        var selected = Protocol.SelectedIndex;
        Protocol.ItemsSource = new[] { Strings.AllEntries, "TCP", "UDP" };
        Protocol.SelectedIndex = Math.Max(0, selected);
        RefreshRows();
    }
    private void OnGroupingChanged(object sender, RoutedEventArgs e)
    {
        if (Rows is null) return;
        _selectedKey = null;
        GroupedChanged?.Invoke(Grouped);
        RefreshRows();
    }
    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshRows();

    private void OnProtocolChanged(object sender, SelectionChangedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        var frame = _frame;
        var entries = frame?.Available == true ? Entries() : [];
        var desired = new List<ConnectionDisplayRow>();
        if (frame?.Available == true)
        {
            var query = Search.Text.Trim();
            var protocol = Protocol.SelectedIndex;
            var groups = entries
                .Where(entry => protocol == 0 ||
                    (protocol == 1 && entry.Connection.Protocol == TransportProtocol.Tcp) ||
                    (protocol == 2 && entry.Connection.Protocol == TransportProtocol.Udp))
                .Where(entry => Grouped || Matches(entry, query))
                .GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => ConnectionGroup.Create(group.Key, group))
                .Where(group => Matches(group, query))
                .OrderBy(group => DisplayName(group.Process), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.Key, StringComparer.Ordinal);
            foreach (var group in groups)
            {
                var expanded = _expanded.Contains(group.Key);
                if (Grouped)
                {
                    var parent = Row(group.Key);
                    parent.UpdateGroup(group, expanded);
                    desired.Add(parent);
                    if (!expanded) continue;
                }
                foreach (var (entry, key) in KeyedEntries(group.Entries))
                {
                    var child = Row(key);
                    child.UpdateConnection(entry, !Grouped);
                    desired.Add(child);
                }
            }
        }
        _refreshing = true;
        try
        {
            for (var i = 0; i < desired.Count; i++)
            {
                if (i < _rows.Count && ReferenceEquals(_rows[i], desired[i])) continue;
                var from = _rows.IndexOf(desired[i]);
                if (from >= 0) _rows.Move(from, i);
                else _rows.Insert(i, desired[i]);
            }
            while (_rows.Count > desired.Count) _rows.RemoveAt(_rows.Count - 1);
            Rows.SelectedItem = _selectedKey is null ? null : _rows.FirstOrDefault(row => row.Key == _selectedKey);
        }
        finally
        {
            _refreshing = false;
        }
        if (frame is not null)
        {
            var valid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in entries.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase))
                valid.Add(group.Key);
            foreach (var (_, key) in KeyedEntries(entries)) valid.Add(key);
            foreach (var key in _cache.Keys.Where(key => !valid.Contains(key)).ToArray()) _cache.Remove(key);
            _expanded.RemoveWhere(key => !valid.Contains(key));
        }
        Notice.Text = frame is null ? Strings.ConnectionsLoading :
            !frame.Available ? Strings.ConnectionsUnavailable :
            frame.PartiallyAvailable ? Strings.ConnectionsPartial :
            _historical ? Strings.ConnectionsLiveOnly :
            _rows.Count == 0 ? Strings.ConnectionsEmpty : "";
        Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionBar();
    }

    private IReadOnlyList<ConnectionEntry> Entries()
    {
        if (_frame?.Available != true) return [];

        var owners = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        foreach (var process in _processes) owners[process.Identity] = process;
        var executableKeys = new Dictionary<string, string>(ProcessIdentityPolicy.Windows.Comparer);

        var entries = new ConnectionEntry[_frame.Connections.Count];
        for (var index = 0; index < entries.Length; index++)
        {
            var connection = _frame.Connections[index];
            ProcessUsage? owner = null;
            if (connection.ProcessCreationTicks is { } created)
                owners.TryGetValue(new ProcessLifetimeIdentity(connection.ProcessId, created,
                    ProcessLifetimeIdentity.WindowsFileTime), out owner);
            string groupKey;
            if (owner is { ImagePath.Length: > 0 })
            {
                if (!executableKeys.TryGetValue(owner.ImagePath, out groupKey!))
                {
                    groupKey = "image:" + ProcessRanking.GroupKey(owner).ToUpperInvariant();
                    executableKeys.Add(owner.ImagePath, groupKey);
                }
            }
            else groupKey = LifetimeKey(connection.ProcessId, connection.ProcessCreationTicks);
            entries[index] = new ConnectionEntry(connection, owner, groupKey);
        }
        return entries;
    }
    private static bool Matches(ConnectionGroup group, string query) => group.Entries.Any(entry => Matches(entry, query));

    private static bool Matches(ConnectionEntry entry, string query) => query.Length == 0 ||
        DisplayName(entry.Process).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
        entry.Connection.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal) ||
        Endpoint(entry.Connection.LocalEndpoint).Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (entry.Connection.RemoteEndpoint is { } remote &&
            Endpoint(remote).Contains(query, StringComparison.OrdinalIgnoreCase));

    private static string DisplayName(ProcessUsage? process) => process?.AppName is { Length: > 0 } name ? name :
        process?.Name is { Length: > 0 } executable ? executable : Strings.ConnectionsUnknownProcess;
    private static string GroupKey(ConnectionEntry entry) => entry.GroupKey;

    private static string LifetimeKey(int pid, long? ticks) => string.Create(CultureInfo.InvariantCulture,
        $"lifetime:{pid}:{ticks?.ToString(CultureInfo.InvariantCulture) ?? "?"}");

    private static string ConnectionKey(NetworkConnection connection) => string.Create(CultureInfo.InvariantCulture,
        $"{connection.Protocol}:{Endpoint(connection.LocalEndpoint)}:" +
        $"{(connection.RemoteEndpoint is { } remote ? Endpoint(remote) : "-")}:{connection.State}");
    private static IEnumerable<(ConnectionEntry Entry, string Key)> KeyedEntries(IEnumerable<ConnectionEntry> entries)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries
                     .OrderBy(entry => entry.Connection.Protocol)
                     .ThenBy(entry => Endpoint(entry.Connection.LocalEndpoint), StringComparer.Ordinal))
        {
            var identity = LifetimeKey(entry.Connection.ProcessId, entry.Connection.ProcessCreationTicks) + "/" +
                ConnectionKey(entry.Connection);
            counts.TryGetValue(identity, out var occurrence);
            counts[identity] = occurrence + 1;
            yield return (entry, identity + "/" + occurrence.ToString(CultureInfo.InvariantCulture));
        }
    }
    private ConnectionDisplayRow Row(string key)
    {
        if (_cache.TryGetValue(key, out var row)) return row;
        _cache[key] = row = new ConnectionDisplayRow(key);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionDisplayRow.Icon) && ReferenceEquals(Rows.SelectedItem, row))
                UpdateActionBar();
        };
        return row;
    }
    private void OnRowKeyDown(object sender, KeyEventArgs e)
    {
        if (Rows.SelectedItem is not ConnectionDisplayRow { IsGroup: true } row ||
            e.Key is not (Key.Left or Key.Right)) return;
        if (e.Key == Key.Right) _expanded.Add(row.Key);
        else _expanded.Remove(row.Key);
        RefreshRows();
        e.Handled = true;
    }
    private void OnExpand(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConnectionDisplayRow { IsGroup: true } row) return;
        if (!_expanded.Add(row.Key)) _expanded.Remove(row.Key);
        RefreshRows();
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _selectedKey = (Rows.SelectedItem as ConnectionDisplayRow)?.Key;
        UpdateActionBar();
    }
    private void UpdateActionBar()
    {
        var row = Rows.SelectedItem as ConnectionDisplayRow;
        SelectionName.Text = row?.Connection is not null ? DisplayName(row.Process) : row?.Name ?? Strings.ConnectionsTab;
        SelectionDetail.Text = row?.Connection is not null
            ? $"{row.Detail}  {row.Local}  →  {row.Remote}  ·  {row.State}"
            : row?.Detail ?? "";
        SelectionIcon.Source = row?.Icon;
        SelectionIcon.Visibility = row?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        ActionsDrawer.Summarize(SelectionName.Text, SelectionDetail.Text, row is not null);
        CopyDetails.IsEnabled = row is { Connections.Count: > 0 } && _frame?.Available == true;
        ShowProcess.IsEnabled = row?.Connection is not null ? row.Process is not null : row is { Members.Count: > 0 };
    }
    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is ConnectionDisplayRow { Connections.Count: > 0 } row)
            CopyRequested?.Invoke(Details(row));
    }
    private void OnShowProcess(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is not ConnectionDisplayRow row) return;
        if (row.Connection is not null && row.Process is { } process)
            ProcessRequested?.Invoke(process);
        else if (row.IsGroup && row.Members.Count > 0)
            GroupRequested?.Invoke(row.Members);
    }
    private static string Details(ConnectionDisplayRow row)
    {
        if (row.Connection is { } selectedConnection) return Details(selectedConnection, row.Process);
        var text = new StringBuilder();
        text.AppendLine(row.Name);
        text.AppendLine($"PID: {string.Join(", ", row.Connections.Select(connection => connection.ProcessId).Distinct())}");
        foreach (var endpoint in row.Connections)
            text.AppendLine($"{endpoint.ProcessId}\t{ProtocolText(endpoint.Protocol)}\t" +
                $"{Endpoint(endpoint.LocalEndpoint)}\t" +
                $"{(endpoint.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}\t" +
                StateText(endpoint.State));
        return text.ToString();
    }
    private static string Details(NetworkConnection c, ProcessUsage? process) => string.Join(Environment.NewLine, [DisplayName(process), $"PID: {c.ProcessId.ToString(CultureInfo.InvariantCulture)}", $"{Strings.ConnectionsProtocol}: {ProtocolText(c.Protocol)}", $"{Strings.ConnectionsLocalEndpoint}: {Endpoint(c.LocalEndpoint)}", $"{Strings.ConnectionsRemoteEndpoint}: {(c.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}", $"{Strings.ConnectionsState}: {StateText(c.State)}"]);
    internal static string Endpoint(IPEndPoint endpoint) => endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{endpoint.Address}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}" : $"{endpoint.Address}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    internal static string ProtocolText(TransportProtocol protocol) => protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
    internal static string StateText(TcpState? state) => state switch { TcpState.Closed => Strings.ConnectionsTcpStateClosed, TcpState.Listen => Strings.ConnectionsTcpStateListen, TcpState.SynSent => Strings.ConnectionsTcpStateSynSent, TcpState.SynReceived => Strings.ConnectionsTcpStateSynReceived, TcpState.Established => Strings.ConnectionsTcpStateEstablished, TcpState.FinWait1 => Strings.ConnectionsTcpStateFinWait1, TcpState.FinWait2 => Strings.ConnectionsTcpStateFinWait2, TcpState.CloseWait => Strings.ConnectionsTcpStateCloseWait, TcpState.Closing => Strings.ConnectionsTcpStateClosing, TcpState.LastAck => Strings.ConnectionsTcpStateLastAck, TcpState.TimeWait => Strings.ConnectionsTcpStateTimeWait, TcpState.DeleteTcb => Strings.ConnectionsTcpStateDeleteTcb, _ => Strings.MissingReading };
    internal sealed record ConnectionEntry(NetworkConnection Connection, ProcessUsage? Process, string GroupKey);
    internal sealed record ConnectionGroup(string Key, IReadOnlyList<ConnectionEntry> Entries, IReadOnlyList<ProcessUsage> Members, ProcessUsage? Process)
    {
        internal static ConnectionGroup Create(string key, IEnumerable<ConnectionEntry> source)
        {
            var entries = source.ToArray(); var members = entries.Select(e => e.Process).OfType<ProcessUsage>().DistinctBy(p => p.Identity).ToArray();
            var process = members.OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.ProcessId).FirstOrDefault();
            return new(key, entries, members, process);
        }
    }
}

internal sealed class ConnectionDisplayRow(string key) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Local { get; private set; } = "";
    public string LocalToolTip { get; private set; } = "";
    public string Remote { get; private set; } = "";
    public string RemoteToolTip { get; private set; } = "";
    public string State { get; private set; } = "";
    public string StateToolTip { get; private set; } = "";
    public string AccessibleName { get; private set; } = "";
    public string Tcp { get; private set; } = "";
    public string Udp { get; private set; } = "";
    public string ExpandGlyph { get; private set; } = "";
    public string ExpandLabel { get; private set; } = "";
    public Visibility ExpandVisibility { get; private set; }
    public Visibility IconVisibility { get; private set; }
    public Thickness Indent { get; private set; }
    public double MinimumHeight { get; private set; }
    public BitmapSource? Icon { get; private set; }
    internal bool IsGroup { get; private set; }
    internal ProcessUsage? Process { get; private set; }
    internal NetworkConnection? Connection { get; private set; }
    internal IReadOnlyList<NetworkConnection> Connections { get; private set; } = [];
    internal IReadOnlyList<ProcessUsage> Members { get; private set; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void UpdateGroup(ConnectionsView.ConnectionGroup group, bool expanded)
    {
        var shown = Shown();
        IsGroup = true;
        Process = group.Process;
        Connection = null;
        Connections = group.Entries.Select(entry => entry.Connection).ToArray();
        Members = group.Members;
        Name = DisplayName(Process);
        Detail = Members.Count > 1
            ? Strings.Format(Strings.ConnectionsProcessCount, Members.Count)
            : $"PID {group.Entries[0].Connection.ProcessId.ToString(CultureInfo.InvariantCulture)}";
        (Local, LocalToolTip) = EndpointSummary(Connections.Select(c => (c.Protocol, c.LocalEndpoint)));
        (Remote, RemoteToolTip) = EndpointSummary(Connections.Where(c => c.Protocol == TransportProtocol.Tcp && c.RemoteEndpoint is not null).Select(c => (c.Protocol, c.RemoteEndpoint!)));
        (State, StateToolTip) = StateSummary(Connections.Where(c => c.Protocol == TransportProtocol.Tcp));
        Tcp = Connections.Count(connection => connection.Protocol == TransportProtocol.Tcp).ToString(CultureInfo.CurrentCulture);
        Udp = Connections.Count(connection => connection.Protocol == TransportProtocol.Udp).ToString(CultureInfo.CurrentCulture);
        AccessibleName = $"{Name}, {Detail}, {Local}, {Remote}, {State}, {Tcp} TCP, {Udp} UDP";
        ExpandGlyph = expanded ? "⌄" : "›";
        ExpandLabel = Strings.Format(expanded ? Strings.ConnectionsCollapse : Strings.ConnectionsExpand, Name);
        ExpandVisibility = Visibility.Visible;
        IconVisibility = Visibility.Visible;
        Indent = new Thickness(0, 4, 8, 4);
        MinimumHeight = 46;
        EnsureIcon();
        if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    internal void UpdateConnection(ConnectionsView.ConnectionEntry entry, bool individual)
    {
        var shown = Shown(); IsGroup = false; Process = entry.Process; Connection = entry.Connection; Connections = [entry.Connection]; Members = entry.Process is null ? [] : [entry.Process];
        Name = DisplayName(Process); Detail = Strings.Format(Strings.ConnectionsEndpointProtocol, $"PID {entry.Connection.ProcessId.ToString(CultureInfo.InvariantCulture)}", ConnectionsView.ProtocolText(entry.Connection.Protocol));
        Local = LocalToolTip = ConnectionsView.Endpoint(entry.Connection.LocalEndpoint); Remote = RemoteToolTip = entry.Connection.RemoteEndpoint is { } remote ? ConnectionsView.Endpoint(remote) : Strings.MissingReading; State = StateToolTip = ConnectionsView.StateText(entry.Connection.State);
        AccessibleName = $"{Name}, {Detail}, {Local}, {Remote}, {State}"; Tcp = Udp = ""; ExpandGlyph = ExpandLabel = ""; ExpandVisibility = Visibility.Collapsed; IconVisibility = Visibility.Visible; Indent = new Thickness(individual ? 0 : 30, 3, 8, 3); MinimumHeight = 32; EnsureIcon(); if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    private static (string Text, string ToolTip) EndpointSummary(
        IEnumerable<(TransportProtocol Protocol, IPEndPoint Endpoint)> source)
    {
        var all = source.Distinct().ToArray();
        if (all.Length == 0) return (Strings.MissingReading, Strings.MissingReading);
        var tooltip = string.Join(Environment.NewLine,
            all.Select(item => $"{ConnectionsView.ProtocolText(item.Protocol)} {ConnectionsView.Endpoint(item.Endpoint)}"));
        var endpoints = all.Select(item => item.Endpoint).Distinct().ToArray();
        if (endpoints.Length == 1) return (ConnectionsView.Endpoint(endpoints[0]), tooltip);
        var addresses = endpoints.Select(endpoint => endpoint.Address).Distinct().ToArray();
        var ports = endpoints.Select(endpoint => endpoint.Port).Distinct().Count();
        return addresses.Length == 1
            ? (Strings.Format(Strings.ConnectionsAddressPorts, addresses[0], ports), tooltip)
            : ports == 1
                ? (Strings.Format(Strings.ConnectionsAddressesSinglePort, addresses.Length), tooltip)
                : (Strings.Format(Strings.ConnectionsAddressesPorts, addresses.Length, ports), tooltip);
    }
    private static (string Text, string ToolTip) StateSummary(IEnumerable<NetworkConnection> source)
    {
        var groups = source.GroupBy(connection => connection.State)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .ToArray();
        if (groups.Length == 0) return (Strings.MissingReading, Strings.MissingReading);
        var parts = groups.Select(group => group.Key is null
            ? Strings.Format(Strings.ConnectionsUnavailableCount, group.Count())
            : Strings.Format(Strings.ConnectionsStateCount, group.Count(), ConnectionsView.StateText(group.Key)))
            .ToArray();
        return (string.Join(" · ", parts), string.Join(Environment.NewLine, parts));
    }

    private static string DisplayName(ProcessUsage? process) => process?.AppName is { Length: > 0 } name ? name :
        process?.Name is { Length: > 0 } executable ? executable : Strings.ConnectionsUnknownProcess;

    private async Task LoadIconAsync(ProcessUsage process)
    {
        var icon = await ProcessIconProvider.GetAsync(process);
        if (Process?.Identity != process.Identity) return;
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    private ProcessLifetimeIdentity? _iconIdentity;
    private void EnsureIcon()
    {
        if (Icon is not null && _iconIdentity == Process?.Identity) return;
        Icon = ProcessIconProvider.Fallback;
        _iconIdentity = Process?.Identity;
        if (Process is not null) _ = LoadIconAsync(Process);
    }

    private (string, string, string, string, string, string, string, string, string, string, string,
        string, string, Visibility, Visibility, Thickness, double) Shown() =>
        (Name, Detail, Local, LocalToolTip, Remote, RemoteToolTip, State, StateToolTip, AccessibleName,
            Tcp, Udp, ExpandGlyph, ExpandLabel, ExpandVisibility, IconVisibility, Indent, MinimumHeight);
}
