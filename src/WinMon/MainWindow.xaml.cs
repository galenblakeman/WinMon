using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using Forms = System.Windows.Forms;

namespace WinMon;

/// <summary>Present a shared CPU recording session in compact and investigation layouts.</summary>
public partial class MainWindow : Window
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly bool diagnosticMode;
    private readonly PreferenceStore preferenceStore = new();
    private readonly ProcessRow[] rows = Enumerable.Range(0, 20).Select(_ => new ProcessRow()).ToArray();
    private readonly ProcessRow otherRow = new() { Visibility = Visibility.Collapsed };
    private readonly CpuHistory history = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<PendingSample> pendingSamples = new();
    private int drainScheduled;
    private MonitorPreferences preferences = new();
    private Task? samplingTask;
    private Task settingsWriteTask = Task.CompletedTask;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private CpuSample? selectedSample;
    private string? samplingError;
    private int generation;
    private int paused;
    private bool expanded;
    private bool closing;
    private bool allowClose;
    private bool forceExit;
    private double compactWidth = 600;
    private double compactHeight = 840;
    private System.Windows.Controls.TextBlock CpuValue => CpuMetric.ValueLabel;
    private MetricReadout[] Metrics => [CpuMetric, MemoryMetric, DiskMetric, NetworkMetric];

    /// <summary>Create the CPU monitor and its shared live/history presentation.</summary>
    public MainWindow() : this(false) { }

    /// <summary>Create a monitor with isolated preferences for diagnostic validation.</summary>
    /// <param name="diagnosticMode">Whether to avoid reading or saving user preferences.</param>
    internal MainWindow(bool diagnosticMode)
    {
        this.diagnosticMode = diagnosticMode;
        InitializeComponent();
        ProcessCountSelector.ItemsSource = new[] { 5, 10, 15, 20 };
        ProcessCountSelector.SelectedItem = preferences.TopProcessCount;
        ProcessCountSelector.SelectionChanged += OnProcessCountChanged;
        WindowTheme.Attach(this);
        foreach (var metric in Metrics)
        {
            metric.Toggle.Checked += OnSeriesChanged;
            metric.Toggle.Unchecked += OnSeriesChanged;
        }
        foreach (var row in rows) { row.Visibility = Visibility.Collapsed; ProcessRows.Children.Add(row); }
        ProcessRows.Children.Add(otherRow);
        OverallGraph.SampleSelected += (_, sample) => { selectedSample = sample; RefreshPresentation(); };
        OverallGraph.LiveRequested += (_, _) => ReturnToLive();
        IsVisibleChanged += (_, _) => { if (IsVisible && !closing) RefreshPresentation(); };
        StateChanged += (_, _) => { if (IsVisible && WindowState != WindowState.Minimized && !closing) RefreshPresentation(); };
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            preferences = diagnosticMode ? new MonitorPreferences() : await preferenceStore.LoadAsync(lifetime.Token);
            ProcessCountSelector.SelectedItem = preferences.TopProcessCount;
            expanded = preferences.Expanded;
            Width = Math.Max(MinWidth, preferences.Width);
            Height = Math.Max(MinHeight, preferences.Height);
            var visibleMask = preferences.VisibleMetrics;
            for (var i = 0; i < Metrics.Length; i++) Metrics[i].Toggle.IsChecked = (visibleMask & (1 << i)) != 0;
            if (preferences.Left is double left && preferences.Top is double top) { Left = left; Top = top; }
            else { Left = SystemParameters.WorkArea.Left + 40; Top = SystemParameters.WorkArea.Top + 40; }
            EnsureVisible();
            Topmost = preferences.AlwaysOnTop;
            history.Resize(preferences.RetentionSeconds);
            ApplyTheme();
            UpdateLayoutMode(false);
            if (!diagnosticMode) CreateTray();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            samplingTask = Task.Run(() => CollectAsync(lifetime.Token), lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon initialization failed: {0}", ex);
            StatusLabel.Text = "Initialization failed. Close and restart WinMon.";
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var sampler = new WindowsCpuSampler();
        using var resources = new WindowsResourceSampler();
        var previousGeneration = -1;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentGeneration = Volatile.Read(ref generation);
                if (previousGeneration != currentGeneration)
                {
                    sampler.Reset();
                    resources.Reset();
                    previousGeneration = currentGeneration;
                }
                if (Volatile.Read(ref paused) != 0) continue;
                try
                {
                    CpuSample sample;
                    string? cpuError = null;
                    try { sample = sampler.Sample(); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or PlatformNotSupportedException)
                    {
                        sampler.Reset();
                        cpuError = "CPU unavailable; other resources continue recording.";
                        Trace.TraceWarning("CPU sampling unavailable: {0}", ex.Message);
                        sample = new CpuSample(DateTimeOffset.UtcNow, null, [], 0, false, Stopwatch.GetTimestamp());
                    }
                    sample = sample with { Resources = resources.Sample(sample.IsGap) };
                    Deliver(new PendingSample(currentGeneration, sample, cpuError));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Trace.TraceError("WinMon CPU sampling failed: {0}", ex);
                    sampler.Reset();
                    resources.Reset();
                    Deliver(new PendingSample(currentGeneration, null, "Readings unavailable; retrying each second."));
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (sampler is IDisposable disposable) disposable.Dispose();
        }
    }

    /// <summary>A captured sample, or a failed pass when <see cref="Sample"/> is null, awaiting the UI thread.</summary>
    private sealed record PendingSample(int Generation, CpuSample? Sample, string? Error);

    // Capture never waits for the UI: a stalled dispatcher would otherwise delay the next capture and
    // stretch the sampling interval past the chart's gap threshold. Samples queue in pendingSamples and
    // are drained in order by at most one pending dispatcher callback.
    private void Deliver(PendingSample pending)
    {
        pendingSamples.Enqueue(pending);
        if (Interlocked.Exchange(ref drainScheduled, 1) == 0)
            Dispatcher.BeginInvoke(DrainPendingSamples, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DrainPendingSamples()
    {
        // Clear the flag before draining so a sample enqueued meanwhile schedules another drain.
        Volatile.Write(ref drainScheduled, 0);
        var changed = false;
        while (pendingSamples.TryDequeue(out var pending))
        {
            if (closing || pending.Generation != Volatile.Read(ref generation)) continue;
            try
            {
                samplingError = pending.Error;
                if (pending.Sample is not null) history.Add(pending.Sample);
                changed = true;
            }
            catch (Exception ex) { Trace.TraceError("WinMon could not record a sample: {0}", ex); }
        }
        if (changed) RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            UpdateTrayText();
            return;
        }
        RefreshVisiblePresentation();
    }

    private void RefreshVisiblePresentation()
    {
        var samples = history.Samples;
        var latest = samples.Count == 0 ? null : samples[^1];
        var visible = selectedSample is null ? latest : samples.FirstOrDefault(s => ReferenceEquals(s, selectedSample));
        var range = expanded ? preferences.RetentionSeconds : preferences.CompactHistorySeconds;
        CpuValue.Text = samplingError is null && latest?.TotalCpuPercent is double total ? $"{total:0.0}%" : "—";
        var allUnavailable = samplingError?.StartsWith("Readings", StringComparison.Ordinal) == true;
        UpdateResources(allUnavailable ? null : latest);
        OverallGraph.SetData(samples, range, selectedSample);
        RangeLabel.Text = $"Last {range / 60} min · 0–100% · network on its own scale";
        LiveButton.Visibility = selectedSample is null ? Visibility.Collapsed : Visibility.Visible;
        SelectionLabel.Text = selectedSample is not null ? visible is null ? "History expired. This sample has left the recording window. Return to live." : $"Historical sample at {visible.Timestamp:HH:mm:ss} · individual processes" : "Individual processes · percentage of total CPU";
        SelectionMetrics.Visibility = selectedSample is not null && visible is not null ? Visibility.Visible : Visibility.Collapsed;
        SelectionMetrics.Text = visible is null ? "" : $"CPU {Percent(visible.TotalCpuPercent)} · Memory {Percent(visible.Resources.MemoryPercent)} · Disk {Percent(visible.Resources.DiskActivePercent)} · Network {Rate(visible.Resources)}";
        var active = visible?.Processes.Where(p => p.CpuPercent > 0).OrderByDescending(p => p.CpuPercent).ThenBy(p => p.ProcessId).ToArray() ?? [];
        var top = active.Take(preferences.TopProcessCount).ToArray();
        var rest = active.Skip(top.Length).ToArray();
        var selectedIndex = selectedSample is null ? -1 : Array.FindIndex(samples.ToArray(), s => ReferenceEquals(s, selectedSample));
        var rowSamples = selectedSample is null ? samples : samples.Take(selectedIndex + 1).ToArray();
        var processData = ProcessGraphData.Build(rowSamples, preferences.CompactHistorySeconds, top);
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i].Visibility = i < top.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < top.Length) rows[i].Update(top[i], rowSamples, preferences.CompactHistorySeconds, expanded, processData[i]);
        }
        otherRow.Visibility = rest.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rest.Length > 0)
            otherRow.ShowRemainder(rest.Length, rest.Sum(p => p.CpuPercent),
                rest.Any(p => p.IoBytesPerSecond is not null) ? rest.Sum(p => p.IoBytesPerSecond ?? 0) : null);
        EmptyLabel.Visibility = top.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = visible is null && selectedSample is not null ? "Return to live to inspect current activity." : latest is null || visible?.TotalCpuPercent is null ? "Collecting a baseline or recovering after a recording gap…" : "No measurable active processes in this interval.";
        CoverageLabel.Text = visible is null ? "" : Coverage(visible, active.Sum(p => p.CpuPercent));
        if (history.WasMemoryLimited) CoverageLabel.Text += (CoverageLabel.Text.Length > 0 ? " · " : "") + "history shortened by memory limit";
        var pausedState = Volatile.Read(ref paused) != 0;
        LiveCaption.Text = allUnavailable ? "System resources · unavailable" : "System resources";
        StatusLabel.Text = samplingError ?? (pausedState ? $"Paused · last {latest?.Timestamp:HH:mm:ss}" : latest?.TotalCpuPercent is null ? "Collecting a CPU interval…" : $"Live · every 1s · {latest.Timestamp:HH:mm:ss}");
        UpdateTrayText(latest);
    }

    /// <summary>Summarize CPU that the measured processes do not cover, without inventing a process for it.</summary>
    private static string Coverage(CpuSample sample, double measuredCpu)
    {
        var unread = sample.UnavailableProcesses > 0 ? $"{sample.UnavailableProcesses} processes need administrator access" : "";
        if (sample.TotalCpuPercent is not double total) return unread;
        var unattributed = $"{Math.Max(0, total - measuredCpu):0.0}% unattributed";
        return unread.Length > 0 ? $"{unattributed} · {unread}" : unattributed;
    }

    private void UpdateTrayText(CpuSample? latest = null)
    {
        if (tray is null) return;
        latest ??= history.Latest;
        tray.Text = $"WinMon · {(Volatile.Read(ref paused) != 0 ? "Paused" : samplingError is not null ? "Unavailable" : Percent(latest?.TotalCpuPercent) + " CPU")}";
    }

    private void OnSeriesChanged(object sender, RoutedEventArgs e)
    {
        var mask = 0;
        for (var i = 0; i < Metrics.Length; i++) if (Metrics[i].Toggle.IsChecked == true) mask |= 1 << i;
        preferences = preferences with { VisibleMetrics = mask };
        OverallGraph.VisibleMetrics = mask;
        OverallGraph.InvalidateVisual();
    }

    private void OnProcessCountChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProcessCountSelector.SelectedItem is not int count || closing) return;
        preferences = preferences with { TopProcessCount = count };
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
    }

    private void UpdateResources(CpuSample? latest)
    {
        var resource = latest?.Resources;
        CpuMetric.DetailLabel.Text = "Total machine";
        MemoryMetric.ValueLabel.Text = Percent(resource?.MemoryPercent);
        MemoryMetric.DetailLabel.Text = resource?.UsedMemoryBytes is ulong used && resource.TotalMemoryBytes is ulong total
            ? $"{used / 1_073_741_824d:0.0} / {total / 1_073_741_824d:0.#} GiB" : "Unavailable";
        DiskMetric.Label = resource?.DiskName is { Length: > 0 } disk ? $"Disk ({disk})" : "Disk";
        DiskMetric.ValueLabel.Text = Percent(resource?.DiskActivePercent);
        DiskMetric.DetailLabel.Text = resource?.DiskBytesPerSecond is double bytes ? $"{bytes / 1_000_000:0.0} MB/s" : "Collecting / unavailable";
        // Throughput leads: link utilization is almost always near 0% and says little on its own.
        if (resource is not null && CpuGraph.NetworkBitsPerSecond(resource) is double bits)
        {
            var (number, unit) = RateText.Parts(bits);
            NetworkMetric.ShowValue(number, unit);
        }
        else NetworkMetric.ValueLabel.Text = "—";
        NetworkMetric.DetailLabel.Text = resource?.ReceiveBytesPerSecond is double rx && resource.SendBytesPerSecond is double tx
            ? RateText.Pair(rx * 8, tx * 8) : "Collecting / disconnected";
        NetworkMetric.Caption = resource?.LinkBitsPerSecond is long speed
            ? $"{Percent(resource.NetworkPercent)} of {RateText.Format(speed)} link" : "Link speed unavailable";
        NetworkMetric.ToolTip = resource?.NetworkName is { Length: > 0 } name
            ? $"{name}\nThroughput of the busier direction; utilization is relative to link speed. Not Internet capacity."
            : "No connected Ethernet or Wi-Fi interface available.";
        OverallGraph.VisibleMetrics = preferences.VisibleMetrics;
    }

    private static string Percent(double? value) => value is double number ? $"{number:0.0}%" : "—";
    private static string Rate(ResourceSample resources) => CpuGraph.NetworkBitsPerSecond(resources) is double bits ? RateText.Format(bits) : "—";

    private void OnPause(object sender, RoutedEventArgs e)
    {
        var next = Volatile.Read(ref paused) == 0;
        Volatile.Write(ref paused, next ? 1 : 0);
        Interlocked.Increment(ref generation);
        PauseButton.Content = next ? "Resume" : "Pause";
        ButtonIcon.SetData(PauseButton, next ? ButtonIcon.Resume : ButtonIcon.Pause);
        RefreshPresentation();
    }

    private void OnPin(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Content = Topmost ? "Unpin" : "Pin";
        ButtonIcon.SetData(PinButton, Topmost ? ButtonIcon.Unpin : ButtonIcon.Pin);
    }

    private void OnExpand(object sender, RoutedEventArgs e)
    {
        if (!expanded) { compactWidth = ActualWidth; compactHeight = ActualHeight; }
        expanded = !expanded;
        UpdateLayoutMode(true);
        RefreshPresentation();
    }

    private void UpdateLayoutMode(bool resize)
    {
        MinWidth = expanded ? 850 : 600;
        MinHeight = 800;
        if (resize)
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            Width = expanded ? 1060 : compactWidth;
            Height = expanded ? 820 : compactHeight;
            EnsureVisible();
        }
        ApplyTheme();
        ViewTitle.Text = expanded ? "WinMon · System history" : "WinMon · System activity";
        ExpandButton.Content = expanded ? "Compact" : "Expand";
        ButtonIcon.SetData(ExpandButton, expanded ? ButtonIcon.Compact : ButtonIcon.Expand);
        System.Windows.Automation.AutomationProperties.SetName(ExpandButton, expanded ? "Return to compact view" : "Expand investigation view");
        PinButton.Content = Topmost ? "Unpin" : "Pin";
        ButtonIcon.SetData(PinButton, Topmost ? ButtonIcon.Unpin : ButtonIcon.Pin);
    }

    private void OnReturnLive(object sender, RoutedEventArgs e) => ReturnToLive();
    private void ReturnToLive() { selectedSample = null; RefreshPresentation(); }
    private void OnTray(object sender, RoutedEventArgs e) { if (tray is not null) Hide(); }

    private void CreateTray()
    {
        using (var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinMon.ico")).Stream)
        using (var sourceIcon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        tray = new Forms.NotifyIcon { Text = "WinMon · CPU monitor", Icon = trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show WinMon", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => { forceExit = true; Close(); }));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        EnsureVisible();
        Activate();
    }

    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        if (closing) return;
        try
        {
            var dialog = new SettingsWindow(preferences with { AlwaysOnTop = Topmost, StartWithWindows = StartupRegistration.IsEnabled }) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result is not MonitorPreferences result || closing) return;
            SettingsButton.IsEnabled = false;
            ProcessCountSelector.IsEnabled = false;
            settingsWriteTask = ApplyPreferencesAsync(result);
            await settingsWriteTask;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon preferences could not be saved: {0}", ex);
            if (!closing) MessageBox.Show(this, "Settings could not be saved. Check access to your local application settings and startup registration.", "WinMon settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!closing)
            {
                SettingsButton.IsEnabled = true;
                ProcessCountSelector.IsEnabled = true;
            }
        }
    }

    private async Task ApplyPreferencesAsync(MonitorPreferences result)
    {
        var wasEnabled = StartupRegistration.IsEnabled;
        var startupChanged = wasEnabled != result.StartWithWindows;
        try
        {
            if (startupChanged) StartupRegistration.SetEnabled(result.StartWithWindows);
            await preferenceStore.SaveAsync(result, lifetime.Token);
        }
        catch
        {
            if (startupChanged)
            {
                try { StartupRegistration.SetEnabled(wasEnabled); }
                catch (Exception rollbackError) { Trace.TraceError("WinMon startup registration rollback failed: {0}", rollbackError); }
            }
            throw;
        }
        preferences = result;
        history.Resize(preferences.RetentionSeconds);
        ApplyTheme();
        RefreshPresentation();
    }
    private void ApplyTheme()
    {
        var light = preferences.Theme == "Light" || preferences.Theme == "System" && IsSystemLight();
        var app = System.Windows.Application.Current;
        if (app is null) return;
        if (SystemParameters.HighContrast)
        {
            app.Resources["Surface"] = SystemColors.WindowBrush;
            app.Resources["Raised"] = SystemColors.ControlBrush;
            app.Resources["TitleBarSurface"] = SystemColors.WindowBrush;
            app.Resources["Text"] = SystemColors.WindowTextBrush;
            app.Resources["Muted"] = SystemColors.WindowTextBrush;
            app.Resources["Line"] = SystemColors.WindowTextBrush;
            app.Resources["Accent"] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent" }) app.Resources[key] = SystemColors.WindowTextBrush;
        }
        else
        {
            SetBrush("Surface", light ? "#F7F9FB" : "#15191E");
            SetBrush("Raised", light ? "#E8EDF2" : "#20262D");
            SetBrush("TitleBarSurface", light ? "#ECEFF3" : "#1B2026");
            SetBrush("Text", light ? "#17212A" : "#EDF2F6");
            SetBrush("Muted", light ? "#465361" : "#B3BDC8");
            SetBrush("Line", light ? "#CBD3DC" : "#35404B");
            SetBrush("Accent", light ? "#36751B" : "#9FE870");
            SetBrush("CpuAccent", light ? "#36751B" : "#9FE870");
            SetBrush("MemoryAccent", light ? "#1469B5" : "#65ADFF");
            SetBrush("DiskAccent", light ? "#B8461B" : "#FF7B54");
            SetBrush("NetworkAccent", light ? "#7944B4" : "#BD91FF");
        }
        foreach (Window window in app.Windows) WindowTheme.Apply(window);
        foreach (var row in rows) row.InvalidateVisual();
        otherRow.InvalidateVisual();
        OverallGraph.InvalidateVisual();
    }

    private static bool IsSystemLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (System.Security.SecurityException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void SetBrush(string key, string color)
    {
        if (System.Windows.Application.Current is System.Windows.Application app)
            app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void EnsureVisible()
    {
        // Keep any window that intersects a display; recover disconnected-monitor bounds to the primary work area.
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = new System.Drawing.Rectangle((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY), (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY));
        var visible = Forms.Screen.AllScreens.Any(s => System.Drawing.Rectangle.Intersect(s.WorkingArea, bounds) is var intersection && intersection.Width >= 100 && intersection.Height >= 80 && bounds.Top >= s.WorkingArea.Top && bounds.Top < s.WorkingArea.Bottom - 30);
        if (visible) return;
        var area = SystemParameters.WorkArea;
        Left = area.Left + 20;
        Top = area.Top + 20;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 40));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 40));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!closing) Dispatcher.BeginInvoke(() => { ApplyTheme(); RefreshPresentation(); });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!closing) Dispatcher.BeginInvoke(EnsureVisible);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && selectedSample is not null && !ProcessCountSelector.IsDropDownOpen) { ReturnToLive(); e.Handled = true; }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        if (!forceExit && preferences.CloseToTray && tray is not null) { Hide(); return; }
        closing = true;
        IsEnabled = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        try
        {
            await lifetime.CancelAsync();
            if (samplingTask is not null) await samplingTask;
            try { await settingsWriteTask; }
            catch (Exception settingsError) { Trace.TraceError("WinMon pending preference save failed during shutdown: {0}", settingsError); }
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!diagnosticMode) await preferenceStore.SaveAsync(preferences with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Expanded = expanded, AlwaysOnTop = Topmost }, CancellationToken.None);
        }
        catch (Exception ex) { Trace.TraceError("WinMon shutdown or preference save failed: {0}", ex); }
        finally
        {
            tray?.ContextMenuStrip?.Dispose();
            tray?.Dispose();
            trayIcon?.Dispose();
            lifetime.Dispose();
            allowClose = true;
            Close();
        }
    }
}






