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
using WinMon.Preferences;

namespace WinMon.Controls;

internal enum ConnectionSort { Receive, Send, Tcp, Udp }

internal sealed record ConnectionListSettings(int TopCount = 10, ConnectionSort Sort = ConnectionSort.Receive,
    bool AppsOnly = false);

public partial class ConnectionsView
{
    public static readonly DependencyProperty ConnectionRowMinWidthProperty = DependencyProperty.Register(
        nameof(ConnectionRowMinWidth), typeof(double), typeof(ConnectionsView), new PropertyMetadata(758d));
    private readonly ObservableCollection<ConnectionDisplayRow> _rows = [];
    private readonly Dictionary<string, ConnectionDisplayRow> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expanded = [];
    private NetworkConnectionsFrame? _frame;
    private IReadOnlyList<ProcessUsage> _processes = [];
    private ProcessNetworkFrame? _network;
    private bool _networkEnabled;
    private readonly Dictionary<string, IReadOnlyList<ProcessUsage>> _networkMembersByPath =
        new(ProcessIdentityPolicy.Windows.Comparer);
    private readonly Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> _networkReadings = [];
    private bool _historical;
    private bool _refreshing;
    private bool _loadingSettings;
    private string? _selectedKey;
    private ConnectionListSettings _settings = new();

    internal event Action<string>? CopyRequested;
    internal event Action<ProcessUsage>? ProcessRequested;
    internal event Action<IReadOnlyList<ProcessUsage>>? GroupRequested;
    internal event Action<bool>? GroupedChanged;
    internal event Action<bool>? ActionsHiddenChanged;
    internal event Action<ConnectionListSettings>? SettingsChanged;
    internal event Action<string, string>? PropertiesRequested;
    internal event Action? TrafficSettingsRequested;
    internal Func<ProcessUsage, bool>? CanShowInProcesses { get; set; }
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
    // Compatibility diagnostic alias: the former combined network column is now Receive.
    internal ColumnDefinition NetworkHeaderColumn => ReceiveHeaderColumn;
    internal TextBlock NetworkHeader => ReceiveHeader;
    internal ConnectionListSettings Settings => _settings;
    internal ProcessUsage? SelectedProcessForFixture => (Rows.SelectedItem as ConnectionDisplayRow)?.Process;
    internal string? SelectedDetailsForFixture => Rows.SelectedItem is ConnectionDisplayRow row ? Details(row) : null;
    public double ConnectionRowMinWidth
    {
        get => (double)GetValue(ConnectionRowMinWidthProperty);
        set => SetValue(ConnectionRowMinWidthProperty, value);
    }

