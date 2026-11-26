using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WinMon.Preferences;

/// <summary>Settings for WinMon's optional services.</summary>
public sealed partial class ServicesSettings : UserControl
{
    internal ServicesSettings(bool hardwareSupported,
        CheckBox hardwareEnabled, TextBlock hardwareStatus, Button hardwareRepair, Button hardwareRemove,
        CheckBox networkEnabled, TextBlock networkStatus, Button networkRepair, Button networkRemove)
    {
        InitializeComponent();
        HardwareSection.Visibility = hardwareSupported ? Visibility.Visible : Visibility.Collapsed;
        HardwareEnabledPresenter.Content = hardwareEnabled;
        HardwareStatusPresenter.Content = hardwareStatus;
        HardwareRepairPresenter.Content = hardwareRepair;
        HardwareRemovePresenter.Content = hardwareRemove;
        NetworkEnabledPresenter.Content = networkEnabled;
        NetworkStatusPresenter.Content = networkStatus;
        NetworkRepairPresenter.Content = networkRepair;
        NetworkRemovePresenter.Content = networkRemove;
        Compact(hardwareRepair, Strings.ServicesRepair);
        Compact(hardwareRemove, Strings.ServicesRemove);
        Compact(networkRepair, Strings.ServicesRepair);
        Compact(networkRemove, Strings.ServicesRemove);
    }

    private static void Compact(Button button, string label)
    {
        // Visible labels are short; accessible names and tooltips retain the service's identity.
        var description = button.Content?.ToString() ?? label;
        AutomationProperties.SetName(button, description);
        button.ToolTip = description;
        button.Content = label;
        button.MinWidth = 88;
        button.Margin = new Thickness(0, 2, 8, 0);
    }
}
