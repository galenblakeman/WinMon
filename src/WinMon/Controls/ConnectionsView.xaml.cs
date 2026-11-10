// Nested data members retain native or binding names; outer static members are qualified at call sites.
// ReSharper disable MemberHidesStaticFromOuterClass

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

internal enum ConnectionSort
{
    Receive,
    Send,
    Tcp,
    Udp,
    Received,
    Sent
}

internal sealed record ConnectionListSettings(
    int TopCount = 10,
    ConnectionSort Sort = ConnectionSort.Receive,
    bool AppsOnly = false,
    bool PinnedOnly = false,
    int TrafficSeconds = 300);

public partial class ConnectionsView
{
    public static readonly DependencyProperty ConnectionRowMinWidthProperty = DependencyProperty.Register(
        nameof(ConnectionRowMinWidth), typeof(double), typeof(ConnectionsView), new PropertyMetadata(758d));

    private readonly ObservableCollection<ConnectionDisplayRow> _rows = [];
    private readonly Dictionary<string, ConnectionDisplayRow> _cache = new(StringComparer.Ordinal);
    internal ProcessPins Pins { get; } = new();
    internal event Action? PinsChanged;
    internal SavedProcessPin[] SavedPins => Pins.Snapshot();
    private NetworkConnectionsFrame? _frame;
    private IReadOnlyList<ProcessUsage> _processes = [];
    private ProcessNetworkFrame? _network;
    private bool _networkEnabled;

    private readonly Dictionary<string, IReadOnlyList<ProcessUsage>> _networkMembersByPath =
        new(ProcessIdentityPolicy.Windows.Comparer);

    private readonly Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> _networkReadings = [];
    private bool _historical;
    private IReadOnlyList<ConnectionCapture> _history = [];
    private ConnectionTrafficTotals? _trafficTotals;
    private ConnectionCapture? _capture;
    private bool _refreshing;
    private bool _loadingSettings;
    private string? _selectedKey;
    private ConnectionListSettings _settings = new();

    internal event Action<string>? CopyRequested;
    internal event Action<IReadOnlyList<ProcessUsage>>? GroupRequested;
    internal event Action<bool>? ActionsHiddenChanged;
    internal event Action<ConnectionListSettings>? SettingsChanged;
    internal event Action<string, string>? PropertiesRequested;
    internal event Action? TrafficSettingsRequested;
    internal Func<ProcessUsage, bool>? CanShowInProcesses { get; set; }

    internal bool ActionsHidden
    {
        // Keep the same readable drawer state contract as the other activity views.
        // ReSharper disable once UnusedMember.Global
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
        ProcessNetworkFrame? network = null, bool networkEnabled = false,
        IReadOnlyList<ConnectionCapture>? history = null, ConnectionCapture? capture = null,
        ConnectionTrafficTotals? trafficTotals = null)
    {
        _frame = frame;
        _processes = processes;
        _historical = historical;
        _history = history ?? [];
        _capture = capture;
        _trafficTotals = trafficTotals;
        _network = network;
        var availabilityChanged = _networkEnabled != networkEnabled;
        _networkEnabled = networkEnabled;
        if (availabilityChanged) Load(_settings);
        TrafficPrompt.Visibility = !networkEnabled && !historical && NetworkSetupBroker.Supported
            ? Visibility.Visible
            : Visibility.Collapsed;
        BuildNetworkMembers();
        ReceiveHeaderColumn.Width = networkEnabled ? new GridLength(80) : new GridLength(0);
        SendHeaderColumn.Width = networkEnabled ? new GridLength(80) : new GridLength(0);
        ReceivedHeaderColumn.Width = SentHeaderColumn.Width = networkEnabled ? new GridLength(82) : new GridLength(0);
        ReceivedHeader.Visibility = SentHeader.Visibility = networkEnabled ? Visibility.Visible : Visibility.Collapsed;
        ReceiveHeader.Visibility = networkEnabled ? Visibility.Visible : Visibility.Collapsed;
        SendHeader.Visibility = networkEnabled ? Visibility.Visible : Visibility.Collapsed;
        ConnectionHeaders.MinWidth = networkEnabled ? 780 : 595;
        ConnectionRowMinWidth = networkEnabled ? 798 : 613;
        RefreshRows();
    }

    internal void RestorePins(IEnumerable<SavedProcessPin> pins)
    {
        Pins.Restore(pins);
        RefreshRows();
    }

