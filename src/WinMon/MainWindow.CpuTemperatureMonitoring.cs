using MonCore.Monitoring;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Updates;

namespace WinMon;

public partial class MainWindow
{
    private CpuTemperatureClient? _cpuTemperature;
    private Task? _cpuTemperatureRestore;

    private void InitializeCpuTemperatureMonitoring()
    {
        if (_diagnosticMode || !SensorSetupBroker.Supported) return;
        _cpuTemperature = new CpuTemperatureClient();
        if (_preferences.CpuTemperatureMonitoringEnabled)
            _cpuTemperatureRestore = UpdateRuntime.IsExpectedRestart && _cpuTemperature.ServiceUpdateAvailable
                ? UpdateCpuTemperatureServiceAsync(_cpuTemperature)
                : _cpuTemperature.RestoreAsync(true, Lifetime.Token);
    }

    // An in-app update replaces the older service too; a declined or failed setup keeps using the old one.
    private async Task UpdateCpuTemperatureServiceAsync(CpuTemperatureClient client)
    {
        if (!(await client.ChangeAsync(true, Lifetime.Token)).Success && !IsClosing)
            await client.RestoreAsync(true, Lifetime.Token);
    }

    private async Task<SensorMonitoringChangeResult> ChangeCpuTemperatureMonitoringAsync(bool enabled, CancellationToken token)
    {
        if (_cpuTemperature is not { } client)
            return new SensorMonitoringChangeResult(false, SensorSetupBroker.Supported
                ? SensorMonitoringState.SetupUnavailable : SensorMonitoringState.StoreUnavailable);
        var result = await client.ChangeAsync(enabled, token);
        if (result.Success && _preferences.CpuTemperatureMonitoringEnabled != enabled)
        {
            var preferences = _preferences with { CpuTemperatureMonitoringEnabled = enabled };
            using var save = CancellationTokenSource.CreateLinkedTokenSource(token, Lifetime.Token);
            try { await _preferenceStore.SaveAsync(preferences, save.Token); }
            catch
            {
                await RestoreCpuTemperatureMonitoringPreferenceAsync();
                throw;
            }
            _preferences = _preferences with { CpuTemperatureMonitoringEnabled = enabled };
        }
        return result;
    }

    private async Task<SensorMonitoringChangeResult> RemoveCpuTemperatureServiceAsync(CancellationToken token)
    {
        if (_cpuTemperature is not { } client)
            return new SensorMonitoringChangeResult(false, SensorMonitoringState.SetupUnavailable);
        var result = await client.RemoveAsync(token);
        if (result.Success)
        {
            var preferences = _preferences with { CpuTemperatureMonitoringEnabled = false };
            await _preferenceStore.SaveAsync(preferences, Lifetime.Token);
            _preferences = preferences;
        }
        return result;
    }

    private void ObserveCpuTemperatureMonitoring(SettingsWindow dialog)
    {
        if (_cpuTemperature is not { } client) return;
        void Changed(SensorMonitoringState state)
        {
            if (!Dispatcher.HasShutdownStarted)
                Dispatcher.BeginInvoke(() => { if (dialog.IsVisible) dialog.RefreshCpuTemperatureMonitoringState(state); });
        }
        client.StateChanged += Changed;
        dialog.Closed += (_, _) => client.StateChanged -= Changed;
    }

    private async Task RestoreCpuTemperatureMonitoringPreferenceAsync()
    {
        if (_cpuTemperature is { } client && !IsClosing)
            await client.RestoreAsync(_preferences.CpuTemperatureMonitoringEnabled, Lifetime.Token);
    }

    private async Task ShutdownCpuTemperatureMonitoringAsync()
    {
        if (_cpuTemperature is { } client) await client.DisposeAsync();
        if (_cpuTemperatureRestore is { } restore) await restore;
    }

    // Samples hold the last valid reading through two missed refreshes, under eight seconds, as the gauge does, so a
    // brief sensor miss does not break charts and history or restart a sustained temperature rule.
    private CpuTemperatureReading? CurrentCpuTemperature() =>
        _preferences.CpuTemperatureMonitoringEnabled && SensorSetupBroker.Supported
            ? _cpuTemperature?.GetDisplayReading(System.Diagnostics.Stopwatch.GetTimestamp())
            : null;

    private CpuTemperatureReading? CpuTemperatureForDisplay(ResourceSample? resource)
    {
        if (resource is null) return null;
        if (_cpuTemperature is null)
            return (_diagnosticMode ? HeldCpuTemperatureForFixture : null) ?? resource.CpuTemperature;
        return _preferences.CpuTemperatureMonitoringEnabled
            ? _cpuTemperature.GetDisplayReading(System.Diagnostics.Stopwatch.GetTimestamp())
            : null;
    }
}
