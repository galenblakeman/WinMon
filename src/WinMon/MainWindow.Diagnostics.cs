using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
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
        var nativeResources = _history.Samples[^1].Resources;
        var nativeProcessGpu = _history.Samples[^1].Processes.Where(p => p.GpuPercent is not null)
            .OrderByDescending(p => p.GpuPercent).Take(10)
            .Select(p => new { p.ProcessId, p.Name, p.GpuPercent, p.GpuEngine, p.GpuId }).ToArray();
        if (nativeResources.MemoryPercent is null)
            throw new InvalidOperationException("Native physical-memory sampling did not arrive.");
        ValidateTitleBar();
        CaptureWindowFrame(Path.Combine(outputDirectory, "titlebar-window.png"));
        await CaptureToolTipAsync(Path.Combine(outputDirectory, "tooltip-dark.png"), cancellationToken);
        MemoryMetric.Toggle.IsChecked = false;
        if ((OverallGraph.VisibleMetrics & 2) != 0) throw new InvalidOperationException("Memory series did not hide.");
        MemoryMetric.Toggle.IsChecked = true;
        if (OverallGraph.VisibleMetrics != 31) throw new InvalidOperationException("Series visibility did not restore.");
        GpuMetric.Toggle.IsChecked = false;
        if ((OverallGraph.VisibleMetrics & 16) != 0 || _preferences.ShowGpu) throw new InvalidOperationException("GPU series did not hide.");
        GpuMetric.Toggle.IsChecked = true;
        if (OverallGraph.VisibleMetrics != 31 || !_preferences.ShowGpu) throw new InvalidOperationException("GPU series did not restore.");
        await CaptureAsync(Path.Combine(outputDirectory, "compact.png"), 1, cancellationToken);
        ProcessCountSelector.IsDropDownOpen = true;
        try
        {
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
            if (ProcessCountSelector.Template.FindName("PART_Popup", ProcessCountSelector) is not System.Windows.Controls.Primitives.Popup
                {
                    Child: FrameworkElement menu
                })
                throw new InvalidOperationException("The process count menu did not open.");
            menu.UpdateLayout();
            await CaptureVisualAsync(menu, Path.Combine(outputDirectory, "process-count-menu.png"), 1, cancellationToken);
        }
        finally { ProcessCountSelector.IsDropDownOpen = false; }
        var count = _history.Samples.Count;
        OnExpand(this, new RoutedEventArgs());
        if (!_expanded || _history.Samples.Count != count) throw new InvalidOperationException("Expansion replaced the recording session.");
        _selectedSample = _history.Samples[^2];
        RefreshPresentation();
        if (LiveButton.Visibility != Visibility.Visible || !SelectionLabel.Text.Contains("Historical") || !SelectionMetrics.Text.Contains("Memory"))
            throw new InvalidOperationException("Historical selection was not presented.");
        await CaptureAsync(Path.Combine(outputDirectory, "investigate.png"), 1, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "investigate-150.png"), 1.5, cancellationToken);
        await CaptureAsync(Path.Combine(outputDirectory, "investigate-200.png"), 2, cancellationToken);
        _history.Resize(1);
        RefreshPresentation();
        if (!SelectionLabel.Text.StartsWith("History expired", StringComparison.Ordinal)) throw new InvalidOperationException("An evicted selection was silently replaced.");
        _history.Resize(_preferences.RetentionSeconds);
        OnPause(this, new RoutedEventArgs());
        var pausedCount = _history.Samples.Count;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (_history.Samples.Count != pausedCount) throw new InvalidOperationException("Recording continued while paused.");
        OnPause(this, new RoutedEventArgs());
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        if (_history.Samples.Count <= pausedCount) throw new InvalidOperationException("Recording failed to resume.");
        if (!_history.Samples.Skip(pausedCount).Any(s => s.IsGap || s.TotalCpuPercent is null))
            throw new InvalidOperationException("Resume failed to create a baseline gap.");
        ReturnToLive();
        if (_selectedSample is not null) throw new InvalidOperationException("Return to live retained a selection.");
        var beforeHidden = StatusLabel.Text;
        var hiddenCount = _history.Samples.Count;
        Hide();
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (_history.Samples.Count <= hiddenCount || StatusLabel.Text != beforeHidden)
            throw new InvalidOperationException("Hidden recording stopped or presentation continued updating.");
        Show();
        if (!StatusLabel.Text.EndsWith(_history.Latest!.Timestamp.ToString("HH:mm:ss"), StringComparison.Ordinal))
            throw new InvalidOperationException("Restoring the window did not refresh the latest sample.");
        var beforeMinimized = StatusLabel.Text;
        var minimizedCount = _history.Samples.Count;
        WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        if (_history.Samples.Count <= minimizedCount || StatusLabel.Text != beforeMinimized)
            throw new InvalidOperationException("Minimized recording stopped or presentation continued updating.");
        WindowState = WindowState.Normal;
        if (!StatusLabel.Text.EndsWith(_history.Latest!.Timestamp.ToString("HH:mm:ss"), StringComparison.Ordinal))
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
        var settings = new Preferences.SettingsWindow(_preferences) { Owner = this };
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
            CompactToExpanded = true,
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

    private async Task CaptureToolTipAsync(string path, CancellationToken cancellationToken)
    {
        var original = OverallGraph.ToolTip;
        var tooltip = new System.Windows.Controls.ToolTip
        {
            Content = Controls.CpuGraph.DescribeSample(_history.Samples[^1]),
            PlacementTarget = OverallGraph,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Center
        };
        try
        {
            OverallGraph.ToolTip = tooltip;
            tooltip.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
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
        var handle = new WindowInteropHelper(this).Handle;
        var titlePoint = ViewTitle.TranslatePoint(new Point(20, ViewTitle.ActualHeight / 2), this);
        var pinPoint = PinButton.TranslatePoint(new Point(PinButton.ActualWidth / 2, PinButton.ActualHeight / 2), this);
        var expandPoint = ExpandButton.TranslatePoint(new Point(ExpandButton.ActualWidth / 2, ExpandButton.ActualHeight / 2), this);
        if (Hit(titlePoint) != 2 || Hit(pinPoint) != 1 || Hit(expandPoint) != 1)
            throw new InvalidOperationException("The title must drag the window, while Pin and Expand must receive client input.");
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

    private static async Task CaptureDesignFixtureAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        // Render-only fixture, separate from native sampling evidence. Never enters a user's session.
        var fixture = new MainWindow(true);
        fixture.ApplyTheme();
        var anchor = DateTimeOffset.Now;
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 92) / 2d, 2));
            var cpu = 18 + 6 * Math.Sin(i * 1.7) + 52 * pulse;
            fixture._history.Add(new CpuSample(anchor.AddSeconds(i - 119), cpu,
                [new ProcessUsage(100, 1, "chrome", 8.4, 1_303_379_968, 18_600_000) { GpuPercent = 12.4, GpuEngine = "3D", GpuId = "fixture" }, new ProcessUsage(101, 2, "devenv", 4.1, 1_914_699_776, 6_400_000) { GpuPercent = 1.2 },
                 new ProcessUsage(102, 3, "MsMpEng", 3.6, 306_184_192, 12_800_000), new ProcessUsage(4, 4, "System", 2.9, 188_743_680, 4_100_000),
                 new ProcessUsage(103, 5, "WinMon", 1.8, 97_517_568, 1_600_000) { GpuPercent = 0.5 },
                 new ProcessUsage(104, 6, "gpu-worker", 0, 200_000_000, 0) { GpuPercent = 65, GpuEngine = "Compute", GpuId = "fixture" }], 0, false, Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = (ulong)((0.62 + 0.003 * Math.Sin(i)) * 32 * 1_073_741_824),
                    DiskName = "C:", DiskActivePercent = 7 + 5 * Math.Abs(Math.Sin(i * 0.8)) + 68 * pulse, DiskBytesPerSecond = 84_000_000,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    GpuId = "illustrative-adapter/0", GpuEngine = "3D", GpuPercent = 43 + 3 * Math.Sin(i * 0.3) + 28 * pulse,
                    ReceiveBytesPerSecond = (2 + 2 * Math.Abs(Math.Sin(i * 0.8)) + 38 * pulse) / 100 * 125_000_000,
                    SendBytesPerSecond = 125_000
                }
            });
        }
        fixture.RefreshVisiblePresentation();
        fixture.StatusLabel.Text = "Illustrative data · design validation";
        if (fixture.Content is not FrameworkElement visual) throw new InvalidOperationException("Missing design fixture visual.");
        visual.Measure(new Size(600, 820));
        visual.Arrange(new Rect(0, 0, 600, 820));
        visual.UpdateLayout();
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact.png"), 1, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-150.png"), 1.5, cancellationToken);
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "design-compact-200.png"), 2, cancellationToken);
        fixture.ProcessSortSelector.SelectedItem = "GPU";
        fixture.RefreshVisiblePresentation();
        visual.UpdateLayout();
        if (!fixture._preferences.SortByGpu || !System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("GPU ranking failed to include a CPU-idle GPU process.");
        fixture._selectedSample = fixture._history.Samples[^2];
        fixture.RefreshVisiblePresentation();
        if (!System.Windows.Automation.AutomationProperties.GetName(fixture._rows[0]).StartsWith("gpu-worker", StringComparison.Ordinal))
            throw new InvalidOperationException("Historical GPU ranking failed.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "gpu-process-sort.png"), 1, cancellationToken);
        fixture._selectedSample = null;
        fixture.ProcessSortSelector.SelectedItem = "CPU";
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
        if (fixture.ProcessScroll.ScrollableHeight <= 0 || fixture.SettingsButton.TranslatePoint(new Point(0, fixture.SettingsButton.ActualHeight), visual).Y > visual.ActualHeight)
            throw new InvalidOperationException($"Long process lists must scroll without hiding the footer at minimum size. Scrollable={fixture.ProcessScroll.ScrollableHeight}, footer={fixture.SettingsButton.TranslatePoint(new Point(), visual).Y}, height={visual.ActualHeight}.");
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
        if (fixture.SettingsButton.TranslatePoint(new Point(0, fixture.SettingsButton.ActualHeight), visual).Y > visual.ActualHeight)
            throw new InvalidOperationException("Historical details pushed the footer outside the minimum-size window.");
        await CaptureVisualAsync(visual, Path.Combine(outputDirectory, "process-count-history.png"), 1, cancellationToken);
        fixture.ProcessCountSelector.SelectedItem = 5;
        fixture.RefreshVisiblePresentation();
        ArrangeFixture();

        return;

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
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
        if (Content is not FrameworkElement visual) throw new InvalidOperationException("The monitor has no root visual.");
        await CaptureVisualAsync(visual, path, scale, cancellationToken);
    }

    private static async Task CaptureVisualAsync(FrameworkElement visual, string path, double scale, CancellationToken cancellationToken)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * scale), (int)Math.Ceiling(visual.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken);
    }
}



