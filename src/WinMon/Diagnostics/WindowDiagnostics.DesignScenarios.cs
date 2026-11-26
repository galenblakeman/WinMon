using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Preferences;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureDesignChartsAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        foreach (var chartType in Enum.GetValues<ResourceChartType>())
        {
            fixture.Preferences = fixture.Preferences with { ResourceChartType = chartType };
            fixture.BuildChartSelectors();
            fixture.RefreshVisiblePresentation();
            visual.Measure(new Size(1000, 820));
            visual.Arrange(new Rect(0, 0, 1000, 820));
            visual.UpdateLayout();
            if (fixture.OverallGraph.ChartType != chartType ||
                !ReferenceEquals(fixture.ChartTypeSelector.SelectedItem,
                    ChartChoiceItem(fixture, chartType, chartType == ResourceChartType.Focus ? 0 : null)))
                throw new InvalidOperationException($"The {chartType} design fixture did not select the chart.");
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory,
                $"chart-design-{chartType.ToString().ToLowerInvariant()}.png"), 1, cancellationToken);
        }

        fixture.Preferences = fixture.Preferences with { ResourceChartType = ResourceChartType.LogicalProcessors };
        fixture.BuildChartSelectors();
        fixture.SelectedSample = fixture.Session.History.Samples[92];
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-logicalprocessors-capture.png"),
            1, cancellationToken);
        fixture.SelectedSample = null;
        fixture.Preferences = fixture.Preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(836, 820));
        visual.Arrange(new Rect(0, 0, 836, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual,
            Path.Combine(outputDirectory, "chart-design-logicalprocessors-light-narrow.png"),
            1, cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Dark" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        var manyProcessors = fixture.Session.History.Samples.Select((sample, second) => sample with
        {
            LogicalProcessorPercents = Enumerable.Range(0, 64)
                .Select(processor => (double?)((second * (processor + 3) + processor * 11) % 100)).ToArray()
        }).ToArray();
        fixture.OverallGraph.SetData(manyProcessors, 120, null);
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-logicalprocessors-64-narrow.png"),
            1, cancellationToken);
        fixture.OverallGraph.SetData([manyProcessors[^1] with { LogicalProcessorPercents = null }], 120, null);
        visual.UpdateLayout();
        await CaptureVisualAsync(visual,
            Path.Combine(outputDirectory, "chart-design-logicalprocessors-unavailable.png"),
            1, cancellationToken);
        fixture.Preferences = fixture.Preferences with { ResourceChartType = ResourceChartType.Focus, FocusMetric = 3 };
        fixture.BuildChartSelectors();
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-focus-network.png"), 1,
            cancellationToken);
        fixture.SelectedSample = fixture.Session.History.Samples[92];
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-focus-capture.png"), 1,
            cancellationToken);
        fixture.SelectedSample = null;
        fixture.Preferences = fixture.Preferences with
        {
            ResourceChartType = ResourceChartType.Overlay,
            FocusMetric = 0
        };
        fixture.BuildChartSelectors();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-150.png"), 1.5,
            cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-200.png"), 2, cancellationToken);
    }

    private static async Task CaptureDesignAppFiltersAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        fixture.Processes.AppsOnlyCheckBox.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        if (!fixture.Preferences.AppsOnly ||
            fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 3 ||
            fixture.Processes.OtherRow.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("App Only did not filter the individual rows before the top limit.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-compact.png"), 1, cancellationToken);
        fixture.Wide = true;
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1000, 820));
        visual.Arrange(new Rect(0, 0, 1000, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-wide.png"), 1, cancellationToken);
        fixture.Wide = false;
        fixture.Processes.ProcessSearchBox.Text = "chrome";
        fixture.Processes.GroupedChip.IsChecked = true;
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        var filteredGroup = AutomationProperties.GetName(fixture.Processes.Rows[0]);
        if (fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 1 ||
            !filteredGroup.Contains(Strings.Format(Strings.Processes, 2), StringComparison.Ordinal) ||
            !filteredGroup.Contains(MainWindow.Percent(9.4), StringComparison.Ordinal) ||
            !filteredGroup.Contains($" · {Strings.ProcessKindApp} · {Strings.Threads}:", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "App Only did not preserve grouped totals with search and historical selection.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-grouped-history.png"), 1,
            cancellationToken);
        fixture.Processes.ProcessSearchBox.Text = "no-matching-app";
        fixture.RefreshVisiblePresentation();
        if (fixture.Processes.EmptyLabel.Visibility != Visibility.Visible ||
            fixture.Processes.EmptyLabel.Text != Strings.NoAppsMatchCurrentView)
            throw new InvalidOperationException("App Only did not explain an empty filtered view.");
        fixture.Processes.AppsOnlyCheckBox.IsChecked = false;
        fixture.Processes.ProcessSearchBox.Clear();
        fixture.SelectedSample = null;
        fixture.RefreshVisiblePresentation();
        fixture.Processes.IndividualChip.IsChecked = true;
    }

    private static async Task CaptureDesignDisksAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        fixture.Preferences = fixture.Preferences with { PrimaryDrive = "C:", SecondaryDrive = "D:" };

        fixture.Preferences = fixture.Preferences with
        {
            ResourceChartType = ResourceChartType.SmallMultiples, FocusMetric = 5
        };
        fixture.BuildChartSelectors();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1000, 820));
        visual.Arrange(new Rect(0, 0, 1000, 820));
        visual.UpdateLayout();
        if (fixture.ChartTypeSelector.Items.OfType<ComboBoxItem>().Count(item =>
                item.Tag is ResourceChartChoice { Type: ResourceChartType.Focus }) != 6 ||
            fixture.OverallGraph.VisibleMetrics != 63)
            throw new InvalidOperationException("The second volume is missing from the chart choices.");
        foreach (var (metric, drive) in new[] { (2, "C:"), (5, "D:") })
        {
            var expected = Strings.Format(Strings.ChartFocusResourceChoice,
                Strings.Format(Strings.DiskLabel, drive));
            if (!Equals(ChartChoiceItem(fixture, ResourceChartType.Focus, metric).Content, expected))
                throw new InvalidOperationException($"The {drive} focus choice has the wrong drive label.");
        }

        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-two-disks.png"), 1,
            cancellationToken);
        fixture.Preferences = fixture.Preferences with { ResourceChartType = ResourceChartType.Focus };
        fixture.BuildChartSelectors();
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (!ReferenceEquals(fixture.ChartTypeSelector.SelectedItem,
                ChartChoiceItem(fixture, ResourceChartType.Focus, 5)))
            throw new InvalidOperationException("The second volume focus choice was not selected.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "chart-design-focus-second-disk.png"), 1,
            cancellationToken);
        fixture.Preferences = fixture.Preferences with { ResourceChartType = ResourceChartType.Overlay };
        fixture.BuildChartSelectors();

        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(720, 820));
        visual.Arrange(new Rect(0, 0, 720, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design-150.png"), 1.5, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design-200.png"), 2, cancellationToken);
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!fixture.SnapshotSecondaryDiskName.Text.Contains("D:") || !CpuGraph.DescribeSample(fixture.SelectedSample)
                .Contains(Strings.Format(Strings.SecondaryDiskHistoryLine, "D:", "", ""), StringComparison.Ordinal))
            throw new InvalidOperationException("Historical inspection lost the second volume.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-history.png"), 1, cancellationToken);
        fixture.SelectedSample = null;
        var diskSettings = new SettingsWindow(fixture.Preferences);
        var settingsVisual = (FrameworkElement)diskSettings.Content;
        settingsVisual.Measure(new Size(444, 601));
        settingsVisual.Arrange(new Rect(0, 0, 444, 601));
        settingsVisual.UpdateLayout();
        await CaptureVisualAsync(settingsVisual, Path.Combine(outputDirectory, "disks-settings-dark.png"), 1,
            cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-light.png"), 1, cancellationToken);
        var lightSettings = new SettingsWindow(fixture.Preferences);
        var lightSettingsVisual = (FrameworkElement)lightSettings.Content;
        lightSettingsVisual.Measure(new Size(444, 601));
        lightSettingsVisual.Arrange(new Rect(0, 0, 444, 601));
        lightSettingsVisual.UpdateLayout();
        await CaptureVisualAsync(lightSettingsVisual, Path.Combine(outputDirectory, "disks-settings-light.png"), 1,
            cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Dark", SecondaryDrive = null };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
    }

    private static async Task CaptureDesignRankingAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        fixture.Processes.ProcessSortSelector.SelectedItem = "GPU";
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (fixture.Preferences.ProcessSort != ProcessSort.Gpu || !AutomationProperties
                .GetName(fixture.Processes.Rows[0])
                .StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("GPU ranking failed to include a CPU-idle GPU process.");
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!AutomationProperties.GetName(fixture.Processes.Rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical GPU ranking failed.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "gpu-process-sort.png"), 1, cancellationToken);
        fixture.SelectedSample = null;
        fixture.RefreshVisiblePresentation();
        // Memory ranks by the largest working set (devenv); I/O by the busiest read/write rate (chrome).
        foreach (var (sort, expected) in new[] { (Strings.Memory, "devenv"), (Strings.IO, "chrome") })
        {
            fixture.Processes.ProcessSortSelector.SelectedItem = sort;
            fixture.RefreshVisiblePresentation();
            visual.UpdateLayout();
            if (!AutomationProperties.GetName(fixture.Processes.Rows[0])
                    .StartsWith(expected, StringComparison.Ordinal) ||
                fixture.Processes.MemoryHeader.Text.EndsWith('▾') != (sort == Strings.Memory))
                throw new InvalidOperationException($"{sort} ranking did not put {expected} first or mark its column.");
            if (sort == Strings.Memory)
                await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "memory-process-sort.png"), 1,
                    cancellationToken);
        }

        // Clicking a column heading drives the same dropdown.
        fixture.Processes.IoHeader.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        if (fixture.Preferences.ProcessSort != ProcessSort.Io ||
            fixture.Processes.ProcessSortSelector.SelectedItem as string != "I/O")
            throw new InvalidOperationException("Clicking the I/O heading did not switch the ranking dropdown.");
        fixture.Processes.ProcessSortSelector.SelectedItem = "CPU";
        // Grouped: the two chrome processes become one row with summed CPU, and memory switches to private bytes.
        fixture.Processes.GroupedChip.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (!fixture.Preferences.GroupProcesses || fixture.Processes.MemoryHeader.Text != Strings.Memory ||
            AutomationProperties.GetName(fixture.Processes.Rows[0]) is not { } groupedName ||
            !groupedName.StartsWith("chrome", StringComparison.Ordinal) ||
            !groupedName.Contains(Strings.Format(Strings.Processes, 2), StringComparison.Ordinal) ||
            !groupedName.Contains(MainWindow.Percent(9.4), StringComparison.Ordinal))
            throw new InvalidOperationException("Grouped mode did not combine the chrome processes.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "grouped.png"), 1, cancellationToken);
        fixture.Processes.IndividualChip.IsChecked = true;
        fixture.RefreshVisiblePresentation();
    }

    private static async Task CaptureDesignProcessCountsAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, DateTimeOffset anchor, CancellationToken cancellationToken)
    {
        var rankedSample = fixture.Session.History.Latest! with
        {
            Timestamp = anchor.AddSeconds(1),
            MonotonicTimestamp = Stopwatch.Frequency * 121,
            Processes =
            [
                .. Enumerable.Range(1, 60).Select(i =>
                    new ProcessUsage(1000 + i, i, $"process-{i:00}", 1d / i))
            ]
        };
        fixture.Session.History.Add(rankedSample);
        foreach (var count in new[] { 5, 10, 15, 20, 50 })
        {
            fixture.Processes.ProcessCountSelector.SelectedItem = count;
            fixture.RefreshVisiblePresentation();
            ArrangeDesignSnapshot(visual, 600, 752);
            if (fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != count ||
                fixture.Preferences.TopProcessCount != count)
                throw new InvalidOperationException($"Selecting {count} did not update the live process count.");
        }

        if (fixture.Processes.ProcessScroll.ScrollableHeight <= 0 ||
            !fixture.TitleBarArea.IsAncestorOf(fixture.SettingsButton) ||
            !fixture.TitleBarArea.IsAncestorOf(fixture.InfoButton))
            throw new InvalidOperationException(
                "Long process lists must scroll while title-bar actions remain available at minimum size.");
        fixture.Processes.ProcessScroll.ScrollToBottom();
        ArrangeDesignSnapshot(visual, 600, 752);
        if (fixture.Processes.ProcessScroll.VerticalOffset <= 0)
            throw new InvalidOperationException("The process list failed to scroll.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-50.png"), 1, cancellationToken);
        fixture.Processes.ProcessCountSelector.SelectedItem = TopCountChoices.All;
        fixture.RefreshVisiblePresentation();
        ArrangeDesignSnapshot(visual, 600, 752);
        if (fixture.Preferences.TopProcessCount != 0 ||
            fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) < 60 ||
            fixture.Processes.OtherRow.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException(
                "All did not render every available process beyond the old 50-row limit.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-all.png"), 1, cancellationToken);
        await CaptureDesignHistoricalProcessCountsAsync(fixture, visual, outputDirectory, rankedSample, anchor, cancellationToken);
    }

    private static async Task CaptureDesignHeadersAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        // Approved compact-header mockup: same wide viewport, dual disks, grouped list, ten-minute range.
        fixture.SelectedSample = null;
        fixture.RefreshVisiblePresentation();
        if (!Equals(fixture.Processes.ProcessCountSelector.SelectedItem, TopCountChoices.All))
            throw new InvalidOperationException("Leaving Capture did not restore the live All choice.");
        fixture.Wide = true;
        fixture.Preferences = fixture.Preferences with
        {
            PrimaryDrive = "C:", SecondaryDrive = "D:", RetentionSeconds = 3600, ChartRangeSeconds = 600,
            ChartPalette = "Ocean"
        };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        fixture.Processes.GroupedChip.IsChecked = true;
        fixture.Processes.ProcessCountSelector.SelectedItem = 20;
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1301, 1137));
        visual.Arrange(new Rect(0, 0, 1301, 1137));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "compact-headers-wide.png"), 1,
            cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "compact-headers-wide-light.png"), 1,
            cancellationToken);

        // Light-theme target: source screenshot dimensions, Classic palette and two-minute range.
        fixture.Preferences = fixture.Preferences with { ChartPalette = "Classic", ChartRangeSeconds = 120 };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1116, 795));
        visual.Arrange(new Rect(0, 0, 1116, 795));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "glacier-light.png"), 1, cancellationToken);
    }

    private static async Task CaptureDesignSnapshotsAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        // Exercise actual selection and layout at every corner, then resize the held selection.
        fixture.Preferences = fixture.Preferences with
        {
            Theme = "Dark", ChartRangeSeconds = 120, ChartPalette = "Vivid"
        };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        foreach (var (width, height) in new[] { (905d, 940d), (720d, 800d), (600d, 800d) })
        {
            fixture.InlineProcessToolbar = width >= MainWindow.InlineProcessToolbarWidth;
            fixture.Preferences = fixture.Preferences with { SecondaryDrive = width < 720 ? null : "D:" };
            fixture.RefreshVisiblePresentation();
            ArrangeDesignSnapshot(visual, width, height);
            fixture.LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (fixture.LiveModeButton.IsChecked != true || fixture.SnapshotModeButton.IsChecked == true ||
                fixture.SelectedSample is not null || fixture.SnapshotLayer.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Live mode did not clear the chart selection.");
            var switchCenter = fixture.ChartInvestigationActions
                .TranslatePoint(new Point(fixture.ChartInvestigationActions.ActualWidth / 2, 0), fixture.ResourceHeader).X;
            if (Math.Abs(switchCenter - fixture.ResourceHeader.ActualWidth / 2) > 1)
                throw new InvalidOperationException("The chart investigation actions are not centered in the header.");
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"chart-mode-live-{width}.png"), 1,
                cancellationToken);
            fixture.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (!ReferenceEquals(fixture.SelectedSample, fixture.Session.History.Latest) ||
                fixture.SnapshotModeButton.IsChecked != true || fixture.LiveModeButton.IsChecked == true ||
                fixture.SnapshotLayer.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Snapshot mode did not select the newest recorded sample.");
            ArrangeDesignSnapshot(visual, width, height);
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"chart-mode-snapshot-{width}.png"), 1,
                cancellationToken);
            foreach (var (name, right, bottom) in new[]
                     {
                         ("top-left", false, false), ("top-right", true, false), ("bottom-left", false, true),
                         ("bottom-right", true, true)
                     })
            {
                var plot = fixture.OverallGraph.PlotBounds;
                fixture.OverallGraph.SelectAt(
                    new Point(right ? plot.Right : plot.Left, bottom ? plot.Bottom : plot.Top));
                fixture.RefreshVisiblePresentation();
                ArrangeDesignSnapshot(visual, width, height);
                AssertDesignSnapshotContained(fixture);
                if (fixture.SelectedSample is null ||
                    fixture.SnapshotCpu.Text != MainWindow.Percent(fixture.SelectedSample.TotalCpuPercent))
                    throw new InvalidOperationException("The selected snapshot did not match the clicked sample.");
                await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"snapshot-{width}-{name}.png"), 1,
                    cancellationToken);
            }
        }
    }

    private static async Task CaptureDesignSnapshotReportAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, CancellationToken cancellationToken)
    {
        // Copy from a held historical sample without touching the user's Windows clipboard.
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        string? copied = null;
        fixture.CopySnapshot(value => copied = value);
        if (copied is null ||
            !copied.Contains(Strings.Format(Strings.ExportSnapshotTime, fixture.SelectedSample.Timestamp)) ||
            !copied.Contains(Strings.SnapshotExportAverages) ||
            fixture.SnapshotCopyButton.Content as string != Strings.Copied ||
            !ReferenceEquals(fixture.SnapshotReport?.Sample, fixture.SelectedSample))
            throw new InvalidOperationException(
                "Copy did not export the held snapshot with processes or confirm success.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "clipboard.txt"), copied, cancellationToken);
        fixture.ResetSnapshotCopyCaption();
        if (fixture.SnapshotCopyButton.Content as string != Strings.CopySnapshot)
            throw new InvalidOperationException("Copy confirmation did not reset.");
        // A shorter graph must scroll its values while keeping the live action within the panel.
        ArrangeDesignSnapshot(visual, 600, 540);
        AssertDesignSnapshotContained(fixture);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-short.png"), 1, cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Light", SecondaryDrive = "D:" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        ArrangeDesignSnapshot(visual, 905, 940);
        AssertDesignSnapshotContained(fixture);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-150.png"), 1.5,
            cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-200.png"), 2, cancellationToken);
        fixture.SelectedSample = fixture.Session.History.Samples[0];
        fixture.Session.History.Resize(1);
        fixture.RefreshVisiblePresentation();
        ArrangeDesignSnapshot(visual, 905, 940);
        if (fixture.SnapshotNote.Text != Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindow ||
            fixture.SnapshotReadings.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("An expired snapshot displayed stale readings.");
        copied = null;
        fixture.CopySnapshot(value => copied = value);
        if (fixture.SnapshotCopyButton.IsEnabled || copied is not null)
            throw new InvalidOperationException("An expired snapshot remained available for copying.");
        fixture.LiveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        fixture.RefreshVisiblePresentation();
        if (fixture.SelectedSample is not null || fixture.SnapshotLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The snapshot action did not return to live.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "snapshot.json"),
            JsonSerializer.Serialize(new
            {
                Corners = 12, ResizedWhileSelected = true, ShortChart = true, LightAndDark = true,
                Expiry = true, ReturnToLive = true, CenteredChartInvestigationActions = true, LatestSampleAction = true,
                SnapshotCopy = true,
                RenderScales = new[] { 1d, 1.5, 2d }
            }), cancellationToken);
    }

    private static async Task CaptureDesignHistoricalProcessCountsAsync(MainWindow fixture, FrameworkElement visual,
        string outputDirectory, MonitorSample rankedSample, DateTimeOffset anchor, CancellationToken cancellationToken)
    {
        fixture.SelectedSample = rankedSample;
        fixture.Session.History.Add(rankedSample with
        {
            Timestamp = anchor.AddSeconds(2), MonotonicTimestamp = Stopwatch.Frequency * 122, Processes = []
        });
        fixture.RefreshVisiblePresentation();
        if (fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 10 ||
            fixture.Preferences.TopProcessCount != 0 || fixture.Processes.ProcessCountSelector.IsEnabled)
            throw new InvalidOperationException("Capture must show top 10 apps while preserving the live All preference.");
        ArrangeDesignSnapshot(visual, 600, 752);
        if (!ReferenceEquals(fixture.SelectedSample, rankedSample) ||
            fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 10)
            throw new InvalidOperationException(
                "Capture replaced the historical sample or used live processes.");
        if (fixture.Processes.ProcessScroll.ActualHeight < 108)
            throw new InvalidOperationException("Historical details left too little room for the process list.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-history.png"), 1,
            cancellationToken);
    }

    private static void ArrangeDesignSnapshot(FrameworkElement visual, double width, double height)
    {
        visual.Measure(new Size(width, height));
        visual.Arrange(new Rect(0, 0, width, height));
        visual.UpdateLayout();
    }

    private static void AssertDesignSnapshotContained(MainWindow fixture)
    {
        var panel = fixture.SnapshotPanel.TransformToAncestor(fixture.SnapshotLayer)
            .TransformBounds(new Rect(fixture.SnapshotPanel.RenderSize));
        var button = fixture.LiveButton.TransformToAncestor(fixture.SnapshotLayer)
            .TransformBounds(new Rect(fixture.LiveButton.RenderSize));
        var copy = fixture.SnapshotCopyButton.TransformToAncestor(fixture.SnapshotLayer)
            .TransformBounds(new Rect(fixture.SnapshotCopyButton.RenderSize));
        if (!fixture.OverallGraph.PlotBounds.Contains(panel) || !panel.Contains(button) || button.Height < 24 ||
            !panel.Contains(copy) || copy.Height < 24)
            throw new InvalidOperationException(
                $"Snapshot panel or live action escaped the chart: {panel}, button {button}, plot {fixture.OverallGraph.PlotBounds}.");
    }

}
