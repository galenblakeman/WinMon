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

    internal void SetChartPanelCollapsed(bool collapsed)
    {
        if (_preferences.ChartPanelCollapsed == collapsed) return;
        if (collapsed) RememberActivityPanelSize();
        var moveFocus = collapsed && (ChartBody.IsKeyboardFocusWithin || ChartProcessDivider.IsKeyboardFocusWithin);
        _preferences = _preferences with { ChartPanelCollapsed = collapsed };
        ApplyActivityPanel();
        if (moveFocus) ChartPanelToggle.Focus();
    }

    private void OnToggleChartPanel(object sender, RoutedEventArgs e)
    {
        SetChartPanelCollapsed(!_preferences.ChartPanelCollapsed);
    }

    private void OnToggleActivityPanel(object sender, RoutedEventArgs e)
    {
        SetActivityPanelCollapsed(!_preferences.ActivityPanelCollapsed);
    }

    private void RememberActivityPanelSize()
    {
        if (_preferences.ActivityPanelCollapsed || _preferences.ChartPanelCollapsed || CompactMode || !ChartAreaRow.Height.IsStar ||
            !ProcessesAreaRow.Height.IsStar || ChartAreaRow.Height.Value <= 0) return;
        _preferences = _preferences with
        {
            ActivityPanelRatio = ProcessesAreaRow.Height.Value / ChartAreaRow.Height.Value
        };
    }

    internal void ApplyActivityPanel()
    {
        var collapsed = _preferences.ActivityPanelCollapsed;
        var chartCollapsed = _preferences.ChartPanelCollapsed;
        ActivityBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ChartBody.Visibility = chartCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ChartProcessDivider.Visibility = collapsed || chartCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ChartAreaRow.MinHeight = chartCollapsed ? 0 : 160;
        ProcessesAreaRow.MinHeight = collapsed ? 0 : 344;
        ChartAreaRow.Height = chartCollapsed ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        ProcessesAreaRow.Height = collapsed ? GridLength.Auto : new GridLength(_preferences.ActivityPanelRatio, GridUnitType.Star);
        UpdateActivityPanelButton();
        UpdateChartPanelButton();
    }

    private void UpdateChartPanelButton()
    {
        var collapsed = _preferences.ChartPanelCollapsed;
        var text = collapsed ? Strings.ExpandChartPanel : Strings.CollapseChartPanel;
        ChartPanelToggle.ToolTip = text;
        AutomationProperties.SetName(ChartPanelToggle, text);
        ButtonIcon.SetData(ChartPanelToggle, collapsed ? ButtonIcon.ChevronDown : ButtonIcon.ChevronUp);
        ChartPanelToggle.SetExpanded(!collapsed);
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
