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
            window.UsageTab.IsChecked == true ? window.UsageTab : window.ProcessesTab;
        try
        {
            window.Width = 1000;
            window.Height = 1000;
            window.ProcessesTab.IsChecked = true;
            window.SetActivityPanelCollapsed(false);
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
    }
}
