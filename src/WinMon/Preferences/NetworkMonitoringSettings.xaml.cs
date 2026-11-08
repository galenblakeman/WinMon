using System.Windows.Controls;

namespace WinMon.Preferences;

/// <summary>Static Settings layout for the optional, privileged per-process network monitor.</summary>
public sealed partial class NetworkMonitoringSettings : UserControl
{
    internal NetworkMonitoringSettings(CheckBox enabled, TextBlock status, Button repair, Button remove)
    {
        InitializeComponent();
        EnabledPresenter.Content = enabled;
        StatusPresenter.Content = status;
        RepairPresenter.Content = repair;
        RemovePresenter.Content = remove;
    }
}
