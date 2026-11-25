using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private async Task<ResourceSample> CaptureUnavailableDiskTargetAsync(MonitorPreferences original, string missing, string outputDirectory, CancellationToken cancellationToken)
    {
        window.Preferences = original with { SecondaryDrive = missing, ShowSecondaryDisk = true };
        window.SecondaryDiskMetric.Toggle.IsChecked = true;
        window.RefreshPresentation();
        await Task.Delay(3000, cancellationToken);
        var unavailable = window.Session.History.Latest!.Resources;
        if (unavailable.SecondaryDiskName != missing || unavailable.SecondaryDiskActivePercent is not null ||
            unavailable.DiskActivePercent is null || unavailable.MemoryPercent is null)
            throw new InvalidOperationException(
                $"An unavailable second volume disrupted native resource sampling: " +
                $"secondary={unavailable.SecondaryDiskName}, secondary active={unavailable.SecondaryDiskActivePercent}, " +
                $"primary={unavailable.DiskName}, primary active={unavailable.DiskActivePercent}, " +
                $"primary bytes={unavailable.DiskBytesPerSecond}, memory={unavailable.MemoryPercent}.");
        await CaptureAsync(Path.Combine(outputDirectory, "disk-unavailable.png"), 1, cancellationToken);
        return unavailable;
    }

    private async Task<ResourceSample?> CaptureAvailableDiskTargetAsync(string? second, string outputDirectory, CancellationToken cancellationToken)
    {
        ResourceSample? dualNative = null;
        if (second is not null)
        {
            window.Preferences = window.Preferences with { SecondaryDrive = second };
            await Task.Delay(3000, cancellationToken);
            dualNative = window.Session.History.Latest!.Resources;
            if (dualNative.SecondaryDiskName != second || dualNative.SecondaryDiskActivePercent is null ||
                dualNative.DiskActivePercent is null)
                throw new InvalidOperationException(
                    "Two available volumes did not produce independent native readings.");
            await CaptureAsync(Path.Combine(outputDirectory, "disk-native.png"), 1, cancellationToken);
        }

        return dualNative;
    }

    private void VerifyIndependentDiskSeriesToggle()
    {
        window.SecondaryDiskMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 32) != 0 || window.Preferences.ShowSecondaryDisk)
            throw new InvalidOperationException("Second disk trace did not hide independently.");
        window.SecondaryDiskMetric.Toggle.IsChecked = true;
        if ((window.OverallGraph.VisibleMetrics & 32) == 0)
            throw new InvalidOperationException("Second disk trace did not restore.");
    }

    private void VerifyAbsentDiskDoesNotGainFocus(MonitorPreferences original)
    {
        // Without a second drive its hidden toggle can stay checked; hiding every other series must not focus it.
        window.Preferences = original with
        {
            SecondaryDrive = null, ResourceChartType = ResourceChartType.Focus, FocusMetric = 0
        };
        window.BuildChartSelectors();
        MetricReadout[] series =
        [
            window.CpuMetric, window.MemoryMetric, window.DiskMetric, window.NetworkMetric, window.GpuMetric,
            window.SecondaryDiskMetric
        ];
        series[5].Toggle.IsChecked = true;
        foreach (var metric in series[..5]) metric.Toggle.IsChecked = false;
        if (window.Preferences.FocusMetric == 5)
            throw new InvalidOperationException("Hiding every series focused the absent second disk.");
        window.Preferences = original;
        for (var i = 0; i < series.Length; i++) series[i].Toggle.IsChecked = (original.SeriesMask & (1 << i)) != 0;
        window.Preferences = original;
        window.BuildChartSelectors();
        window.RefreshPresentation();
    }

    private void VerifyDiskSettingsSelections(MonitorPreferences original, string? second, string missing)
    {
        foreach (var selection in new[] { second ?? missing, null })
        {
            var dialog = new SettingsWindow(original with { SecondaryDrive = second ?? missing }) { Owner = window };
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                var selector = dialog.SecondaryDriveSelector;
                selector.SelectedItem = selector.Items.Cast<object>().Single(item => selection is null
                    ? item.ToString() == Strings.None
                    : item.ToString()!.StartsWith(selection, StringComparison.Ordinal));
                dialog.SaveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }, DispatcherPriority.ContextIdle);
            if (dialog.ShowDialog() != true || dialog.Result?.PrimaryDrive != original.PrimaryDrive ||
                dialog.Result.SecondaryDrive != selection)
                throw new InvalidOperationException("Saving the disk selectors did not return the selected volumes.");
        }
    }

}
