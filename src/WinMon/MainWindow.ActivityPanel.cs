using System.Windows;
using System.Windows.Automation;
using WinMon.Controls;

namespace WinMon;

public partial class MainWindow
{
    internal void SetActivityPanelCollapsed(bool collapsed)
    {
        if (_preferences.ActivityPanelCollapsed == collapsed) return;
        if (collapsed) RememberActivityPanelSize();
        var moveFocus = collapsed && (ActivityBody.IsKeyboardFocusWithin || ChartProcessDivider.IsKeyboardFocusWithin);
        _preferences = _preferences with { ActivityPanelCollapsed = collapsed };
        ApplyActivityPanel();
        if (moveFocus) ActivityPanelToggle.Focus();
    }

    private void OnToggleActivityPanel(object sender, RoutedEventArgs e)
    {
        SetActivityPanelCollapsed(!_preferences.ActivityPanelCollapsed);
    }

    private void RememberActivityPanelSize()
    {
        if (_preferences.ActivityPanelCollapsed || CompactMode || !ChartAreaRow.Height.IsStar ||
            !ProcessesAreaRow.Height.IsStar || ChartAreaRow.Height.Value <= 0) return;
        _preferences = _preferences with
        {
            ActivityPanelRatio = ProcessesAreaRow.Height.Value / ChartAreaRow.Height.Value
        };
    }

    internal void ApplyActivityPanel()
    {
        var collapsed = _preferences.ActivityPanelCollapsed;
        ActivityBody.Visibility = ChartProcessDivider.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ProcessesAreaRow.MinHeight = collapsed ? 0 : 344;
        ChartAreaRow.Height = new GridLength(1, GridUnitType.Star);
        ProcessesAreaRow.Height = collapsed ? GridLength.Auto : new GridLength(_preferences.ActivityPanelRatio, GridUnitType.Star);
        UpdateActivityPanelButton();
    }

    private void UpdateActivityPanelButton()
    {
        var collapsed = _preferences.ActivityPanelCollapsed;
        var text = collapsed ? Strings.ExpandActivityPanel : Strings.CollapseActivityPanel;
        ActivityPanelToggle.ToolTip = text;
        AutomationProperties.SetName(ActivityPanelToggle, text);
        ButtonIcon.SetData(ActivityPanelToggle, collapsed ? ButtonIcon.ChevronUp : ButtonIcon.ChevronDown);
        ActivityPanelToggle.SetExpanded(!collapsed);
    }
}
