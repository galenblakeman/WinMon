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

/// <summary>Read-only, live TCP and UDP endpoints grouped by the current process lifetime.</summary>
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
    internal event Action<bool>? ActionsHiddenChanged;
    internal bool ActionsHidden
    {
        get => ActionsDrawer.Collapsed;
        set => ActionsDrawer.SetCollapsed(value);
    }

    /// <summary>Fixture-facing rows; the normal list keeps their identity across live refreshes.</summary>
    internal IReadOnlyList<ConnectionDisplayRow> DisplayedRows => _rows;
    internal ProcessUsage? SelectedProcessForFixture => (Rows.SelectedItem as ConnectionDisplayRow)?.Process;
    internal string? SelectedDetailsForFixture => (Rows.SelectedItem as ConnectionDisplayRow)?.Connection is { } connection
        ? Details(connection, (Rows.SelectedItem as ConnectionDisplayRow)?.Process) : null;

    internal void SelectConnectionForFixture(int processId, long creationTicks, int connectionIndex = 0)
    {
        var group = _frame?.Connections.GroupBy(connection => (connection.ProcessId, connection.ProcessCreationTicks))
            .FirstOrDefault(group => group.Key.ProcessId == processId && group.Key.ProcessCreationTicks == creationTicks);
        if (group is null) return;
        var key = string.Create(CultureInfo.InvariantCulture, $"{processId}:{creationTicks}");
        _expanded.Add(key);
        RefreshRows();
        var selected = _rows.Where(row => row.Key.StartsWith(key + "/", StringComparison.Ordinal)).ElementAtOrDefault(connectionIndex);
        Rows.SelectedItem = selected;
    }

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

    /// <summary>Refresh from the current endpoint snapshot. Endpoints never follow Capture history.</summary>
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

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        var protocol = Protocol.SelectedIndex;
        Protocol.ItemsSource = new[] { Strings.AllEntries, "TCP", "UDP" };
        Protocol.SelectedIndex = Math.Max(0, protocol);
        RefreshRows();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RefreshRows();
    private void OnProtocolChanged(object sender, SelectionChangedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        var frame = _frame;
        var processes = _processes;
        var desired = new List<ConnectionDisplayRow>();
        if (frame?.Available == true)
        {
            var query = Search.Text.Trim();
            var protocol = Protocol.SelectedIndex;
            var groups = frame.Connections
                .Where(connection => protocol == 0 || (protocol == 1 && connection.Protocol == TransportProtocol.Tcp) ||
                                      (protocol == 2 && connection.Protocol == TransportProtocol.Udp))
                .GroupBy(connection => (connection.ProcessId, connection.ProcessCreationTicks))
                .Select(group => new ConnectionGroup(group.Key.ProcessId, group.Key.ProcessCreationTicks,
                    processes.FirstOrDefault(process => group.Key.ProcessCreationTicks is { } created &&
                        process.ProcessId == group.Key.ProcessId && process.CreationTicks == created), group.ToArray()))
                .Where(group => Matches(group, query))
                .OrderBy(group => DisplayName(group.Process), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(group => group.ProcessId)
                .ToArray();

            foreach (var group in groups)
            {
                var key = GroupKey(group);
                var expanded = _expanded.Contains(key);
                var parent = Row(key);
                parent.UpdateGroup(group, expanded);
                desired.Add(parent);
                if (!expanded) continue;
                foreach (var (connection, rowKey) in KeyedConnections(group.Connections.OrderBy(connection => connection.Protocol)
                             .ThenBy(connection => Endpoint(connection.LocalEndpoint), StringComparer.Ordinal)))
                {
                    var child = Row(rowKey);
                    child.UpdateConnection(group, connection);
                    desired.Add(child);
                }
            }
        }

        _refreshing = true;
        try
        {
            for (var index = 0; index < desired.Count; index++)
            {
                if (index < _rows.Count && ReferenceEquals(_rows[index], desired[index])) continue;
                var from = _rows.IndexOf(desired[index]);
                if (from >= 0) _rows.Move(from, index);
                else _rows.Insert(index, desired[index]);
            }
            while (_rows.Count > desired.Count) _rows.RemoveAt(_rows.Count - 1);
            Rows.SelectedItem = _selectedKey is null ? null : _rows.FirstOrDefault(row => row.Key == _selectedKey);
        }
        finally { _refreshing = false; }

        // Endpoint tables are live and short-lived. Drop vanished identities, while keeping rows hidden only by a filter.
        if (frame is not null)
        {
            var valid = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (connection, rowKey) in KeyedConnections(frame.Connections))
            {
                var groupKey = GroupKey(connection.ProcessId, connection.ProcessCreationTicks);
                valid.Add(groupKey);
                valid.Add(rowKey);
            }
            foreach (var key in _cache.Keys.Where(key => !valid.Contains(key)).ToArray()) _cache.Remove(key);
            _expanded.RemoveWhere(key => !valid.Contains(key));
        }

        Notice.Text = frame is null ? Strings.ConnectionsLoading :
            !frame.Available ? Strings.ConnectionsUnavailable :
            frame.PartiallyAvailable ? Strings.ConnectionsPartial :
            _historical ? Strings.ConnectionsLiveOnly :
            _rows.Count == 0 ? Strings.ConnectionsEmpty : "";
        Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Summary.Text = frame?.Available == true ? Strings.Format(Strings.ConnectionsCounts,
            frame.Connections.Count(c => c.Protocol == TransportProtocol.Tcp),
            frame.Connections.Count(c => c.Protocol == TransportProtocol.Udp)) : "";
        UpdateActionBar();
    }

    private static bool Matches(ConnectionGroup group, string query)
    {
        if (query.Length == 0) return true;
        return DisplayName(group.Process).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               group.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal) ||
               group.Connections.Any(connection => Endpoint(connection.LocalEndpoint).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   (connection.RemoteEndpoint is { } remote && Endpoint(remote).Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static string DisplayName(ProcessUsage? process) => process?.AppName is { Length: > 0 } name ? name :
        process?.Name is { Length: > 0 } executable ? executable : Strings.ConnectionsUnknownProcess;

    private static string GroupKey(ConnectionGroup group) => GroupKey(group.ProcessId, group.CreationTicks);

    private static string GroupKey(int processId, long? creationTicks) => string.Create(CultureInfo.InvariantCulture,
        $"{processId}:{creationTicks?.ToString(CultureInfo.InvariantCulture) ?? "?"}");

    private static string ConnectionKey(NetworkConnection connection) => string.Create(CultureInfo.InvariantCulture,
        $"{connection.Protocol}:{Endpoint(connection.LocalEndpoint)}:{(connection.RemoteEndpoint is { } remote ? Endpoint(remote) : "-")}:{connection.State}");

    private static IEnumerable<(NetworkConnection Connection, string Key)> KeyedConnections(IEnumerable<NetworkConnection> connections)
    {
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var connection in connections)
        {
            var key = GroupKey(connection.ProcessId, connection.ProcessCreationTicks) + "/" + ConnectionKey(connection);
            occurrences.TryGetValue(key, out var occurrence);
            occurrences[key] = occurrence + 1;
            yield return (connection, key + "/" + occurrence.ToString(CultureInfo.InvariantCulture));
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
        SelectionName.Text = row?.Connection is not null ? DisplayName(row.Process) : row?.Name ?? Strings.ConnectionsLive;
        SelectionDetail.Text = row?.Connection is not null
            ? $"{row.Name}  {row.Local}  →  {row.Remote}  ·  {row.State}"
            : row?.Detail ?? "";
        SelectionIcon.Source = row?.Icon;
        SelectionIcon.Visibility = row?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        ActionsDrawer.Summarize(SelectionName.Text, SelectionDetail.Text, row is not null);
        CopyDetails.IsEnabled = row is { Connections.Count: > 0 } && _frame?.Available == true;
        ShowProcess.IsEnabled = row?.Process is not null && row.CreationTicks == row.Process.CreationTicks;
    }

    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is not ConnectionDisplayRow { Connections.Count: > 0 } row) return;
        CopyRequested?.Invoke(Details(row.Connections, row.Process));
    }

    private void OnShowProcess(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is ConnectionDisplayRow { Process: { } process } row &&
            row.CreationTicks == process.CreationTicks)
            ProcessRequested?.Invoke(process);
    }

    private static string Details(NetworkConnection connection, ProcessUsage? process)
    {
        var text = new StringBuilder();
        text.AppendLine(DisplayName(process));
        text.AppendLine($"PID: {connection.ProcessId.ToString(CultureInfo.InvariantCulture)}");
        text.AppendLine($"{Strings.ConnectionsProtocol}: {ProtocolText(connection.Protocol)}");
        text.AppendLine($"{Strings.ConnectionsLocalEndpoint}: {Endpoint(connection.LocalEndpoint)}");
        text.AppendLine($"{Strings.ConnectionsRemoteEndpoint}: {(connection.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.ConnectionsState}: {StateText(connection.State)}");
        return text.ToString();
    }

    private static string Details(IReadOnlyList<NetworkConnection> connections, ProcessUsage? process)
    {
        if (connections.Count == 1) return Details(connections[0], process);
        var text = new StringBuilder();
        text.AppendLine(DisplayName(process));
        text.AppendLine($"PID: {connections[0].ProcessId.ToString(CultureInfo.InvariantCulture)}");
        foreach (var connection in connections)
            text.AppendLine($"{ProtocolText(connection.Protocol)}\t{Endpoint(connection.LocalEndpoint)}\t" +
                $"{(connection.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}\t{StateText(connection.State)}");
        return text.ToString();
    }

    internal static string Endpoint(IPEndPoint endpoint) => endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
        ? $"[{endpoint.Address}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
        : $"{endpoint.Address}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    internal static string ProtocolText(TransportProtocol protocol) => protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
    internal static string StateText(TcpState? state) => state switch
    {
        TcpState.Closed => Strings.ConnectionsTcpStateClosed, TcpState.Listen => Strings.ConnectionsTcpStateListen,
        TcpState.SynSent => Strings.ConnectionsTcpStateSynSent, TcpState.SynReceived => Strings.ConnectionsTcpStateSynReceived,
        TcpState.Established => Strings.ConnectionsTcpStateEstablished, TcpState.FinWait1 => Strings.ConnectionsTcpStateFinWait1,
        TcpState.FinWait2 => Strings.ConnectionsTcpStateFinWait2, TcpState.CloseWait => Strings.ConnectionsTcpStateCloseWait,
        TcpState.Closing => Strings.ConnectionsTcpStateClosing, TcpState.LastAck => Strings.ConnectionsTcpStateLastAck,
        TcpState.TimeWait => Strings.ConnectionsTcpStateTimeWait, TcpState.DeleteTcb => Strings.ConnectionsTcpStateDeleteTcb,
        _ => Strings.MissingReading
    };

    internal sealed record ConnectionGroup(int ProcessId, long? CreationTicks, ProcessUsage? Process,
        IReadOnlyList<NetworkConnection> Connections);
}

