using System.Diagnostics;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private NetworkConnectionsFrame? _connections;
    private bool _connectionsBusy;
    private long _connectionsReadAt;

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
    }

    private void RefreshConnections()
    {
        if (ConnectionsTab.IsChecked != true || CompactMode || _preferences.ActivityPanelCollapsed) return;
        ConnectionView.Update(_connections, Session.LatestFull?.Processes ?? [], SelectedSample is not null);
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
                !CompactMode && !_preferences.ActivityPanelCollapsed)
                ConnectionView.Update(frame, Session.LatestFull?.Processes ?? [], SelectedSample is not null);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _connectionsBusy = false;
        }
    }

}
