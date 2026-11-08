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
            preferences.ConnectionAppsOnly, preferences.ConnectionPinnedOnly,
            preferences.ConnectionTrafficSeconds);

    private void InitializeConnections()
    {
        ConnectionView.TrafficSettingsRequested += () =>
        {
            _lastSettingsSection = SettingsSection.Network;
            OnSettings(this, new System.Windows.RoutedEventArgs());
        };
        ConnectionView.CanShowInProcesses = process =>
            (SelectedSample ?? Session.LatestFull)?.Processes.Any(current => current.Identity == process.Identity) == true;
        ConnectionView.SettingsChanged += settings =>
        {
            _preferences = _preferences with
            {
                TopConnectionCount = settings.TopCount, ConnectionSortColumn = (int)settings.Sort,
                ConnectionAppsOnly = settings.AppsOnly, ConnectionPinnedOnly = settings.PinnedOnly,
                ConnectionTrafficSeconds = settings.TrafficSeconds
            };
            RefreshConnections();
        };
        ConnectionView.CopyRequested += details => CopyProcessDetails(ConnectionDetailsAtMoment(details));
        ConnectionView.PropertiesRequested += (name, details) =>
            new ServicePropertiesWindow(name, ConnectionDetailsAtMoment(details), CopyProcessDetails) { Owner = this }.ShowDialog();
        ConnectionView.PinsChanged += () => _preferences = _preferences with { PinnedConnections = ConnectionView.SavedPins };
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
        var end = _diagnosticMode && SelectedSample is null && Session.LatestFull is { } latest
            ? new ConnectionCapture(latest, frame, owners, network) : capture;
        var history = networkEnabled ? Session.Connections.Captures.Where(past => end is not null &&
            CpuGraph.Moment(past.Sample) <= CpuGraph.Moment(end.Sample)).ToArray() : [];
        var totals = networkEnabled ? ConnectionTrafficTotals.Build(history, end, _preferences.ConnectionTrafficSeconds) : null;
        var sum = totals?.Sum() ?? (0L, 0L);
        var prefix = totals?.Incomplete == true ? "≈ " : "";
        ConnectionVolumeValue.Text = networkEnabled
            ? $"  ·  {Strings.History}: " +
                $"↓ {(totals?.Available == true ? prefix + UsageText.Bytes(sum.Item1) : Strings.MissingReading)} | " +
                $"↑ {(totals?.Available == true ? prefix + UsageText.Bytes(sum.Item2) : Strings.MissingReading)}" : "";
        var details = $"TCP: {ExactCount(_connectionCounts?.Total)}\nUDP: {ExactCount(_connectionCounts?.Secondary)}";
        if (networkEnabled)
            details += $"\n{Strings.ConnectionsNetworkOwnerTotals}\n{Strings.ConnectionsRateTiming}";
        if (frame?.PartiallyAvailable == true) details = $"{Strings.ConnectionsPartial}\n{details}";
        if (SelectedSample is not null && frame is null) details = $"{Strings.ConnectionsHistoryUnavailable}\n{details}";
        if (networkEnabled)
            details += $"\n{(_preferences.ConnectionTrafficSeconds == 0 ? Strings.ConnectionsSinceStart : Strings.Format(Strings.GaugePeakWindowMinutes, _preferences.ConnectionTrafficSeconds / 60))}" +
                $"\n{ConnectionVolumeValue.Text.Trim()}\n{ConnectionsView.TrafficDetails(totals)}";
        ConnectionCounts.ToolTip = details;
        System.Windows.Automation.AutomationProperties.SetName(ConnectionCounts, details);
        if (!_preferences.ActivityPanelCollapsed)
            ConnectionView.Update(frame, owners, SelectedSample is not null, network, networkEnabled,
                history, end, totals);
    }
}
