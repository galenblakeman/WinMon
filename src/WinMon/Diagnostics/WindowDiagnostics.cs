using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Preferences;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using CheckBox = System.Windows.Controls.CheckBox;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using ToolTip = System.Windows.Controls.ToolTip;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics(MainWindow window)
{
    internal async Task CaptureSearchDiagnosticsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        if (window.Session.History.Latest?.TotalCpuPercent is null)
            throw new InvalidOperationException("Search diagnostics need native samples.");
        window.Processes.ProcessSearchBox.Text = "__winmon_search_no_match__";
        if (window.Processes.ClearProcessSearchButton.Visibility != Visibility.Visible ||
            window.Processes.ProcessSearchPlaceholder.Visibility != Visibility.Collapsed ||
            window.Processes.Rows.Any(row => row.Visibility == Visibility.Visible) ||
            window.Processes.EmptyLabel.Visibility != Visibility.Visible ||
            window.Processes.EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
            throw new InvalidOperationException("Process search did not filter the visible rows.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-empty.png"), 1, cancellationToken);
        window.Processes.OnClearProcessSearch(window, new RoutedEventArgs());
        if (window.Processes.ProcessSearchBox.Text.Length != 0 ||
            window.Processes.ClearProcessSearchButton.Visibility != Visibility.Collapsed ||
            window.Processes.ProcessSearchPlaceholder.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Clearing process search did not restore the idle field.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-idle.png"), 1, cancellationToken);
        var originalWidth = window.Width;
        window.Width = 886;
        window.UpdateLayout();
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-wide.png"), 1, cancellationToken);
        window.Width = originalWidth;
        foreach (var (sort, header) in new[]
                 {
                     (ProcessSort.Threads, window.Processes.ThreadsHeader),
                     (ProcessSort.Handles, window.Processes.HandlesHeader)
                 })
        {
            window.Processes.ProcessSortSelector.SelectedIndex = (int)sort;
            if (window.Preferences.ProcessSort != sort || !header.Text.EndsWith('▾'))
                throw new InvalidOperationException($"The {sort} dropdown did not update its column heading.");
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-live.png"), 1, cancellationToken);
            window.Processes.GroupedChip.IsChecked = true;
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-grouped.png"), 1, cancellationToken);
            window.SelectedSample = window.Session.History.Samples[^2];
            window.RefreshPresentation();
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-history.png"), 1, cancellationToken);
            window.SelectedSample = null;
            window.Processes.IndividualChip.IsChecked = true;
            window.Processes.ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
            header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            if (window.Preferences.ProcessSort != sort ||
                window.Processes.ProcessSortSelector.SelectedIndex != (int)sort)
                throw new InvalidOperationException($"Clicking the {sort} heading did not update the dropdown.");
        }

        window.Processes.ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "search-passed.txt"),
            "Native search filtering, clear, count sorting and header checks passed.", cancellationToken);
    }

    /// <summary>Record whole-process CPU, memory and GC cost of live monitoring, after a warm-up that fills the compact history.</summary>
    /// <param name="outputDirectory">Directory for profile.json.</param>
    /// <param name="seconds">Measured duration after warm-up.</param>
    /// <param name="cancellationToken">Cancel the profile run.</param>
    internal async Task CaptureProfileAsync(string outputDirectory, int seconds, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
        using var self = Process.GetCurrentProcess();
        var startCpu = self.TotalProcessorTime;
        var startAllocated = GC.GetTotalAllocatedBytes(true);
        var startCollections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var workingSets = new List<long>();
        var privateBytes = new List<long>();
        var managedHeaps = new List<long>();
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed.TotalSeconds < seconds)
        {
            await Task.Delay(1000, cancellationToken);
            // Process.Refresh re-enumerates every process on the machine; read only this process.
            var memory = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (GetProcessMemoryInfo(GetCurrentProcess(), ref memory, memory.Size))
            {
                workingSets.Add((long)memory.WorkingSetSize);
                privateBytes.Add((long)memory.PrivateUsage);
            }

            managedHeaps.Add(GC.GetGCMemoryInfo().HeapSizeBytes);
        }

        var wall = elapsed.Elapsed.TotalSeconds;
        var cpuSeconds = (self.TotalProcessorTime - startCpu).TotalSeconds;
        var samples = window.Session.History.Samples;
        var report = new
        {
            MeasuredSeconds = wall,
            LogicalProcessors = Environment.ProcessorCount,
            CpuPercentOfMachine = 100 * cpuSeconds / wall / Environment.ProcessorCount,
            CpuMillisecondsPerSecond = 1000 * cpuSeconds / wall,
            WorkingSetMiBAverage = MiB(workingSets),
            WorkingSetMiBMax = workingSets.Max() / 1_048_576d,
            PrivateMiBAverage = MiB(privateBytes),
            PrivateMiBMax = privateBytes.Max() / 1_048_576d,
            ManagedHeapMiBAverage = MiB(managedHeaps),
            AllocatedKiBPerSecond = (GC.GetTotalAllocatedBytes(true) - startAllocated) / 1024d / wall,
            Gen0Collections = GC.CollectionCount(0) - startCollections.Item1,
            Gen1Collections = GC.CollectionCount(1) - startCollections.Item2,
            Gen2Collections = GC.CollectionCount(2) - startCollections.Item3,
            RetainedSamples = samples.Count,
            RetainedProcessEntries = samples.Sum(s => s.Processes.Count),
            ProcessesPerSample = samples.Count == 0 ? 0 : samples[^1].Processes.Count,
            UnavailableProcesses = samples.Count == 0 ? 0 : samples[^1].UnavailableProcesses
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "profile.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return;

        static double MiB(IEnumerable<long> values)
        {
            return values.Average() / 1_048_576d;
        }
    }

    /// <summary>Exercise every full-view chart choice against native samples and export its visible state.</summary>
    internal async Task CaptureChartDiagnosticsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        if (window.ChartLegend.IsVisible || window.ChartResourcesToggle.ToolTip as string != Strings.ShowChartResources)
            throw new InvalidOperationException("Resource controls must start collapsed with a show-controls tooltip.");
        window.ChartResourcesToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window.Session.History.Samples.Count < 3)
            throw new InvalidOperationException("Native chart samples did not arrive.");
        if (window.ChartTypeSelector.Items.OfType<ComboBoxItem>().Count() != (window.Preferences.TemperatureChartsEnabled ? 10 : 9) ||
            window.ChartTypeSelector.Items.OfType<Separator>().Count() != 1)
            throw new InvalidOperationException("The combined chart menu has the wrong choices.");
        await CaptureChartHelpAsync(outputDirectory, cancellationToken);

        foreach (var chartType in Enum.GetValues<ResourceChartType>().Where(type =>
                     type != ResourceChartType.Temperature || window.Preferences.TemperatureChartsEnabled))
            await CaptureChartScenarioAsync(chartType, outputDirectory, cancellationToken);

        window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, ResourceChartType.Focus, 3);
        if (window.Preferences.FocusMetric != 3 || window.OverallGraph.FocusMetric != 3)
            throw new InvalidOperationException("The focus resource choice did not apply.");
        window.NetworkMetric.Toggle.IsChecked = false;
        window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, ResourceChartType.Focus, 3);
        if (window.NetworkMetric.Toggle.IsChecked != true || window.Preferences.FocusMetric != 3 ||
            window.OverallGraph.VisibleMetrics != 31)
            throw new InvalidOperationException("Selecting a hidden focus resource did not show it again.");
        await CaptureAsync(Path.Combine(outputDirectory, "chart-focus-network.png"), 1, cancellationToken);
        window.ChartTypeSelector.SelectedItem = ChartChoiceItem(window, ResourceChartType.Overlay);
        var legendPreferences = window.Preferences;
        var legendWidth = window.Width;
        try
        {
            window.Preferences = legendPreferences with { Language = "de", SecondaryDrive = "D:" };
            window.ApplyLanguage();
            window.Width = window.MinWidth;
            window.RefreshPresentation();
            window.UpdateLayout();
            foreach (var host in window.ChartLegend.Children.OfType<ContentControl>())
            {
                var toggle = (CheckBox)host.Content;
                var bounds = toggle.TransformToAncestor(window.ChartLegend)
                    .TransformBounds(new Rect(toggle.RenderSize));
                if (!toggle.IsVisible || bounds.Left < 0 || bounds.Right > window.ChartLegend.ActualWidth + 1 ||
                    bounds.Bottom > window.ChartLegend.ActualHeight + 1)
                    throw new InvalidOperationException(
                        "The narrow German resource legend clipped a configured resource.");
            }

            await CaptureAsync(Path.Combine(outputDirectory, "chart-legend-narrow-german.png"), 1, cancellationToken);
            window.CpuMetric.Toggle.Focus();
            window.SetChartPanelCollapsed(true);
            if (window.ChartLegend.IsVisible || !window.ChartPanelToggle.IsKeyboardFocused)
                throw new InvalidOperationException("Collapsing the chart did not hide its legend and recover focus.");
            window.SetChartPanelCollapsed(false);
            if (!window.ChartLegend.IsVisible)
                throw new InvalidOperationException("Expanding the chart did not restore its legend.");
        }
        finally
        {
            window.Preferences = legendPreferences;
            window.Width = legendWidth;
            window.ApplyLanguage();
            window.RefreshPresentation();
        }
    }

    private static ComboBoxItem ChartChoiceItem(MainWindow target, ResourceChartType type, int? metric = null)
    {
        return target.ChartTypeSelector.Items.OfType<ComboBoxItem>().Single(item =>
            item.Tag is ResourceChartChoice choice && choice.Type == type && choice.Metric == metric);
    }

    /// <summary>Validate the real recording window and export render evidence without changing user preferences.</summary>
    /// <param name="outputDirectory">Directory for diagnostic screenshots and measurements.</param>
    /// <param name="cancellationToken">Cancel the bounded diagnostic run.</param>
    /// <returns>A task that completes after the smoke checks and evidence writes.</returns>
    internal async Task CaptureDiagnosticAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        using var self = Process.GetCurrentProcess();
        var startCpu = self.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        await VerifyDiagnosticSnapshotTransitionsAsync(cancellationToken);
        var nativeResources = window.Session.History.Samples[^1].Resources;
        var nativeProcessGpu = window.Session.History.Samples[^1].Processes.Where(p => p.GpuPercent is not null)
            .OrderByDescending(p => p.GpuPercent).Take(10)
            .Select(p => new { p.ProcessId, p.Name, p.GpuPercent, p.GpuEngine, p.GpuId }).ToArray();
        if (nativeResources.MemoryPercent is null)
            throw new InvalidOperationException("Native physical-memory sampling did not arrive.");
        await CaptureChartDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureFeatureDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-dark.png"), cancellationToken);
        await CaptureMetricDetailsAsync(outputDirectory, "dark", cancellationToken);
        VerifyDiagnosticSeriesToggles();
        await CaptureSearchDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureDiskDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "compact.png"), 1, cancellationToken);
        await CaptureDiagnosticProcessViewsAsync(outputDirectory, cancellationToken);
        await CaptureDiagnosticHistorySelectionAsync(outputDirectory, cancellationToken);
        await VerifyDiagnosticHiddenAndMinimizedRecordingAsync(cancellationToken);
        await VerifyDiagnosticRecordingDuringUiStallAsync(cancellationToken);
        await CaptureDiagnosticLightViewsAsync(outputDirectory, cancellationToken);
        var idleStartCpu = self.TotalProcessorTime;
        var idleElapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        var idleCpu = (self.TotalProcessorTime - idleStartCpu).TotalSeconds / idleElapsed.Elapsed.TotalSeconds /
            Environment.ProcessorCount * 100;
        self.Refresh();
        await CaptureDesignFixtureAsync(outputDirectory, cancellationToken);
        var report = new
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            NativeSamples = window.Session.History.Samples.Count,
            LastTotalCpu = window.Session.History.Samples[^1].TotalCpuPercent,
            // Preserve the smoke report's public JSON property name.
            // ReSharper disable once RedundantAnonymousTypePropertyName
            NativeResources = nativeResources,
            NativeProcessGpu = nativeProcessGpu,
            ProcessGpuRanking = true,
            SeriesToggle = true,
            ProcessCountChoicesLiveAndHistorical = true,
            ProcessListScrollAtMinimumSize = true,
            IntegratedTitleBarHitTesting = true,
            ToolTipDarkAndLight = true,
            HistoricalResourceValues = true,
            window.Session.History.Samples[^1].UnavailableProcesses,
            ChartRangeAndWideLayout = true,
            HistoricalSelection = true,
            SelectionExpiry = true,
            PauseResumeWithGap = true,
            ReturnToLive = true,
            HiddenRecordingAndRestore = true,
            MinimizedRecordingAndRestore = true,
            RenderScales = new[] { 1.0, 1.5, 2.0 },
            RenderScaleLimitation =
                "Bitmap scale checks are not a substitute for changing actual desktop DPI or testing mixed-DPI monitor transitions.",
            MeasurementSeconds = elapsed.Elapsed.TotalSeconds,
            CpuPercentOfMachineDuringSmoke = (self.TotalProcessorTime - startCpu).TotalSeconds /
                elapsed.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
            WorkingSetBytes = self.WorkingSet64,
            QuietObservationSeconds = idleElapsed.Elapsed.TotalSeconds,
            QuietObservationCpuPercentOfMachine = idleCpu,
            MeasurementLimitation =
                "Short smoke measurement includes PNG rendering; the additional 15-second quiet observation excludes captures but is not a sustained overhead benchmark."
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "smoke.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    internal async Task CaptureFeatureDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        await CaptureFeatureTabHelpAsync(output, cancellationToken);
        if (window.Session.History.Samples.Count == 0) await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        VerifyFeatureHeaderWidths();
        CaptureWindowFrame(Path.Combine(output, "titlebar-window.png"));
        await CaptureFeatureExportsAsync(output, cancellationToken);
        await CaptureFeatureHardwareAsync(output, cancellationToken);
        var original = window.Preferences;
        await CaptureFeaturePalettesAsync(original, output, cancellationToken);
        await CaptureFeatureProcessMenuAsync(original, output, cancellationToken);
        await CaptureFeatureProcessDialogsAsync(original, output, cancellationToken);
        await CaptureActionDrawerAsync(output, cancellationToken);
        await CaptureActivityPanelAsync(output, cancellationToken);

        var settings = new SettingsWindow(window.Preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate))
            { Owner = window };
        try
        {
            settings.Show();
            settings.UpdateLayout();
            await CaptureVisualAsync((FrameworkElement)settings.Content, Path.Combine(output, "feature-settings.png"),
                1, cancellationToken);
        }
        finally
        {
            settings.Close();
        }

        await CaptureUpdateDiagnosticsAsync(output, cancellationToken);
        await CaptureLanguageSettingsDiagnosticsAsync(output, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "features.json"),
            JsonSerializer.Serialize(new
            {
                HardwareInfo = true, PaletteRenders = true, ProcessContextMenu = true, CsvExportDialog = true,
                HistoricalActionsDisabled = true, GroupTerminationOffered = true, ThemedProcessDialogs = true,
                ActionDrawer = true, SystemCounts = true, LanguageSelection = true,
                LiveLanguageSwitching = true, RecordingStatePreserved = true,
                Limitation =
                    "No destructive command invoked by the UI smoke; native termination is tested on an owned helper in the test suite."
            }), cancellationToken);
    }

    // The chevron and Show actions move the action bar on the live window; every tab follows.
    private async Task CaptureActionDrawerAsync(string output, CancellationToken cancellationToken)
    {
        var drawer = window.Processes.ProcessActionDrawer;
        var original = window.Preferences;
        if (window.Session.History.Latest?.Processes is not [var process, ..])
            throw new InvalidOperationException("No sampled process to select for the action drawer.");
        try
        {
            window.Processes.SelectProcess(process, false);
            window.UpdateLayout();
            // The tab strip shows the machine's counts where the old Hide actions toggle sat.
            if (new[] { window.ProcessCountValue, window.ThreadCountValue, window.HandleCountValue }.Any(v =>
                    v.Text.Length == 0 || v.Text == Strings.MissingReading) ||
                window.SystemCounts.ToolTip is not string counts || !counts.Contains(Strings.Handles))
                throw new InvalidOperationException("The tab strip did not show process, thread and handle counts.");
            // A failed live pass must clear live counts, but not the retained moment being inspected.
            var selected = window.SelectedSample;
            var error = window.ShownSamplingError;
            try
            {
                var sample = window.Session.History.Latest!;
                var expected = MainWindow.CompactCount(sample.Resources.ProcessCount);
                window.SelectedSample = sample;
                window.ShownSamplingError = SamplingError.ReadingsUnavailable;
                window.RefreshVisiblePresentation();
                if (window.ProcessCountValue.Text != expected)
                    throw new InvalidOperationException(
                        "A live sampling failure cleared the inspected sample's counts.");
                window.SelectedSample = null;
                window.RefreshVisiblePresentation();
                if (window.ProcessCountValue.Text != Strings.MissingReading)
                    throw new InvalidOperationException("A live sampling failure left stale live counts visible.");
            }
            finally
            {
                window.SelectedSample = selected;
                window.ShownSamplingError = error;
                window.RefreshVisiblePresentation();
            }

            var open = drawer.ActualHeight;
            var bar = window.Processes.ProcessActions;
            var hide = Descendants(bar).OfType<Button>().Single(b => b.Command == ActionDrawer.CollapseCommand);
            var hideBounds = hide.TransformToAncestor(bar).TransformBounds(new Rect(hide.RenderSize));
            if (!hide.IsEnabled || hideBounds.Right > bar.ActualWidth || hideBounds.Bottom > bar.ActualHeight)
                throw new InvalidOperationException(
                    $"The hide chevron is not usable inside the action bar: {hideBounds}.");
            await CaptureAsync(Path.Combine(output, "action-drawer-open.png"), 1, cancellationToken);
            ActionDrawer.CollapseCommand.Execute(null, hide);
            // The slide and the strip's rise take 240 ms.
            await Task.Delay(500, cancellationToken);
            window.UpdateLayout();
            if (!window.Preferences.ProcessActionsHidden || !window.ServiceView.ActionsHidden ||
                !window.UsageView.ActionsHidden || drawer.Peek.Visibility != Visibility.Visible ||
                drawer.PeekTitle != process.Name || drawer.ActualHeight > open - 20)
                throw new InvalidOperationException(
                    $"The chevron did not collapse the action bar to its strip: {drawer.ActualHeight} of {open}.");
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = window.Preferences with { Theme = theme };
                window.ApplyTheme();
                await CaptureAsync(Path.Combine(output, $"action-drawer-collapsed-{theme.ToLowerInvariant()}.png"), 1,
                    cancellationToken);
            }

            drawer.ShowButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(500, cancellationToken);
            window.UpdateLayout();
            if (window.Preferences.ProcessActionsHidden || window.ServiceView.ActionsHidden ||
                drawer.Peek.Visibility == Visibility.Visible || Math.Abs(drawer.ActualHeight - open) > 0.5)
                throw new InvalidOperationException(
                    $"Show actions did not restore the action bar: {drawer.ActualHeight} of {open}.");
        }
        finally
        {
            window.Preferences = original;
            window.ApplyTheme();
            window.Processes.ActionsHidden = window.ServiceView.ActionsHidden =
                window.UsageView.ActionsHidden = original.ProcessActionsHidden;
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

    private async Task CaptureUpdateDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var original = window.Preferences;
        var originalHeight = window.Height;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = original with { Theme = theme };
                window.ApplyTheme();
                window.ShowUpdateStatus(Strings.Format(Strings.UpdateAvailable, "1.0.3"), true);
                window.ReleaseNotesButton.Visibility = Visibility.Visible;
                window.UpdateNowButton.Visibility = Visibility.Visible;
                window.UpdateLayout();
                if (window.UpdateStatusText.ActualWidth < 60 || window.UpdateNowButton.ActualWidth < 50 ||
                    window.UpdateFooter.ActualHeight < 30)
                    throw new InvalidOperationException("The update footer controls do not fit the compact window.");
                await CaptureAsync(Path.Combine(output, $"update-footer-{theme.ToLowerInvariant()}.png"), 1,
                    cancellationToken);
            }

            // With room, the footer has a row of its own; shorter than the rows' minimums, it overlays their bottom.
            var content = (Grid)window.ActivityPanel.Parent;
            var rowsHeight = content.RowDefinitions.Sum(row => row.ActualHeight);
            if (rowsHeight <= window.FullView.ActualHeight - content.Margin.Top - content.Margin.Bottom + 0.5 &&
                Bounds(window.Processes).Bottom > Bounds(window.UpdateFooter).Top + 0.5)
                throw new InvalidOperationException(
                    $"The update footer covers the process list: rows={rowsHeight}, visible={window.FullView.ActualHeight}, processes={Bounds(window.Processes)}, footer={Bounds(window.UpdateFooter)}.");
            foreach (var height in new[] { 300, window.MinHeight })
            {
                window.Height = height;
                window.UpdateLayout();
                var footer = Bounds(window.UpdateFooter);
                if (footer.Top < Bounds(window.FullView).Top || footer.Bottom > Bounds(window.FullView).Bottom + 0.5)
                    throw new InvalidOperationException($"The update footer is not visible in a {height}-DIP window.");
            }

            await CaptureAsync(Path.Combine(output, "update-footer-short.png"), 1, cancellationToken);
            window.Height = originalHeight;
            window.UpdateLayout();
            window.ShowUpdateStatus(Strings.Format(Strings.DownloadingUpdate, 42), false);
            window.CancelUpdateButton.Visibility = Visibility.Visible;
            await CaptureAsync(Path.Combine(output, "update-progress.png"), 1, cancellationToken);
        }
        finally
        {
            window.Height = originalHeight;
            window.UpdateFooter.Visibility = Visibility.Collapsed;
            window.Preferences = original;
            window.ApplyTheme();
        }

        Rect Bounds(FrameworkElement element)
        {
            return element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
        }
    }

    private async Task CaptureLanguageSettingsDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var original = window.Preferences;
        var selection = window.SelectedSample;
        var query = window.Processes.ProcessSearchBox.Text;
        var samplingTask = window.SamplingTask;
        var anchor = window.Session.History.Latest;
        var tray = window.Tray;
        using var diagnosticTray = new NotifyIcon();
        diagnosticTray.ContextMenuStrip = new ContextMenuStrip();
        diagnosticTray.ContextMenuStrip.Items.Add(Strings.ShowWinMon);
        diagnosticTray.ContextMenuStrip.Items.Add(Strings.Exit);
        window.Tray = diagnosticTray;
        try
        {
            window.Processes.ProcessSortSelector.SelectedIndex = (int)ProcessSort.Memory;
            window.SelectedSample = anchor;
            window.Processes.ProcessSearchBox.Text = "__live_language_no_match__";
            foreach (var choice in AppLanguages.Choices)
            {
                // Exercise the scrollable dialog even on desktops tall enough to show it all.
                var dialog = new SettingsWindow(window.Preferences) { Owner = window, MinHeight = 320, Height = 600 };
                _ = window.Dispatcher.BeginInvoke(() =>
                {
                    var selector = dialog.LanguageSelector;
                    if (selector.SelectedValue as string != AppLanguages.Normalize(window.Preferences.Language))
                        throw new InvalidOperationException("The language selector did not restore the saved choice.");
                    selector.SelectedValue = choice.Name;
                    var save = dialog.SaveButton;
                    save.BringIntoView();
                    dialog.UpdateLayout();
                    var surface = (FrameworkElement)dialog.Content;
                    var bounds = new Rect(save.TranslatePoint(new Point(), surface), save.RenderSize);
                    if (!new Rect(surface.RenderSize).Contains(bounds))
                        throw new InvalidOperationException("The Settings save button was clipped.");
                    save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }, DispatcherPriority.ContextIdle);
                if (dialog.ShowDialog() != true || dialog.Result?.Language != choice.Name)
                    throw new InvalidOperationException("Language selection was not saved.");
                await window.ApplyPreferencesAsync(dialog.Result);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                ValidateLiveLanguageState(anchor, samplingTask);
                var error = window.ShownSamplingError;
                try
                {
                    window.ShownSamplingError = SamplingError.ReadingsUnavailable;
                    window.RefreshVisiblePresentation();
                    if (window.ErrorLabel.Text != Strings.ReadingsUnavailableRetryingEachSecond ||
                        window.LiveCaption.Text != Strings.SystemResourcesUnavailable)
                        throw new InvalidOperationException("Sampling error state depended on the old language.");
                }
                finally
                {
                    window.ShownSamplingError = error;
                    window.RefreshVisiblePresentation();
                }

                if (choice.Name is "en" or "de" or "ja" or "zh-Hans")
                    await CaptureAsync(Path.Combine(output, $"live-language-{choice.Name}.png"), 1, cancellationToken);
            }

            var beforeCancel = Strings.Culture;
            var cancelled = new SettingsWindow(window.Preferences) { Owner = window };
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                var selector = cancelled.LanguageSelector;
                selector.SelectedValue = "en";
                cancelled.DialogResult = false;
            }, DispatcherPriority.ContextIdle);
            if (cancelled.ShowDialog() == true || cancelled.Result is not null ||
                !Equals(Strings.Culture, beforeCancel))
                throw new InvalidOperationException("Cancelling Settings changed the language.");
            if ((await window.Store.LoadAsync(cancellationToken)).Language != window.Preferences.Language)
                throw new InvalidOperationException("Cancelling Settings changed the saved language.");
            await window.ApplyPreferencesAsync(window.Preferences with { Language = AppLanguages.SystemDefault });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (!Equals(Strings.Culture, AppLanguages.SystemDisplayCulture) ||
                window.InfoButton.Content as string != Strings.Info ||
                window.SettingsButton.Content as string != Strings.Settings)
                throw new InvalidOperationException("The system choice did not restore the original Windows language.");
        }
        finally
        {
            window.Preferences = original;
            window.SelectedSample = selection;
            window.Tray = tray;
            window.Processes.ProcessSearchBox.Text = query;
            window.ApplyLanguage();
            diagnosticTray.ContextMenuStrip?.Dispose();
        }
    }

    private async Task CaptureToolTipAsync(string path, CancellationToken cancellationToken)
    {
        var original = window.OverallGraph.ToolTip;
        var tooltip = new ToolTip
        {
            Content = CpuGraph.DescribeSample(window.Session.History.Samples[^1]),
            PlacementTarget = window.OverallGraph,
            Placement = PlacementMode.Center
        };
        try
        {
            window.OverallGraph.ToolTip = tooltip;
            tooltip.IsOpen = true;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            tooltip.UpdateLayout();
            var expectedText = (SolidColorBrush)window.FindResource("Text");
            var expectedBackground = (SolidColorBrush)window.FindResource("Raised");
            if (tooltip.Foreground is not SolidColorBrush foreground || foreground.Color != expectedText.Color ||
                tooltip.Background is not SolidColorBrush background || background.Color != expectedBackground.Color)
                throw new InvalidOperationException(
                    "Tooltip did not use the active theme's foreground and background.");
            await CaptureVisualAsync(tooltip, path, 1, cancellationToken);
        }
        finally
        {
            tooltip.IsOpen = false;
            window.OverallGraph.ToolTip = original;
        }
    }

    private async Task CaptureMetricDetailsAsync(string output, string theme, CancellationToken cancellationToken)
    {
        foreach (var metric in new[] { window.CpuMetric, window.MemoryMetric, window.DiskMetric, window.GpuMetric })
        {
            if (metric.ToolTip is not MetricDetails details ||
                ToolTipService.GetShowsToolTipOnKeyboardFocus(metric.Toggle) is not true ||
                !ReferenceEquals(metric.Toggle.ToolTip, details))
                throw new InvalidOperationException("Full resource tooltip does not share its detail model.");
            if (metric == window.MemoryMetric && (details.Rows.Count != 11 ||
                details.Rows[3].Label != Strings.DetailSystemCache || details.Rows[4].Label != Strings.DetailPagingReads ||
                details.Rows[5].Label != Strings.DetailStandby || details.Rows[6].Label != Strings.DetailModified ||
                details.Rows[7].Label != Strings.DetailFree ||
                details.Rows[8].Label != Strings.DetailCompressionStore))
                throw new InvalidOperationException("Memory details did not expose all nullable pressure readings.");
            await CaptureMetric(false, metric);
        }

        window.SetCompactView(true);
        try
        {
            foreach (var metric in new[] { window.CpuMetric, window.MemoryMetric, window.DiskMetric, window.GpuMetric })
            {
                var box = window.CompactTiles.Single(tile => tile.Source == metric).Box;
                if (!box.Focusable || ToolTipService.GetShowsToolTipOnKeyboardFocus(box) is not true ||
                    !ReferenceEquals(box.ToolTip, metric.ToolTip) ||
                    !AutomationProperties.GetName(box).Contains(metric.Label, StringComparison.Ordinal))
                    throw new InvalidOperationException("Compact resource tile lacks keyboard-accessible details.");
                await CaptureMetric(true, metric);
            }

            var visibleBoxes = window.CompactTiles.Where(tile => tile.Box.Visibility == Visibility.Visible)
                .Select(tile => tile.Box).ToArray();
            for (var i = 0; i + 1 < visibleBoxes.Length; i++)
            {
                visibleBoxes[i].Focus();
                visibleBoxes[i].MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                if (!ReferenceEquals(Keyboard.FocusedElement, visibleBoxes[i + 1]))
                    throw new InvalidOperationException("Compact resource tab order is not left to right.");
            }
        }
        finally
        {
            window.SetCompactView(false);
        }

        return;

        async Task CaptureMetric(bool compact, MetricReadout metric)
        {
            FrameworkElement target = compact
                ? window.CompactTiles.Single(tile => tile.Source == metric).Box
                : metric;
            var tooltip = new ToolTip
            {
                Content = metric.ToolTip, PlacementTarget = target, Placement = PlacementMode.Bottom
            };
            var name = metric == window.CpuMetric ? "cpu"
                : metric == window.MemoryMetric ? "memory"
                : metric == window.GpuMetric ? "gpu" : "disk";
            try
            {
                window.OpenDetails(metric);
                window.RefreshVisiblePresentation();
                tooltip.IsOpen = true;
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                tooltip.UpdateLayout();
                await CaptureVisualAsync(tooltip,
                    Path.Combine(output, $"tooltip-{name}-{theme}-{(compact ? "compact" : "full")}.png"), 1,
                    cancellationToken);
                if (metric == window.CpuMetric)
                {
                    // Exercise the long thermal rows even on machines without optional sensors.
                    var details = (MetricDetails)metric.ToolTip;
                    var thermalDetails = new MetricDetails
                    {
                        Title = details.Title, Live = details.Live, Definition = details.Definition,
                        Accent = details.Accent, Peak = details.Peak
                    };
                    foreach (var row in details.Rows)
                        thermalDetails.Rows.Add(new DetailRow
                        {
                            Label = row.Label, Value = row.Value, Visibility = row.Visibility, FullWidth = row.FullWidth
                        });
                    var rows = thermalDetails.Rows;
                    rows[1].Value = GaugeReadoutText.Temperature(47, window.Preferences.TemperatureUnit);
                    rows[2].Value = Strings.Format(Strings.TemperaturePeakAt,
                        GaugeReadoutText.Temperature(68, window.Preferences.TemperatureUnit), DateTimeOffset.Now);
                    rows[8].Value = Strings.DetailNoPerformanceLimit;
                    rows[9].Value = "\\_TZ.THRM: " + rows[2].Value + "\n\\_TZ.LONG_THERMAL_ZONE_IDENTIFIER: " +
                        rows[1].Value + "\n" + Strings.DetailAcpiThermalZoneNotCpuDie;
                    foreach (var index in new[] { 1, 2, 8, 9 }) rows[index].Visibility = Visibility.Visible;
                    tooltip.Content = thermalDetails;
                    tooltip.UpdateLayout();
                    await CaptureVisualAsync(tooltip,
                        Path.Combine(output, $"tooltip-cpu-thermal-{theme}-{(compact ? "compact" : "full")}.png"),
                        1, cancellationToken);
                }
            }
            finally
            {
                tooltip.IsOpen = false;
                window.CloseDetails(metric);
            }
        }
    }

    private void ValidateTitleBar()
    {
        // A mode change may just have collapsed the historical summary; measure the current layout.
        window.UpdateLayout();
        ValidateProcessToolbar();
        var handle = new WindowInteropHelper(window).Handle;
        var titlePoint = window.ViewTitle.TranslatePoint(new Point(20, window.ViewTitle.ActualHeight / 2), window);
        var pinPoint =
            window.PinButton.TranslatePoint(
                new Point(window.PinButton.ActualWidth / 2, window.PinButton.ActualHeight / 2), window);
        if (Hit(titlePoint) != 2 || Hit(pinPoint) != 1)
            throw new InvalidOperationException("The title must drag the window, while Pin must receive client input.");
        foreach (var action in new[] { window.InfoButton, window.SettingsButton, window.ExportButton })
        {
            var point = action.TranslatePoint(new Point(action.ActualWidth / 2, action.ActualHeight / 2), window);
            if (Hit(point) != 1 || !window.TitleBarArea.IsAncestorOf(action))
                throw new InvalidOperationException($"{action.Content} must receive client input in the title bar.");
            var bounds = action.TransformToAncestor(window.TitleBarArea).TransformBounds(new Rect(action.RenderSize));
            if (bounds.Top < 0 || bounds.Bottom > window.TitleBarArea.ActualHeight ||
                bounds.Right > window.TitleBarArea.ActualWidth || action.ActualHeight < 28)
                throw new InvalidOperationException($"{action.Content} does not fit fully inside the title bar.");
        }

        var titleRight = window.ViewTitle
            .TranslatePoint(new Point(window.ViewTitle.ActualWidth, 0), window.TitleBarArea).X;
        var infoLeft = window.InfoButton.TranslatePoint(new Point(), window.TitleBarArea).X;
        var settingsLeft = window.SettingsButton.TranslatePoint(new Point(), window.TitleBarArea).X;
        if (infoLeft < titleRight || infoLeft - titleRight > 16 ||
            settingsLeft < infoLeft + window.InfoButton.ActualWidth)
            throw new InvalidOperationException("Info and Settings must follow the version in the title bar.");
        // The centered mode switch adds a 28-DIP input target to the resource header.
        if (!window.ResourceHeader.IsAncestorOf(window.RangeSelector) ||
            !window.ResourceHeader.IsAncestorOf(window.ChartModeSwitch) ||
            window.RangeSelector.ActualHeight > 25 || window.ResourceHeader.ActualHeight > 44 ||
            window.Processes.ProcessHeader.ActualHeight >
            (window.InlinePinToolbar ? 44 : window.InlineProcessToolbar ? 78 : 112))
            throw new InvalidOperationException(
                $"The live chart/process headers did not stay compact: range={window.RangeSelector.ActualHeight}, resource={window.ResourceHeader.ActualHeight}, process={window.Processes.ProcessHeader.ActualHeight}.");
        if (Hit(new Point(2, window.ActualHeight / 2)) != 10)
            throw new InvalidOperationException("The integrated title bar lost the left resize border.");
        if (Hit(new Point(window.ActualWidth - 116, 16)) != 8 || Hit(new Point(window.ActualWidth - 70, 16)) != 9 ||
            Hit(new Point(window.ActualWidth - 24, 16)) != 20)
            throw new InvalidOperationException(
                "Native Minimize, Maximize and Close caption buttons must remain available.");

        return;

        int Hit(Point point)
        {
            var screen = window.PointToScreen(point);
            var coordinates = ((uint)(int)screen.X & 0xffff) | ((long)((int)screen.Y & 0xffff) << 16);
            return (int)SendMessageW(handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates)); // WM_NCHITTEST
        }
    }

    private void CaptureWindowFrame(string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Window bounds unavailable.");
        using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Native title-bar capture failed.");
        }
        finally
        {
            graphics.ReleaseHdc(dc);
        }

        // PrintWindow can omit DWM-owned caption glyphs. Capture the composed frame only
        // when our isolated diagnostic window is foreground, never another app's surface.
        if (GetForegroundWindow() == handle)
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        bitmap.Save(path, ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out WindowRectangle rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;

        public UIntPtr PeakWorkingSetSize,
            WorkingSetSize,
            QuotaPeakPagedPoolUsage,
            QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage,
            QuotaNonPagedPoolUsage,
            PagefileUsage,
            PeakPagefileUsage,
            PrivateUsage;
    }

    private async Task CaptureDiskDiagnosticsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var original = window.Preferences;
        var drives = DriveInfo.GetDrives();
        var second = drives.FirstOrDefault(d => d is { IsReady: true, DriveType: DriveType.Fixed } &&
                                                d.Name.TrimEnd('\\') != original.PrimaryDrive)?.Name.TrimEnd('\\');
        var missing = Enumerable.Range('A', 26).Select(c => $"{(char)c}:")
            .FirstOrDefault(n => drives.All(d => d.Name.TrimEnd('\\') != n));
        if (missing is null)
            throw new InvalidOperationException("No unused drive letter for the unavailable-drive check.");
        var unavailable = await CaptureUnavailableDiskTargetAsync(original, missing, outputDirectory, cancellationToken);
        var dualNative = await CaptureAvailableDiskTargetAsync(second, outputDirectory, cancellationToken);
        VerifyIndependentDiskSeriesToggle();
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "disks.json"),
            JsonSerializer.Serialize(
                new { UnavailableDrive = unavailable, DualNative = dualNative, IndependentToggle = true },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        window.Preferences = original;
        window.RefreshPresentation();
        if (window.SecondaryDiskMetric.Visibility != Visibility.Collapsed || Math.Abs(window.MinWidth - 836) > 0.001)
            throw new InvalidOperationException("Single-drive layout did not restore.");

        VerifyAbsentDiskDoesNotGainFocus(original);
        window.Width = window.MinWidth;
        VerifyDiskSettingsSelections(original, second, missing);
    }

    internal static async Task CaptureDesignFixtureAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        // Render-only fixture, separate from native sampling evidence. Never enters a user's session.
        var fixture = new MainWindow(true);
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        if (fixture.SnapshotModeButton.IsEnabled || fixture.LiveModeButton.IsChecked != true)
            throw new InvalidOperationException("An empty recording must start live with Snapshot disabled.");
        var anchor = DateTimeOffset.Now;
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 92) / 2d, 2));
            var cpu = 18 + 6 * Math.Sin(i * 1.7) + 52 * pulse;
            fixture.Session.History.Add(new MonitorSample(anchor.AddSeconds(i - 119), cpu,
            [
                new ProcessUsage(100, 1, "chrome", 8.4, 1_303_379_968, 18_600_000)
                {
                    HasAppWindow = true, GpuPercent = 12.4, GpuEngine = "3D", GpuId = "fixture",
                    PrivateBytes = 900_000_000
                },
                new ProcessUsage(106, 7, "chrome", 1.0, 400_000_000, 500_000)
                    { HasAppWindow = false, PrivateBytes = 250_000_000 },
                new ProcessUsage(101, 2, "devenv", 4.1, 1_914_699_776, 6_400_000)
                    { HasAppWindow = true, GpuPercent = 1.2 },
                new ProcessUsage(102, 3, "MsMpEng", 3.6, 306_184_192, 12_800_000) { HasAppWindow = false },
                new ProcessUsage(4, 4, "System", 2.9, 188_743_680, 4_100_000) { HasAppWindow = false },
                new ProcessUsage(103, 5, "WinMon", 1.8, 97_517_568, 1_600_000)
                    { HasAppWindow = true, GpuPercent = 0.5 },
                new ProcessUsage(104, 6, "gpu-worker", 0, 200_000_000, 0)
                    { HasAppWindow = false, GpuPercent = 65, GpuEngine = "Compute", GpuId = "fixture" }
            ], 0, false, Stopwatch.Frequency * (i + 1))
            {
                LogicalProcessorPercents = Enumerable.Range(0, 16).Select(processor => (double?)Math.Clamp(
                    10 + processor * 2 + 12 * Math.Sin(i * 0.19 + processor) +
                    35 * Math.Exp(-Math.Pow((i - 25 - processor * 5) / 7d, 2)), 0, 100)).ToArray(),
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32UL * 1_073_741_824,
                    UsedMemoryBytes = (ulong)((0.62 + 0.003 * Math.Sin(i)) * 32 * 1_073_741_824),
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 42.6 + 18 * Math.Sin(i * 0.2),
                    SecondaryDiskBytesPerSecond = 128_000_000,
                    DiskName = "C:", DiskActivePercent = 7 + 5 * Math.Abs(Math.Sin(i * 0.8)) + 68 * pulse,
                    DiskBytesPerSecond = 84_000_000,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    GpuId = "illustrative-adapter/0", GpuEngine = "3D",
                    GpuPercent = 43 + 3 * Math.Sin(i * 0.3) + 28 * pulse,
                    ReceiveBytesPerSecond = (2 + 2 * Math.Abs(Math.Sin(i * 0.8)) + 38 * pulse) / 100 * 125_000_000,
                    SendBytesPerSecond = 125_000
                }
            });
        }

        fixture.RefreshVisiblePresentation();
        if (fixture.Content is not FrameworkElement visual)
            throw new InvalidOperationException("Missing design fixture visual.");
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        await CaptureDesignChartsAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignAppFiltersAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignDisksAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignRankingAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignProcessCountsAsync(fixture, visual, outputDirectory, anchor, cancellationToken);
        await CaptureDesignHeadersAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignSnapshotsAsync(fixture, visual, outputDirectory, cancellationToken);
        await CaptureDesignSnapshotReportAsync(fixture, visual, outputDirectory, cancellationToken);
        return;
    }

    private async Task CaptureAsync(string path, double scale, CancellationToken cancellationToken)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        if (window.Content is not FrameworkElement visual)
            throw new InvalidOperationException("The monitor has no root visual.");
        await CaptureVisualAsync(visual, path, scale, cancellationToken);
    }

    private static async Task CaptureVisualAsync(FrameworkElement visual, string path, double scale,
        CancellationToken cancellationToken)
    {
        visual.UpdateLayout();
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale),
            (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken);
    }
}
