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
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ComboBox = System.Windows.Controls.ComboBox;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
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
            window.Processes.EmptyLabel.Visibility != Visibility.Visible || window.Processes.EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
            throw new InvalidOperationException("Process search did not filter the visible rows.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-empty.png"), 1, cancellationToken);
        window.Processes.OnClearProcessSearch(window, new RoutedEventArgs());
        if (window.Processes.ProcessSearchBox.Text.Length != 0 || window.Processes.ClearProcessSearchButton.Visibility != Visibility.Collapsed ||
            window.Processes.ProcessSearchPlaceholder.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Clearing process search did not restore the idle field.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-idle.png"), 1, cancellationToken);
        var originalWidth = window.Width;
        window.Width = 886;
        window.UpdateLayout();
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-wide.png"), 1, cancellationToken);
        window.Width = originalWidth;
        foreach (var (sort, header) in new[]
                     { (ProcessSort.Threads, window.Processes.ThreadsHeader), (ProcessSort.Handles, window.Processes.HandlesHeader) })
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
            if (window.Preferences.ProcessSort != sort || window.Processes.ProcessSortSelector.SelectedIndex != (int)sort)
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
        if (window.Session.History.Samples.Count < 3 || window.Session.History.Samples[^1].TotalCpuPercent is null)
            throw new InvalidOperationException("Native CPU samples did not arrive in the live window.");
        window.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var heldSnapshot = window.SelectedSample;
        if (heldSnapshot is null || window.SnapshotModeButton.IsChecked != true)
            throw new InvalidOperationException("The native Snapshot action did not hold a sample.");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (!Object.ReferenceEquals(window.SelectedSample, heldSnapshot) || Object.ReferenceEquals(window.Session.History.Latest, heldSnapshot))
            throw new InvalidOperationException("Recording must advance while the snapshot stays fixed.");
        window.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (Object.ReferenceEquals(window.SelectedSample, heldSnapshot) || !Object.ReferenceEquals(window.SelectedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Clicking Snapshot again must select the newest recorded sample.");
        window.LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (window.SelectedSample is not null || window.LiveModeButton.IsChecked != true ||
            window.SnapshotLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The native Live action did not release the snapshot.");
        var nativeResources = window.Session.History.Samples[^1].Resources;
        var nativeProcessGpu = window.Session.History.Samples[^1].Processes.Where(p => p.GpuPercent is not null)
            .OrderByDescending(p => p.GpuPercent).Take(10)
            .Select(p => new { p.ProcessId, p.Name, p.GpuPercent, p.GpuEngine, p.GpuId }).ToArray();
        if (nativeResources.MemoryPercent is null)
            throw new InvalidOperationException("Native physical-memory sampling did not arrive.");
        await CaptureFeatureDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-dark.png"), cancellationToken);
        window.MemoryMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 2) != 0) throw new InvalidOperationException("Memory series did not hide.");
        window.MemoryMetric.Toggle.IsChecked = true;
        if (window.OverallGraph.VisibleMetrics != 31)
            throw new InvalidOperationException("Series visibility did not restore.");
        window.GpuMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 16) != 0 || window.Preferences.ShowGpu)
            throw new InvalidOperationException("GPU series did not hide.");
        window.GpuMetric.Toggle.IsChecked = true;
        if (window.OverallGraph.VisibleMetrics != 31 || !window.Preferences.ShowGpu)
            throw new InvalidOperationException("GPU series did not restore.");
        await CaptureSearchDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureDiskDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "compact.png"), 1, cancellationToken);
        window.Processes.GroupedChip.IsChecked = true;
        window.RefreshPresentation();
        if (window.Processes.Rows[0].Visibility != Visibility.Visible ||
            (window.Processes.OtherRow.Visibility == Visibility.Visible && AutomationProperties.GetName(window.Processes.OtherRow)
                ?.Contains(Strings.Executables, StringComparison.Ordinal) != true))
            throw new InvalidOperationException("Grouped mode showed no native processes or mislabeled the remainder.");
        await CaptureAsync(Path.Combine(outputDirectory, "grouped-native.png"), 1, cancellationToken);
        window.Processes.IndividualChip.IsChecked = true;
        window.Processes.ProcessCountSelector.IsDropDownOpen = true;
        try
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (window.Processes.ProcessCountSelector.Template.FindName("PART_Popup", window.Processes.ProcessCountSelector) is not Popup
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

        // Switching range and widening the window keep the same recording; wide rows gain a PID line.
        var count = window.Session.History.Samples.Count;
        if (window.RangeSelector.Children.OfType<RadioButton>().FirstOrDefault(c => c.Tag is 600) is not { } tenMinutes)
            throw new InvalidOperationException("The 10-minute range chip is missing.");
        tenMinutes.IsChecked = true;
        window.Width = 1000;
        window.UpdateLayout();
        if (window.Preferences.EffectiveChartRange != 600 || !window.Wide || window.Session.History.Samples.Count < count)
            throw new InvalidOperationException("Range or width change did not apply to the existing recording.");
        window.SelectedSample = window.Session.History.Samples[^2];
        window.RefreshPresentation();
        if (window.SnapshotLayer.Visibility != Visibility.Visible || window.Processes.SelectionLabel.Text != Strings.Format(
                Strings.ProcessesAtSAverage,
                window.SelectedSample.Timestamp, ProcessRanking.DefaultWindowSeconds, "") || window.SnapshotMemory.Text.Length == 0)
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
        if (window.SelectedSample is not null) throw new InvalidOperationException("Return to live retained a selection.");
        var beforeHidden = window.PresentedSample;
        var hiddenCount = window.Session.History.Samples.Count;
        window.Hide();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (window.Session.History.Samples.Count <= hiddenCount || !Object.ReferenceEquals(window.PresentedSample, beforeHidden))
            throw new InvalidOperationException("Hidden recording stopped or presentation continued updating.");
        window.Show();
        if (!Object.ReferenceEquals(window.PresentedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Restoring the window did not refresh the latest sample.");
        var beforeMinimized = window.PresentedSample;
        var minimizedCount = window.Session.History.Samples.Count;
        window.WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (window.Session.History.Samples.Count <= minimizedCount || !Object.ReferenceEquals(window.PresentedSample, beforeMinimized))
            throw new InvalidOperationException("Minimized recording stopped or presentation continued updating.");
        window.WindowState = WindowState.Normal;
        if (!Object.ReferenceEquals(window.PresentedSample, window.Session.History.Latest))
            throw new InvalidOperationException("Restoring the minimized window did not refresh the latest sample.");
        // A stalled UI thread must not delay capture: intervals stay on schedule and the chart draws no gap.
        var stallCount = window.Session.History.Samples.Count;
        Thread.Sleep(3500);
        await Task.Delay(1500, cancellationToken);
        var stalled = window.Session.History.Samples.Skip(stallCount - 1).ToArray();
        var longestInterval = stalled
            .Zip(stalled.Skip(1), (a, b) => (b.MonotonicTimestamp - a.MonotonicTimestamp) / (double)Stopwatch.Frequency)
            .DefaultIfEmpty().Max();
        if (stalled.Length < 5 || longestInterval > 2.5 || stalled.Skip(1).Any(s => s.IsGap))
            throw new InvalidOperationException(
                $"A UI stall delayed sampling: {stalled.Length - 1} samples, longest interval {longestInterval:0.00}s.");
        window.Preferences = window.Preferences with { Theme = "Light" };
        window.ApplyTheme();
        await CaptureAsync(Path.Combine(outputDirectory, "light.png"), 1, cancellationToken);
        window.Processes.ProcessCountSelector.SelectedItem = 20;
        await CaptureAsync(Path.Combine(outputDirectory, "light-scroll.png"), 1, cancellationToken);
        window.Processes.ProcessCountSelector.SelectedItem = 5;
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-light.png"), cancellationToken);
        var settings = new SettingsWindow(window.Preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate))
            { Owner = window };
        settings.Show();
        settings.UpdateLayout();
        if (settings.Content is FrameworkElement settingsVisual)
            await CaptureVisualAsync(settingsVisual, Path.Combine(outputDirectory, "settings.png"), 1,
                cancellationToken);
        settings.Close();
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
        if (window.Session.History.Samples.Count == 0) await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        // Check the compact process toolbar at the minimum and wider supported window sizes.
        var headerWidth = window.Width;
        try
        {
            foreach (var width in new[] { window.MinWidth, 886, 1000 })
            {
                window.Width = width;
                window.UpdateLayout();
                ValidateTitleBar();
            }
        }
        finally
        {
            window.Width = headerWidth;
            window.UpdateLayout();
        }

        CaptureWindowFrame(Path.Combine(output, "titlebar-window.png"));
        var hardware = await Task.Run(WindowsHardwareInfo.Read, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "hardware.json"),
            JsonSerializer.Serialize(hardware, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        string? copiedInfo = null;
        var info = new HardwareInfoWindow(text => copiedInfo = text) { Owner = window };
        try
        {
            info.Show();
            await info.Ready.WaitAsync(cancellationToken);
            info.UpdateLayout();
            var copyLayout = (DockPanel)((Border)info.Content).Child;
            var copyButton = copyLayout.Children.OfType<DockPanel>().Single().Children
                .OfType<Button>().Single(b => AutomationProperties.GetName(b) == Strings.CopyAllInformation);
            if (!copyButton.IsEnabled)
                throw new InvalidOperationException("Copy did not become available after loading information.");
            copyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (copiedInfo is null || !copiedInfo.StartsWith(Strings.Format(Strings.ProductVersion, AppInfo.Version),
                    StringComparison.Ordinal) ||
                !copiedInfo.Contains(Strings.InstalledMemory, StringComparison.Ordinal) ||
                !copiedInfo.Contains(Strings.PhysicalDrives, StringComparison.Ordinal))
                throw new InvalidOperationException("Copy did not export the loaded information.");
            await File.WriteAllTextAsync(Path.Combine(output, "copied-info.txt"), copiedInfo, cancellationToken);
            await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output, "hardware-info.png"), 1,
                cancellationToken);
            var hardwareLayout = (DockPanel)((Border)info.Content).Child;
            var hardwareScroll = hardwareLayout.Children.OfType<ScrollViewer>().Single();
            var sections = (StackPanel)hardwareScroll.Content;
            foreach (var expander in sections.Children.OfType<Expander>())
            {
                expander.IsExpanded = true;
                info.UpdateLayout();
                hardwareScroll.ScrollToVerticalOffset(expander.TransformToAncestor(sections).Transform(new Point()).Y -
                                                      24);
                await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output,
                    expander.Header.ToString()!.StartsWith("CPU", StringComparison.Ordinal) ? "cpu-details.png" :
                    expander.Header.ToString()!.StartsWith("Module", StringComparison.Ordinal) ? "memory-details.png" :
                    "physical-drive-details.png"), 1, cancellationToken);
                expander.IsExpanded = false;
            }
        }
        finally
        {
            info.Close();
        }

        var original = window.Preferences;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var palette in ChartPalettes.Names)
            {
                window.Preferences = original with { Theme = theme, ChartPalette = palette };
                window.ApplyTheme();
                window.RefreshPresentation();
                var fileName = string.Concat(palette.ToLowerInvariant()
                    .Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
                await CaptureAsync(Path.Combine(output, $"palette-{fileName}-{theme.ToLowerInvariant()}.png"), 1,
                    cancellationToken);
            }
        }
        finally
        {
            window.Preferences = original;
            window.ApplyTheme();
            window.RefreshPresentation();
        }

        var row = window.Processes.Rows.First(r => r.Visibility == Visibility.Visible);
        // Exercise menu creation without invoking the destructive command.
        row.PrepareProcessMenu();
        var menu = row.ContextMenu ?? throw new InvalidOperationException("Process context menu was not created.");
        var items = menu.Items.OfType<MenuItem>().ToArray();
        var item = items.Last();
        var priority = items.SingleOrDefault(i => Object.Equals(i.Header, Strings.SetPriority));
        if (items.Length != 7 || priority?.Items.Count != ProcessPriorityChanger.Choices.Length ||
            items.Where(i => i != priority).Any(i => i.Icon is null) ||
            item.ToolTip as string != Strings.AvailableForLiveProcesses)
            throw new InvalidOperationException("Process context menu did not capture its target.");
        try
        {
            menu.PlacementTarget = row;
            menu.IsOpen = true;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(output, "process-context-menu.png"), 1, cancellationToken);
            var enabled = item.IsEnabled;
            try
            {
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    window.Preferences = original with { Theme = theme };
                    window.ApplyTheme();
                    foreach (var isEnabled in new[] { true, false })
                    {
                        item.IsEnabled = isEnabled;
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                        menu.UpdateLayout();
                        await CaptureVisualAsync(menu, Path.Combine(output,
                                $"process-context-menu-{theme.ToLowerInvariant()}-{(isEnabled ? "enabled" : "disabled")}.png"),
                            1, cancellationToken);
                    }
                }
            }
            finally
            {
                item.IsEnabled = enabled;
                window.Preferences = original;
                window.ApplyTheme();
            }
        }
        finally
        {
            menu.IsOpen = false;
        }

        var selection = window.SelectedSample;
        try
        {
            window.SelectedSample = window.Session.History.Latest;
            window.RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items.OfType<MenuItem>().Last().IsEnabled ||
                menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == Strings.CreateDumpFile + "…").IsEnabled)
                throw new InvalidOperationException("Historical rows allowed process termination.");
            window.SelectedSample = null;
            window.Processes.GroupedChip.IsChecked = true;
            window.RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items.OfType<MenuItem>().Last().Header is not string groupHeader ||
                !groupHeader.Contains(Strings.Format(Strings.EndGroupMenu, "").Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("Grouped rows did not offer group termination.");
        }
        finally
        {
            window.Processes.IndividualChip.IsChecked = true;
            window.SelectedSample = selection;
            window.RefreshPresentation();
        }

        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                window.Preferences = original with { Theme = theme };
                window.ApplyTheme();
                var example = new ProcessUsage(12345, DateTime.UtcNow.AddHours(-2).ToFileTimeUtc(), "example.exe", 4.2,
                    100 * 1_048_576, 230_000)
                {
                    ImagePath = @"C:\Program Files\Example\example.exe", AppName = "Example application",
                    Publisher = "Example publisher", ThreadCount = 12, HandleCount = 240, PrivateBytes = 80 * 1_048_576
                };
                var snapshot = new ProcessDetailsSnapshot(example, false, DateTimeOffset.Now, false, true, "");
                foreach (var dialog in new Window[]
                         {
                             new ProcessPropertiesWindow(snapshot, _ => { }),
                             new ProcessDumpDialog(example, () => false)
                         })
                {
                    dialog.Owner = window;
                    try
                    {
                        dialog.Show();
                        dialog.UpdateLayout();
                        await CaptureVisualAsync(dialog, Path.Combine(output,
                                $"{(dialog is ProcessPropertiesWindow ? "process-properties" : "process-dump")}-{theme.ToLowerInvariant()}.png"),
                            1, cancellationToken);
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }

                foreach (var confirmation in new[] { true, false })
                {
                    var text = confirmation
                        ? Strings.Format(Strings.EndGroupConfirmation, 2, "Example") +
                          "\n\nExample (PID 12345)\nExample (PID 12346)"
                        : Strings.AccessDeniedWindowsDidNotAllowWinMonToEndThis;
                    var dialog = new ProcessActionDialog(text, confirmation) { Owner = window };
                    try
                    {
                        // Show non-modally for a screenshot; never invoke termination in diagnostics.
                        dialog.Show();
                        dialog.UpdateLayout();
                        await CaptureVisualAsync((FrameworkElement)dialog.Content, Path.Combine(output,
                                $"process-dialog-{theme.ToLowerInvariant()}-{(confirmation ? "confirmation" : "result")}.png"),
                            1, cancellationToken);
                    }
                    finally
                    {
                        dialog.Close();
                    }
                }
            }
        }
        finally
        {
            window.Preferences = original;
            window.ApplyTheme();
        }

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
                HardwareInfo = true, PaletteRenders = true, ProcessContextMenu = true,
                HistoricalActionsDisabled = true, GroupTerminationOffered = true, ThemedProcessDialogs = true,
                LanguageSelection = true,
                LiveLanguageSwitching = true, RecordingStatePreserved = true,
                Limitation =
                    "No destructive command invoked by the UI smoke; native termination is tested on an owned helper in the test suite."
            }), cancellationToken);
    }

    private async Task CaptureUpdateDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var original = window.Preferences;
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

            window.ShowUpdateStatus(Strings.Format(Strings.DownloadingUpdate, 42), false);
            window.CancelUpdateButton.Visibility = Visibility.Visible;
            await CaptureAsync(Path.Combine(output, "update-progress.png"), 1, cancellationToken);
        }
        finally
        {
            window.UpdateFooter.Visibility = Visibility.Collapsed;
            window.Preferences = original;
            window.ApplyTheme();
        }
    }

    private static Grid SettingsLayout(SettingsWindow dialog)
    {
        return (Grid)
            ((Border)((ScrollViewer)dialog.Content).Content).Child;
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
                    var layout = SettingsLayout(dialog);
                    var selector = layout.Children.OfType<Grid>()
                        .SelectMany(field => field.Children.OfType<ComboBox>())
                        .Single(control => AutomationProperties.GetName(control) == Strings.Language);
                    if (selector.SelectedValue as string != AppLanguages.Normalize(window.Preferences.Language))
                        throw new InvalidOperationException("The language selector did not restore the saved choice.");
                    selector.SelectedValue = choice.Name;
                    var save = layout.Children.OfType<StackPanel>().Single().Children
                        .OfType<Button>().Single(button => button.IsDefault);
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
                if (window.InfoButton.Content as string != Strings.Info ||
                    window.SettingsButton.Content as string != Strings.Settings ||
                    window.Processes.ProcessHistoryHeader.Text != Strings.History || window.MemoryMetric.Label != Strings.Memory ||
                    AutomationProperties.GetName(window.MemoryMetric.Toggle) !=
                    Strings.Format(Strings.ShowOnChart, Strings.Memory) ||
                    window.Tray.ContextMenuStrip!.Items[0].Text != Strings.ShowWinMon ||
                    window.Tray.ContextMenuStrip.Items[1].Text != Strings.Exit ||
                    window.Preferences.ProcessSort != ProcessSort.Memory ||
                    window.Processes.ProcessSortSelector.SelectedIndex != (int)ProcessSort.Memory ||
                    window.Processes.ProcessSortSelector.SelectedItem as string != Strings.Memory ||
                    window.Processes.ProcessSearchBox.Text != "__live_language_no_match__" ||
                    !Object.ReferenceEquals(window.SelectedSample, anchor) ||
                    (anchor is not null && !window.Session.History.Samples.Contains(anchor)) ||
                    !Object.ReferenceEquals(window.SamplingTask, samplingTask) ||
                    window.Processes.EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
                    throw new InvalidOperationException(
                        "Live language switching left stale text or changed recording state.");
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
                var layout = SettingsLayout(cancelled);
                var selector = layout.Children.OfType<Grid>()
                    .SelectMany(field => field.Children.OfType<ComboBox>())
                    .Single(control => AutomationProperties.GetName(control) == Strings.Language);
                selector.SelectedValue = "en";
                cancelled.DialogResult = false;
            }, DispatcherPriority.ContextIdle);
            if (cancelled.ShowDialog() == true || cancelled.Result is not null ||
                !Object.Equals(Strings.Culture, beforeCancel))
                throw new InvalidOperationException("Cancelling Settings changed the language.");
            if ((await window.Store.LoadAsync(cancellationToken)).Language != window.Preferences.Language)
                throw new InvalidOperationException("Cancelling Settings changed the saved language.");
            await window.ApplyPreferencesAsync(window.Preferences with { Language = AppLanguages.SystemDefault });
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (!Object.Equals(Strings.Culture, AppLanguages.SystemDisplayCulture) ||
                window.InfoButton.Content as string != Strings.Info || window.SettingsButton.Content as string != Strings.Settings)
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

    private void ValidateTitleBar()
    {
        // A mode change may just have collapsed the historical summary; measure the current layout.
        window.UpdateLayout();
        ValidateProcessToolbar();
        var handle = new WindowInteropHelper(window).Handle;
        var titlePoint = window.ViewTitle.TranslatePoint(new Point(20, window.ViewTitle.ActualHeight / 2), window);
        var pinPoint = window.PinButton.TranslatePoint(new Point(window.PinButton.ActualWidth / 2, window.PinButton.ActualHeight / 2), window);
        if (Hit(titlePoint) != 2 || Hit(pinPoint) != 1)
            throw new InvalidOperationException("The title must drag the window, while Pin must receive client input.");
        foreach (var action in new[] { window.InfoButton, window.SettingsButton })
        {
            var point = action.TranslatePoint(new Point(action.ActualWidth / 2, action.ActualHeight / 2), window);
            if (Hit(point) != 1 || !window.TitleBarArea.IsAncestorOf(action))
                throw new InvalidOperationException($"{action.Content} must receive client input in the title bar.");
            var bounds = action.TransformToAncestor(window.TitleBarArea).TransformBounds(new Rect(action.RenderSize));
            if (bounds.Top < 0 || bounds.Bottom > window.TitleBarArea.ActualHeight ||
                bounds.Right > window.TitleBarArea.ActualWidth || action.ActualHeight < 28)
                throw new InvalidOperationException($"{action.Content} does not fit fully inside the title bar.");
        }

        var titleRight = window.ViewTitle.TranslatePoint(new Point(window.ViewTitle.ActualWidth, 0), window.TitleBarArea).X;
        var infoLeft = window.InfoButton.TranslatePoint(new Point(), window.TitleBarArea).X;
        var settingsLeft = window.SettingsButton.TranslatePoint(new Point(), window.TitleBarArea).X;
        if (infoLeft < titleRight || infoLeft - titleRight > 16 || settingsLeft < infoLeft + window.InfoButton.ActualWidth)
            throw new InvalidOperationException("Info and Settings must follow the version in the title bar.");
        // The centered mode switch adds a 28-DIP input target to the resource header.
        if (!window.ResourceHeader.IsAncestorOf(window.RangeSelector) || !window.ResourceHeader.IsAncestorOf(window.ChartModeSwitch) ||
            window.RangeSelector.ActualHeight > 25 || window.ResourceHeader.ActualHeight > 44 ||
            window.Processes.ProcessHeader.ActualHeight > (window.InlinePinToolbar ? 44 : window.InlineProcessToolbar ? 78 : 112))
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
        window.Preferences = original with { SecondaryDrive = missing, ShowSecondaryDisk = true };
        window.SecondaryDiskMetric.Toggle.IsChecked = true;
        window.RefreshPresentation();
        await Task.Delay(3000, cancellationToken);
        var unavailable = window.Session.History.Latest!.Resources;
        if (unavailable.SecondaryDiskName != missing || unavailable.SecondaryDiskActivePercent is not null ||
            unavailable.DiskActivePercent is null || unavailable.MemoryPercent is null)
            throw new InvalidOperationException("An unavailable second volume disrupted native resource sampling.");
        await CaptureAsync(Path.Combine(outputDirectory, "disk-unavailable.png"), 1, cancellationToken);
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

        window.SecondaryDiskMetric.Toggle.IsChecked = false;
        if ((window.OverallGraph.VisibleMetrics & 32) != 0 || window.Preferences.ShowSecondaryDisk)
            throw new InvalidOperationException("Second disk trace did not hide independently.");
        window.SecondaryDiskMetric.Toggle.IsChecked = true;
        if ((window.OverallGraph.VisibleMetrics & 32) == 0)
            throw new InvalidOperationException("Second disk trace did not restore.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "disks.json"),
            JsonSerializer.Serialize(
                new { UnavailableDrive = unavailable, DualNative = dualNative, IndependentToggle = true },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        window.Preferences = original;
        window.RefreshPresentation();
        if (window.SecondaryDiskMetric.Visibility != Visibility.Collapsed || Math.Abs(window.MinWidth - 836) > 0.001)
            throw new InvalidOperationException("Single-drive layout did not restore.");
        window.Width = window.MinWidth;
        foreach (var selection in new[] { second ?? missing, null })
        {
            var dialog = new SettingsWindow(original with { SecondaryDrive = second ?? missing }) { Owner = window };
            _ = window.Dispatcher.BeginInvoke(() =>
            {
                var layout = SettingsLayout(dialog);
                var selector = layout.Children.OfType<Grid>()
                    .SelectMany(g => g.Children.OfType<ComboBox>())
                    .Single(c => AutomationProperties.GetName(c) == Strings.SecondDrive);
                selector.SelectedItem = selector.Items.Cast<object>().Single(item => selection is null
                    ? item.ToString() == Strings.None
                    : item.ToString()!.StartsWith(selection, StringComparison.Ordinal));
                var buttons = layout.Children.OfType<StackPanel>().Single();
                ((Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }, DispatcherPriority.ContextIdle);
            if (dialog.ShowDialog() != true || dialog.Result?.PrimaryDrive != original.PrimaryDrive ||
                dialog.Result.SecondaryDrive != selection)
                throw new InvalidOperationException("Saving the disk selectors did not return the selected volumes.");
        }
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
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-150.png"), 1.5,
            cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-200.png"), 2, cancellationToken);
        fixture.Processes.AppsOnlyCheckBox.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        if (!fixture.Preferences.AppsOnly || fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 3 ||
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
        fixture.Processes.IndividualChip.IsChecked = true;
        fixture.SelectedSample = null;
        fixture.Preferences = fixture.Preferences with { PrimaryDrive = "C:", SecondaryDrive = "D:" };

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
        fixture.Processes.ProcessSortSelector.SelectedItem = "GPU";
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (fixture.Preferences.ProcessSort != ProcessSort.Gpu || !AutomationProperties.GetName(fixture.Processes.Rows[0])
                .StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("GPU ranking failed to include a CPU-idle GPU process.");
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!AutomationProperties.GetName(fixture.Processes.Rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical GPU ranking failed.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "gpu-process-sort.png"), 1, cancellationToken);
        fixture.SelectedSample = null;
        // Memory ranks by the largest working set (devenv); I/O by the busiest read/write rate (chrome).
        foreach (var (sort, expected) in new[] { (Strings.Memory, "devenv"), (Strings.IO, "chrome") })
        {
            fixture.Processes.ProcessSortSelector.SelectedItem = sort;
            fixture.RefreshVisiblePresentation();
            visual.UpdateLayout();
            if (!AutomationProperties.GetName(fixture.Processes.Rows[0]).StartsWith(expected, StringComparison.Ordinal) ||
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
        if (!fixture.Preferences.GroupProcesses || fixture.Processes.MemoryHeader.Text != $"{Strings.Private} MiB" ||
            AutomationProperties.GetName(fixture.Processes.Rows[0]) is not { } groupedName ||
            !groupedName.StartsWith("chrome", StringComparison.Ordinal) ||
            !groupedName.Contains(Strings.Format(Strings.Processes, 2), StringComparison.Ordinal) ||
            !groupedName.Contains(MainWindow.Percent(9.4), StringComparison.Ordinal))
            throw new InvalidOperationException("Grouped mode did not combine the chrome processes.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "grouped.png"), 1, cancellationToken);
        fixture.Processes.IndividualChip.IsChecked = true;
        fixture.RefreshVisiblePresentation();
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
            ArrangeFixture();
            if (fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != count ||
                fixture.Preferences.TopProcessCount != count)
                throw new InvalidOperationException($"Selecting {count} did not update the live process count.");
        }

        if (fixture.Processes.ProcessScroll.ScrollableHeight <= 0 || !fixture.TitleBarArea.IsAncestorOf(fixture.SettingsButton) ||
            !fixture.TitleBarArea.IsAncestorOf(fixture.InfoButton))
            throw new InvalidOperationException(
                "Long process lists must scroll while title-bar actions remain available at minimum size.");
        fixture.Processes.ProcessScroll.ScrollToBottom();
        ArrangeFixture();
        if (fixture.Processes.ProcessScroll.VerticalOffset <= 0)
            throw new InvalidOperationException("The process list failed to scroll.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-50.png"), 1, cancellationToken);
        fixture.Processes.ProcessCountSelector.SelectedItem = TopCountChoices.All;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();
        if (fixture.Preferences.TopProcessCount != 0 ||
            fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) < 60 ||
            fixture.Processes.OtherRow.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException(
                "All did not render every available process beyond the old 50-row limit.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-all.png"), 1, cancellationToken);
        fixture.SelectedSample = rankedSample;
        fixture.Session.History.Add(rankedSample with
        {
            Timestamp = anchor.AddSeconds(2), MonotonicTimestamp = Stopwatch.Frequency * 122, Processes = []
        });
        fixture.RefreshVisiblePresentation();
        if (fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) < 60)
            throw new InvalidOperationException("All lost processes from the selected historical sample.");
        fixture.Processes.ProcessCountSelector.SelectedItem = 10;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();
        if (!Object.ReferenceEquals(fixture.SelectedSample, rankedSample) ||
            fixture.Processes.Rows.Count(row => row.Visibility == Visibility.Visible) != 10)
            throw new InvalidOperationException(
                "Changing the count replaced the historical sample or used live processes.");
        if (fixture.Processes.ProcessScroll.ActualHeight < 108)
            throw new InvalidOperationException("Historical details left too little room for the process list.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-history.png"), 1,
            cancellationToken);
        fixture.Processes.ProcessCountSelector.SelectedItem = 5;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();

        // Approved compact-header mockup: same wide viewport, dual disks, grouped list, ten-minute range.
        fixture.SelectedSample = null;
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
            ArrangeSnapshot(width, height);
            fixture.LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (fixture.LiveModeButton.IsChecked != true || fixture.SnapshotModeButton.IsChecked == true ||
                fixture.SelectedSample is not null || fixture.SnapshotLayer.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Live mode did not clear the chart selection.");
            var switchCenter = fixture.ChartModeSwitch
                .TranslatePoint(new Point(fixture.ChartModeSwitch.ActualWidth / 2, 0), fixture.ResourceHeader).X;
            if (Math.Abs(switchCenter - fixture.ResourceHeader.ActualWidth / 2) > 1)
                throw new InvalidOperationException("The chart mode switch is not centered in the header.");
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"chart-mode-live-{width}.png"), 1,
                cancellationToken);
            fixture.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (!Object.ReferenceEquals(fixture.SelectedSample, fixture.Session.History.Latest) ||
                fixture.SnapshotModeButton.IsChecked != true || fixture.LiveModeButton.IsChecked == true ||
                fixture.SnapshotLayer.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Snapshot mode did not select the newest recorded sample.");
            ArrangeSnapshot(width, height);
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
                ArrangeSnapshot(width, height);
                AssertSnapshotContained();
                if (fixture.SelectedSample is null ||
                    fixture.SnapshotCpu.Text != MainWindow.Percent(fixture.SelectedSample.TotalCpuPercent))
                    throw new InvalidOperationException("The selected snapshot did not match the clicked sample.");
                await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"snapshot-{width}-{name}.png"), 1,
                    cancellationToken);
            }
        }

        // Copy from a held historical sample without touching the user's Windows clipboard.
        fixture.SelectedSample = fixture.Session.History.Samples[^2];
        fixture.RefreshVisiblePresentation();
        string? copied = null;
        fixture.CopySnapshot(value => copied = value);
        if (copied is null ||
            !copied.Contains(Strings.Format(Strings.ExportSnapshotTime, fixture.SelectedSample.Timestamp)) ||
            !copied.Contains(Strings.SnapshotExportAverages) ||
            fixture.SnapshotCopyButton.Content as string != Strings.Copied ||
            !Object.ReferenceEquals(fixture.SnapshotReport?.Sample, fixture.SelectedSample))
            throw new InvalidOperationException(
                "Copy did not export the held snapshot with processes or confirm success.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "clipboard.txt"), copied, cancellationToken);
        fixture.ResetSnapshotCopyCaption();
        if (fixture.SnapshotCopyButton.Content as string != Strings.CopySnapshot)
            throw new InvalidOperationException("Copy confirmation did not reset.");
        // A shorter graph must scroll its values while keeping the live action within the panel.
        ArrangeSnapshot(600, 540);
        AssertSnapshotContained();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-short.png"), 1, cancellationToken);
        fixture.Preferences = fixture.Preferences with { Theme = "Light", SecondaryDrive = "D:" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        ArrangeSnapshot(905, 940);
        AssertSnapshotContained();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-150.png"), 1.5,
            cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-200.png"), 2, cancellationToken);
        fixture.SelectedSample = fixture.Session.History.Samples[0];
        fixture.Session.History.Resize(1);
        fixture.RefreshVisiblePresentation();
        ArrangeSnapshot(905, 940);
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
                Expiry = true, ReturnToLive = true, CenteredChartModeSwitch = true, LatestSampleAction = true,
                SnapshotCopy = true,
                RenderScales = new[] { 1d, 1.5, 2d }
            }), cancellationToken);

        return;

        void ArrangeSnapshot(double width, double height)
        {
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
        }

        void AssertSnapshotContained()
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

        void ArrangeFixture()
        {
            visual.Measure(new Size(600, 752));
            visual.Arrange(new Rect(0, 0, 600, 752));
            visual.UpdateLayout();
        }
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
