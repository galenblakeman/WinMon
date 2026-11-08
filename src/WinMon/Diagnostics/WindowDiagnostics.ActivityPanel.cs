using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private async Task CaptureActivityPanelAsync(string output, CancellationToken cancellationToken)
    {
        var original = window.Preferences;
        var originalWidth = window.Width;
        var originalHeight = window.Height;
        var originalTab = window.ServicesTab.IsChecked == true ? window.ServicesTab :
            window.UsageTab.IsChecked == true ? window.UsageTab :
            window.ConnectionsTab.IsChecked == true ? window.ConnectionsTab : window.ProcessesTab;
        try
        {
            var tabs = new[] { window.ProcessesTab, window.ServicesTab, window.UsageTab, window.ConnectionsTab };
            var tabStore = new PreferenceStore(Path.Combine(output, "activity-tab-settings.json"));
            for (var selected = 0; selected < tabs.Length; selected++)
            {
                tabs[selected].IsChecked = true;
                if (window.Preferences.SelectedActivityTab != selected)
                    throw new InvalidOperationException("Selecting an activity tab did not update its saved preference.");
                window.SetActivityPanelCollapsed(true);
                await tabStore.SaveAsync(window.Preferences, cancellationToken);
                var tabRestored = new MainWindow(true) { Preferences = await tabStore.LoadAsync(cancellationToken) };
                try
                {
                    tabRestored.RestoreActivityTab();
                    tabRestored.ApplyActivityPanel();
                    var restoredTabs = new[] { tabRestored.ProcessesTab, tabRestored.ServicesTab,
                        tabRestored.UsageTab, tabRestored.ConnectionsTab };
                    if (restoredTabs[selected].IsChecked != true || !tabRestored.Preferences.ActivityPanelCollapsed ||
                        tabRestored.ActivityBody.Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("The saved activity tab did not restore with its collapsed panel.");
                    restoredTabs[(selected + 1) % tabs.Length].IsChecked = true;
                    if (tabRestored.Preferences.ActivityPanelCollapsed ||
                        tabRestored.Preferences.SelectedActivityTab != (selected + 1) % tabs.Length)
                        throw new InvalidOperationException("Changing a restored activity tab did not expand and remember it.");
                }
                finally { tabRestored.CloseForFixture(); }
            }
            window.Width = 1000;
            window.Height = 1000;
            window.ProcessesTab.IsChecked = true;
            window.SetActivityPanelCollapsed(false);
            window.UpdateLayout();
            foreach (var height in new[] { 900, 720, 600 })
            {
                window.Height = height;
                foreach (var tab in new[] { window.ProcessesTab, window.ServicesTab, window.UsageTab })
                {
                    tab.IsChecked = true;
                    window.UpdateLayout();
                    CheckHeader(window.ResourceHeader);
                    CheckHeader(window.ActivityHeader);
                    var chartRight = window.ChartPanelToggle.TranslatePoint(new Point(window.ChartPanelToggle.ActualWidth, 0), window.FullView).X;
                    var activityRight = window.ActivityPanelToggle.TranslatePoint(new Point(window.ActivityPanelToggle.ActualWidth, 0), window.FullView).X;
                    if (Math.Abs(chartRight - activityRight) > 0.5)
                        throw new InvalidOperationException("Chart and activity chevrons have different right margins.");
                    CheckHeader(tab == window.ProcessesTab ? window.Processes.ProcessHeader :
                        tab == window.ServicesTab ? window.ServiceView.ToolbarHeader : window.UsageView.ToolbarHeader);
                    await CaptureAsync(Path.Combine(output, $"headers-{tab.Name}-{height}.png"), 1, cancellationToken);
                }
            }
            window.ProcessesTab.IsChecked = true;
            window.Height = 1000;
            window.UpdateLayout();
            window.ChartProcessDivider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            window.ChartProcessDivider.RaiseEvent(new DragDeltaEventArgs(0, -35) { RoutedEvent = Thumb.DragDeltaEvent });
            window.ChartProcessDivider.RaiseEvent(new DragCompletedEventArgs(0, -35, false) { RoutedEvent = Thumb.DragCompletedEvent });
            window.UpdateLayout();
            var panelHeight = window.ProcessesAreaRow.ActualHeight;
            var chartHeight = window.ChartAreaRow.ActualHeight;
            var resizedRatio = window.ProcessesAreaRow.Height.Value / window.ChartAreaRow.Height.Value;
            window.SetCompactView(true);
            if (Math.Abs(window.Preferences.ActivityPanelRatio - resizedRatio) > 0.001)
                throw new InvalidOperationException("Entering Compact view did not retain the resized split for a later restart.");
            window.SetCompactView(false);
            window.UpdateLayout();
            var search = window.Processes.ProcessSearchBox.Text;
            await CaptureAsync(Path.Combine(output, "activity-panel-expanded.png"), 1, cancellationToken);
            window.Processes.ProcessSearchBox.Focus();
            Click(window.ActivityPanelToggle);
            window.UpdateLayout();
            if (!window.Preferences.ActivityPanelCollapsed || window.ActivityBody.IsVisible ||
                window.ChartProcessDivider.IsVisible || window.ProcessesAreaRow.ActualHeight > 45 ||
                window.ChartAreaRow.ActualHeight < chartHeight + panelHeight - 45 ||
                !window.ActivityPanelToggle.IsKeyboardFocused ||
                AutomationProperties.GetName(window.ActivityPanelToggle) != Strings.ExpandActivityPanel)
                throw new InvalidOperationException("Activity collapse did not reclaim space, preserve the header, or restore focus.");
            var savedRatio = window.Preferences.ActivityPanelRatio;
            window.SetActivityPanelCollapsed(false);
            window.UpdateLayout();
            window.OverallGraph.Focus();
            Click(window.ChartPanelToggle);
            window.UpdateLayout();
            if (!window.Preferences.ChartPanelCollapsed || window.ChartBody.IsVisible ||
                window.ChartProcessDivider.IsVisible || window.ChartAreaRow.ActualHeight > 0.5 ||
                !window.ChartPanelToggle.IsKeyboardFocused ||
                window.ProcessesAreaRow.ActualHeight < chartHeight + panelHeight - 1 ||
                AutomationProperties.GetName(window.ChartPanelToggle) != Strings.ExpandChartPanel)
                throw new InvalidOperationException("Chart collapse did not reclaim space or restore focus.");
            await CaptureAsync(Path.Combine(output, "chart-panel-collapsed-dark.png"), 1, cancellationToken);
            window.SetActivityPanelCollapsed(true);
            window.UpdateLayout();
            if (window.ChartBody.IsVisible || window.ActivityBody.IsVisible || !window.ChartPanelToggle.IsVisible ||
                !window.ActivityPanelToggle.IsVisible)
                throw new InvalidOperationException("Both collapsed panels must retain their restore buttons.");
            await CaptureAsync(Path.Combine(output, "chart-panel-both-collapsed.png"), 1, cancellationToken);
            window.SetCompactView(true);
            window.SetCompactView(false);
            window.SetActivityPanelCollapsed(false);
            if (!window.Preferences.ChartPanelCollapsed || window.Preferences.ActivityPanelRatio != savedRatio)
                throw new InvalidOperationException("Collapsing both panels or Compact view changed the saved split.");
            window.Preferences = window.Preferences with { Theme = "Light" };
            window.ApplyTheme();
            window.Width = window.MinWidth;
            window.Height = 600;
            window.UpdateLayout();
            await CaptureAsync(Path.Combine(output, "chart-panel-collapsed-light-minimum.png"), 1.5, cancellationToken);
            var chartStore = new PreferenceStore(Path.Combine(output, "chart-panel-settings.json"));
            await chartStore.SaveAsync(window.Preferences, cancellationToken);
            var chartRestored = new MainWindow(true) { Preferences = await chartStore.LoadAsync(cancellationToken) };
            try
            {
                chartRestored.ApplyActivityPanel();
                if (chartRestored.ChartBody.Visibility != Visibility.Collapsed)
                    throw new InvalidOperationException("Saved chart collapse did not restore in a new window.");
                chartRestored.SetChartPanelCollapsed(false);
                if (Math.Abs(chartRestored.ProcessesAreaRow.Height.Value / chartRestored.ChartAreaRow.Height.Value - savedRatio) > 0.001)
                    throw new InvalidOperationException("Chart expansion did not restore the saved split.");
            }
            finally
            {
                chartRestored.CloseForFixture();
            }
            Click(window.ChartPanelToggle);
            window.Width = 1000;
            window.Height = 1000;
            window.UpdateLayout();
            if (!window.ChartBody.IsVisible || Math.Abs(window.ChartAreaRow.ActualHeight - chartHeight) > 1)
                throw new InvalidOperationException("Chart expansion did not restore its previous size.");
            window.SetActivityPanelCollapsed(true);
            window.Preferences = window.Preferences with { Theme = original.Theme };
            window.ApplyTheme();
            await CaptureAsync(Path.Combine(output, "activity-panel-collapsed-dark.png"), 1, cancellationToken);
            window.Preferences = window.Preferences with { Theme = "Light" };
            window.ApplyTheme();
            window.Width = window.MinWidth;
            window.UpdateLayout();
            await CaptureAsync(Path.Combine(output, "activity-panel-collapsed-light-minimum.png"), 1.5, cancellationToken);
            window.Height = 800;
            window.SetCompactView(true);
            window.SetCompactView(false);
            if (!window.Preferences.ActivityPanelCollapsed || window.ActivityBody.Visibility != Visibility.Collapsed ||
                window.Preferences.ActivityPanelRatio != savedRatio)
                throw new InvalidOperationException("Compact view changed the saved activity-panel state or split.");
            Click(window.ActivityPanelToggle);
            window.UpdateLayout();
            if (window.ProcessesAreaRow.ActualHeight < 344 || window.ChartAreaRow.ActualHeight < 160)
                throw new InvalidOperationException("Expanding after a resize violated the chart or activity minimum.");
            window.Width = 1000;
            window.Height = 1000;
            window.UpdateLayout();
            if (Math.Abs(window.ProcessesAreaRow.ActualHeight - panelHeight) > 1 ||
                window.Processes.ProcessSearchBox.Text != search)
                throw new InvalidOperationException("Activity expansion did not restore its previous size and search.");
            foreach (var tab in new[] { window.ProcessesTab, window.ServicesTab, window.UsageTab })
            {
                window.SetActivityPanelCollapsed(true);
                tab.IsChecked = true;
                Click(tab);
                if (window.Preferences.ActivityPanelCollapsed || window.ActivityBody.Visibility != Visibility.Visible)
                    throw new InvalidOperationException("Clicking an activity tab did not expand the panel.");
            }
            // Recreate the layout from persisted settings, as startup does, without touching the user's store.
            window.SetActivityPanelCollapsed(true);
            var store = new PreferenceStore(Path.Combine(output, "activity-panel-settings.json"));
            await store.SaveAsync(window.Preferences, cancellationToken);
            var restored = new MainWindow(true) { Preferences = await store.LoadAsync(cancellationToken) };
            try
            {
                restored.ApplyActivityPanel();
                if (restored.ActivityBody.Visibility != Visibility.Collapsed || restored.Preferences.ActivityPanelRatio != savedRatio)
                    throw new InvalidOperationException("Saved activity-panel state did not restore in a new window.");
                restored.SetActivityPanelCollapsed(false);
                if (Math.Abs(restored.ProcessesAreaRow.Height.Value / restored.ChartAreaRow.Height.Value - savedRatio) > 0.001)
                    throw new InvalidOperationException("A restarted window did not restore the saved split.");
            }
            finally
            {
                restored.CloseForFixture();
            }
            await File.WriteAllTextAsync(Path.Combine(output, "activity-panel-passed.txt"),
                "Collapse, restore, resized split, all tabs, focus, Compact view and persisted restart checks passed.", cancellationToken);
        }
        finally
        {
            window.Preferences = original;
            window.ApplyActivityPanel();
            originalTab.IsChecked = true;
            window.Width = originalWidth;
            window.Height = originalHeight;
            window.ApplyTheme();
            window.RefreshPresentation();
        }

        static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        void CheckHeader(FrameworkElement header)
        {
            var bounds = header.TransformToAncestor(window.FullView).TransformBounds(new Rect(header.RenderSize));
            if (Math.Abs(bounds.Left) > 0.5 || Math.Abs(bounds.Right - window.FullView.ActualWidth) > 0.5)
                throw new InvalidOperationException($"Header {header.Name} does not span the full view at height {window.Height}: {bounds}.");
            // Layout clipping can occur without ClipToBounds when row minimums exceed the available height.
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(header); parent is FrameworkElement element;
                 parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            {
                var clip = LayoutInformation.GetLayoutClip(element);
                if (clip is null) continue;
                var local = header.TransformToAncestor(element).TransformBounds(new Rect(header.RenderSize));
                if (local.Left < clip.Bounds.Left - 0.5 || local.Right > clip.Bounds.Right + 0.5)
                    throw new InvalidOperationException($"Header {header.Name} is clipped horizontally at height {window.Height} by {element.GetType().Name}.");
            }
        }
    }
}
