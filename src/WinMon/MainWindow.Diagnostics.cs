using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using WinMon.Monitoring;
// ReSharper disable StringLiteralTypo

namespace WinMon;

public partial class MainWindow
{
    internal async Task CaptureSearchDiagnosticsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        if (_history.Latest?.TotalCpuPercent is null)
            throw new InvalidOperationException("Search diagnostics need native samples.");
        ProcessSearchBox.Text = "__winmon_search_no_match__";
        if (ClearProcessSearchButton.Visibility != Visibility.Visible ||
            ProcessSearchPlaceholder.Visibility != Visibility.Collapsed ||
            _rows.Any(row => row.Visibility == Visibility.Visible) ||
            EmptyLabel.Visibility != Visibility.Visible || EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
            throw new InvalidOperationException("Process search did not filter the visible rows.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-empty.png"), 1, cancellationToken);
        OnClearProcessSearch(this, new RoutedEventArgs());
        if (ProcessSearchBox.Text.Length != 0 || ClearProcessSearchButton.Visibility != Visibility.Collapsed ||
            ProcessSearchPlaceholder.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Clearing process search did not restore the idle field.");
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-idle.png"), 1, cancellationToken);
        var originalWidth = Width;
        Width = 886;
        UpdateLayout();
        await CaptureAsync(Path.Combine(outputDirectory, "process-search-wide.png"), 1, cancellationToken);
        Width = originalWidth;
        foreach (var (sort, header) in new[] { (ProcessSort.Threads, ThreadsHeader), (ProcessSort.Handles, HandlesHeader) })
        {
            ProcessSortSelector.SelectedIndex = (int)sort;
            if (_preferences.ProcessSort != sort || !header.Text.EndsWith('▾'))
                throw new InvalidOperationException($"The {sort} dropdown did not update its column heading.");
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-live.png"), 1, cancellationToken);
            GroupedChip.IsChecked = true;
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-grouped.png"), 1, cancellationToken);
            _selectedSample = _history.Samples[^2];
            RefreshPresentation();
            await CaptureAsync(Path.Combine(outputDirectory, $"process-sort-{sort}-history.png"), 1, cancellationToken);
            _selectedSample = null;
            IndividualChip.IsChecked = true;
            ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
            header.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = MouseLeftButtonUpEvent });
            if (_preferences.ProcessSort != sort || ProcessSortSelector.SelectedIndex != (int)sort)
                throw new InvalidOperationException($"Clicking the {sort} heading did not update the dropdown.");
        }
        ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
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
        var samples = _history.Samples;
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
        static double MiB(IEnumerable<long> values) => values.Average() / 1_048_576d;
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
        if (_history.Samples.Count < 3 || _history.Samples[^1].TotalCpuPercent is null)
            throw new InvalidOperationException("Native CPU samples did not arrive in the live window.");
        SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var heldSnapshot = _selectedSample;
        if (heldSnapshot is null || SnapshotModeButton.IsChecked != true)
            throw new InvalidOperationException("The native Snapshot action did not hold a sample.");
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (!ReferenceEquals(_selectedSample, heldSnapshot) || ReferenceEquals(_history.Latest, heldSnapshot))
            throw new InvalidOperationException("Recording must advance while the snapshot stays fixed.");
        SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (ReferenceEquals(_selectedSample, heldSnapshot) || !ReferenceEquals(_selectedSample, _history.Latest))
            throw new InvalidOperationException("Clicking Snapshot again must select the newest recorded sample.");
        LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (_selectedSample is not null || LiveModeButton.IsChecked != true || SnapshotLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The native Live action did not release the snapshot.");
        var nativeResources = _history.Samples[^1].Resources;
        var nativeProcessGpu = _history.Samples[^1].Processes.Where(p => p.GpuPercent is not null)
            .OrderByDescending(p => p.GpuPercent).Take(10)
            .Select(p => new { p.ProcessId, p.Name, p.GpuPercent, p.GpuEngine, p.GpuId }).ToArray();
        if (nativeResources.MemoryPercent is null)
            throw new InvalidOperationException("Native physical-memory sampling did not arrive.");
        await CaptureFeatureDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-dark.png"), cancellationToken);
        MemoryMetric.Toggle.IsChecked = false;
        if ((OverallGraph.VisibleMetrics & 2) != 0) throw new InvalidOperationException("Memory series did not hide.");
        MemoryMetric.Toggle.IsChecked = true;
        if (OverallGraph.VisibleMetrics != 31) throw new InvalidOperationException("Series visibility did not restore.");
        GpuMetric.Toggle.IsChecked = false;
        if ((OverallGraph.VisibleMetrics & 16) != 0 || _preferences.ShowGpu) throw new InvalidOperationException("GPU series did not hide.");
        GpuMetric.Toggle.IsChecked = true;
        if (OverallGraph.VisibleMetrics != 31 || !_preferences.ShowGpu) throw new InvalidOperationException("GPU series did not restore.");
        await CaptureSearchDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureDiskDiagnosticsAsync(outputDirectory, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "compact.png"), 1, cancellationToken);
        GroupedChip.IsChecked = true;
        RefreshPresentation();
        if (_rows[0].Visibility != Visibility.Visible ||
            (_otherRow.Visibility == Visibility.Visible && System.Windows.Automation.AutomationProperties.GetName(_otherRow)?.Contains(Strings.Executables, StringComparison.Ordinal) != true))
            throw new InvalidOperationException("Grouped mode showed no native processes or mislabeled the remainder.");
        await CaptureAsync(Path.Combine(outputDirectory, "grouped-native.png"), 1, cancellationToken);
        IndividualChip.IsChecked = true;
        ProcessCountSelector.IsDropDownOpen = true;
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (ProcessCountSelector.Template.FindName("PART_Popup", ProcessCountSelector) is not Popup
                {
                    Child: FrameworkElement menu
                })
                throw new InvalidOperationException("The process count menu did not open.");
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(outputDirectory, "process-count-menu.png"), 1, cancellationToken);
        }
        finally { ProcessCountSelector.IsDropDownOpen = false; }
        // Switching range and widening the window keep the same recording; wide rows gain a PID line.
        var count = _history.Samples.Count;
        if (RangeSelector.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(c => c.Tag is 600) is not { } tenMinutes)
            throw new InvalidOperationException("The 10-minute range chip is missing.");
        tenMinutes.IsChecked = true;
        Width = 1000;
        UpdateLayout();
        if (_preferences.EffectiveChartRange != 600 || !_wide || _history.Samples.Count < count)
            throw new InvalidOperationException("Range or width change did not apply to the existing recording.");
        _selectedSample = _history.Samples[^2];
        RefreshPresentation();
        if (SnapshotLayer.Visibility != Visibility.Visible || SelectionLabel.Text != Strings.Format(Strings.ProcessesAtSAverage,
                _selectedSample.Timestamp, ProcessRanking.DefaultWindowSeconds, "") || SnapshotMemory.Text.Length == 0)
            throw new InvalidOperationException("Historical selection was not presented.");
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide.png"), 1, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide-150.png"), 1.5, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "range-wide-200.png"), 2, cancellationToken);
        _history.Resize(1);
        RefreshPresentation();
        if (SelectionLabel.Text != Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindowReturn) throw new InvalidOperationException("An evicted selection was silently replaced.");
        _history.Resize(_preferences.RetentionSeconds);
        ReturnToLive();
        if (_selectedSample is not null) throw new InvalidOperationException("Return to live retained a selection.");
        var beforeHidden = _presentedSample;
        var hiddenCount = _history.Samples.Count;
        Hide();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (_history.Samples.Count <= hiddenCount || !ReferenceEquals(_presentedSample, beforeHidden))
            throw new InvalidOperationException("Hidden recording stopped or presentation continued updating.");
        Show();
        if (!ReferenceEquals(_presentedSample, _history.Latest))
            throw new InvalidOperationException("Restoring the window did not refresh the latest sample.");
        var beforeMinimized = _presentedSample;
        var minimizedCount = _history.Samples.Count;
        WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (_history.Samples.Count <= minimizedCount || !ReferenceEquals(_presentedSample, beforeMinimized))
            throw new InvalidOperationException("Minimized recording stopped or presentation continued updating.");
        WindowState = WindowState.Normal;
        if (!ReferenceEquals(_presentedSample, _history.Latest))
            throw new InvalidOperationException("Restoring the minimized window did not refresh the latest sample.");
        // A stalled UI thread must not delay capture: intervals stay on schedule and the chart draws no gap.
        var stallCount = _history.Samples.Count;
        Thread.Sleep(3500);
        await Task.Delay(1500, cancellationToken);
        var stalled = _history.Samples.Skip(stallCount - 1).ToArray();
        var longestInterval = stalled.Zip(stalled.Skip(1), (a, b) => (b.MonotonicTimestamp - a.MonotonicTimestamp) / (double)Stopwatch.Frequency).DefaultIfEmpty().Max();
        if (stalled.Length < 5 || longestInterval > 2.5 || stalled.Skip(1).Any(s => s.IsGap))
            throw new InvalidOperationException($"A UI stall delayed sampling: {stalled.Length - 1} samples, longest interval {longestInterval:0.00}s.");
        _preferences = _preferences with { Theme = "Light" };
        ApplyTheme();
        await CaptureAsync(Path.Combine(outputDirectory, "light.png"), 1, cancellationToken);
        ProcessCountSelector.SelectedItem = 20;
        await CaptureAsync(Path.Combine(outputDirectory, "light-scroll.png"), 1, cancellationToken);
        ProcessCountSelector.SelectedItem = 5;
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-light.png"), cancellationToken);
        var settings = new Preferences.SettingsWindow(_preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate)) { Owner = this };
        settings.Show();
        settings.UpdateLayout();
        if (settings.Content is FrameworkElement settingsVisual)
            await CaptureVisualAsync(settingsVisual, Path.Combine(outputDirectory, "settings.png"), 1, cancellationToken);
        settings.Close();
        var idleStartCpu = self.TotalProcessorTime;
        var idleElapsed = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        var idleCpu = (self.TotalProcessorTime - idleStartCpu).TotalSeconds / idleElapsed.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
        self.Refresh();
        await CaptureDesignFixtureAsync(outputDirectory, cancellationToken);
        var report = new
        {
            CompletedAtUtc = DateTimeOffset.UtcNow,
            NativeSamples = _history.Samples.Count,
            LastTotalCpu = _history.Samples[^1].TotalCpuPercent,
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
            _history.Samples[^1].UnavailableProcesses,
            ChartRangeAndWideLayout = true,
            HistoricalSelection = true,
            SelectionExpiry = true,
            PauseResumeWithGap = true,
            ReturnToLive = true,
            HiddenRecordingAndRestore = true,
            MinimizedRecordingAndRestore = true,
            RenderScales = new[] { 1.0, 1.5, 2.0 },
            RenderScaleLimitation = "Bitmap scale checks are not a substitute for changing actual desktop DPI or testing mixed-DPI monitor transitions.",
            MeasurementSeconds = elapsed.Elapsed.TotalSeconds,
            CpuPercentOfMachineDuringSmoke = (self.TotalProcessorTime - startCpu).TotalSeconds / elapsed.Elapsed.TotalSeconds / Environment.ProcessorCount * 100,
            WorkingSetBytes = self.WorkingSet64,
            QuietObservationSeconds = idleElapsed.Elapsed.TotalSeconds,
            QuietObservationCpuPercentOfMachine = idleCpu,
            MeasurementLimitation = "Short smoke measurement includes PNG rendering; the additional 15-second quiet observation excludes captures but is not a sustained overhead benchmark."
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "smoke.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }

    internal async Task CaptureFeatureDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(output);
        if (_history.Samples.Count == 0) await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        // Check the compact process toolbar at the minimum and wider supported window sizes.
        var headerWidth = Width;
        try
        {
            foreach (var width in new[] { MinWidth, 886, 1000 })
            {
                Width = width;
                UpdateLayout();
                ValidateTitleBar();
            }
        }
        finally
        {
            Width = headerWidth;
            UpdateLayout();
        }
        CaptureWindowFrame(Path.Combine(output, "titlebar-window.png"));
        var hardware = await Task.Run(WindowsHardwareInfo.Read, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "hardware.json"),
            JsonSerializer.Serialize(hardware, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        string? copiedInfo = null;
        var info = new Controls.HardwareInfoWindow(text => copiedInfo = text) { Owner = this };
        try
        {
            info.Show();
            await info.Ready.WaitAsync(cancellationToken);
            info.UpdateLayout();
            var copyLayout = (System.Windows.Controls.DockPanel)((System.Windows.Controls.Border)info.Content).Child;
            var copyButton = copyLayout.Children.OfType<System.Windows.Controls.DockPanel>().Single().Children
                .OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == Strings.CopyAllInformation);
            if (!copyButton.IsEnabled) throw new InvalidOperationException("Copy did not become available after loading information.");
            copyButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            if (copiedInfo is null || !copiedInfo.StartsWith(Strings.Format(Strings.ProductVersion, AppInfo.Version), StringComparison.Ordinal) ||
                !copiedInfo.Contains(Strings.InstalledMemory, StringComparison.Ordinal) || !copiedInfo.Contains(Strings.PhysicalDrives, StringComparison.Ordinal))
                throw new InvalidOperationException("Copy did not export the loaded information.");
            await File.WriteAllTextAsync(Path.Combine(output, "copied-info.txt"), copiedInfo, cancellationToken);
            await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output, "hardware-info.png"), 1, cancellationToken);
            var hardwareLayout = (System.Windows.Controls.DockPanel)((System.Windows.Controls.Border)info.Content).Child;
            var hardwareScroll = hardwareLayout.Children.OfType<System.Windows.Controls.ScrollViewer>().Single();
            var sections = (System.Windows.Controls.StackPanel)hardwareScroll.Content;
            foreach (var expander in sections.Children.OfType<System.Windows.Controls.Expander>())
            {
                expander.IsExpanded = true;
                info.UpdateLayout();
                hardwareScroll.ScrollToVerticalOffset(expander.TransformToAncestor(sections).Transform(new Point()).Y - 24);
                await CaptureVisualAsync((FrameworkElement)info.Content, Path.Combine(output,
                    expander.Header.ToString()!.StartsWith("CPU", StringComparison.Ordinal) ? "cpu-details.png" :
                    expander.Header.ToString()!.StartsWith("Module", StringComparison.Ordinal) ? "memory-details.png" : "physical-drive-details.png"), 1, cancellationToken);
                expander.IsExpanded = false;
            }
        }
        finally { info.Close(); }

        var original = _preferences;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            foreach (var palette in Preferences.ChartPalettes.Names)
            {
                _preferences = original with { Theme = theme, ChartPalette = palette };
                ApplyTheme();
                RefreshPresentation();
                var fileName = string.Concat(palette.ToLowerInvariant().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
                await CaptureAsync(Path.Combine(output, $"palette-{fileName}-{theme.ToLowerInvariant()}.png"), 1, cancellationToken);
            }
        }
        finally { _preferences = original; ApplyTheme(); RefreshPresentation(); }

        var row = _rows.First(r => r.Visibility == Visibility.Visible);
        // Exercise menu creation without invoking the destructive command.
        row.PrepareProcessMenu();
        var menu = row.ContextMenu ?? throw new InvalidOperationException("Process context menu was not created.");
        if (menu.Items is not [System.Windows.Controls.MenuItem, System.Windows.Controls.MenuItem { Header: string { Length: > 0 } } item] ||
            item.ToolTip as string != Strings.AvailableForLiveProcesses)
            throw new InvalidOperationException("Process context menu did not capture its target.");
        try
        {
            menu.PlacementTarget = row;
            menu.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(output, "process-context-menu.png"), 1, cancellationToken);
            var enabled = item.IsEnabled;
            try
            {
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    _preferences = original with { Theme = theme };
                    ApplyTheme();
                    foreach (var isEnabled in new[] { true, false })
                    {
                        item.IsEnabled = isEnabled;
                        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                        menu.UpdateLayout();
                        await CaptureVisualAsync(menu, Path.Combine(output,
                            $"process-context-menu-{theme.ToLowerInvariant()}-{(isEnabled ? "enabled" : "disabled")}.png"), 1, cancellationToken);
                    }
                }
            }
            finally
            {
                item.IsEnabled = enabled;
                _preferences = original;
                ApplyTheme();
            }
        }
        finally { menu.IsOpen = false; }
        var selection = _selectedSample;
        try
        {
            _selectedSample = _history.Latest;
            RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items is not [System.Windows.Controls.MenuItem, System.Windows.Controls.MenuItem { IsEnabled: false }])
                throw new InvalidOperationException("Historical rows allowed process termination.");
            _selectedSample = null;
            GroupedChip.IsChecked = true;
            RefreshPresentation();
            row.PrepareProcessMenu();
            if (menu.Items is not [System.Windows.Controls.MenuItem, System.Windows.Controls.MenuItem { Header: string groupHeader }] ||
                !groupHeader.Contains(Strings.Format(Strings.EndGroupMenu, "").Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("Grouped rows did not offer group termination.");
        }
        finally
        {
            IndividualChip.IsChecked = true;
            _selectedSample = selection;
            RefreshPresentation();
        }
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                _preferences = original with { Theme = theme };
                ApplyTheme();
                foreach (var confirmation in new[] { true, false })
                {
                    var text = confirmation
                        ? Strings.Format(Strings.EndGroupConfirmation, 2, "Example") + "\n\nExample (PID 12345)\nExample (PID 12346)"
                        : Strings.AccessDeniedWindowsDidNotAllowWinMonToEndThis;
                    var dialog = new Controls.ProcessActionDialog(text, confirmation) { Owner = this };
                    try
                    {
                        // Show non-modally for a screenshot; never invoke termination in diagnostics.
                        dialog.Show(); dialog.UpdateLayout();
                        await CaptureVisualAsync((FrameworkElement)dialog.Content, Path.Combine(output,
                            $"process-dialog-{theme.ToLowerInvariant()}-{(confirmation ? "confirmation" : "result")}.png"), 1, cancellationToken);
                    }
                    finally { dialog.Close(); }
                }
            }
        }
        finally { _preferences = original; ApplyTheme(); }
        var settings = new Preferences.SettingsWindow(_preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate)) { Owner = this };
        try
        {
            settings.Show(); settings.UpdateLayout();
            await CaptureVisualAsync((FrameworkElement)settings.Content, Path.Combine(output, "feature-settings.png"), 1, cancellationToken);
        }
        finally { settings.Close(); }
        await CaptureUpdateDiagnosticsAsync(output, cancellationToken);
        await CaptureLanguageSettingsDiagnosticsAsync(output, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "features.json"),
            JsonSerializer.Serialize(new { HardwareInfo = true, PaletteRenders = true, ProcessContextMenu = true,
                HistoricalActionsDisabled = true, GroupTerminationOffered = true, ThemedProcessDialogs = true, LanguageSelection = true,
                LiveLanguageSwitching = true, RecordingStatePreserved = true,
                Limitation = "No destructive command invoked by the UI smoke; native termination is tested on an owned helper in the test suite." }), cancellationToken);
    }

    private async Task CaptureUpdateDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var original = _preferences;
        try
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                _preferences = original with { Theme = theme };
                ApplyTheme();
                ShowUpdateStatus(Strings.Format(Strings.UpdateAvailable, "1.0.3"), dismissible: true);
                ReleaseNotesButton.Visibility = Visibility.Visible;
                UpdateNowButton.Visibility = Visibility.Visible;
                UpdateLayout();
                if (UpdateStatusText.ActualWidth < 60 || UpdateNowButton.ActualWidth < 50 || UpdateFooter.ActualHeight < 30)
                    throw new InvalidOperationException("The update footer controls do not fit the compact window.");
                await CaptureAsync(Path.Combine(output, $"update-footer-{theme.ToLowerInvariant()}.png"), 1, cancellationToken);
            }
            ShowUpdateStatus(Strings.Format(Strings.DownloadingUpdate, 42), dismissible: false);
            CancelUpdateButton.Visibility = Visibility.Visible;
            await CaptureAsync(Path.Combine(output, "update-progress.png"), 1, cancellationToken);
        }
        finally
        {
            UpdateFooter.Visibility = Visibility.Collapsed;
            _preferences = original;
            ApplyTheme();
        }
    }

    private static System.Windows.Controls.Grid SettingsLayout(Preferences.SettingsWindow dialog) =>
        (System.Windows.Controls.Grid)((System.Windows.Controls.Border)((System.Windows.Controls.ScrollViewer)dialog.Content).Content).Child;

    private async Task CaptureLanguageSettingsDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var original = _preferences;
        var selection = _selectedSample;
        var query = ProcessSearchBox.Text;
        var samplingTask = _samplingTask;
        var anchor = _history.Latest;
        var tray = _tray;
        using var diagnosticTray = new System.Windows.Forms.NotifyIcon();
        diagnosticTray.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
        diagnosticTray.ContextMenuStrip.Items.Add(Strings.ShowWinMon);
        diagnosticTray.ContextMenuStrip.Items.Add(Strings.Exit);
        _tray = diagnosticTray;
        try
        {
            ProcessSortSelector.SelectedIndex = (int)ProcessSort.Memory;
            _selectedSample = anchor;
            ProcessSearchBox.Text = "__live_language_no_match__";
            foreach (var choice in Preferences.AppLanguages.Choices)
            {
                // Exercise the scrollable dialog even on desktops tall enough to show it all.
                var dialog = new Preferences.SettingsWindow(_preferences) { Owner = this, MinHeight = 320, Height = 600 };
                _ = Dispatcher.BeginInvoke(() =>
                {
                    var layout = SettingsLayout(dialog);
                    var selector = layout.Children.OfType<System.Windows.Controls.Grid>()
                        .SelectMany(field => field.Children.OfType<System.Windows.Controls.ComboBox>())
                        .Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == Strings.Language);
                    if (selector.SelectedValue as string != Preferences.AppLanguages.Normalize(_preferences.Language))
                        throw new InvalidOperationException("The language selector did not restore the saved choice.");
                    selector.SelectedValue = choice.Name;
                    var save = layout.Children.OfType<System.Windows.Controls.StackPanel>().Single().Children
                        .OfType<System.Windows.Controls.Button>().Single(button => button.IsDefault);
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
                await ApplyPreferencesAsync(dialog.Result);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                if (InfoButton.Content as string != Strings.Info || SettingsButton.Content as string != Strings.Settings ||
                    ProcessHistoryHeader.Text != Strings.History || MemoryMetric.Label != Strings.Memory ||
                    System.Windows.Automation.AutomationProperties.GetName(MemoryMetric.Toggle) != Strings.Format(Strings.ShowOnChart, Strings.Memory) ||
                    _tray.ContextMenuStrip!.Items[0].Text != Strings.ShowWinMon || _tray.ContextMenuStrip.Items[1].Text != Strings.Exit ||
                    _preferences.ProcessSort != ProcessSort.Memory || ProcessSortSelector.SelectedIndex != (int)ProcessSort.Memory ||
                    ProcessSortSelector.SelectedItem as string != Strings.Memory || ProcessSearchBox.Text != "__live_language_no_match__" ||
                    !ReferenceEquals(_selectedSample, anchor) || (anchor is not null && !_history.Samples.Contains(anchor)) ||
                    !ReferenceEquals(_samplingTask, samplingTask) || EmptyLabel.Text != Strings.NoRankedProcessesMatchYourSearch)
                    throw new InvalidOperationException("Live language switching left stale text or changed recording state.");
                var error = _samplingError;
                try
                {
                    _samplingError = SamplingError.ReadingsUnavailable;
                    RefreshVisiblePresentation();
                    if (ErrorLabel.Text != Strings.ReadingsUnavailableRetryingEachSecond || LiveCaption.Text != Strings.SystemResourcesUnavailable)
                        throw new InvalidOperationException("Sampling error state depended on the old language.");
                }
                finally { _samplingError = error; RefreshVisiblePresentation(); }
                if (choice.Name is "en" or "de" or "ja" or "zh-Hans")
                    await CaptureAsync(Path.Combine(output, $"live-language-{choice.Name}.png"), 1, cancellationToken);
            }
            var beforeCancel = Strings.Culture;
            var cancelled = new Preferences.SettingsWindow(_preferences) { Owner = this };
            _ = Dispatcher.BeginInvoke(() =>
            {
                var layout = SettingsLayout(cancelled);
                var selector = layout.Children.OfType<System.Windows.Controls.Grid>()
                    .SelectMany(field => field.Children.OfType<System.Windows.Controls.ComboBox>())
                    .Single(control => System.Windows.Automation.AutomationProperties.GetName(control) == Strings.Language);
                selector.SelectedValue = "en";
                cancelled.DialogResult = false;
            }, DispatcherPriority.ContextIdle);
            if (cancelled.ShowDialog() == true || cancelled.Result is not null || !Equals(Strings.Culture, beforeCancel))
                throw new InvalidOperationException("Cancelling Settings changed the language.");
            if ((await _preferenceStore.LoadAsync(cancellationToken)).Language != _preferences.Language)
                throw new InvalidOperationException("Cancelling Settings changed the saved language.");
            await ApplyPreferencesAsync(_preferences with { Language = Preferences.AppLanguages.SystemDefault });
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            if (!Equals(Strings.Culture, Preferences.AppLanguages.SystemDisplayCulture) ||
                InfoButton.Content as string != Strings.Info || SettingsButton.Content as string != Strings.Settings)
                throw new InvalidOperationException("The system choice did not restore the original Windows language.");
        }
        finally
        {
            _preferences = original;
            _selectedSample = selection;
            _tray = tray;
            ProcessSearchBox.Text = query;
            ApplyLanguage();
            diagnosticTray.ContextMenuStrip?.Dispose();
        }
    }

    private async Task CaptureToolTipAsync(string path, CancellationToken cancellationToken)
    {
        var original = OverallGraph.ToolTip;
        var tooltip = new System.Windows.Controls.ToolTip
        {
            Content = Controls.CpuGraph.DescribeSample(_history.Samples[^1]),
            PlacementTarget = OverallGraph,
            Placement = PlacementMode.Center
        };
        try
        {
            OverallGraph.ToolTip = tooltip;
            tooltip.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            tooltip.UpdateLayout();
            var expectedText = (SolidColorBrush)FindResource("Text");
            var expectedBackground = (SolidColorBrush)FindResource("Raised");
            if (tooltip.Foreground is not SolidColorBrush foreground || foreground.Color != expectedText.Color ||
                tooltip.Background is not SolidColorBrush background || background.Color != expectedBackground.Color)
                throw new InvalidOperationException("Tooltip did not use the active theme's foreground and background.");
            await CaptureVisualAsync(tooltip, path, 1, cancellationToken);
        }
        finally
        {
            tooltip.IsOpen = false;
            OverallGraph.ToolTip = original;
        }
    }

    private void ValidateTitleBar()
    {
        // A mode change may just have collapsed the historical summary; measure the current layout.
        UpdateLayout();
        ValidateProcessToolbar();
        var handle = new WindowInteropHelper(this).Handle;
        var titlePoint = ViewTitle.TranslatePoint(new Point(20, ViewTitle.ActualHeight / 2), this);
        var pinPoint = PinButton.TranslatePoint(new Point(PinButton.ActualWidth / 2, PinButton.ActualHeight / 2), this);
        if (Hit(titlePoint) != 2 || Hit(pinPoint) != 1)
            throw new InvalidOperationException("The title must drag the window, while Pin must receive client input.");
        foreach (var action in new[] { InfoButton, SettingsButton })
        {
            var point = action.TranslatePoint(new Point(action.ActualWidth / 2, action.ActualHeight / 2), this);
            if (Hit(point) != 1 || !TitleBarArea.IsAncestorOf(action))
                throw new InvalidOperationException($"{action.Content} must receive client input in the title bar.");
            var bounds = action.TransformToAncestor(TitleBarArea).TransformBounds(new Rect(action.RenderSize));
            if (bounds.Top < 0 || bounds.Bottom > TitleBarArea.ActualHeight ||
                bounds.Right > TitleBarArea.ActualWidth || action.ActualHeight < 28)
                throw new InvalidOperationException($"{action.Content} does not fit fully inside the title bar.");
        }
        var titleRight = ViewTitle.TranslatePoint(new Point(ViewTitle.ActualWidth, 0), TitleBarArea).X;
        var infoLeft = InfoButton.TranslatePoint(new Point(), TitleBarArea).X;
        var settingsLeft = SettingsButton.TranslatePoint(new Point(), TitleBarArea).X;
        if (infoLeft < titleRight || infoLeft - titleRight > 16 || settingsLeft < infoLeft + InfoButton.ActualWidth)
            throw new InvalidOperationException("Info and Settings must follow the version in the title bar.");
        // The centered mode switch adds a 28-DIP input target to the resource header.
        if (!ResourceHeader.IsAncestorOf(RangeSelector) || !ResourceHeader.IsAncestorOf(ChartModeSwitch) ||
            RangeSelector.ActualHeight > 25 || ResourceHeader.ActualHeight > 44 ||
            ProcessHeader.ActualHeight > (_inlinePinToolbar ? 44 : _inlineProcessToolbar ? 78 : 112))
            throw new InvalidOperationException($"The live chart/process headers did not stay compact: range={RangeSelector.ActualHeight}, resource={ResourceHeader.ActualHeight}, process={ProcessHeader.ActualHeight}.");
        if (Hit(new Point(2, ActualHeight / 2)) != 10)
            throw new InvalidOperationException("The integrated title bar lost the left resize border.");
        if (Hit(new Point(ActualWidth - 116, 16)) != 8 || Hit(new Point(ActualWidth - 70, 16)) != 9 || Hit(new Point(ActualWidth - 24, 16)) != 20)
            throw new InvalidOperationException("Native Minimize, Maximize and Close caption buttons must remain available.");

        return;

        int Hit(Point point)
        {
            var screen = PointToScreen(point);
            var coordinates = ((uint)(int)screen.X & 0xffff) | ((long)((int)screen.Y & 0xffff) << 16);
            return (int)SendMessageW(handle, 0x0084, IntPtr.Zero, new IntPtr(coordinates)); // WM_NCHITTEST
        }
    }

    private void CaptureWindowFrame(string path)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Window bounds unavailable.");
        using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try
        {
            if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Native title-bar capture failed.");
        }
        finally { graphics.ReleaseHdc(dc); }
        // PrintWindow can omit DWM-owned caption glyphs. Capture the composed frame only
        // when our isolated diagnostic window is foreground, never another app's surface.
        if (GetForegroundWindow() == handle)
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle { public int Left, Top, Right, Bottom; }
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
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    private async Task CaptureDiskDiagnosticsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var original = _preferences;
        var drives = DriveInfo.GetDrives();
        var second = drives.FirstOrDefault(d => d is { IsReady: true, DriveType: DriveType.Fixed } &&
                                                d.Name.TrimEnd('\\') != original.PrimaryDrive)?.Name.TrimEnd('\\');
        var missing = Enumerable.Range('A', 26).Select(c => $"{(char)c}:")
            .FirstOrDefault(n => drives.All(d => d.Name.TrimEnd('\\') != n));
        if (missing is null) throw new InvalidOperationException("No unused drive letter for the unavailable-drive check.");
        _preferences = original with { SecondaryDrive = missing, ShowSecondaryDisk = true };
        SecondaryDiskMetric.Toggle.IsChecked = true;
        RefreshPresentation();
        await Task.Delay(3000, cancellationToken);
        var unavailable = _history.Latest!.Resources;
        if (unavailable.SecondaryDiskName != missing || unavailable.SecondaryDiskActivePercent is not null ||
            unavailable.DiskActivePercent is null || unavailable.MemoryPercent is null)
            throw new InvalidOperationException("An unavailable second volume disrupted native resource sampling.");
        await CaptureAsync(Path.Combine(outputDirectory, "disk-unavailable.png"), 1, cancellationToken);
        ResourceSample? dualNative = null;
        if (second is not null)
        {
            _preferences = _preferences with { SecondaryDrive = second };
            await Task.Delay(3000, cancellationToken);
            dualNative = _history.Latest!.Resources;
            if (dualNative.SecondaryDiskName != second || dualNative.SecondaryDiskActivePercent is null || dualNative.DiskActivePercent is null)
                throw new InvalidOperationException("Two available volumes did not produce independent native readings.");
            await CaptureAsync(Path.Combine(outputDirectory, "disk-native.png"), 1, cancellationToken);
        }
        SecondaryDiskMetric.Toggle.IsChecked = false;
        if ((OverallGraph.VisibleMetrics & 32) != 0 || _preferences.ShowSecondaryDisk)
            throw new InvalidOperationException("Second disk trace did not hide independently.");
        SecondaryDiskMetric.Toggle.IsChecked = true;
        if ((OverallGraph.VisibleMetrics & 32) == 0) throw new InvalidOperationException("Second disk trace did not restore.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "disks.json"),
            JsonSerializer.Serialize(new { UnavailableDrive = unavailable, DualNative = dualNative, IndependentToggle = true },
                new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        _preferences = original;
        RefreshPresentation();
        if (SecondaryDiskMetric.Visibility != Visibility.Collapsed || Math.Abs(MinWidth - 836) > 0.001)
            throw new InvalidOperationException("Single-drive layout did not restore.");
        Width = MinWidth;
        foreach (var selection in new[] { second ?? missing, null })
        {
            var dialog = new Preferences.SettingsWindow(original with { SecondaryDrive = second ?? missing }) { Owner = this };
            _ = Dispatcher.BeginInvoke(() =>
            {
                var layout = SettingsLayout(dialog);
                var selector = layout.Children.OfType<System.Windows.Controls.Grid>()
                    .SelectMany(g => g.Children.OfType<System.Windows.Controls.ComboBox>())
                    .Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == Strings.SecondDrive);
                selector.SelectedItem = selector.Items.Cast<object>().Single(item => selection is null
                    ? item.ToString() == Strings.None : item.ToString()!.StartsWith(selection, StringComparison.Ordinal));
                var buttons = layout.Children.OfType<System.Windows.Controls.StackPanel>().Single();
                ((System.Windows.Controls.Button)buttons.Children[1]).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }, DispatcherPriority.ContextIdle);
            if (dialog.ShowDialog() != true || dialog.Result?.PrimaryDrive != original.PrimaryDrive || dialog.Result.SecondaryDrive != selection)
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
            fixture._history.Add(new CpuSample(anchor.AddSeconds(i - 119), cpu,
                [new ProcessUsage(100, 1, "chrome", 8.4, 1_303_379_968, 18_600_000) { HasAppWindow = true, GpuPercent = 12.4, GpuEngine = "3D", GpuId = "fixture", PrivateBytes = 900_000_000 },
                 new ProcessUsage(106, 7, "chrome", 1.0, 400_000_000, 500_000) { HasAppWindow = false, PrivateBytes = 250_000_000 }, new ProcessUsage(101, 2, "devenv", 4.1, 1_914_699_776, 6_400_000) { HasAppWindow = true, GpuPercent = 1.2 },
                 new ProcessUsage(102, 3, "MsMpEng", 3.6, 306_184_192, 12_800_000) { HasAppWindow = false }, new ProcessUsage(4, 4, "System", 2.9, 188_743_680, 4_100_000) { HasAppWindow = false },
                 new ProcessUsage(103, 5, "WinMon", 1.8, 97_517_568, 1_600_000) { HasAppWindow = true, GpuPercent = 0.5 },
                 new ProcessUsage(104, 6, "gpu-worker", 0, 200_000_000, 0) { HasAppWindow = false, GpuPercent = 65, GpuEngine = "Compute", GpuId = "fixture" }], 0, false, Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = (ulong)((0.62 + 0.003 * Math.Sin(i)) * 32 * 1_073_741_824),
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 42.6 + 18 * Math.Sin(i * 0.2), SecondaryDiskBytesPerSecond = 128_000_000,
                    DiskName = "C:", DiskActivePercent = 7 + 5 * Math.Abs(Math.Sin(i * 0.8)) + 68 * pulse, DiskBytesPerSecond = 84_000_000,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    GpuId = "illustrative-adapter/0", GpuEngine = "3D", GpuPercent = 43 + 3 * Math.Sin(i * 0.3) + 28 * pulse,
                    ReceiveBytesPerSecond = (2 + 2 * Math.Abs(Math.Sin(i * 0.8)) + 38 * pulse) / 100 * 125_000_000,
                    SendBytesPerSecond = 125_000
                }
            });
        }
        fixture.RefreshVisiblePresentation();
        if (fixture.Content is not FrameworkElement visual) throw new InvalidOperationException("Missing design fixture visual.");
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-150.png"), 1.5, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-200.png"), 2, cancellationToken);
        fixture.AppsOnlyCheckBox.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        if (!fixture._preferences.AppsOnly || fixture._rows.Count(row => row.Visibility == Visibility.Visible) != 3 ||
            fixture._otherRow.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("App Only did not filter the individual rows before the top limit.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-compact.png"), 1, cancellationToken);
        fixture._wide = true;
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1000, 820));
        visual.Arrange(new Rect(0, 0, 1000, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-wide.png"), 1, cancellationToken);
        fixture._wide = false;
        fixture.ProcessSearchBox.Text = "chrome";
        fixture.GroupedChip.IsChecked = true;
        fixture._selectedSample = fixture._history.Samples[^2];
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        var filteredGroup = System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]);
        if (fixture._rows.Count(row => row.Visibility == Visibility.Visible) != 1 ||
            !filteredGroup.Contains(Strings.Format(Strings.Processes, 2), StringComparison.Ordinal) ||
            !filteredGroup.Contains(Percent(9.4), StringComparison.Ordinal) ||
            !filteredGroup.Contains($" · {Strings.ProcessKindApp} · {Strings.Threads}:", StringComparison.Ordinal))
            throw new InvalidOperationException("App Only did not preserve grouped totals with search and historical selection.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "apps-only-grouped-history.png"), 1, cancellationToken);
        fixture.ProcessSearchBox.Text = "no-matching-app";
        fixture.RefreshVisiblePresentation();
        if (fixture.EmptyLabel.Visibility != Visibility.Visible || fixture.EmptyLabel.Text != Strings.NoAppsMatchCurrentView)
            throw new InvalidOperationException("App Only did not explain an empty filtered view.");
        fixture.AppsOnlyCheckBox.IsChecked = false;
        fixture.ProcessSearchBox.Clear();
        fixture.IndividualChip.IsChecked = true;
        fixture._selectedSample = null;
        fixture._preferences = fixture._preferences with { PrimaryDrive = "C:", SecondaryDrive = "D:" };

        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(720, 820));
        visual.Arrange(new Rect(0, 0, 720, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design-150.png"), 1.5, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-design-200.png"), 2, cancellationToken);
        fixture._selectedSample = fixture._history.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!fixture.SnapshotSecondaryDiskName.Text.Contains("D:") || !Controls.CpuGraph.DescribeSample(fixture._selectedSample)
                .Contains(Strings.Format(Strings.SecondaryDiskHistoryLine, "D:", "", ""), StringComparison.Ordinal))
            throw new InvalidOperationException("Historical inspection lost the second volume.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-history.png"), 1, cancellationToken);
        fixture._selectedSample = null;
        var diskSettings = new Preferences.SettingsWindow(fixture._preferences);
        var settingsVisual = (FrameworkElement)diskSettings.Content;
        settingsVisual.Measure(new Size(444, 601));
        settingsVisual.Arrange(new Rect(0, 0, 444, 601));
        settingsVisual.UpdateLayout();
        await CaptureVisualAsync(settingsVisual, Path.Combine(outputDirectory, "disks-settings-dark.png"), 1, cancellationToken);
        fixture._preferences = fixture._preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "disks-light.png"), 1, cancellationToken);
        var lightSettings = new Preferences.SettingsWindow(fixture._preferences);
        var lightSettingsVisual = (FrameworkElement)lightSettings.Content;
        lightSettingsVisual.Measure(new Size(444, 601));
        lightSettingsVisual.Arrange(new Rect(0, 0, 444, 601));
        lightSettingsVisual.UpdateLayout();
        await CaptureVisualAsync(lightSettingsVisual, Path.Combine(outputDirectory, "disks-settings-light.png"), 1, cancellationToken);
        fixture._preferences = fixture._preferences with { Theme = "Dark", SecondaryDrive = null };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        fixture.ProcessSortSelector.SelectedItem = "GPU";
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (fixture._preferences.ProcessSort != ProcessSort.Gpu || !System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("GPU ranking failed to include a CPU-idle GPU process.");
        fixture._selectedSample = fixture._history.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical GPU ranking failed.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "gpu-process-sort.png"), 1, cancellationToken);
        fixture._selectedSample = null;
        // Memory ranks by the largest working set (devenv); I/O by the busiest read/write rate (chrome).
        foreach (var (sort, expected) in new[] { (Strings.Memory, "devenv"), (Strings.IO, "chrome") })
        {
            fixture.ProcessSortSelector.SelectedItem = sort;
            fixture.RefreshVisiblePresentation();
            visual.UpdateLayout();
            if (!System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]).StartsWith(expected, StringComparison.Ordinal) ||
                fixture.MemoryHeader.Text.EndsWith('▾') != (sort == Strings.Memory))
                throw new InvalidOperationException($"{sort} ranking did not put {expected} first or mark its column.");
            if (sort == Strings.Memory) await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "memory-process-sort.png"), 1, cancellationToken);
        }
        // Clicking a column heading drives the same dropdown.
        fixture.IoHeader.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            { RoutedEvent = MouseLeftButtonUpEvent });
        if (fixture._preferences.ProcessSort != ProcessSort.Io || fixture.ProcessSortSelector.SelectedItem as string != "I/O")
            throw new InvalidOperationException("Clicking the I/O heading did not switch the ranking dropdown.");
        fixture.ProcessSortSelector.SelectedItem = "CPU";
        // Grouped: the two chrome processes become one row with summed CPU, and memory switches to private bytes.
        fixture.GroupedChip.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (!fixture._preferences.GroupProcesses || fixture.MemoryHeader.Text != Strings.Private ||
            System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]) is not { } groupedName ||
            !groupedName.StartsWith("chrome", StringComparison.Ordinal) ||
            !groupedName.Contains(Strings.Format(Strings.Processes, 2), StringComparison.Ordinal) ||
            !groupedName.Contains(Percent(9.4), StringComparison.Ordinal))
            throw new InvalidOperationException("Grouped mode did not combine the chrome processes.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "grouped.png"), 1, cancellationToken);
        fixture.IndividualChip.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        var rankedSample = fixture._history.Latest! with
        {
            Timestamp = anchor.AddSeconds(1),
            MonotonicTimestamp = Stopwatch.Frequency * 121,
            Processes =
            [
                .. Enumerable.Range(1, 25).Select(i =>
                    new ProcessUsage(1000 + i, i, $"process-{i:00}", 1d / i))
            ]
        };
        fixture._history.Add(rankedSample);
        foreach (var count in new[] { 5, 10, 15, 20 })
        {
            fixture.ProcessCountSelector.SelectedItem = count;
            fixture.RefreshVisiblePresentation();
            ArrangeFixture();
            if (fixture._rows.Count(row => row.Visibility == Visibility.Visible) != count || fixture._preferences.TopProcessCount != count)
                throw new InvalidOperationException($"Selecting {count} did not update the live process count.");
        }
        if (fixture.ProcessScroll.ScrollableHeight <= 0 || !fixture.TitleBarArea.IsAncestorOf(fixture.SettingsButton) ||
            !fixture.TitleBarArea.IsAncestorOf(fixture.InfoButton))
            throw new InvalidOperationException("Long process lists must scroll while title-bar actions remain available at minimum size.");
        fixture.ProcessScroll.ScrollToBottom();
        ArrangeFixture();
        if (fixture.ProcessScroll.VerticalOffset <= 0) throw new InvalidOperationException("The process list failed to scroll.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-20.png"), 1, cancellationToken);
        fixture._selectedSample = rankedSample;
        fixture._history.Add(rankedSample with { Timestamp = anchor.AddSeconds(2), MonotonicTimestamp = Stopwatch.Frequency * 122, Processes = [] });
        fixture.ProcessCountSelector.SelectedItem = 10;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();
        if (!ReferenceEquals(fixture._selectedSample, rankedSample) || fixture._rows.Count(row => row.Visibility == Visibility.Visible) != 10)
            throw new InvalidOperationException("Changing the count replaced the historical sample or used live processes.");
        if (fixture.ProcessScroll.ActualHeight < 108)
            throw new InvalidOperationException("Historical details left too little room for the process list.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-history.png"), 1, cancellationToken);
        fixture.ProcessCountSelector.SelectedItem = 5;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();

        // Approved compact-header mockup: same wide viewport, dual disks, grouped list, ten-minute range.
        fixture._selectedSample = null;
        fixture._wide = true;
        fixture._preferences = fixture._preferences with { PrimaryDrive = "C:", SecondaryDrive = "D:", RetentionSeconds = 3600, ChartRangeSeconds = 600, ChartPalette = "Ocean" };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        fixture.GroupedChip.IsChecked = true;
        fixture.ProcessCountSelector.SelectedItem = 20;
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1301, 1137));
        visual.Arrange(new Rect(0, 0, 1301, 1137));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "compact-headers-wide.png"), 1, cancellationToken);
        fixture._preferences = fixture._preferences with { Theme = "Light" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "compact-headers-wide-light.png"), 1, cancellationToken);

        // Light-theme target: source screenshot dimensions, Classic palette and two-minute range.
        fixture._preferences = fixture._preferences with { ChartPalette = "Classic", ChartRangeSeconds = 120 };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        fixture.RefreshVisiblePresentation();
        visual.Measure(new Size(1116, 795));
        visual.Arrange(new Rect(0, 0, 1116, 795));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "glacier-light.png"), 1, cancellationToken);

        // Exercise actual selection and layout at every corner, then resize the held selection.
        fixture._preferences = fixture._preferences with { Theme = "Dark", ChartRangeSeconds = 120, ChartPalette = "Vivid" };
        fixture.ApplyTheme();
        fixture.BuildRangeChips();
        foreach (var (width, height) in new[] { (905d, 940d), (720d, 800d), (600d, 800d) })
        {
            fixture._inlineProcessToolbar = width >= InlineProcessToolbarWidth;
            fixture._preferences = fixture._preferences with { SecondaryDrive = width < 720 ? null : "D:" };
            fixture.RefreshVisiblePresentation();
            ArrangeSnapshot(width, height);
            fixture.LiveModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (fixture.LiveModeButton.IsChecked != true || fixture.SnapshotModeButton.IsChecked == true ||
                fixture._selectedSample is not null || fixture.SnapshotLayer.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Live mode did not clear the chart selection.");
            var switchCenter = fixture.ChartModeSwitch.TranslatePoint(new Point(fixture.ChartModeSwitch.ActualWidth / 2, 0), fixture.ResourceHeader).X;
            if (Math.Abs(switchCenter - fixture.ResourceHeader.ActualWidth / 2) > 1)
                throw new InvalidOperationException("The chart mode switch is not centered in the header.");
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"chart-mode-live-{width}.png"), 1, cancellationToken);
            fixture.SnapshotModeButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            fixture.RefreshVisiblePresentation();
            if (!ReferenceEquals(fixture._selectedSample, fixture._history.Latest) ||
                fixture.SnapshotModeButton.IsChecked != true || fixture.LiveModeButton.IsChecked == true ||
                fixture.SnapshotLayer.Visibility != Visibility.Visible)
                throw new InvalidOperationException("Snapshot mode did not select the newest recorded sample.");
            ArrangeSnapshot(width, height);
            await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"chart-mode-snapshot-{width}.png"), 1, cancellationToken);
            foreach (var (name, right, bottom) in new[] { ("top-left", false, false), ("top-right", true, false), ("bottom-left", false, true), ("bottom-right", true, true) })
            {
                var plot = fixture.OverallGraph.PlotBounds;
                fixture.OverallGraph.SelectAt(new Point(right ? plot.Right : plot.Left, bottom ? plot.Bottom : plot.Top));
                fixture.RefreshVisiblePresentation();
                ArrangeSnapshot(width, height);
                AssertSnapshotContained();
                if (fixture._selectedSample is null || fixture.SnapshotCpu.Text != Percent(fixture._selectedSample.TotalCpuPercent))
                    throw new InvalidOperationException("The selected snapshot did not match the clicked sample.");
                await CaptureVisualAsync(visual, Path.Combine(outputDirectory, $"snapshot-{width}-{name}.png"), 1, cancellationToken);
            }
        }
        // Copy from a held historical sample without touching the user's Windows clipboard.
        fixture._selectedSample = fixture._history.Samples[^2];
        fixture.RefreshVisiblePresentation();
        string? copied = null;
        fixture.CopySnapshot(value => copied = value);
        if (copied is null || !copied.Contains(Strings.Format(Strings.ExportSnapshotTime, fixture._selectedSample.Timestamp)) ||
            !copied.Contains(Strings.SnapshotExportAverages) ||
            fixture.SnapshotCopyButton.Content as string != Strings.Copied ||
            !ReferenceEquals(fixture._snapshotReport?.Sample, fixture._selectedSample))
            throw new InvalidOperationException("Copy did not export the held snapshot with processes or confirm success.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "clipboard.txt"), copied, cancellationToken);
        fixture.ResetSnapshotCopyCaption();
        if (fixture.SnapshotCopyButton.Content as string != Strings.CopySnapshot)
            throw new InvalidOperationException("Copy confirmation did not reset.");
        // A shorter graph must scroll its values while keeping the live action within the panel.
        ArrangeSnapshot(600, 540);
        AssertSnapshotContained();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-short.png"), 1, cancellationToken);
        fixture._preferences = fixture._preferences with { Theme = "Light", SecondaryDrive = "D:" };
        fixture.ApplyTheme();
        fixture.RefreshVisiblePresentation();
        ArrangeSnapshot(905, 940);
        AssertSnapshotContained();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-150.png"), 1.5, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "snapshot-light-200.png"), 2, cancellationToken);
        fixture._selectedSample = fixture._history.Samples[0];
        fixture._history.Resize(1);
        fixture.RefreshVisiblePresentation();
        ArrangeSnapshot(905, 940);
        if (fixture.SnapshotNote.Text != Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindow || fixture.SnapshotReadings.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("An expired snapshot displayed stale readings.");
        copied = null;
        fixture.CopySnapshot(value => copied = value);
        if (fixture.SnapshotCopyButton.IsEnabled || copied is not null)
            throw new InvalidOperationException("An expired snapshot remained available for copying.");
        fixture.LiveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        fixture.RefreshVisiblePresentation();
        if (fixture._selectedSample is not null || fixture.SnapshotLayer.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The snapshot action did not return to live.");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "snapshot.json"),
            JsonSerializer.Serialize(new { Corners = 12, ResizedWhileSelected = true, ShortChart = true, LightAndDark = true,
                Expiry = true, ReturnToLive = true, CenteredChartModeSwitch = true, LatestSampleAction = true, SnapshotCopy = true,
                RenderScales = new[] { 1d, 1.5, 2d } }), cancellationToken);

        return;

        void ArrangeSnapshot(double width, double height)
        {
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            visual.UpdateLayout();
        }

        void AssertSnapshotContained()
        {
            var panel = fixture.SnapshotPanel.TransformToAncestor(fixture.SnapshotLayer).TransformBounds(new Rect(fixture.SnapshotPanel.RenderSize));
            var button = fixture.LiveButton.TransformToAncestor(fixture.SnapshotLayer).TransformBounds(new Rect(fixture.LiveButton.RenderSize));
            var copy = fixture.SnapshotCopyButton.TransformToAncestor(fixture.SnapshotLayer).TransformBounds(new Rect(fixture.SnapshotCopyButton.RenderSize));
            if (!fixture.OverallGraph.PlotBounds.Contains(panel) || !panel.Contains(button) || button.Height < 24 ||
                !panel.Contains(copy) || copy.Height < 24)
                throw new InvalidOperationException($"Snapshot panel or live action escaped the chart: {panel}, button {button}, plot {fixture.OverallGraph.PlotBounds}.");
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
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        if (Content is not FrameworkElement visual) throw new InvalidOperationException("The monitor has no root visual.");
        await CaptureVisualAsync(visual, path, scale, cancellationToken);
    }

    private static async Task CaptureVisualAsync(FrameworkElement visual, string path, double scale, CancellationToken cancellationToken)
    {
        visual.UpdateLayout();
        await visual.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale), (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken);
    }
}