internal sealed class ConnectionDisplayRow(string key) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Local { get; private set; } = "";
    public string Remote { get; private set; } = "";
    public string State { get; private set; } = "";
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
    internal long? CreationTicks { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateGroup(ConnectionsView.ConnectionGroup group, bool expanded)
    {
        var shown = Shown();
        IsGroup = true;
        Process = group.Process;
        Connection = null;
        Connections = group.Connections;
        CreationTicks = group.CreationTicks;
        Name = Process?.AppName is { Length: > 0 } app ? app : Process?.Name is { Length: > 0 } name ? name : Strings.ConnectionsUnknownProcess;
        Detail = $"PID {group.ProcessId}";
        Local = Remote = State = "";
        Tcp = group.Connections.Count((Func<NetworkConnection, bool>)(connection => connection.Protocol == TransportProtocol.Tcp)).ToString(CultureInfo.CurrentCulture);
        Udp = group.Connections.Count((Func<NetworkConnection, bool>)(connection => connection.Protocol == TransportProtocol.Udp)).ToString(CultureInfo.CurrentCulture);
        AccessibleName = $"{Name}, {Detail}, {Tcp} TCP, {Udp} UDP";
        ExpandGlyph = expanded ? "⌄" : "›";
        ExpandLabel = Strings.Format(expanded ? Strings.ConnectionsCollapse : Strings.ConnectionsExpand, Name);
        ExpandVisibility = Visibility.Visible;
        IconVisibility = Visibility.Visible;
        Indent = new Thickness(0, 4, 8, 4);
        MinimumHeight = 46;
        EnsureIcon();
        if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal void UpdateConnection(ConnectionsView.ConnectionGroup group, NetworkConnection connection)
    {
        var shown = Shown();
        IsGroup = false;
        Process = group.Process;
        Connection = connection;
        Connections = [connection];
        CreationTicks = connection.ProcessCreationTicks;
        Name = ConnectionsView.ProtocolText(connection.Protocol);
        Detail = "";
        Local = ConnectionsView.Endpoint(connection.LocalEndpoint);
        Remote = connection.RemoteEndpoint is { } remote ? ConnectionsView.Endpoint(remote) : Strings.MissingReading;
        State = ConnectionsView.StateText(connection.State);
        AccessibleName = $"{Name}, {Local}, {Remote}, {State}";
        Tcp = Udp = "";
        ExpandGlyph = ExpandLabel = "";
        ExpandVisibility = Visibility.Collapsed;
        EnsureIcon();
        IconVisibility = Visibility.Collapsed;
        Indent = new Thickness(30, 3, 8, 3);
        MinimumHeight = 32;
        if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

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

    private (string, string, string, string, string, string, string, string, string, string, Visibility, Visibility, Thickness, double) Shown() =>
        (Name, Detail, Local, Remote, State, AccessibleName, Tcp, Udp, ExpandGlyph, ExpandLabel, ExpandVisibility, IconVisibility, Indent, MinimumHeight);
}
