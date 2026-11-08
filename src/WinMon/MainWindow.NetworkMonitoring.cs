using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private NetworkMonitoringController? _networkMonitoring;
    private Task? _networkRestore;
    private ProcessNetworkFrame? _networkForFixture;

    private void InitializeNetworkMonitoring()
    {
        if (_diagnosticMode || !NetworkSetupBroker.Supported) return;
        _networkMonitoring = new NetworkMonitoringController();
        if (_preferences.ProcessNetworkMonitoringEnabled)
            _networkRestore = _networkMonitoring.RestoreAsync(true, Lifetime.Token);
    }

    private Task<NetworkMonitoringChangeResult> ChangeNetworkMonitoringAsync(bool enabled, CancellationToken token)
    {
        return _networkMonitoring is { } controller
            ? controller.ChangeAsync(enabled, token)
            : Task.FromResult(new NetworkMonitoringChangeResult(false,
                NetworkSetupBroker.Supported
                    ? NetworkMonitoringState.SetupUnavailable
                    : NetworkMonitoringState.StoreUnavailable));
    }

    private async Task<NetworkMonitoringChangeResult> RemoveNetworkMonitoringAsync(CancellationToken token)
    {
        if (_networkMonitoring is not { } controller)
            return new NetworkMonitoringChangeResult(false, NetworkMonitoringState.SetupUnavailable);
        var result = await controller.RemoveAsync(token);
        if (result.Success)
        {
            // Removal is independent of the Settings Save button and must survive cancelling the dialog.
            var preferences = _preferences with { ProcessNetworkMonitoringEnabled = false };
            await _preferenceStore.SaveAsync(preferences, Lifetime.Token);
            _preferences = _preferences with { ProcessNetworkMonitoringEnabled = false };
        }

        return result;
    }

    private void ObserveNetworkMonitoring(SettingsWindow dialog)
    {
        if (_networkMonitoring is not { } controller) return;

        void Changed(NetworkMonitoringState state)
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() =>
                {
                    if (dialog.IsVisible) dialog.RefreshNetworkMonitoringState(state);
                });
        }

        controller.StateChanged += Changed;
        dialog.Closed += (_, _) => controller.StateChanged -= Changed;
    }

    private async Task RestoreNetworkMonitoringPreferenceAsync()
    {
        if (_networkMonitoring is { } controller && !IsClosing)
            await controller.RestoreAsync(_preferences.ProcessNetworkMonitoringEnabled, Lifetime.Token);
    }

    private async Task ShutdownNetworkMonitoringAsync()
    {
        if (_networkMonitoring is { } controller) await controller.DisposeAsync();
        if (_networkRestore is { } restore) await restore;
    }
}