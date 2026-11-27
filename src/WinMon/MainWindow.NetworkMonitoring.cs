using MonCore.Monitoring;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Updates;

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
            _networkRestore = UpdateRuntime.IsExpectedRestart && _networkMonitoring.ServiceUpdateAvailable
                ? UpdateNetworkServiceAsync(_networkMonitoring)
                : _networkMonitoring.RestoreAsync(true, Lifetime.Token);
    }

    // An in-app update replaces the older service too; a declined or failed setup keeps using the old one.
    private async Task UpdateNetworkServiceAsync(NetworkMonitoringController controller)
    {
        if (!(await controller.ChangeAsync(true, Lifetime.Token)).Success && !IsClosing)
            await controller.RestoreAsync(true, Lifetime.Token);
    }

    private async Task<NetworkMonitoringChangeResult> ChangeNetworkMonitoringAsync(bool enabled, CancellationToken token)
    {
        if (_networkMonitoring is not { } controller)
            return new NetworkMonitoringChangeResult(false,
                NetworkSetupBroker.Supported
                    ? NetworkMonitoringState.SetupUnavailable
                    : NetworkMonitoringState.StoreUnavailable);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime.Token);
        var result = await controller.ChangeAsync(enabled, operation.Token);
        if (result.Success && _preferences.ProcessNetworkMonitoringEnabled != enabled)
        {
            var preferences = _preferences with { ProcessNetworkMonitoringEnabled = enabled };
            try { await _preferenceStore.SaveAsync(preferences, operation.Token); }
            catch
            {
                await RestoreNetworkMonitoringPreferenceAsync();
                throw;
            }
            _preferences = _preferences with { ProcessNetworkMonitoringEnabled = enabled };
        }
        return result;
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
