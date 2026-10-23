using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Monitoring;
using Path = System.Windows.Shapes.Path;

// ReSharper disable StringLiteralTypo

namespace WinMon;

public partial class MainWindow
{
    private void ValidateProcessToolbar()
    {
        if (!_inlinePinToolbar) return;
        var right = 0.0;
        foreach (var control in new FrameworkElement[]
                 {
                     ProcessRankingControls, ProcessSearchBorder, AppsOnlyCheckBox, PinnedOnlyCheckBox,
                     ProcessGroupingControls
                 })
        {
            var bounds = control.TransformToAncestor(ProcessToolbarGrid).TransformBounds(new Rect(control.RenderSize));
            if (Grid.GetRow(control) != 0 || bounds.Left < right - 0.5 ||
                bounds.Right > ProcessToolbarGrid.ActualWidth + 0.5 ||
                bounds.Top < -0.5 || bounds.Bottom > ProcessToolbarGrid.ActualHeight + 0.5)
                throw new InvalidOperationException(
                    $"Process toolbar control {control.Name} overlaps, clips, or wraps at width {ProcessToolbarGrid.ActualWidth}.");
            right = bounds.Right;
        }

        if (ProcessSearchBorder.ActualWidth < ProcessSearchBorder.MinWidth)
            throw new InvalidOperationException("The compact process toolbar lost its minimum search width.");
    }

