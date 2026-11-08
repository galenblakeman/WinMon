using System.Windows;
using System.Windows.Automation;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private bool _chartLegendCollapsed;

    private void OnToggleChartResources(object sender, RoutedEventArgs e)
    {
        var moveFocus = ChartLegend.IsKeyboardFocusWithin;
        _chartLegendCollapsed = !_chartLegendCollapsed;
        UpdateChartLegend();
        if (moveFocus && _chartLegendCollapsed) ChartResourcesToggle.Focus();
    }

    private void UpdateChartLegend()
    {
        var relevant = _preferences.ResourceChartType != ResourceChartType.LogicalProcessors;
        ChartResourcesToggle.Visibility = relevant ? Visibility.Visible : Visibility.Collapsed;
        ChartResourcesToggle.IsEnabled = !_preferences.ChartPanelCollapsed;
        ChartLegend.Visibility = relevant && !_chartLegendCollapsed ? Visibility.Visible : Visibility.Collapsed;
        var help = _chartLegendCollapsed ? Strings.ShowChartResources : Strings.HideChartResources;
        ChartResourcesToggle.ToolTip = help;
        AutomationProperties.SetHelpText(ChartResourcesToggle, help);
        ChartResourcesToggle.SetExpanded(!_chartLegendCollapsed);
        UpdateChartMinimumHeight();
    }

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
        UpdateChartLegend();
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

    private void OnChartLegendSizeChanged(object sender, SizeChangedEventArgs e) => UpdateChartMinimumHeight();

    private void UpdateChartMinimumHeight()
    {
        ChartAreaRow.MinHeight = _preferences.ChartPanelCollapsed ? 0 : 160 +
            (ChartLegend.Visibility == Visibility.Visible
                ? ChartLegendHost.ActualHeight + ChartLegendHost.Margin.Top + ChartLegendHost.Margin.Bottom : 0);
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