    public ConnectionsView()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
        Rows.PreviewKeyDown += OnRowKeyDown;
        ActionsDrawer.CollapsedChanged += hidden => ActionsHiddenChanged?.Invoke(hidden);
        Protocol.ItemsSource = new[] { Strings.AllEntries, "TCP", "UDP" };
        Protocol.SelectedIndex = 0;
        TopCount.ItemsSource = TopCountChoices.Items;
        Load(_settings);
        Strings.Text.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => Strings.Text.PropertyChanged -= OnLanguageChanged;
        Loaded += (_, _) =>
        {
            Strings.Text.PropertyChanged -= OnLanguageChanged;
            Strings.Text.PropertyChanged += OnLanguageChanged;
        };
        RefreshRows();
    }

    internal void Update(NetworkConnectionsFrame? frame, IReadOnlyList<ProcessUsage> processes, bool historical,
        ProcessNetworkFrame? network = null, bool networkEnabled = false)
    {
        _frame = frame;
        _processes = processes;
        _historical = historical;
        _network = network;
        var availabilityChanged = _networkEnabled != networkEnabled;
        _networkEnabled = networkEnabled;
        if (availabilityChanged) Load(_settings);
        TrafficPrompt.Visibility = !networkEnabled && !historical && NetworkSetupBroker.Supported
            ? Visibility.Visible : Visibility.Collapsed;
        BuildNetworkMembers();
        ReceiveHeaderColumn.Width = networkEnabled ? new GridLength(100) : new GridLength(0);
        SendHeaderColumn.Width = networkEnabled ? new GridLength(100) : new GridLength(0);
        ReceiveHeader.Visibility = networkEnabled ? Visibility.Visible : Visibility.Collapsed;
        SendHeader.Visibility = networkEnabled ? Visibility.Visible : Visibility.Collapsed;
        ConnectionHeaders.MinWidth = networkEnabled ? 650 : 450;
        ConnectionRowMinWidth = networkEnabled ? 668 : 468;
        RefreshRows();
    }

    internal void Load(ConnectionListSettings settings)
    {
        _loadingSettings = true;
        try
        {
            _settings = settings;
            TopCount.SelectedItem = TopCountChoices.Selection(TopCountChoices.IsSupported(settings.TopCount) ? settings.TopCount : 10);
            var help = NetworkSetupBroker.Supported ? Strings.ConnectionsTrafficRankingUnavailable :
                Strings.NetworkProcessMonitoringStoreUnavailable;
            Rank.ItemsSource = new[] { Strings.ConnectionsReceiveRate, Strings.ConnectionsSendRate,
                Strings.ConnectionsTcpCount, Strings.ConnectionsUdpCount }.Select((label, index) =>
            {
                var item = new ComboBoxItem { Content = label, IsEnabled = _networkEnabled || index >= 2,
                    ToolTip = !_networkEnabled && index < 2 ? help : null };
                ToolTipService.SetShowOnDisabled(item, true);
                return item;
            }).ToArray();
            Rank.ToolTip = _networkEnabled ? null : help;
            Rank.SelectedIndex = (int)EffectiveSort;
            AppsOnly.IsChecked = settings.AppsOnly;
        }
        finally { _loadingSettings = false; }
        RefreshRows();
    }

    internal void FocusSearch()
    {
        Search.Focus();
        Search.SelectAll();
    }

    internal bool HasOpenDropDown => Protocol.IsDropDownOpen || TopCount.IsDropDownOpen || Rank.IsDropDownOpen;
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
        _loadingSettings = true;
        try
        {
            TopCount.ItemsSource = TopCountChoices.Items;
        }
        finally { _loadingSettings = false; }
        Load(_settings);
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

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || !TopCountChoices.TryGetCount(TopCount.SelectedItem, out var top) || Rank.SelectedIndex < 0) return;
        if (!_networkEnabled && Rank.SelectedIndex < 2)
        {
            Load(_settings);
            return;
        }
        _settings = new ConnectionListSettings(top, ReferenceEquals(sender, Rank) ? (ConnectionSort)Rank.SelectedIndex : _settings.Sort,
            AppsOnly.IsChecked == true);
        SettingsChanged?.Invoke(_settings);
        RefreshRows();
    }

    private void OnSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox) OnSettingsChanged(sender, new RoutedEventArgs());
    }

    private void OnTrafficSettings(object sender, RoutedEventArgs e) => TrafficSettingsRequested?.Invoke();

    private ConnectionSort EffectiveSort => !_networkEnabled && _settings.Sort is ConnectionSort.Receive or ConnectionSort.Send
        ? ConnectionSort.Tcp : _settings.Sort;

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
                .Where(group => !_settings.AppsOnly || group.Members.Any(process => process.HasHadAppWindow || process.HasAppWindow == true))
                .Select(group => (Group: group, Network: _networkEnabled ? CollectNetworkSummary(NetworkMembers(group)) : NetworkSummary.Disabled))
                .OrderByDescending(item => SortValue(item.Group, item.Network))
                .ThenBy(item => DisplayName(item.Group.Process), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.Group.Key, StringComparer.Ordinal)
                .Take(_settings.TopCount == 0 ? int.MaxValue : _settings.TopCount);
            foreach (var item in groups)
            {
                var group = item.Group;
                var expanded = _expanded.Contains(group.Key);
                if (Grouped)
                {
                    var parent = Row(group.Key);
                    parent.UpdateGroup(group, expanded, item.Network);
                    desired.Add(parent);
                    if (!expanded) continue;
                }
                foreach (var (entry, key) in KeyedEntries(group.Entries))
                {
                    var child = Row(key);
                    child.UpdateConnection(entry, !Grouped, OwnerNetworkSummary(entry.Process));
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
        Notice.Text = frame is null ? (_historical ? Strings.ConnectionsHistoryUnavailable : Strings.ConnectionsLoading) :
            !frame.Available ? Strings.ConnectionsUnavailable :
            frame.PartiallyAvailable ? Strings.ConnectionsPartial :
            _historical ? Strings.ConnectionsHistoryNote :
            _rows.Count == 0 ? Strings.ConnectionsEmpty : NetworkStateText();
        Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_historical && frame?.Available == true)
            Notice.Text = string.Join(Environment.NewLine, new[] { Strings.ConnectionsHistoryNote,
                frame.PartiallyAvailable ? Strings.ConnectionsPartial : "", NetworkStateText() }
                .Where(text => text.Length > 0));
        UpdateActionBar();
    }

    private double SortValue(ConnectionGroup group, NetworkSummary network) => EffectiveSort switch
    {
        ConnectionSort.Receive => network.ReceiveRate ?? double.NegativeInfinity,
        ConnectionSort.Send => network.SendRate ?? double.NegativeInfinity,
        ConnectionSort.Tcp => group.Entries.Count(entry => entry.Connection.Protocol == TransportProtocol.Tcp),
        ConnectionSort.Udp => group.Entries.Count(entry => entry.Connection.Protocol == TransportProtocol.Udp),
        _ => double.NegativeInfinity
    };

    private NetworkSummary CollectNetworkSummary(IReadOnlyList<ProcessUsage> members)
    {
        if (!_networkEnabled) return NetworkSummary.Disabled;
        if (members.Count == 0 || _network is null) return NetworkSummary.Unavailable;
        var received = 0L;
        var sent = 0L;
        var found = 0;
        var rateCount = 0;
        var receiveRate = 0d;
        var sendRate = 0d;
        var ratesAvailable = true;
        foreach (var member in members)
        {
            if (!_networkReadings.TryGetValue(member.Identity, out var reading)) continue;
            found++;
            if (reading.ReceivedBytes < 0 || reading.SentBytes < 0) return NetworkSummary.Unavailable;
            received = SaturatingAdd(received, reading.ReceivedBytes);
            sent = SaturatingAdd(sent, reading.SentBytes);
            if (_network.CurrentLifetimes is not null && !_network.CurrentLifetimes.Contains(member.Identity)) continue;
            rateCount++;
            if (!ValidRate(reading.ReceiveBytesPerSecond) || !ValidRate(reading.SendBytesPerSecond) ||
                reading.ReceiveBytesPerSecond is null || reading.SendBytesPerSecond is null)
            {
                ratesAvailable = false;
                continue;
            }
            receiveRate += reading.ReceiveBytesPerSecond.Value;
            sendRate += reading.SendBytesPerSecond.Value;
        }
        if (found == 0) return NetworkSummary.Unavailable;
        ratesAvailable &= double.IsFinite(receiveRate) && double.IsFinite(sendRate);
        return new NetworkSummary(received, sent, ratesAvailable && rateCount > 0 ? receiveRate : null,
            ratesAvailable && rateCount > 0 ? sendRate : null,
            _network.Quality | (found == members.Count ? ProcessNetworkQuality.None : ProcessNetworkQuality.Incomplete), true, true);
    }

    private static bool ValidRate(double? rate) => rate is null || (double.IsFinite(rate.Value) && rate.Value >= 0);
    private static long SaturatingAdd(long total, long value) => value > long.MaxValue - total ? long.MaxValue : total + value;

    private NetworkSummary OwnerNetworkSummary(ProcessUsage? owner)
    {
        if (!_networkEnabled) return NetworkSummary.Disabled;
        if (owner is null || _network is null ||
            !_networkReadings.TryGetValue(owner.Identity, out var reading) || reading.ReceivedBytes < 0 || reading.SentBytes < 0)
            return NetworkSummary.Unavailable;
        var current = _network.CurrentLifetimes is null || _network.CurrentLifetimes.Contains(owner.Identity);
        var rates = !_network.Quality.HasFlag(ProcessNetworkQuality.Unavailable) && current && ValidRate(reading.ReceiveBytesPerSecond) && ValidRate(reading.SendBytesPerSecond) &&
            reading.ReceiveBytesPerSecond is not null && reading.SendBytesPerSecond is not null;
        return new NetworkSummary(reading.ReceivedBytes, reading.SentBytes,
            rates ? reading.ReceiveBytesPerSecond : null, rates ? reading.SendBytesPerSecond : null,
            _network.Quality, true, true);
    }

    private IReadOnlyList<ProcessUsage> NetworkMembers(ConnectionGroup group)
    {
        var path = group.Members.Select(NetworkOwner).FirstOrDefault(process => process?.ImagePath.Length > 0)?.ImagePath;
        return path is not null && _networkMembersByPath.TryGetValue(path, out var members) ? members : group.Members;
    }

    private void BuildNetworkMembers()
    {
        _networkMembersByPath.Clear();
        _networkReadings.Clear();
        if (!_networkEnabled) return;
        foreach (var reading in _network?.Processes ?? [])
        {
            var identity = new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime);
            if (!_networkReadings.ContainsKey(identity)) _networkReadings.Add(identity, reading);
        }
        var known = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        foreach (var owner in _network?.Owners ?? []) known[owner.Identity] = owner;
        foreach (var process in _processes)
        {
            if (!known.TryGetValue(process.Identity, out var owner) || process.ImagePath.Length > 0 || owner.ImagePath.Length == 0)
                known[process.Identity] = process;
        }
        foreach (var group in known.Values.Where(process => process.ImagePath.Length > 0)
                     .GroupBy(process => process.ImagePath, ProcessIdentityPolicy.Windows.Comparer))
            _networkMembersByPath.Add(group.Key, group.ToArray());
    }

    private ProcessUsage? NetworkOwner(ProcessUsage process) => process.ImagePath.Length > 0 ? process :
        _network?.Owners.FirstOrDefault(owner => owner.Identity == process.Identity && owner.ImagePath.Length > 0) ?? process;

    private string NetworkStateText()
    {
        if (!_networkEnabled) return _settings.Sort is ConnectionSort.Receive or ConnectionSort.Send
            ? Strings.NetworkProcessMonitoringDisabled : "";
        var quality = _network?.Quality ?? ProcessNetworkQuality.WarmingUp;
        return quality.HasFlag(ProcessNetworkQuality.Unavailable) ? Strings.NetworkProcessMonitoringUnavailable :
            quality.HasFlag(ProcessNetworkQuality.WarmingUp) ? Strings.NetworkProcessMonitoringWarmingUp :
            quality.HasFlag(ProcessNetworkQuality.Incomplete) || quality.HasFlag(ProcessNetworkQuality.Discontinuity) ||
            quality.HasFlag(ProcessNetworkQuality.CapacityExceeded) ? Strings.NetworkProcessMonitoringIncomplete :
            quality.HasFlag(ProcessNetworkQuality.Provisional) ? Strings.ConnectionsNetworkProvisional : "";
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
        if (e.Key == Key.Enter && Rows.SelectedItem is ConnectionDisplayRow { Connections.Count: > 0 } selected)
        {
            PropertiesRequested?.Invoke(selected.Name, Details(selected));
            e.Handled = true;
            return;
        }
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
    private void OnRowsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0)
            HeaderScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
    }
    private void UpdateActionBar()
    {
        var row = Rows.SelectedItem as ConnectionDisplayRow;
        SelectionName.Text = row?.Connection is not null ? DisplayName(row.Process) : row?.Name ?? Strings.ConnectionsTab;
        SelectionDetail.Text = row?.Detail ?? "";
        SelectionIcon.Source = row?.Icon;
        SelectionIcon.Visibility = row?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        var networkDetails = NetworkDetails(row?.NetworkSummary ?? NetworkSummary.Disabled);
        SelectionNetwork.Text = string.Join(Environment.NewLine,
            networkDetails.Split(Environment.NewLine).Take(3));
        SelectionNetwork.ToolTip = networkDetails;
        SelectionNetwork.Visibility = SelectionNetwork.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ActionsDrawer.Summarize(SelectionName.Text, string.Join(Environment.NewLine,
            new[] { SelectionDetail.Text, SelectionNetwork.Text }.Where(text => text.Length > 0)), row is not null);
        CopyDetails.IsEnabled = row is { Connections.Count: > 0 } && _frame?.Available == true;
        Properties.IsEnabled = CopyDetails.IsEnabled;
        ShowProcess.IsEnabled = row?.Connection is not null
            ? row.Process is { } process && (CanShowInProcesses?.Invoke(process) ?? true)
            : row?.Members.Any(member => CanShowInProcesses?.Invoke(member) ?? true) == true;
    }
    private void OnProperties(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is ConnectionDisplayRow { Connections.Count: > 0 } row)
            PropertiesRequested?.Invoke(row.Name, Details(row));
    }
    private void OnCopyDetails(object sender, RoutedEventArgs e)
    {
        if (Rows.SelectedItem is ConnectionDisplayRow { Connections.Count: > 0 } row)
            CopyRequested?.Invoke(Details(row));
    }
    private void OnShowProcess(object sender, RoutedEventArgs e)
    {
        if (!ShowProcess.IsEnabled) return;
        if (Rows.SelectedItem is not ConnectionDisplayRow row) return;
        if (row.Connection is not null && row.Process is { } process)
            ProcessRequested?.Invoke(process);
        else if (row.IsGroup && row.Members.Count > 0)
            GroupRequested?.Invoke(row.Members);
    }
    private static string Details(ConnectionDisplayRow row)
    {
        if (row.Connection is { } selectedConnection) return Details(selectedConnection, row.Process, row.NetworkSummary);
        var text = new StringBuilder();
        text.AppendLine(row.Name);
        text.AppendLine($"PID: {string.Join(", ", row.Connections.Select(connection => connection.ProcessId).Distinct())}");
        AppendProcessPath(text, row.Process);
        text.AppendLine($"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}");
        foreach (var endpoint in row.Connections)
            text.AppendLine($"{endpoint.ProcessId}\t{ProtocolText(endpoint.Protocol)}\t" +
                $"{Endpoint(endpoint.LocalEndpoint)}\t" +
                $"{(endpoint.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}\t" +
                StateText(endpoint.State));
        AppendNetworkDetails(text, row.NetworkSummary);
        return text.ToString();
    }
    private static string Details(NetworkConnection c, ProcessUsage? process, NetworkSummary network)
    {
        var text = new StringBuilder(string.Join(Environment.NewLine, [DisplayName(process), $"PID: {c.ProcessId.ToString(CultureInfo.InvariantCulture)}", $"{Strings.ConnectionsProtocol}: {ProtocolText(c.Protocol)}", $"{Strings.ConnectionsLocalEndpoint}: {Endpoint(c.LocalEndpoint)}", $"{Strings.ConnectionsRemoteEndpoint}: {(c.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}", $"{Strings.ConnectionsState}: {StateText(c.State)}"]));
        text.AppendLine();
        AppendProcessPath(text, process);
        AppendNetworkDetails(text, network);
        return text.ToString();
    }
    private static void AppendProcessPath(StringBuilder text, ProcessUsage? process) =>
        text.AppendLine($"{Strings.ProcessPathLabel}: {(process?.ImagePath is { Length: > 0 } path ? path : Strings.ExecutablePathUnavailable)}");
    private static void AppendNetworkDetails(StringBuilder text, NetworkSummary network)
    {
        var details = NetworkDetails(network);
        if (details.Length == 0) return;
        text.AppendLine();
        text.Append(details);
    }
    internal static string NetworkDetails(NetworkSummary network)
    {
        if (!network.Enabled) return "";
        var quality = network.Quality.HasFlag(ProcessNetworkQuality.Unavailable) ? Strings.NetworkProcessMonitoringUnavailable :
            network.Quality.HasFlag(ProcessNetworkQuality.Incomplete) || network.Quality.HasFlag(ProcessNetworkQuality.Discontinuity) ||
            network.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded) ? Strings.NetworkProcessMonitoringIncomplete :
            network.Quality.HasFlag(ProcessNetworkQuality.WarmingUp) ? Strings.NetworkProcessMonitoringWarmingUp :
            network.Quality.HasFlag(ProcessNetworkQuality.Provisional) ? Strings.ConnectionsNetworkProvisional : "";
        return string.Join(Environment.NewLine, new[]
        {
            Strings.ConnectionsNetworkOwnerTotals,
            $"↓ {NetworkRate(network.RatesAvailable ? network.ReceiveRate : null)}  ↑ {NetworkRate(network.RatesAvailable ? network.SendRate : null)}",
            $"{Strings.DetailReceivedSent}: {(network.HasTotals ? $"{UsageText.Bytes(network.ReceivedBytes)} / {UsageText.Bytes(network.SentBytes)}" : Strings.MissingReading)}",
            quality
        }.Where(text => text.Length > 0));
    }
    internal static string NetworkRate(double? value) => value is { } rate ? TableMetricText.IoRate(rate) : Strings.MissingReading;
    internal static string Endpoint(IPEndPoint endpoint) => endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{endpoint.Address}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}" : $"{endpoint.Address}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    internal static string ProtocolText(TransportProtocol protocol) => protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
    internal static string StateText(TcpState? state) => state switch { TcpState.Closed => Strings.ConnectionsTcpStateClosed, TcpState.Listen => Strings.ConnectionsTcpStateListen, TcpState.SynSent => Strings.ConnectionsTcpStateSynSent, TcpState.SynReceived => Strings.ConnectionsTcpStateSynReceived, TcpState.Established => Strings.ConnectionsTcpStateEstablished, TcpState.FinWait1 => Strings.ConnectionsTcpStateFinWait1, TcpState.FinWait2 => Strings.ConnectionsTcpStateFinWait2, TcpState.CloseWait => Strings.ConnectionsTcpStateCloseWait, TcpState.Closing => Strings.ConnectionsTcpStateClosing, TcpState.LastAck => Strings.ConnectionsTcpStateLastAck, TcpState.TimeWait => Strings.ConnectionsTcpStateTimeWait, TcpState.DeleteTcb => Strings.ConnectionsTcpStateDeleteTcb, _ => Strings.MissingReading };
    internal sealed record ConnectionEntry(NetworkConnection Connection, ProcessUsage? Process, string GroupKey);
    internal readonly record struct NetworkSummary(long ReceivedBytes, long SentBytes, double? ReceiveRate,
        double? SendRate, ProcessNetworkQuality Quality, bool Enabled, bool HasTotals)
    {
        internal static NetworkSummary Disabled { get; } = new(0, 0, null, null, ProcessNetworkQuality.None, false, false);
        internal static NetworkSummary Unavailable { get; } = new(0, 0, null, null, ProcessNetworkQuality.Unavailable, true, false);
        internal bool RatesAvailable => HasTotals && !Quality.HasFlag(ProcessNetworkQuality.Unavailable);
    }
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
    public string Network { get; private set; } = "";
    public string Receive { get; private set; } = "";
    public string Send { get; private set; } = "";
    public string NetworkToolTip { get; private set; } = "";
    public GridLength NetworkColumnWidth { get; private set; }
    public GridLength ReceiveColumnWidth { get; private set; }
    public GridLength SendColumnWidth { get; private set; }
    public Visibility NetworkVisibility { get; private set; } = Visibility.Collapsed;
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
    internal ConnectionsView.NetworkSummary NetworkSummary { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void UpdateGroup(ConnectionsView.ConnectionGroup group, bool expanded, ConnectionsView.NetworkSummary network)
    {
        var shown = Shown();
        IsGroup = true;
        Process = group.Process;
        Connection = null;
        Connections = group.Entries.Select(entry => entry.Connection).ToArray();
        Members = group.Members;
        NetworkSummary = network;
        Name = DisplayName(Process);
        Detail = Members.Count > 1
            ? Strings.Format(Strings.ConnectionsProcessCount, Members.Count)
            : $"PID {group.Entries[0].Connection.ProcessId.ToString(CultureInfo.InvariantCulture)}";
        Local = LocalToolTip = Remote = RemoteToolTip = State = StateToolTip = "";
        Tcp = Connections.Count(connection => connection.Protocol == TransportProtocol.Tcp).ToString(CultureInfo.CurrentCulture);
        Udp = Connections.Count(connection => connection.Protocol == TransportProtocol.Udp).ToString(CultureInfo.CurrentCulture);
        SetNetwork(network, true);
        AccessibleName = $"{Name}, {Detail}, {Tcp} TCP, {Udp} UDP" +
            (network.Enabled ? $", {Strings.ConnectionsNetworkOwnerTotals}, {Network}" : "");
        ExpandGlyph = expanded ? "⌄" : "›";
        ExpandLabel = Strings.Format(expanded ? Strings.ConnectionsCollapse : Strings.ConnectionsExpand, Name);
        ExpandVisibility = Visibility.Visible;
        IconVisibility = Visibility.Visible;
        Indent = new Thickness(0, 4, 8, 4);
        MinimumHeight = 46;
        EnsureIcon();
        if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    internal void UpdateConnection(ConnectionsView.ConnectionEntry entry, bool individual, ConnectionsView.NetworkSummary network)
    {
        var shown = Shown(); IsGroup = false; Process = entry.Process; Connection = entry.Connection; Connections = [entry.Connection]; Members = entry.Process is null ? [] : [entry.Process]; NetworkSummary = network;
        Name = DisplayName(Process); Detail = Strings.Format(Strings.ConnectionsEndpointProtocol, $"PID {entry.Connection.ProcessId.ToString(CultureInfo.InvariantCulture)}", ConnectionsView.ProtocolText(entry.Connection.Protocol)) +
            $"  ·  {(entry.Connection.RemoteEndpoint is { } endpoint ? ConnectionsView.Endpoint(endpoint) : Strings.MissingReading)}  ·  {ConnectionsView.StateText(entry.Connection.State)}";
        Local = LocalToolTip = ConnectionsView.Endpoint(entry.Connection.LocalEndpoint); Remote = RemoteToolTip = entry.Connection.RemoteEndpoint is { } remote ? ConnectionsView.Endpoint(remote) : Strings.MissingReading; State = StateToolTip = ConnectionsView.StateText(entry.Connection.State);
        AccessibleName = $"{Name}, {Detail}, {Remote}, {State}"; Tcp = Udp = ""; SetNetwork(network, false); ExpandGlyph = ExpandLabel = ""; ExpandVisibility = Visibility.Collapsed; IconVisibility = Visibility.Visible; Indent = new Thickness(individual ? 0 : 30, 3, 8, 3); MinimumHeight = 32; EnsureIcon(); if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
    private void SetNetwork(ConnectionsView.NetworkSummary network, bool summary)
    {
        NetworkColumnWidth = network.Enabled ? new GridLength(172) : new GridLength(0);
        ReceiveColumnWidth = network.Enabled ? new GridLength(100) : new GridLength(0);
        SendColumnWidth = network.Enabled ? new GridLength(100) : new GridLength(0);
        NetworkVisibility = NetworkColumnWidth.Value == 0 ? Visibility.Collapsed : Visibility.Visible;
        Network = summary && network.Enabled ? $"↓ {ConnectionsView.NetworkRate(network.RatesAvailable ? network.ReceiveRate : null)}  ↑ {ConnectionsView.NetworkRate(network.RatesAvailable ? network.SendRate : null)}" : "";
        Receive = summary && network.Enabled ? ConnectionsView.NetworkRate(network.RatesAvailable ? network.ReceiveRate : null) : "";
        Send = summary && network.Enabled ? ConnectionsView.NetworkRate(network.RatesAvailable ? network.SendRate : null) : "";
        NetworkToolTip = summary && network.Enabled ? ConnectionsView.NetworkDetails(network) : "";
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
        string, string, Visibility, Visibility, Thickness, double, string, string, string, string, GridLength, GridLength, GridLength, Visibility) Shown() =>
        (Name, Detail, Local, LocalToolTip, Remote, RemoteToolTip, State, StateToolTip, AccessibleName,
            Tcp, Udp, ExpandGlyph, ExpandLabel, ExpandVisibility, IconVisibility, Indent, MinimumHeight,
            Network, NetworkToolTip, Receive, Send, NetworkColumnWidth, ReceiveColumnWidth, SendColumnWidth, NetworkVisibility);
}
