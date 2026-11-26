using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Preferences;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using RadioButton = System.Windows.Controls.RadioButton;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private async Task VerifyDiagnosticSnapshotTransitionsAsync(CancellationToken cancellationToken)
    {
        if (window.Session.History.Samples.Count < 3 || window.Session.History.Samples[^1].TotalCpuPercent is null)
            throw new InvalidOperationException("Native CPU samples did not arrive in the live window.");
        window.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var heldSnapshot = window.SelectedSample;
        if (heldSnapshot is null || window.SnapshotModeButton.IsChecked != true)
            throw new InvalidOperationException("The native Snapshot action did not hold a sample.");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (!ReferenceEquals(window.SelectedSample, heldSnapshot) ||
            ReferenceEquals(window.Session.History.Latest, heldSnapshot))
            throw new InvalidOperationException("Recording must advance while the snapshot stays fixed.");
        window.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (ReferenceEquals(window.SelectedSample, heldSnapshot) ||
            !ReferenceEquals(window.SelectedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Clicking Snapshot again must select the newest recorded sample.");
        window.LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window.SelectedSample is not null || window.LiveModeButton.IsChecked != true ||
            window.SnapshotLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The native Live action did not release the snapshot.");
    }

    private void VerifyDiagnosticSeriesToggles()
    {
        window.MemoryMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 2) != 0)
            throw new InvalidOperationException("Memory series did not hide.");
        window.MemoryMetric.Toggle.IsChecked = true;
        if (window.OverallGraph.VisibleMetrics != 31)
            throw new InvalidOperationException("Series visibility did not restore.");
        window.GpuMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 16) != 0 || window.Preferences.ShowGpu)
            throw new InvalidOperationException("GPU series did not hide.");
        window.GpuMetric.Toggle.IsChecked = true;
        if (window.OverallGraph.VisibleMetrics != 31 || !window.Preferences.ShowGpu)
            throw new InvalidOperationException("GPU series did not restore.");
    }

    private async Task CaptureDiagnosticProcessViewsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        window.Processes.GroupedChip.IsChecked = true;
        window.RefreshPresentation();
        if (window.Processes.Rows[0].Visibility != Visibility.Visible ||
            (window.Processes.OtherRow.Visibility == Visibility.Visible && AutomationProperties
                .GetName(window.Processes.OtherRow)
                ?.Contains(Strings.Executables, StringComparison.Ordinal) != true))
            throw new InvalidOperationException("Grouped mode showed no native processes or mislabeled the remainder.");
        await CaptureAsync(Path.Combine(outputDirectory, "grouped-native.png"), 1, cancellationToken);
        window.Processes.IndividualChip.IsChecked = true;
        window.Processes.ProcessCountSelector.IsDropDownOpen = true;
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (window.Processes.ProcessCountSelector.Template.FindName("PART_Popup",
                    window.Processes.ProcessCountSelector) is not Popup
                {
                    Child: FrameworkElement menu
                })
                throw new InvalidOperationException("The process count menu did not open.");
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(outputDirectory, "process-count-menu.png"), 1,
                cancellationToken);
        }
        finally
        {
            window.Processes.ProcessCountSelector.IsDropDownOpen = false;
        }
    }

    private async Task CaptureDiagnosticHistorySelectionAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        // Switching range and widening the window keep the same recording; wide rows gain a PID line.
        var count = window.Session.History.Samples.Count;
        if (window.RangeSelector.Children.OfType<RadioButton>().FirstOrDefault(c => c.Tag is 600) is not { } tenMinutes)
            throw new InvalidOperationException("The 10-minute range chip is missing.");
        tenMinutes.IsChecked = true;
        window.Width = 1000;
        window.UpdateLayout();
        if (window.Preferences.EffectiveChartRange != 600 || !window.Wide ||
            window.Session.History.Samples.Count < count)
            throw new InvalidOperationException("Range or width change did not apply to the existing recording.");
        window.SelectedSample = window.Session.History.Samples[^2];
        window.RefreshPresentation();
        if (window.SnapshotLayer.Visibility != Visibility.Visible || window.Processes.SelectionLabel.Text !=
            Strings.Format(
                Strings.ProcessesAtSAverage,
                window.SelectedSample.Timestamp, ProcessRanking.DefaultWindowSeconds, Strings.IdleGroupMembersMayBeOmitted) ||
            window.SnapshotMemory.Text.Length == 0)
            throw new InvalidOperationException("Historical selection was not presented.");
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide.png"), 1, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide-150.png"), 1.5, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide-200.png"), 2, cancellationToken);
        window.Session.History.Resize(1);
        window.RefreshPresentation();
        if (window.Processes.SelectionLabel.Text != Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindowReturn)
            throw new InvalidOperationException("An evicted selection was silently replaced.");
        window.Session.History.Resize(window.Preferences.RetentionSeconds);
        window.ReturnToLive();
        if (window.SelectedSample is not null)
            throw new InvalidOperationException("Return to live retained a selection.");
    }

    private async Task VerifyDiagnosticHiddenAndMinimizedRecordingAsync(CancellationToken cancellationToken)
    {
        var beforeHidden = window.PresentedSample;
        var hiddenCount = window.Session.History.Samples.Count;
        window.Hide();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        // Capture drains run at Background priority; observe them before the normal-priority test continuation.
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, cancellationToken);
        if (window.Session.History.Samples.Count <= hiddenCount ||
            !ReferenceEquals(window.PresentedSample, beforeHidden))
            throw new InvalidOperationException(
                $"Hidden recording stopped or presentation continued updating: samples {hiddenCount} -> " +
                $"{window.Session.History.Samples.Count}, presentation unchanged " +
                $"{ReferenceEquals(window.PresentedSample, beforeHidden)}, visible {window.IsVisible}.");
        window.Show();
        if (!ReferenceEquals(window.PresentedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Restoring the window did not refresh the latest sample.");
        var beforeMinimized = window.PresentedSample;
        var minimizedCount = window.Session.History.Samples.Count;
        window.WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, cancellationToken);
        if (window.Session.History.Samples.Count <= minimizedCount ||
            !ReferenceEquals(window.PresentedSample, beforeMinimized))
            throw new InvalidOperationException("Minimized recording stopped or presentation continued updating.");
        window.WindowState = WindowState.Normal;
        if (!ReferenceEquals(window.PresentedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Restoring the minimized window did not refresh the latest sample.");
    }

    private async Task VerifyDiagnosticRecordingDuringUiStallAsync(CancellationToken cancellationToken)
    {
        // A stalled UI thread must not delay capture: intervals stay on schedule and the chart draws no gap.
        var stallCount = window.Session.History.Samples.Count;
        Thread.Sleep(3500);
        await Task.Delay(1500, cancellationToken);
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle, cancellationToken);
        var stalled = window.Session.History.Samples.Skip(stallCount - 1).ToArray();
        var longestInterval = stalled
            .Zip(stalled.Skip(1), (a, b) => (b.MonotonicTimestamp - a.MonotonicTimestamp) / (double)Stopwatch.Frequency)
            .DefaultIfEmpty().Max();
        if (stalled.Length < 5 || longestInterval > 2.5 || stalled.Skip(1).Any(s => s.IsGap))
            throw new InvalidOperationException(
                $"A UI stall delayed sampling: {stalled.Length - 1} samples, longest interval {longestInterval:0.00}s.");
    }

    private async Task CaptureDiagnosticLightViewsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        window.Preferences = window.Preferences with { Theme = "Light" };
        window.ApplyTheme();
        await CaptureAsync(Path.Combine(outputDirectory, "light.png"), 1, cancellationToken);
        window.Processes.ProcessCountSelector.SelectedItem = 20;
        await CaptureAsync(Path.Combine(outputDirectory, "light-scroll.png"), 1, cancellationToken);
        window.Processes.ProcessCountSelector.SelectedItem = 5;
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-light.png"), cancellationToken);
        await CaptureMetricDetailsAsync(outputDirectory, "light", cancellationToken);
        var settings = new SettingsWindow(window.Preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate))
            { Owner = window };
        settings.Show();
        settings.UpdateLayout();
        if (settings.Content is FrameworkElement settingsVisual)
            await CaptureVisualAsync(settingsVisual, Path.Combine(outputDirectory, "settings.png"), 1,
                cancellationToken);
        settings.Close();
    }

}