    internal void Load(ConnectionListSettings settings)
    {
        _loadingSettings = true;
        try
        {
            _settings = settings;
            TopCount.SelectedItem =
                TopCountChoices.Selection(TopCountChoices.IsSupported(settings.TopCount) ? settings.TopCount : 10);
            var help = NetworkSetupBroker.Supported
                ? Strings.ConnectionsTrafficRankingUnavailable
                : Strings.NetworkProcessMonitoringStoreUnavailable;
            Rank.ItemsSource = new[]
            {
                Strings.ConnectionsReceiveRate, Strings.ConnectionsSendRate,
                Strings.ConnectionsTcpCount, Strings.ConnectionsUdpCount,
                Strings.ConnectionsReceived, Strings.ConnectionsSent
            }.Select((label, index) =>
            {
                var item = new ComboBoxItem
                {
                    Content = label, IsEnabled = _networkEnabled || !IsTrafficSort((ConnectionSort)index),
                    ToolTip = !_networkEnabled && IsTrafficSort((ConnectionSort)index) ? help : null
                };
                ToolTipService.SetShowOnDisabled(item, true);
                return item;
            }).ToArray();
            Rank.ToolTip = _networkEnabled ? Strings.ConnectionsRankingHelp : help;
            AutomationProperties.SetHelpText(Rank, (string)Rank.ToolTip);
            Rank.SelectedIndex = (int)EffectiveSort;
            AppsOnly.IsChecked = settings.AppsOnly;
            PinnedOnly.IsChecked = settings.PinnedOnly;
            TrafficPeriod.ItemsSource = new[]
            {
                Strings.ConnectionsLastMinute, Strings.ConnectionsLastFiveMinutes,
                Strings.ConnectionsLastThirtyMinutes, Strings.ConnectionsSinceStart
            };
            TrafficPeriod.SelectedIndex = settings.TrafficSeconds switch { 60 => 0, 1800 => 2, 0 => 3, _ => 1 };
        }
        finally
        {
            _loadingSettings = false;
        }

        RefreshRows();
    }

    internal void FocusSearch()
    {
        Search.Focus();
        Search.SelectAll();
    }

    internal bool HasOpenDropDown => TopCount.IsDropDownOpen || Rank.IsDropDownOpen || TrafficPeriod.IsDropDownOpen;

    internal bool ClearSearch()
    {
        if (!Search.IsKeyboardFocusWithin || Search.Text.Length == 0) return false;
        Search.Clear();
        return true;
    }

    internal void SelectConnectionForFixture(int processId, long creationTicks)
    {
        var entry = Entries().FirstOrDefault(entry => entry.Connection.ProcessId == processId &&
                                                      entry.Connection.ProcessCreationTicks == creationTicks);
        if (entry is null) return;
        Rows.SelectedItem = _rows.FirstOrDefault(row => row.Key == entry.GroupKey);
    }

    internal bool ShowApp(ProcessUsage process)
    {
        var entry = Entries().FirstOrDefault(entry => entry.Process is { ImagePath.Length: > 0 } owner &&
                process.ImagePath.Length > 0 && ProcessIdentityPolicy.Windows.Comparer.Equals(owner.ImagePath, process.ImagePath) ||
            entry.Connection.ProcessId == process.ProcessId && entry.Connection.ProcessCreationTicks == process.CreationTicks);
        if (entry is null) return false;
        AppsOnly.IsChecked = false;
        PinnedOnly.IsChecked = false;
        Search.Clear();
        RefreshRows();
        var row = _rows.FirstOrDefault(row => row.Key == entry.GroupKey);
        if (row is null)
        {
            TopCount.SelectedItem = TopCountChoices.Selection(0);
            RefreshRows();
            row = _rows.FirstOrDefault(row => row.Key == entry.GroupKey);
        }
        if (row is null) return false;
        Rows.SelectedItem = row;
        Rows.ScrollIntoView(row);
        return true;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        _loadingSettings = true;
        try
        {
            TopCount.ItemsSource = TopCountChoices.Items;
        }
        finally
        {
            _loadingSettings = false;
        }

        Load(_settings);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ClearConnectionSearchButton.Visibility = Search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshRows();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        Search.Focus();
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings || !TopCountChoices.TryGetCount(TopCount.SelectedItem, out var top) ||
            Rank.SelectedIndex < 0) return;
        if (!_networkEnabled && IsTrafficSort((ConnectionSort)Rank.SelectedIndex))
        {
            Load(_settings);
            return;
        }