    internal static async Task CapturePinsFixtureAsync(string output, CancellationToken cancellationToken)
    {
        var fixture = new MainWindow(true);
        var visual = (FrameworkElement)fixture.Content;
        fixture._wide = true;
        fixture._inlineProcessToolbar = true;
        fixture._preferences = fixture._preferences with { TopProcessCount = 5, SecondaryDrive = "D:" };
        var code = new ProcessUsage(12003, 3, "Code", 4.7, 840UL * 1_048_576, 3_000_000)
            { AppName = "Visual Studio Code", HasAppWindow = true, GpuPercent = 1.3 };
        var chrome = new ProcessUsage(12000, 1, "chrome", 8.7, 1260UL * 1_048_576, 3_000_000)
            { AppName = "Google Chrome", HasAppWindow = true, GpuPercent = 6.4, ImagePath = @"C:\Apps\chrome.exe" };
        var exited = new ProcessUsage(17420, 7, "WinMon.Performance", 12, 200_000_000, 1234);
        var anchor = DateTimeOffset.Now;
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 92) / 2d, 2));
            fixture._history.Add(new CpuSample(anchor.AddSeconds(i - 119), 32.8 + 40 * pulse,
            [
                code, chrome,
                new ProcessUsage(12006, 5, "MsMpEng", 3.3, 286UL * 1_048_576, 3_000_000)
                    { AppName = "Antimalware Service Executable", HasAppWindow = false },
                new ProcessUsage(4, 4, "System", 2.3, 84UL * 1_048_576, 80_000) { HasAppWindow = false },
                new ProcessUsage(12004, 8, "Taskmgr", 2, 195UL * 1_048_576, 80_000)
                    { AppName = "Task Manager", HasAppWindow = true },
                new ProcessUsage(12010, 9, "OneDrive", 0.8, 175UL * 1_048_576, 80_000)
                    { AppName = "Microsoft OneDrive", HasAppWindow = true }
            ], 0, MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    UsedMemoryBytes = 23_056_165_683, TotalMemoryBytes = 32UL * 1_073_741_824,
                    DiskName = "C:", DiskActivePercent = 6.4 + 60 * pulse, SecondaryDiskName = "D:",
                    SecondaryDiskActivePercent = 3.5 + 18 * pulse,
                    GpuPercent = 10.5 + 40 * pulse, GpuMemoryUsedBytes = 2_254_857_830,
                    GpuMemoryTotalBytes = 8UL * 1_073_741_824,
                    NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 955_000 + 10_000_000 * pulse, SendBytesPerSecond = 150_000,
                    CpuClockMhz = 4200
                }
            });
        }

        fixture.ToggleProcessPin(code, false);
        fixture.ToggleProcessPin(chrome, false);
        fixture.ToggleProcessPin(exited, false);
        fixture.ApplyTheme();
        Draw();
        fixture._pinnedRows[1].RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = PreviewMouseDownEvent });
        Draw();
        if (!fixture._processSelection.Contains(chrome, false) ||
            Descendants(fixture.ProcessActions).OfType<TextBlock>().All(t => t.Text != chrome.Name))
            throw new InvalidOperationException("Clicking a process did not populate the process action bar.");
        var filledPin = Descendants(fixture._pinnedRows[0]).OfType<Button>().Single();
        if (fixture.PinSummary.Visibility != Visibility.Visible ||
            fixture.PinnedCountLabel.Text != Strings.Format(Strings.PinnedProcessCount, 3) ||
            fixture.PinSummary.Parent != fixture.CoverageLabel.Parent)
            throw new InvalidOperationException("Pinned count was not displayed beside CPU coverage.");
        var pinPath = (Path)filledPin.Template.FindName("CaptionIcon", filledPin);
        if (!ButtonIcon.GetFilled(filledPin) || pinPath.Fill is null ||
            !Equals(pinPath.Fill, filledPin.Foreground) ||
            fixture.CpuMetric.DetailLabel.Text != $"{4.2:0.00} GHz" ||
            fixture.NetworkMetric.ValueLabel.Inlines.FirstInline is not Run { Text: var networkText } ||
            !networkText.StartsWith('↓') || !fixture.NetworkMetric.DetailLabel.Text.StartsWith('↑') ||
            fixture.NetworkMetric.DetailLabel.Text.Contains('↓') ||
            fixture.GpuMetric.DetailLabel.Text != $"{2.1:0.0} / {8.0:0.0} GiB" ||
            fixture.MemoryHeader.Text != $"{Strings.Memory} MiB")
            throw new InvalidOperationException(
                $"UI presentation failed: filled={ButtonIcon.GetFilled(filledPin)}, fill={pinPath.Fill}, foreground={filledPin.Foreground}, clock={fixture.CpuMetric.DetailLabel.Text}, network={fixture.NetworkMetric.ValueLabel.Text}, memory={fixture.MemoryHeader.Text}.");
        if (fixture._pinnedRows.Count != 3 || fixture._rows.Count(r => r.Visibility == Visibility.Visible) != 4 ||
            !AutomationProperties.GetName(fixture._pinnedRows[0])
                .StartsWith("Visual Studio Code", StringComparison.Ordinal) ||
            !AutomationProperties.GetName(fixture._pinnedRows[2])
                .Contains(Strings.ProcessExited, StringComparison.Ordinal))
            throw new InvalidOperationException("Pinned order, ranking deduplication or exit state failed.");
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "pins-dark.png"), 1.254, cancellationToken);
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "pins-dark-150.png"), 1.5, cancellationToken);
        fixture.PinnedOnlyCheckBox.IsChecked = true;
        Draw();
        if (fixture._rows.Any(r => r.Visibility == Visibility.Visible) ||
            fixture.RankedDivider.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Pinned only left ranked rows visible.");
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "pins-only.png"), 1, cancellationToken);
        fixture.ProcessSearchBox.Text = "Code";
        Draw();
        if (Descendants(fixture.ProcessActions).OfType<TextBlock>().All(t => t.Text != chrome.Name))
            throw new InvalidOperationException("Filtering a selected process out of the list cleared its action bar.");
        if (fixture._pinnedRows.Count(r => r.Visibility == Visibility.Visible) != 1)
            throw new InvalidOperationException("Search did not filter pinned rows.");
        fixture.ProcessSearchBox.Clear();
        fixture.PinnedOnlyCheckBox.IsChecked = false;
        fixture.AppsOnlyCheckBox.IsChecked = true;
        Draw();
        if (fixture._pinnedRows.Count(r => r.Visibility == Visibility.Visible) != 2)
            throw new InvalidOperationException("App Only did not filter pinned rows.");
        fixture.AppsOnlyCheckBox.IsChecked = false;
        fixture.GroupedChip.IsChecked = true;
        Draw();
        if (fixture._pinnedRows.Any(r => r.Visibility == Visibility.Visible) ||
            fixture.PinSummary.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Individual pins leaked into grouped view.");
        fixture.ToggleProcessPin(chrome, true);
        Draw();
        if (fixture._pinnedRows.Count(r => r.Visibility == Visibility.Visible) != 1)
            throw new InvalidOperationException("Grouped pin was not displayed.");
        fixture.IndividualChip.IsChecked = true;
        fixture._selectedSample = fixture._history.Samples[^2];
        Draw();
        string? copied = null;
        fixture.CopySnapshot(text => copied = text);
        if (fixture._snapshotReport?.Processes.Count != 7 || copied is null ||
            !copied.Contains(Strings.ProcessNotRecorded, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Snapshot copying did not preserve pinned rows and missing-state labels.");
        fixture._selectedSample = null;
        fixture._preferences = fixture._preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        Draw();
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "pins-light.png"), 1.254, cancellationToken);
        fixture._preferences = fixture._preferences with { Theme = "Dark" };
        fixture.ApplyTheme();
        Draw(836, 840);
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "pins-minimum.png"), 1, cancellationToken);
        var initialChartHeight = fixture.ChartAreaRow.Height;
        var initialProcessHeight = fixture.ProcessesAreaRow.Height;
        var chartHeight = fixture.ChartAreaRow.ActualHeight;
        var processHeight = fixture.ProcessesAreaRow.ActualHeight;
        DragDivider(-40);
        if (Math.Abs(fixture.ChartAreaRow.ActualHeight - (chartHeight - 40)) > 1 ||
            Math.Abs(fixture.ProcessesAreaRow.ActualHeight - (processHeight + 40)) > 1)
            throw new InvalidOperationException("Dragging the divider did not redistribute chart and process height.");
        DragDivider(-10_000);
        if (Math.Abs(fixture.ChartAreaRow.ActualHeight - fixture.ChartAreaRow.MinHeight) > 1)
            throw new InvalidOperationException("The divider did not protect the chart minimum height.");
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "divider-chart-minimum.png"), 1,
            cancellationToken);
        DragDivider(10_000);
        if (Math.Abs(fixture.ProcessesAreaRow.ActualHeight - fixture.ProcessesAreaRow.MinHeight) > 1 ||
            fixture.ProcessScroll.ActualHeight < 108)
            throw new InvalidOperationException(
                "The divider did not protect the process section and scrolling list minimums.");
        await CaptureVisualAsync(visual, System.IO.Path.Combine(output, "divider-process-minimum.png"), 1,
            cancellationToken);
        fixture.ChartAreaRow.Height = initialChartHeight;
        fixture.ProcessesAreaRow.Height = initialProcessHeight;
        Draw(836, 840);
        // Invoke the real row button; the row can change position after this click.
        var pinButton = Descendants(fixture._pinnedRows[0]).OfType<Button>().Single();
        pinButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Draw();
        if (fixture._processPins.Contains(code, false) ||
            fixture._pinnedRows.Count(r => r.Visibility == Visibility.Visible) != 2)
            throw new InvalidOperationException("The row button did not unpin its process.");
        fixture._snapshotCopiedTimer.Stop();
        await File.WriteAllTextAsync(System.IO.Path.Combine(output, "pins.json"), JsonSerializer.Serialize(new
        {
            StableOrder = true, Deduplicated = true, Exited = true, PinnedOnly = true, Search = true, AppsOnly = true,
            GroupedPins = true, SnapshotCopy = true, RowButtonUnpin = true, Themes = new[] { "Dark", "Light" }
        }), cancellationToken);
        return;

        void DragDivider(double delta)
        {
            fixture.ChartProcessDivider.RaiseEvent(new DragStartedEventArgs(0, 0)
                { RoutedEvent = Thumb.DragStartedEvent });
            fixture.ChartProcessDivider.RaiseEvent(new DragDeltaEventArgs(0, delta)
                { RoutedEvent = Thumb.DragDeltaEvent });
            fixture.ChartProcessDivider.RaiseEvent(new DragCompletedEventArgs(0, delta, false)
                { RoutedEvent = Thumb.DragCompletedEvent });
            Draw(836, 840);
        }

        void Draw(double width = 1000, double height = 1000)
        {
            fixture._inlineProcessToolbar = width >= InlineProcessToolbarWidth;
            fixture._inlinePinToolbar = fixture._inlineProcessToolbar;
            fixture.RefreshVisiblePresentation();
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
            fixture.ValidateProcessToolbar();
        }

        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }
    }
}