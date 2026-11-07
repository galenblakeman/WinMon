using System.Diagnostics;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private NetworkConnectionsFrame? _connections;
    private bool _connectionsBusy;
    private long _connectionsReadAt;
    private (uint? Total, uint? Secondary)? _connectionCounts;
    private bool _connectionsPartial;

    private void InitializeConnections()
    {
        ConnectionView.CopyRequested += CopyProcessDetails;
        ConnectionView.ProcessRequested += process =>
        {
            var current = Session.LatestFull?.Processes.FirstOrDefault(p => p.Identity == process.Identity);
            if (current is null) return;
            SelectedSample = null;
            Processes.ShowProcess(current);
            ProcessesTab.IsChecked = true;
            RefreshPresentation();
            Processes.SelectProcess(current, false);
        };
        ConnectionView.GroupRequested += members =>
        {
            var live = Session.LatestFull?.Processes.FirstOrDefault(p => members.Any(member => member.Identity == p.Identity));
            if (live is null) return;
            SelectedSample = null;
            if (live.ImagePath.Length > 0) Processes.ShowGroup(live);
            else Processes.ShowProcess(live);
            ProcessesTab.IsChecked = true;
            RefreshPresentation();
        };
    }

    private void RefreshConnections()
    {
        if (ConnectionsTab.IsChecked != true || CompactMode) return;
        PresentConnections();
        if (_diagnosticMode || _connectionsBusy || Lifetime.IsCancellationRequested || IsClosing) return;
        var now = Stopwatch.GetTimestamp();
        if (_connectionsReadAt != 0 && Stopwatch.GetElapsedTime(_connectionsReadAt, now).TotalSeconds < 1) return;
        _connectionsReadAt = now;
        _ = ReadConnectionsAsync();
    }

    private async Task ReadConnectionsAsync()
    {
        _connectionsBusy = true;
        try
        {
            var frame = await Task.Run(WindowsConnections.Capture, Lifetime.Token);
            if (Lifetime.IsCancellationRequested || IsClosing) return;
            _connections = frame;
            if (ConnectionsTab.IsChecked == true && IsVisible && WindowState != System.Windows.WindowState.Minimized &&
                !CompactMode)
                PresentConnections();
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _connectionsBusy = false;
        }
    }

    private void PresentConnections()
    {
        var partial = _connections?.PartiallyAvailable == true;
        if (partial != _connectionsPartial)
        {
            _connectionsPartial = partial;
            _connectionCounts = null;
        }
        UpdateActivityPair(ConnectionCounts, ConnectionTcpValue, ConnectionUdpValue,
            _connections?.Available == true ? (uint)_connections.Connections.Count(c => c.Protocol == TransportProtocol.Tcp) : null,
            _connections?.Available == true ? (uint)_connections.Connections.Count(c => c.Protocol == TransportProtocol.Udp) : null,
            "TCP", "UDP", ref _connectionCounts);
        if (partial)
        {
            var exact = $"{Strings.ConnectionsPartial}\nTCP: {ExactCount(_connectionCounts?.Total)}\nUDP: {ExactCount(_connectionCounts?.Secondary)}";
            ConnectionCounts.ToolTip = exact;
            System.Windows.Automation.AutomationProperties.SetName(ConnectionCounts, exact);
        }
        if (!_preferences.ActivityPanelCollapsed)
            ConnectionView.Update(_connections, Session.LatestFull?.Processes ?? [], SelectedSample is not null);
    }

}