        _settings = new ConnectionListSettings(top,
            ReferenceEquals(sender, Rank) ? (ConnectionSort)Rank.SelectedIndex : _settings.Sort,
            AppsOnly.IsChecked == true, PinnedOnly.IsChecked == true,
            TrafficPeriod.SelectedIndex switch { 0 => 60, 2 => 1800, 3 => 0, _ => 300 });
        _trafficTotals = _networkEnabled
            ? ConnectionTrafficTotals.Build(_history, _capture, _settings.TrafficSeconds)
            : null;
        SettingsChanged?.Invoke(_settings);
        RefreshRows();
    }

    private void OnSettingsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox) OnSettingsChanged(sender, new RoutedEventArgs());
    }

    private void OnTrafficSettings(object sender, RoutedEventArgs e)
    {
        TrafficSettingsRequested?.Invoke();
    }

    private void OnTrafficPromptSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 640;
        Grid.SetColumnSpan(TrafficPromptText, narrow ? 2 : 1);
        Grid.SetRow(EnableTrafficMonitoring, narrow ? 1 : 0);
        Grid.SetColumn(EnableTrafficMonitoring, narrow ? 0 : 1);
        EnableTrafficMonitoring.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        EnableTrafficMonitoring.Margin = narrow ? new Thickness(0, 8, 0, 0) : new Thickness(0);
    }

    private static bool IsTrafficSort(ConnectionSort sort)
    {
        return sort is ConnectionSort.Receive or ConnectionSort.Send or ConnectionSort.Received or ConnectionSort.Sent;
    }

    private ConnectionSort EffectiveSort => !_networkEnabled && IsTrafficSort(_settings.Sort)
        ? ConnectionSort.Tcp
        : _settings.Sort;

    private void RefreshRows()
    {
        var frame = _frame;
        var entries = frame?.Available == true ? Entries() : [];
        var desired = new List<ConnectionDisplayRow>();
        var query = Search.Text.Trim();
        var groups = entries.GroupBy(GroupKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => ConnectionGroup.Create(group.Key, group)).ToArray();
        var pinnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pins = Pins.Snapshot();
        if (pins.Any(pin => !pin.Grouped))
        {
            var resolved = pins.Select(pin =>
            {
                var owner = !pin.Grouped
                    ? _processes.FirstOrDefault(process =>
                        process.Identity ==
                        new ProcessLifetimeIdentity(pin.ProcessId, pin.CreationTicks, pin.LifetimeDomain) &&
                        process.ImagePath.Length > 0)
                    : null;
                return owner is null
                    ? pin
                    : pin with
                    {
                        Grouped = true, ImagePath = owner.ImagePath,
                        Name = owner.Name, AppName = owner.AppName, HasAppWindow = owner.HasAppWindow
                    };
            }).ToArray();
            if (!pins.SequenceEqual(resolved))
            {
                Pins.Restore(resolved);
                pins = Pins.Snapshot();
                PinsChanged?.Invoke();
            }
        }

        foreach (var pin in pins)
        {
            var saved = new ProcessUsage(pin.ProcessId, pin.CreationTicks, pin.Name, 0)
            {
                ImagePath = pin.ImagePath, AppName = pin.AppName, HasAppWindow = pin.HasAppWindow,
                LifetimeDomain = pin.LifetimeDomain
            };
            var key = OwnerKey(saved);
            pinnedKeys.Add(key);
            var group = groups.FirstOrDefault(group => group.Key == key) ?? new ConnectionGroup(key, [], [], saved);
            if (group.Process is null) group = group with { Process = saved };
            if (!Visible(group, query)) continue;
            Add(group, true);
        }

        if (!_settings.PinnedOnly && frame?.Available == true)
            foreach (var item in groups.Where(group => !pinnedKeys.Contains(group.Key) && Visible(group, query))
                         .Select(group => (Group: group,
                             Network: _networkEnabled
                                 ? CollectNetworkSummary(NetworkMembers(group))
                                 : NetworkSummary.Disabled))
                         .OrderByDescending(item => SortValue(item.Group, item.Network))
                         .ThenBy(item => DisplayName(item.Group.Process), StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(item => item.Group.Key, StringComparer.Ordinal)
                         .Take(_settings.TopCount == 0 ? int.MaxValue : _settings.TopCount))
                Add(item.Group, false, item.Network);
        PinnedCountLabel.Text = Strings.Format(Strings.PinnedProcessCount, pins.Length);
        PinSummary.Visibility = pins.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool Visible(ConnectionGroup group, string text)
        {
            return (Matches(group, text) || (group.Entries.Count == 0 && (text.Length == 0 ||
                                                                          DisplayName(group.Process).Contains(text,
                                                                              StringComparison
                                                                                  .CurrentCultureIgnoreCase) ||
                                                                          group.Process!.ProcessId
                                                                              .ToString(CultureInfo.InvariantCulture)
                                                                              .Contains(text,
                                                                                  StringComparison.Ordinal)))) &&
                   (!_settings.AppsOnly || group.Members.Append(group.Process).Any(process =>
                       process is not null && (process.HasHadAppWindow || process.HasAppWindow == true)));
        }

        void Add(ConnectionGroup group, bool pinned, NetworkSummary? summary = null)
        {
            var row = Row(group.Key);
            var status = group.Entries.Count > 0 ? null :
                frame?.Available != true ? Strings.Unavailable :
                _historical ? Strings.ProcessNotRecorded : Strings.ConnectionsNoObservedEndpoints;
            row.UpdateGroup(group, pinned, summary ?? (_networkEnabled
                ? CollectNetworkSummary(NetworkMembers(group))
                : NetworkSummary.Disabled), status);
            desired.Add(row);
        }

        var graphHistory = _history.Where(capture => _capture is null || _settings.TrafficSeconds == 0 ||
                                                     CpuGraph.Moment(capture.Sample) >=
                                                     CpuGraph.Moment(_capture.Sample) - _settings.TrafficSeconds)
            .ToArray();
        var graphs = _networkEnabled
            ? ConnectionGraphData.Build(graphHistory, desired)
            : null;
        var firstRanked = desired.Any(row => row.Pinned) ? desired.FirstOrDefault(row => !row.Pinned) : null;
        foreach (var row in desired)
        {
            row.UpdateTraffic(_trafficTotals, NetworkMembersForRow(row));
            row.SetRankedLabel(ReferenceEquals(row, firstRanked)
                ? Strings.Format(Strings.RankedByMetric, ((ComboBoxItem)Rank.SelectedItem).Content)
                : "");
            row.Graph.Visibility = _networkEnabled && row.Process is not null
                ? Visibility.Visible
                : Visibility.Hidden;
            if (row.Graph.Visibility != Visibility.Visible) continue;
            row.Graph.SetProcessData(graphs![ConnectionGraphData.Key(row.Process!)],
                _settings.TrafficSeconds == 0
                    ? (int)Math.Max(1, _trafficTotals?.CoveredSeconds ?? MonitorPreferences.ShortRangeSeconds)
                    : _settings.TrafficSeconds, row.Process!);
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
            var valid = entries.Select(entry => entry.GroupKey).Concat(pinnedKeys).ToHashSet(StringComparer.Ordinal);
            foreach (var key in _cache.Keys.Where(key => !valid.Contains(key)).ToArray()) _cache.Remove(key);
        }

        Notice.Text = frame is null ? _historical ? Strings.ConnectionsHistoryUnavailable : Strings.ConnectionsLoading :
            !frame.Available ? Strings.ConnectionsUnavailable :
            frame.PartiallyAvailable ? Strings.ConnectionsPartial :
            _historical ? Strings.ConnectionsHistoryNote :
            _rows.Count == 0 ? Strings.ConnectionsEmpty : NetworkStateText();
        Notice.Visibility = _rows.Count == 0 && Notice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_historical && frame?.Available == true)
            Notice.Text = string.Join(Environment.NewLine, new[]
                {
                    Strings.ConnectionsHistoryNote,
                    frame.PartiallyAvailable ? Strings.ConnectionsPartial : "", NetworkStateText()
                }
                .Where(text => text.Length > 0));
        UpdateActionBar();
    }

    private IReadOnlyList<ProcessUsage> NetworkMembersForRow(ConnectionDisplayRow row)
    {
        return row.Process?.ImagePath is { Length: > 0 } path &&
               _networkMembersByPath.TryGetValue(path, out var members)
            ? members
            : row.Members;
    }

    private double SortValue(ConnectionGroup group, NetworkSummary network)
    {
        return EffectiveSort switch
        {
            ConnectionSort.Receive => network.ReceiveRate ?? double.NegativeInfinity,
            ConnectionSort.Send => network.SendRate ?? double.NegativeInfinity,
            ConnectionSort.Tcp => group.Entries.Count(entry => entry.Connection.Protocol == TransportProtocol.Tcp),
            ConnectionSort.Udp => group.Entries.Count(entry => entry.Connection.Protocol == TransportProtocol.Udp),
            ConnectionSort.Received or ConnectionSort.Sent => TrafficSortValue(group),
            _ => double.NegativeInfinity
        };
    }

    private double TrafficSortValue(ConnectionGroup group)
    {
        if (_trafficTotals?.Available != true) return double.NegativeInfinity;
        var identities = NetworkMembers(group).Select(member => member.Identity).ToArray();
        if (!identities.Any(identity => _trafficTotals.Processes.ContainsKey(identity)))
            return double.NegativeInfinity;
        var totals = _trafficTotals.Sum(identities);
        return EffectiveSort == ConnectionSort.Received ? totals.Received : totals.Sent;
    }

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
            _network.Quality | (found == members.Count ? ProcessNetworkQuality.None : ProcessNetworkQuality.Incomplete),
            true, true);
    }

    private static bool ValidRate(double? rate)
    {
        return rate is null || (double.IsFinite(rate.Value) && rate.Value >= 0);
    }

    private static long SaturatingAdd(long total, long value)
    {
        return value > long.MaxValue - total ? long.MaxValue : total + value;
    }

    private IReadOnlyList<ProcessUsage> NetworkMembers(ConnectionGroup group)
    {
        var path = group.Members.Append(group.Process).OfType<ProcessUsage>().Select(NetworkOwner)
            .FirstOrDefault(process => process.ImagePath.Length > 0)?.ImagePath;
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
            if (!known.TryGetValue(process.Identity, out var owner) || process.ImagePath.Length > 0 ||
                owner.ImagePath.Length == 0)
                known[process.Identity] = process;
        foreach (var group in known.Values.Where(process => process.ImagePath.Length > 0)
                     .GroupBy(process => process.ImagePath, ProcessIdentityPolicy.Windows.Comparer))
            _networkMembersByPath.Add(group.Key, group.ToArray());
    }

    private ProcessUsage NetworkOwner(ProcessUsage process)
    {
        return process.ImagePath.Length > 0
            ? process
            : _network?.Owners.FirstOrDefault(owner =>
                owner.Identity == process.Identity && owner.ImagePath.Length > 0) ?? process;
    }

    private string NetworkStateText()
    {
        if (!_networkEnabled)
            return IsTrafficSort(_settings.Sort)
                ? Strings.NetworkProcessMonitoringDisabled
                : "";
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

        var connections = _frame.Connections;
        var entries = new ConnectionEntry[connections.Count];
        for (var index = 0; index < entries.Length; index++)
        {
            var connection = connections[index];
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
            else
            {
                groupKey = LifetimeKey(connection.ProcessId, connection.ProcessCreationTicks);
            }

            entries[index] = new ConnectionEntry(connection, owner, groupKey);
        }

        return entries;
    }

    private static bool Matches(ConnectionGroup group, string query)
    {
        return group.Entries.Any(entry => Matches(entry, query));
    }

    private static bool Matches(ConnectionEntry entry, string query)
    {
        return query.Length == 0 ||
               DisplayName(entry.Process).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               entry.Connection.ProcessId.ToString(CultureInfo.InvariantCulture)
                   .Contains(query, StringComparison.Ordinal) ||
               Endpoint(entry.Connection.LocalEndpoint).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (entry.Connection.RemoteEndpoint is { } remote &&
                Endpoint(remote).Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string DisplayName(ProcessUsage? process)
    {
        return process?.AppName is { Length: > 0 } name ? name :
            process?.Name is { Length: > 0 } executable ? executable : Strings.ConnectionsUnknownProcess;
    }

    private static string GroupKey(ConnectionEntry entry)
    {
        return entry.GroupKey;
    }

    private static string OwnerKey(ProcessUsage owner)
    {
        return owner.ImagePath.Length > 0
            ? "image:" + ProcessRanking.GroupKey(owner).ToUpperInvariant()
            : LifetimeKey(owner.ProcessId, owner.CreationTicks);
    }

    private static string LifetimeKey(int pid, long? ticks)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"lifetime:{pid}:{ticks?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
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
        if (e.OriginalSource is Button) return;
        if (e.Key == Key.Enter && Rows.SelectedItem is ConnectionDisplayRow { Connections.Count: > 0 } selected)
        {
            PropertiesRequested?.Invoke(selected.Name, Details(selected));
            e.Handled = true;
            return;
        }
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConnectionDisplayRow { Process: { } process }) return;
        TogglePin(process);
        e.Handled = true;
    }

    internal void TogglePin(ProcessUsage process)
    {
        Pins.Toggle(process, process.ImagePath.Length > 0);
        PinsChanged?.Invoke();
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
        SelectionName.Text = row?.Name ?? Strings.ConnectionsTab;
        SelectionIcon.Source = row?.Icon;
        SelectionIcon.Visibility = row?.Icon is null ? Visibility.Collapsed : Visibility.Visible;
        var network = row?.NetworkSummary ?? NetworkSummary.Disabled;
        SelectionDetail.Text = string.Join(" · ",
            new[] { row?.Detail ?? "", network.Enabled ? NetworkTotals(network) : "" }
                .Where(text => text.Length > 0));
        var details = string.Join(Environment.NewLine,
            new[] { row?.Detail ?? "", NetworkDetails(network) }.Where(text => text.Length > 0));
        SelectionDetail.ToolTip = details;
        ActionsDrawer.Summarize(SelectionName.Text, SelectionDetail.Text, row is not null);
        CopyDetails.IsEnabled = row is { Connections.Count: > 0 } && _frame?.Available == true;
        Properties.IsEnabled = CopyDetails.IsEnabled;
        ShowProcess.IsEnabled = row?.Members.Any(member => CanShowInProcesses?.Invoke(member) ?? true) == true;
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
        if (row.Members.Count > 0)
            GroupRequested?.Invoke(row.Members);
    }

    private static string Details(ConnectionDisplayRow row)
    {
        var text = new StringBuilder();
        text.AppendLine(row.Name);
        text.AppendLine(
            $"PID: {string.Join(", ", row.Connections.Select(connection => connection.ProcessId).Distinct())}");
        AppendProcessPath(text, row.Process);
        text.AppendLine(
            $"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}");
        foreach (var endpoint in row.Connections)
            text.AppendLine($"{endpoint.ProcessId}\t{ProtocolText(endpoint.Protocol)}\t" +
                            $"{Endpoint(endpoint.LocalEndpoint)}\t" +
                            $"{(endpoint.RemoteEndpoint is { } remote ? Endpoint(remote) : Strings.MissingReading)}\t" +
                            StateText(endpoint.State));
        AppendNetworkDetails(text, row.NetworkSummary);
        return text.ToString();
    }

    private static void AppendProcessPath(StringBuilder text, ProcessUsage? process)
    {
        text.AppendLine(
            $"{Strings.ProcessPathLabel}: {(process?.ImagePath is { Length: > 0 } path ? path : Strings.ExecutablePathUnavailable)}");
    }

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
        var quality = network.Quality.HasFlag(ProcessNetworkQuality.Unavailable)
            ?
            Strings.NetworkProcessMonitoringUnavailable
            :
            network.Quality.HasFlag(ProcessNetworkQuality.Incomplete) ||
            network.Quality.HasFlag(ProcessNetworkQuality.Discontinuity) ||
            network.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded)
                ? Strings.NetworkProcessMonitoringIncomplete
                :
                network.Quality.HasFlag(ProcessNetworkQuality.WarmingUp)
                    ? Strings.NetworkProcessMonitoringWarmingUp
                    :
                    network.Quality.HasFlag(ProcessNetworkQuality.Provisional)
                        ? Strings.ConnectionsNetworkProvisional
                        : "";
        return string.Join(Environment.NewLine, new[]
        {
            Strings.ConnectionsNetworkOwnerTotals,
            $"↓ {NetworkRate(network.RatesAvailable ? network.ReceiveRate : null)}  ↑ {NetworkRate(network.RatesAvailable ? network.SendRate : null)}",
            NetworkTotals(network),
            Strings.ConnectionsRateTiming,
            quality
        }.Where(text => text.Length > 0));
    }

    private static string NetworkTotals(NetworkSummary network)
    {
        return
            $"{Strings.DetailReceivedSent}: {(network.HasTotals ? $"{UsageText.Bytes(network.ReceivedBytes)} / {UsageText.Bytes(network.SentBytes)}" : Strings.MissingReading)}";
    }

    internal static string NetworkRate(double? value)
    {
        return value is { } rate ? TableMetricText.IoRate(rate) : Strings.MissingReading;
    }

    internal static string TrafficDetails(ConnectionTrafficTotals? totals)
    {
        return Strings.ConnectionsTrafficHelp + Environment.NewLine + (totals?.Available == true
            ? Strings.Format(Strings.ConnectionsTrafficCoverage, (int)totals.CoveredSeconds) +
              (totals.Incomplete ? Environment.NewLine + Strings.NetworkProcessMonitoringIncomplete : "")
            : Strings.NetworkProcessMonitoringUnavailable);
    }

    internal static string Endpoint(IPEndPoint endpoint)
    {
        return endpoint.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
            : $"{endpoint.Address}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    internal static string ProtocolText(TransportProtocol protocol)
    {
        return protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
    }

    internal static string StateText(TcpState? state)
    {
        return state switch
        {
            TcpState.Closed => Strings.ConnectionsTcpStateClosed,
            TcpState.Listen => Strings.ConnectionsTcpStateListen,
            TcpState.SynSent => Strings.ConnectionsTcpStateSynSent,
            TcpState.SynReceived => Strings.ConnectionsTcpStateSynReceived,
            TcpState.Established => Strings.ConnectionsTcpStateEstablished,
            TcpState.FinWait1 => Strings.ConnectionsTcpStateFinWait1,
            TcpState.FinWait2 => Strings.ConnectionsTcpStateFinWait2,
            TcpState.CloseWait => Strings.ConnectionsTcpStateCloseWait,
            TcpState.Closing => Strings.ConnectionsTcpStateClosing,
            TcpState.LastAck => Strings.ConnectionsTcpStateLastAck,
            TcpState.TimeWait => Strings.ConnectionsTcpStateTimeWait,
            TcpState.DeleteTcb => Strings.ConnectionsTcpStateDeleteTcb, _ => Strings.MissingReading
        };
    }

    internal sealed record ConnectionEntry(NetworkConnection Connection, ProcessUsage? Process, string GroupKey);

    internal readonly record struct NetworkSummary(
        long ReceivedBytes,
        long SentBytes,
        double? ReceiveRate,
        double? SendRate,
        ProcessNetworkQuality Quality,
        bool Enabled,
        bool HasTotals)
    {
        internal static NetworkSummary Disabled { get; } =
            new(0, 0, null, null, ProcessNetworkQuality.None, false, false);

        internal static NetworkSummary Unavailable { get; } =
            new(0, 0, null, null, ProcessNetworkQuality.Unavailable, true, false);

        internal bool RatesAvailable => HasTotals && !Quality.HasFlag(ProcessNetworkQuality.Unavailable);
    }

    internal sealed record ConnectionGroup(
        string Key,
        IReadOnlyList<ConnectionEntry> Entries,
        IReadOnlyList<ProcessUsage> Members,
        ProcessUsage? Process)
    {
        internal static ConnectionGroup Create(string key, IEnumerable<ConnectionEntry> source)
        {
            var entries = source.ToArray();
            var members = entries.Select(e => e.Process).OfType<ProcessUsage>().DistinctBy(p => p.Identity).ToArray();
            var process = members.OrderBy(DisplayName, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.ProcessId)
                .FirstOrDefault();
            return new(key, entries, members, process);
        }
    }
}

