namespace WinMon.Monitoring;

/// <summary>Localized by the Settings view; raw privileged-helper errors are never displayed.</summary>
public enum SensorMonitoringState
{
    Disabled,
    Enabling,
    Starting,
    Active,
    Unavailable,
    UpdateRequired,
    SetupUnavailable,
    Cancelled,
    Failed,
    StoreUnavailable,
    PawnIoSetupFailed,
    RestartRequired
}

public sealed record SensorMonitoringChangeResult(bool Success, SensorMonitoringState State);
