using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private async Task CaptureChartHelpAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var helpPreferences = window.Preferences;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = helpPreferences with { Theme = theme };
                window.ApplyTheme();
                foreach (var (target, name) in new FrameworkElement[] { window.LiveCaption, window.ChartTypeSelector }
                             .Select((target, index) => (target, index == 0 ? "header" : "selector")))
                {
                    if (target.ToolTip is not ToolTip { Content: StackPanel panel } tooltip ||
                        panel.Children.OfType<ContentControl>().Single().Content is not StackPanel help)
                        throw new InvalidOperationException("Chart controls did not explain the chart types.");
                    var text = help.Children.OfType<Border>().SelectMany(border =>
                        ((StackPanel)border.Child).Children.OfType<TextBlock>()).Select(block => block.Text);
                    if (!text.SequenceEqual(new[]
                        {
                            Strings.ChartOverlay, Strings.ChartOverlayHelp,
                            Strings.ChartSmallMultiples, Strings.ChartSmallMultiplesHelp,
                            Strings.ChartHeatmap, Strings.ChartHeatmapHelp,
                            Strings.ChartLogicalProcessors, Strings.ChartLogicalProcessorsHelp,
                            Strings.ChartFocus, Strings.ChartFocusHelp
                        }))
                        throw new InvalidOperationException(
                            "Chart help did not contain five separate labeled descriptions.");
                    tooltip.PlacementTarget = target;
                    tooltip.Placement = PlacementMode.Bottom;
                    try
                    {
                        tooltip.IsOpen = true;
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                        tooltip.UpdateLayout();
                        await CaptureVisualAsync(tooltip,
                            Path.Combine(outputDirectory, $"chart-help-{name}-{theme.ToLowerInvariant()}.png"),
                            1, cancellationToken);
                    }
                    finally
                    {
                        tooltip.IsOpen = false;
                    }
                }
            }
        }
        finally
        {
            window.Preferences = helpPreferences;
            window.ApplyTheme();
        }

        window.ChartTypeSelector.IsDropDownOpen = true;
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (window.ChartTypeSelector.Template.FindName("PART_Popup", window.ChartTypeSelector) is not Popup
                {
                    Child: FrameworkElement menu
                })
                throw new InvalidOperationException("The combined chart menu did not open.");
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(outputDirectory, "chart-menu.png"), 1, cancellationToken);
        }
        finally
        {
            window.ChartTypeSelector.IsDropDownOpen = false;
        }
    }

    private void ConfigureChartScenario(ResourceChartType chartType)
    {
        window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, chartType,
            chartType == ResourceChartType.Focus ? 0 : null);
        if (window.Preferences.ResourceChartType != chartType || window.OverallGraph.ChartType != chartType)
            throw new InvalidOperationException($"The {chartType} chart choice did not apply.");
        if (chartType != ResourceChartType.LogicalProcessors && window.Preferences.ChartResourcesCollapsed)
            window.ChartResourcesToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window.ChartLegend.IsVisible != (chartType != ResourceChartType.LogicalProcessors))
            throw new InvalidOperationException($"The {chartType} resource legend has the wrong visibility.");
        if (window.ChartResourcesToggle.IsVisible != (chartType != ResourceChartType.LogicalProcessors))
            throw new InvalidOperationException($"The {chartType} resource disclosure has the wrong visibility.");
        if (chartType != ResourceChartType.LogicalProcessors)
        {
            var selectedResources = window.Preferences.SeriesMask;
            window.ChartResourcesToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            window.RefreshPresentation();
            if (window.ChartLegend.IsVisible || !window.OverallGraph.IsVisible ||
                window.Preferences.SeriesMask != selectedResources)
                throw new InvalidOperationException(
                    "Hiding resource controls changed the plot or selected resources.");
            window.ChartResourcesToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (!window.ChartLegend.IsVisible || window.Preferences.SeriesMask != selectedResources)
                throw new InvalidOperationException(
                    "Showing resource controls did not restore the legend and selections.");
        }

        foreach (var metric in new[]
                 {
                     window.CpuMetric, window.MemoryMetric, window.DiskMetric,
                     window.NetworkMetric, window.GpuMetric
                 })
            if (metric.ShowSeriesToggle || !metric.Toggle.IsDescendantOf(window.ChartLegend) ||
                !metric.Focusable || string.IsNullOrEmpty(AutomationProperties.GetName(metric.Toggle)))
                throw new InvalidOperationException(
                    "Resource controls must belong to the chart, with accessible gauge details.");
    }

    private async Task CaptureChartScenarioAsync(ResourceChartType chartType, string outputDirectory,
        CancellationToken cancellationToken)
    {
        ConfigureChartScenario(chartType);
        if (chartType == ResourceChartType.Focus)
        {
            window.CpuMetric.Toggle.IsChecked = false;
            if (window.Preferences.FocusMetric != 1 || window.OverallGraph.FocusMetric != 1 ||
                !ReferenceEquals(window.ChartTypeSelector.SelectedItem,
                    ChartChoiceItem(window, ResourceChartType.Focus, 1)))
                throw new InvalidOperationException("Hiding the focused resource did not choose a visible one.");
            window.CpuMetric.Toggle.IsChecked = true;
        }

        if (chartType == ResourceChartType.LogicalProcessors)
        {
            if (window.Session.History.Latest?.LogicalProcessorPercents is not { Count: > 0 } processors ||
                processors.Count > 64 || !processors.Any(value => value is not null) ||
                processors.Any(value => value is < 0 or > 100))
                throw new InvalidOperationException("Native logical-processor readings did not arrive.");
            window.CpuMetric.Toggle.IsChecked = false;
            window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, ResourceChartType.Overlay);
            window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, chartType);
            if (window.CpuMetric.Toggle.IsChecked != true)
                throw new InvalidOperationException("Selecting logical processors did not restore the CPU series.");
        }

        window.OverallGraph.SelectLatest();
        if (window.SelectedSample is null || window.SnapshotLayer.Visibility != Visibility.Visible)
            throw new InvalidOperationException($"The {chartType} chart did not support capture inspection.");
        window.ReturnToLive();
        await CaptureAsync(Path.Combine(outputDirectory, $"chart-{chartType.ToString().ToLowerInvariant()}.png"),
            1, cancellationToken);
    }
}