internal sealed class ConnectionDisplayRow(string key) : INotifyPropertyChanged
{
    public CpuGraph Graph { get; } = new() { Height = 24, ShowGrid = false, SparklineAccentKey = "NetworkChartAccent" };
    public bool Pinned { get; private set; }
    public string PinLabel { get; private set; } = "";
    public string PinAccessibleName => $"{PinLabel}: {Name}";
    public Geometry PinIcon { get; private set; } = ButtonIcon.Pin;
    public bool CanPin => Process is not null;
    public string Key { get; } = key;
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string AccessibleName { get; private set; } = "";
    public string Tcp { get; private set; } = "";
    public string Udp { get; private set; } = "";
    public string RankedLabel { get; private set; } = "";
    public Visibility RankedVisibility => RankedLabel.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string Network { get; private set; } = "";
    public string Receive { get; private set; } = "";
    public string Send { get; private set; } = "";
    public string Received { get; private set; } = "";
    public string Sent { get; private set; } = "";
    public string TrafficToolTip { get; private set; } = "";

    public string TrafficAccessibleName =>
        $"{Strings.ConnectionsReceived}: {Received}, {Strings.ConnectionsSent}: {Sent}";

    public GridLength TotalColumnWidth => NetworkVisibility == Visibility.Visible ? new(82) : new(0);
    public string NetworkToolTip { get; private set; } = "";
    public GridLength NetworkColumnWidth { get; private set; }
    public GridLength ReceiveColumnWidth { get; private set; }
    public GridLength SendColumnWidth { get; private set; }
    public Visibility NetworkVisibility { get; private set; } = Visibility.Collapsed;
    public BitmapSource? Icon { get; private set; }
    internal ProcessUsage? Process { get; private set; }
    internal IReadOnlyList<NetworkConnection> Connections { get; private set; } = [];
    internal IReadOnlyList<ProcessUsage> Members { get; private set; } = [];
    internal ConnectionsView.NetworkSummary NetworkSummary { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void UpdateTraffic(ConnectionTrafficTotals? totals, IReadOnlyList<ProcessUsage> members)
    {
        var identities = members.Select(member => member.Identity).Distinct().ToArray();
        var available = totals?.Available == true && identities.Any(identity => totals.Processes.ContainsKey(identity));
        var sum = totals?.Sum(identities) ?? (0L, 0L);
        var received = available ? UsageText.Bytes(sum.Received) : Strings.MissingReading;
        var sent = available ? UsageText.Bytes(sum.Sent) : Strings.MissingReading;
        if (available && totals!.Incomplete)
        {
            received = "≈ " + received;
            sent = "≈ " + sent;
        }

        var help = ConnectionsView.TrafficDetails(totals);
        if ((Received, Sent, TrafficToolTip) == (received, sent, help)) return;
        (Received, Sent, TrafficToolTip) = (received, sent, help);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal void SetRankedLabel(string label)
    {
        if (RankedLabel == label) return;
        RankedLabel = label;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal void UpdateGroup(ConnectionsView.ConnectionGroup group, bool pinned,
        ConnectionsView.NetworkSummary network, string? status = null)
    {
        var shown = Shown();
        var wasPinned = Pinned;
        Process = group.Process;
        Pinned = pinned;
        Connections = group.Entries.Select(entry => entry.Connection).ToArray();
        Members = group.Members;
        NetworkSummary = network;
        Name = DisplayName(Process);
        Detail = status ?? (Members.Count > 1
            ? Strings.Format(Strings.ConnectionsProcessCount, Members.Count)
            : $"PID {(group.Entries.Count > 0 ? group.Entries[0].Connection.ProcessId : Process!.ProcessId).ToString(CultureInfo.InvariantCulture)}");
        var countsAvailable = status is null || status == Strings.ConnectionsNoObservedEndpoints;
        Tcp = countsAvailable
            ? Connections.Count(connection => connection.Protocol == TransportProtocol.Tcp)
                .ToString(CultureInfo.CurrentCulture)
            : Strings.MissingReading;
        Udp = countsAvailable
            ? Connections.Count(connection => connection.Protocol == TransportProtocol.Udp)
                .ToString(CultureInfo.CurrentCulture)
            : Strings.MissingReading;
        SetNetwork(network, true);
        AccessibleName = $"{Name}, {Detail}, {Tcp} TCP, {Udp} UDP" +
                         (network.Enabled
                             ? $", {Strings.ConnectionsNetworkOwnerTotals}, {Network}, {TrafficAccessibleName}"
                             : "");
        PinLabel = pinned ? Strings.UnpinProcess : Strings.PinProcess;
        PinIcon = pinned ? ButtonIcon.PinFilled : ButtonIcon.Pin;
        EnsureIcon();
        if (Shown() != shown || pinned != wasPinned)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private void SetNetwork(ConnectionsView.NetworkSummary network, bool summary)
    {
        NetworkColumnWidth = network.Enabled ? new GridLength(172) : new GridLength(0);
        ReceiveColumnWidth = network.Enabled ? new GridLength(80) : new GridLength(0);
        SendColumnWidth = network.Enabled ? new GridLength(80) : new GridLength(0);
        NetworkVisibility = NetworkColumnWidth.Value == 0 ? Visibility.Collapsed : Visibility.Visible;
        Network = summary && network.Enabled
            ? $"↓ {ConnectionsView.NetworkRate(network.RatesAvailable ? network.ReceiveRate : null)}  ↑ {ConnectionsView.NetworkRate(network.RatesAvailable ? network.SendRate : null)}"
            : "";
        Receive = summary && network.Enabled
            ? ConnectionsView.NetworkRate(network.RatesAvailable ? network.ReceiveRate : null)
            : "";
        Send = summary && network.Enabled
            ? ConnectionsView.NetworkRate(network.RatesAvailable ? network.SendRate : null)
            : "";
        NetworkToolTip = summary && network.Enabled ? ConnectionsView.NetworkDetails(network) : "";
    }

    private static string DisplayName(ProcessUsage? process)
    {
        return process?.AppName is { Length: > 0 } name ? name :
            process?.Name is { Length: > 0 } executable ? executable : Strings.ConnectionsUnknownProcess;
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

    private (string, string, string, string, string, string, string, string, string, string, GridLength, GridLength,
        GridLength, Visibility) Shown()
    {
        return (Name, Detail, AccessibleName, Tcp, Udp, PinLabel, Network, NetworkToolTip, Receive, Send,
            NetworkColumnWidth, ReceiveColumnWidth, SendColumnWidth, NetworkVisibility);
    }
}
