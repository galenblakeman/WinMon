using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    // Diagnostic fixtures supply endpoint/traffic observations without native sampling.
    private NetworkConnectionsFrame? _connections;
    private (uint? Total, uint? Secondary)? _connectionCounts;

    private static ConnectionListSettings ConnectionSettings(MonitorPreferences preferences) =>
        new(preferences.TopConnectionCount, (ConnectionSort)preferences.ConnectionSortColumn,
            preferences.ConnectionAppsOnly);

    private void InitializeConnections()
    {
        ConnectionView.TrafficSettingsRequested += () =>
        {
            _lastSettingsSection = SettingsSection.Network;
            OnSettings(this, new System.Windows.RoutedEventArgs());
        };
        ConnectionView.CanShowInProcesses = process =>
            (SelectedSample ?? Session.LatestFull)?.Processes.Any(current => current.Identity == process.Identity) == true;
        ConnectionView.SettingsChanged += settings => _preferences = _preferences with
        {
            TopConnectionCount = settings.TopCount, ConnectionSortColumn = (int)settings.Sort,
            ConnectionAppsOnly = settings.AppsOnly
        };
        ConnectionView.CopyRequested += details => CopyProcessDetails(ConnectionDetailsAtMoment(details));
        ConnectionView.PropertiesRequested += (name, details) =>
            new ServicePropertiesWindow(name, ConnectionDetailsAtMoment(details), CopyProcessDetails) { Owner = this }.ShowDialog();
        ConnectionView.ProcessRequested += process =>
        {
            var current = (SelectedSample ?? Session.LatestFull)?.Processes.FirstOrDefault(p => p.Identity == process.Identity);
            if (current is null) return;
            Processes.ShowProcess(current);
            ProcessesTab.IsChecked = true;
            RefreshPresentation();
            Processes.SelectProcess(current, false);
        };
        ConnectionView.GroupRequested += members =>
        {
            var owner = (SelectedSample ?? Session.LatestFull)?.Processes.FirstOrDefault(p => members.Any(member => member.Identity == p.Identity));
            if (owner is null) return;
            if (owner.ImagePath.Length > 0) Processes.ShowGroup(owner);
            else Processes.ShowProcess(owner);
            ProcessesTab.IsChecked = true;
            RefreshPresentation();
        };
    }

    private string ConnectionDetailsAtMoment(string details) => SelectedSample is { } selected
        ? $"{selected.Timestamp.ToLocalTime():G}\n{Strings.ConnectionsHistoryNote}\n\n{details}"
        : details;

    private void RefreshConnections()
    {
        if (ConnectionsTab.IsChecked != true || CompactMode) return;
        var capture = SelectedSample is { } selected ? Session.Connections.Find(selected) :
            _samplingError == SamplingError.ReadingsUnavailable ? null : Session.Connections.Latest;
        var frame = _diagnosticMode && SelectedSample is null ? _connections : capture?.Connections;
        var owners = _diagnosticMode && SelectedSample is null
            ? Session.LatestFull?.Processes ?? []
            : capture?.Owners ?? [];
        var network = _diagnosticMode && SelectedSample is null ? _networkForFixture : capture?.Network;
        var networkEnabled = SelectedSample is not null ? network is not null :
            _diagnosticMode ? _networkForFixture is not null :
            _preferences.ProcessNetworkMonitoringEnabled && NetworkSetupBroker.Supported;
        UpdateActivityPair(ConnectionCounts, ConnectionTcpValue, ConnectionUdpValue,
            frame?.Available == true ? (uint)frame.Connections.Count(c => c.Protocol == TransportProtocol.Tcp) : null,
            frame?.Available == true ? (uint)frame.Connections.Count(c => c.Protocol == TransportProtocol.Udp) : null,
            "TCP", "UDP", ref _connectionCounts);
        var ratesAvailable = network is not null &&
            (network.Quality & (ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp)) == 0;
        var readings = network?.Processes.Where(reading => network.CurrentLifetimes is null ||
            network.CurrentLifetimes.Contains(new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime))).ToArray() ?? [];
        double? receive = ratesAvailable && readings.All(reading => reading.ReceiveBytesPerSecond is >= 0)
            ? readings.Sum(reading => reading.ReceiveBytesPerSecond!.Value) : null;
        double? send = ratesAvailable && readings.All(reading => reading.SendBytesPerSecond is >= 0)
            ? readings.Sum(reading => reading.SendBytesPerSecond!.Value) : null;
        ConnectionTrafficValue.Text = networkEnabled
            ? $"\n↓ {ConnectionsView.NetworkRate(receive)}  ↑ {ConnectionsView.NetworkRate(send)}" : "";
        var details = $"TCP: {ExactCount(_connectionCounts?.Total)}\nUDP: {ExactCount(_connectionCounts?.Secondary)}";
        if (networkEnabled)
            details += $"\n{Strings.ConnectionsNetworkOwnerTotals}\n{Strings.ConnectionsReceiveRate}: {ConnectionsView.NetworkRate(receive)}\n{Strings.ConnectionsSendRate}: {ConnectionsView.NetworkRate(send)}";
        if (frame?.PartiallyAvailable == true) details = $"{Strings.ConnectionsPartial}\n{details}";
        if (SelectedSample is not null && frame is null) details = $"{Strings.ConnectionsHistoryUnavailable}\n{details}";
        ConnectionCounts.ToolTip = details;
        System.Windows.Automation.AutomationProperties.SetName(ConnectionCounts, details);
        if (!_preferences.ActivityPanelCollapsed)
            ConnectionView.Update(frame, owners, SelectedSample is not null, network, networkEnabled);
    }
}
