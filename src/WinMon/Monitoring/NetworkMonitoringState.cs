namespace WinMon.Monitoring;

/// <summary>Localized by the Settings view; never display a privileged helper's raw error text.</summary>
public enum NetworkMonitoringState
{
    Disabled,
    Enabling,
    WarmingUp,
    Active,
    Incomplete,
    Unavailable,
    UpdateRequired,
    SetupUnavailable,
    Cancelled,
    Failed,
    StoreUnavailable
}

public sealed record NetworkMonitoringChangeResult(bool Success, NetworkMonitoringState State);
