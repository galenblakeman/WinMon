using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Shortcuts;
using Forms = System.Windows.Forms;

namespace WinMon;

/// <summary>Present a shared CPU recording session in compact and investigation layouts.</summary>
public partial class MainWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _diagnosticMode;
    private readonly PreferenceStore _preferenceStore = new();
    private readonly ProcessRow[] _rows = [.. Enumerable.Range(0, 20).Select(_ => new ProcessRow())];
    private readonly ProcessRow _otherRow = new() { Visibility = Visibility.Collapsed };
    // History keeps compacted samples; the live view ranks the newest one in full so group counts and memory are exact.
    private CpuSample? _latestFull;
    private CpuSample? _latestCompacted;
    private readonly CpuHistory _history = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<PendingSample> _pendingSamples = new();
    private int _drainScheduled;
    private MonitorPreferences _preferences = new();
    private Task? _samplingTask;
    private Task _settingsWriteTask = Task.CompletedTask;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private CpuSample? _selectedSample;
    // The newest sample the visible presentation last drew; diagnostics check it stays put while hidden.
    private CpuSample? _presentedSample;
    private string? _samplingError;
    private bool _wide;
    private const double WideWidth = 800;
    private bool _closing;
    private bool _allowClose;
    private WindowState _restoreState = WindowState.Normal;
    private GlobalHotkey? _hotkey;
    private HotkeyStatus _hotkeyStatus;
    private DispatcherTimer? _hotkeyRetry;
    private System.Windows.Controls.TextBlock CpuValue => CpuMetric.ValueLabel;
    private MetricReadout[] Metrics => [CpuMetric, MemoryMetric, DiskMetric, NetworkMetric, GpuMetric, SecondaryDiskMetric];

    /// <summary>Create the CPU monitor and its shared live/history presentation.</summary>
    public MainWindow() : this(false) { }

    /// <summary>Create a monitor with isolated preferences for diagnostic validation.</summary>
    /// <param name="diagnosticMode">Whether to avoid reading or saving user preferences.</param>
    internal MainWindow(bool diagnosticMode)
    {
        _diagnosticMode = diagnosticMode;
        InitializeComponent();
        VersionRun.Text = $" {AppInfo.Version}";
        ProcessCountSelector.ItemsSource = new[] { 5, 10, 15, 20 };
        ProcessCountSelector.SelectedItem = _preferences.TopProcessCount;
        ProcessCountSelector.SelectionChanged += OnProcessCountChanged;
        // Item order matches ProcessSort so the selected index is the sort column.
        ProcessSortSelector.ItemsSource = new[] { "CPU", "GPU", "Memory", "I/O" };
        ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
        ProcessSortSelector.SelectionChanged += OnProcessSortChanged;
        IndividualChip.Checked += OnGroupingChanged;
        GroupedChip.Checked += OnGroupingChanged;
        // Column headings are shortcuts for the "processes by" dropdown, which stays the keyboard path.
        foreach (var (header, sort, text) in SortHeaders)
        {
            header.Cursor = Cursors.Hand;
            header.ToolTip = $"{header.ToolTip}\nClick to rank processes by {text.Replace(" %", "").Replace(" MB/s", "")}.";
            header.MouseEnter += (_, _) => header.TextDecorations = TextDecorations.Underline;
            header.MouseLeave += (_, _) => header.TextDecorations = null;
            header.MouseLeftButtonUp += (_, e) => { ProcessSortSelector.SelectedIndex = (int)sort; e.Handled = true; };
        }
        WindowTheme.Attach(this);
        foreach (var metric in Metrics)
        {
            metric.Toggle.Checked += OnSeriesChanged;
            metric.Toggle.Unchecked += OnSeriesChanged;
        }
        foreach (var row in _rows)
        {
            row.Visibility = Visibility.Collapsed;
            row.EnableProcessActions(CanEndProcess, EndProcess);
            ProcessRows.Children.Add(row);
        }
        ProcessRows.Children.Add(_otherRow);
        BuildRangeChips();
        // Rows gain a PID line in a wide window; there is no separate expanded mode.
        SizeChanged += (_, _) => AlignTitleBar();
        DpiChanged += (_, _) => AlignTitleBar();
        SizeChanged += (_, _) =>
        {
            var wide = ActualWidth >= WideWidth;
            if (wide == _wide || _closing) return;
            _wide = wide;
            RefreshPresentation();
        };
        OverallGraph.SampleSelected += (_, sample) => { _selectedSample = sample; RefreshPresentation(); };
        OverallGraph.LiveRequested += (_, _) => ReturnToLive();
        IsVisibleChanged += (_, _) => { if (IsVisible && !_closing) RefreshPresentation(); };
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _preferences = _diagnosticMode ? new MonitorPreferences() : await _preferenceStore.LoadAsync(_lifetime.Token);
            ProcessSortSelector.SelectedIndex = (int)_preferences.ProcessSort;
            GroupedChip.IsChecked = _preferences.GroupProcesses;
            IndividualChip.IsChecked = !_preferences.GroupProcesses;
            ProcessCountSelector.SelectedItem = _preferences.TopProcessCount;
            Width = Math.Max(MinWidth, _preferences.Width);
            Height = Math.Max(MinHeight, _preferences.Height);
            var visibleMask = _preferences.SeriesMask;
            for (var i = 0; i < Metrics.Length; i++) Metrics[i].Toggle.IsChecked = (visibleMask & (1 << i)) != 0;
            if (_preferences is { Left: { } left, Top: { } top }) { Left = left; Top = top; }
            else { Left = SystemParameters.WorkArea.Left + 40; Top = SystemParameters.WorkArea.Top + 40; }
            EnsureVisible();
            Topmost = _preferences.AlwaysOnTop;
            _history.Resize(_preferences.RetentionSeconds);
            ApplyTheme();
            BuildRangeChips();
            UpdatePinButton();
            AlignTitleBar();
            if (!_diagnosticMode)
            {
                CreateTray();
                _hotkey = new GlobalHotkey(this, ShowRequested);
                ApplyShortcut(StartMenuShortcut.Exists);
            }
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _samplingTask = Task.Run(() => CollectAsync(_lifetime.Token), _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon initialization failed: {0}", ex);
            ShowError("Initialization failed. Close and restart WinMon.");
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var sampler = new WindowsCpuSampler();
        using var resources = new WindowsResourceSampler();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    CpuSample sample;
                    string? cpuError = null;
                    try { sample = sampler.Sample(); }
                    catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
                    {
                        sampler.Reset();
                        cpuError = "CPU unavailable; other resources continue recording.";
                        Trace.TraceWarning("CPU sampling unavailable: {0}", ex.Message);
                        sample = new CpuSample(DateTimeOffset.UtcNow, null, [], 0, false, Stopwatch.GetTimestamp());
                    }
                    var diskPreferences = Volatile.Read(ref _preferences);
                    var full = resources.Sample(sample, diskPreferences.PrimaryDrive, diskPreferences.SecondaryDrive);
                    Deliver(new PendingSample(ProcessRetention.Compact(full), cpuError, full));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Trace.TraceError("WinMon CPU sampling failed: {0}", ex);
                    sampler.Reset();
                    resources.Reset();
                    Deliver(new PendingSample(null, "Readings unavailable; retrying each second."));
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
    private sealed record PendingSample(CpuSample? Sample, string? Error, CpuSample? Full = null);

    // Capture never waits for the UI: a stalled dispatcher would otherwise delay the next capture and
    // stretch the sampling interval past the chart's gap threshold. Samples queue in pendingSamples and
    // are drained in order by at most one pending dispatcher callback.
    private void Deliver(PendingSample pending)
    {
        _pendingSamples.Enqueue(pending);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            Dispatcher.BeginInvoke(DrainPendingSamples, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void DrainPendingSamples()
    {
        // Clear the flag before draining so a sample enqueued meanwhile schedules another drain.
        Volatile.Write(ref _drainScheduled, 0);
        var changed = false;
        while (_pendingSamples.TryDequeue(out var pending))
        {
            if (_closing) continue;
            try
            {
                _samplingError = pending.Error;
                if (pending.Sample is not null)
                {
                    _history.Add(pending.Sample);
                    _latestCompacted = pending.Sample;
                    _latestFull = pending.Full;
                }
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
        var samples = _history.Samples;
        var latest = samples.Count == 0 ? null : samples[^1];
        var visible = _selectedSample is null ? latest : samples.FirstOrDefault(s => ReferenceEquals(s, _selectedSample));
        var range = _preferences.EffectiveChartRange;
        CpuValue.Text = _samplingError is null && latest?.TotalCpuPercent is { } total ? $"{total:0.0}%" : "—";
        var allUnavailable = _samplingError?.StartsWith("Readings", StringComparison.Ordinal) == true;
        UpdateResources(allUnavailable ? null : latest);
        OverallGraph.SetData(samples, range, _selectedSample);
        LiveButton.Visibility = _selectedSample is null ? Visibility.Collapsed : Visibility.Visible;
        var grouped = _preferences.GroupProcesses;
        var historyMetric = _preferences.ProcessSort switch
        {
            ProcessSort.Gpu => grouped ? "busiest-member GPU" : "GPU",
            ProcessSort.Memory => grouped ? "summed private memory" : "memory",
            ProcessSort.Io => grouped ? "summed I/O" : "I/O",
            _ => grouped ? "summed CPU" : "CPU"
        };
        ProcessHistoryHeader.ToolTip = $"{historyMetric} history on one scale shared by every row. Hover a row for the scale.";
        SelectionLabel.Text = _selectedSample is not null ? visible is null ? "History expired. This sample has left the recording window. Return to live." : $"Historical sample at {visible.Timestamp:HH:mm:ss} · {ProcessRanking.DefaultWindowSeconds}s average ending here{(grouped ? " · idle group members may be omitted" : "")}"
            : grouped ? $"Grouped by executable · {ProcessRanking.DefaultWindowSeconds}s averages · {historyMetric} history" : $"Individual processes · {ProcessRanking.DefaultWindowSeconds}s averages · {historyMetric} history sparklines";
        SelectionMetrics.Visibility = _selectedSample is not null && visible is not null ? Visibility.Visible : Visibility.Collapsed;
        SelectionMetrics.Text = visible is null ? "" : $"CPU {Percent(visible.TotalCpuPercent)} · Memory {Percent(visible.Resources.MemoryPercent)} · Disk {visible.Resources.DiskName} {Percent(visible.Resources.DiskActivePercent)}{(visible.Resources.SecondaryDiskName.Length > 0 ? $" · Disk {visible.Resources.SecondaryDiskName} {Percent(visible.Resources.SecondaryDiskActivePercent)}" : "")} · Network {Rate(visible.Resources)} · GPU {Percent(visible.Resources.GpuPercent)}";
        var selectedIndex = _selectedSample is null ? -1 : Array.FindIndex([.. samples], s => ReferenceEquals(s, _selectedSample));
        var rowSamples = _selectedSample is null ? samples : [.. samples.Take(selectedIndex + 1)];
        if (_selectedSample is null && _latestFull is not null && rowSamples.Count > 0 && ReferenceEquals(rowSamples[^1], _latestCompacted))
            rowSamples = [.. rowSamples.Take(rowSamples.Count - 1), _latestFull];
        // Rank on a short average ending at the live or selected sample; single intervals are tick-quantized.
        var cpuRanking = visible is null ? new ProcessRanking([], null, 0) : ProcessRanking.Rank(rowSamples, grouped: grouped);
        var ranking = _preferences.ProcessSort == ProcessSort.Cpu ? cpuRanking : ProcessRanking.Rank(rowSamples, sort: _preferences.ProcessSort, grouped: grouped);
        UpdateSortHeaders();
        var active = ranking.Processes;
        var top = active.Take(_preferences.TopProcessCount).ToArray();
        var rest = active.Skip(top.Length).ToArray();
        var processData = ProcessGraphData.Build(rowSamples, MonitorPreferences.ShortRangeSeconds, top, grouped, _preferences.ProcessSort);
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].Visibility = i < top.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < top.Length) _rows[i].Update(top[i], rowSamples, MonitorPreferences.ShortRangeSeconds, _wide, processData[i], grouped);
        }
        _otherRow.Visibility = rest.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rest.Length > 0)
            // Memory ranking also covers idle processes compacted out of the sample.
            _otherRow.ShowRemainder(rest.Length + (_preferences.ProcessSort == ProcessSort.Memory && !grouped ? visible?.CompactedProcesses ?? 0 : 0),
                rest.Sum(p => p.CpuPercent),
                rest.Any(p => p.IoBytesPerSecond is not null) ? rest.Sum(p => p.IoBytesPerSecond ?? 0) : null, grouped);
        EmptyLabel.Visibility = top.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = visible is null && _selectedSample is not null ? "Return to live to inspect current activity." : latest is null || visible?.TotalCpuPercent is null ? "Collecting a baseline or recovering after a recording gap…" : _preferences.ProcessSort switch
            {
                ProcessSort.Gpu => "No measurable GPU activity among accessible processes in this window.",
                ProcessSort.Memory => "No memory readings among accessible processes.",
                ProcessSort.Io => "No measurable I/O among accessible processes in this window.",
                _ => "No measurable active processes in this window."
            };
        CoverageLabel.Text = visible is null ? "" : Coverage(visible.UnavailableProcesses, cpuRanking.TotalCpuPercent, cpuRanking.Processes.Sum(p => p.CpuPercent));
        if (_history.WasMemoryLimited) CoverageLabel.Text += (CoverageLabel.Text.Length > 0 ? " · " : "") + "history shortened by memory limit";
        LiveCaption.Text = allUnavailable ? "System resources · unavailable" : "System resources";
        ShowError(_samplingError);
        _presentedSample = latest;
        UpdateTrayText(latest);
    }

    private void ShowError(string? message)
    {
        ErrorLabel.Text = message ?? "";
        ErrorLabel.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Summarize CPU that the measured processes do not cover, without inventing a process for it.</summary>
    private static string Coverage(int unavailableProcesses, double? totalCpu, double measuredCpu)
    {
        var unread = unavailableProcesses > 0 ? $"{unavailableProcesses} processes need administrator access" : "";
        if (totalCpu is not { } total) return unread;
        var unattributed = $"{Math.Max(0, total - measuredCpu):0.0}% unattributed";
        return unread.Length > 0 ? $"{unattributed} · {unread}" : unattributed;
    }

    private void UpdateTrayText(CpuSample? latest = null)
    {
        if (_tray is null) return;
        latest ??= _history.Latest;
        _tray.Text = $"WinMon · {(_samplingError is not null ? "Unavailable" : Percent(latest?.TotalCpuPercent) + " CPU")}" +
            (_hotkeyStatus is HotkeyStatus.InUse or HotkeyStatus.Failed ? " · Shortcut unavailable" : "");
    }

    private void OnSeriesChanged(object sender, RoutedEventArgs e)
    {
        var mask = 0;
        for (var i = 0; i < Metrics.Length; i++) if (Metrics[i].Toggle.IsChecked == true) mask |= 1 << i;
        _preferences = _preferences with { VisibleMetrics = mask & 15, ShowGpu = (mask & 16) != 0, ShowSecondaryDisk = _preferences.SecondaryDrive is null ? _preferences.ShowSecondaryDisk : (mask & 32) != 0 };
        OverallGraph.VisibleMetrics = mask;
        OverallGraph.InvalidateVisual();
    }

    private void OnProcessCountChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProcessCountSelector.SelectedItem is not int count || _closing) return;
        _preferences = _preferences with { TopProcessCount = count };
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
    }

    private void OnGroupingChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _preferences = _preferences with { GroupProcesses = GroupedChip.IsChecked == true };
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
    }

    private void OnProcessSortChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProcessSortSelector.SelectedIndex < 0 || _closing) return;
        _preferences = _preferences with { ProcessSort = (ProcessSort)ProcessSortSelector.SelectedIndex };
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
    }

    /// <summary>Mark the column that orders the rows; the others stay muted.</summary>
    private (System.Windows.Controls.TextBlock Header, ProcessSort Sort, string Text)[] SortHeaders =>
    [
        (CpuHeader, ProcessSort.Cpu, "CPU %"), (GpuHeader, ProcessSort.Gpu, "GPU %"),
        (MemoryHeader, ProcessSort.Memory, "Memory"), (IoHeader, ProcessSort.Io, "I/O MB/s")
    ];

    private void UpdateSortHeaders()
    {
        foreach (var (header, sort, name) in SortHeaders)
        {
            var text = sort == ProcessSort.Memory && _preferences.GroupProcesses ? "Private" : name;
            var active = sort == _preferences.ProcessSort;
            header.Text = active ? $"{text} ▾" : text;
            header.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            header.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, active ? "Text" : "Muted");
        }
    }

    private void UpdateResources(CpuSample? latest)
    {
        var resource = latest?.Resources;
        GpuMetric.ValueLabel.Text = Percent(resource?.GpuPercent);
        GpuMetric.DetailLabel.Text = resource?.GpuPercent is null ? "Unavailable"
            : resource.GpuEngine is { Length: > 0 } engine ? engine : "Busiest GPU";
        GpuMetric.ToolTip = resource?.GpuPercent is not null
            ? $"Adapter {resource.GpuId} · {resource.GpuEngine}\nUtilization of the busiest engine across available GPUs."
            : "GPU counters unavailable or collecting a fresh interval.";
        CpuMetric.DetailLabel.Text = "Total machine";
        MemoryMetric.ValueLabel.Text = Percent(resource?.MemoryPercent);
        MemoryMetric.DetailLabel.Text = resource is { UsedMemoryBytes: { } used, TotalMemoryBytes: { } total }
            ? $"{used / 1_073_741_824d:0.0} / {total / 1_073_741_824d:0.#} GiB" : "Unavailable";
        ConfigureDiskLayout();
        UpdateDiskMetric(DiskMetric, _preferences.PrimaryDrive,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskActivePercent : null,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is { } secondary)
            UpdateDiskMetric(SecondaryDiskMetric, secondary,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskActivePercent : null,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is not null) SecondaryDiskMetric.ToolTip += "\nDrawn as a dashed line on the chart.";
        // Throughput leads: link utilization is almost always near 0% and says little on its own.
        if (resource is not null && CpuGraph.NetworkBitsPerSecond(resource) is { } bits)
        {
            var (number, unit) = RateText.Parts(bits);
            NetworkMetric.ShowValue(number, unit);
        }
        else NetworkMetric.ValueLabel.Text = "—";
        NetworkMetric.DetailLabel.Text = resource is { ReceiveBytesPerSecond: { } rx, SendBytesPerSecond: { } tx }
            ? RateText.Pair(rx * 8, tx * 8) : "Collecting / disconnected";
        // Link speed and utilization live in the tooltip; the tiles show only the value and rates.
        var link = resource?.LinkBitsPerSecond is { } speed ? $" · {RateText.Format(speed)} link" : "";
        var utilization = resource?.NetworkPercent is not null ? $" · {Percent(resource.NetworkPercent)} of link" : " · link speed unavailable";
        NetworkMetric.ToolTip = resource?.NetworkName is { Length: > 0 } name
            ? $"{name}{link}{utilization}\n↓ receive · ↑ send. The value is the busier direction; utilization is relative to link speed. Not Internet capacity."
            : "No connected Ethernet or Wi-Fi interface available.";
        OverallGraph.VisibleMetrics = _preferences.SeriesMask;
    }

    private void ConfigureDiskLayout()
    {
        var dual = _preferences.SecondaryDrive is not null;
        SecondaryDiskColumn.Width = dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SecondaryDiskMetric.Visibility = SecondaryDiskDivider.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = dual ? 720 : 600;
        if (Width < MinWidth) Width = MinWidth;
    }

    private static void UpdateDiskMetric(MetricReadout metric, string name, double? active, double? bytes)
    {
        metric.Label = $"Disk ({name})";
        metric.ValueLabel.Text = Percent(active);
        metric.DetailLabel.Text = bytes is { } rate ? $"{rate / 1_000_000:0.0} MB/s" : "Unavailable";
        metric.ToolTip = $"Volume {name}\nActive time, not space used. Read/write throughput in MB/s.\nUnavailable while disconnected or collecting a fresh interval.";
    }
    private static string Percent(double? value) => value is { } number ? $"{number:0.0}%" : "—";
    private static string Rate(ResourceSample resources) => CpuGraph.NetworkBitsPerSecond(resources) is { } bits ? RateText.Format(bits) : "—";

    private void OnPin(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinButton();
    }

    private void UpdatePinButton()
    {
        // Icon-only toggle: the pin turns accent-colored while the window stays on top.
        PinButton.SetResourceReference(ForegroundProperty, Topmost ? "Accent" : "Text");
        PinButton.ToolTip = Topmost ? "Kept on top of other windows. Click to stop." : "Keep this window on top of other windows";
        System.Windows.Automation.AutomationProperties.SetName(PinButton, Topmost ? "Keep on top, on" : "Keep on top, off");
    }

    /// <summary>Sit Pin flush against the native caption buttons at their exact height, whatever the DPI.</summary>
    private void AlignTitleBar()
    {
        if (WindowTheme.CaptionButtonBounds(this) is not { } buttons || ActualWidth <= 0) return;
        var height = Math.Max(24, buttons.Bottom);
        TitleRow.Height = new GridLength(height);
        TitleBarArea.Margin = new Thickness(0, 0, Math.Max(0, ActualWidth - buttons.Left), 0);
        PinButton.Height = buttons.Height;
        PinButton.Margin = new Thickness(0, buttons.Top, 0, 0);
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome && Math.Abs(chrome.CaptionHeight - height) > 0.5)
        {
            chrome.CaptionHeight = height;
            chrome.GlassFrameThickness = new Thickness(0, height, 0, 0);
        }
    }

    /// <summary>Rebuild the range chips; choices longer than the retention setting are left out.</summary>
    private void BuildRangeChips()
    {
        RangeSelector.Children.Clear();
        foreach (var seconds in _preferences.ChartRanges)
        {
            var chip = new System.Windows.Controls.RadioButton
            {
                Content = $"{seconds / 60}m",
                Tag = seconds,
                GroupName = "ChartRange",
                IsChecked = seconds == _preferences.EffectiveChartRange,
                Style = (Style)FindResource("RangeChip"),
                ToolTip = $"Show the last {seconds / 60} minutes"
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, $"Show the last {seconds / 60} minutes");
            chip.Checked += OnRangeChecked;
            RangeSelector.Children.Add(chip);
        }
    }

    private void OnRangeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.RadioButton { Tag: int seconds } || _closing) return;
        _preferences = _preferences with { ChartRangeSeconds = seconds };
        RefreshPresentation();
    }

    private void OnReturnLive(object sender, RoutedEventArgs e) => ReturnToLive();
    private void OnHardwareInfo(object sender, RoutedEventArgs e)
    {
        if (!_closing) new HardwareInfoWindow { Owner = this }.ShowDialog();
    }
    private bool CanEndProcess(ProcessUsage process) => !_closing && ProcessTerminator.IsEligible(process,
        _selectedSample is null, _preferences.GroupProcesses);

    private void EndProcess(ProcessUsage process)
    {
        if (!CanEndProcess(process)) return;
        var answer = MessageBox.Show(this,
            $"End {process.Name} (PID {process.ProcessId})?\n\nUnsaved work may be lost. Only this process will be ended.",
            "End process", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || !CanEndProcess(process)) return;
        var error = ProcessTerminator.End(process);
        if (error is not null) MessageBox.Show(this, error, "End process", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ReturnToLive() { _selectedSample = null; RefreshPresentation(); }

    private void CreateTray()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinMon.ico"))
            ?? throw new InvalidOperationException("The embedded tray icon is unavailable.");
        using (var stream = resource.Stream)
        using (var sourceIcon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            _trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        _tray = new Forms.NotifyIcon { Text = "WinMon · CPU monitor", Icon = _trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show WinMon", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(RestoreFromTray); };
    }

    private void OnWindowStateChanged()
    {
        if (_closing) return;
        if (WindowState != WindowState.Minimized)
        {
            _restoreState = WindowState;
            if (IsVisible) RefreshPresentation();
        }
        else if (_preferences.MinimizeToTray && _tray is not null) Hide();
    }

    /// <summary>Show the window for the shortcut or another launch; minimize it instead when it is already in front.</summary>
    internal void ShowRequested()
    {
        if (_closing) return;
        if (IsVisible && WindowState != WindowState.Minimized && IsActive) WindowState = WindowState.Minimized;
        else RestoreFromTray();
        // A modal dialog such as Settings keeps the focus.
        if (OwnedWindows.Count > 0) OwnedWindows[0].Activate();
    }

    /// <summary>Give the show shortcut to Explorer when it should also start WinMon; otherwise hold it here.</summary>
    private void ApplyShortcut(bool launch)
    {
        if (_hotkey is null) return;
        _hotkeyRetry?.Stop();
        var gesture = _preferences.ShowGesture;
        try
        {
            if (launch && gesture is not null)
            {
                // Explorer cannot take a combination WinMon holds, and does not retry once WinMon exits.
                _hotkey.Unregister();
                StartMenuShortcut.Write(gesture);
                _hotkeyStatus = HotkeyStatus.StartMenu;
            }
            else
            {
                StartMenuShortcut.Delete();
                RegisterShortcut(gesture);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            Trace.TraceError("WinMon Start menu shortcut could not be updated: {0}", ex);
            RegisterShortcut(gesture);
        }
        UpdateTrayText();
    }

    private void RegisterShortcut(HotkeyGesture? gesture)
    {
        var hotkey = _hotkey!;
        _hotkeyStatus = hotkey.Register(gesture);
        if (_hotkeyStatus != HotkeyStatus.InUse) return;
        // Explorer releases a deleted shortcut's hotkey within a few seconds, so keep trying briefly.
        var attempts = 0;
        _hotkeyRetry = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (!_closing) _hotkeyStatus = hotkey.Register(gesture);
            if (_closing || _hotkeyStatus != HotkeyStatus.InUse || ++attempts >= 10) _hotkeyRetry?.Stop();
            UpdateTrayText();
        }, Dispatcher);
    }

    private bool IsShortcutAvailable(HotkeyGesture gesture) =>
        gesture == (_hotkey?.Current ?? (_hotkeyStatus == HotkeyStatus.StartMenu ? _preferences.ShowGesture : null)) || GlobalHotkey.IsAvailable(gesture);

    private string ShortcutStatus => _hotkeyStatus switch
    {
        HotkeyStatus.Registered => "Works from any app while WinMon is running.",
        HotkeyStatus.StartMenu => "Works from any app, and starts WinMon when it isn't running.",
        HotkeyStatus.InUse => "Another app is using this shortcut. Choose another.",
        HotkeyStatus.Failed => "This shortcut could not be registered. Choose another.",
        _ => ""
    };

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _restoreState;
        EnsureVisible();
        Activate();
    }

    // WPF requires a void event handler; operation failures are caught and logged below.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnSettings(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        try
        {
            var dialog = new SettingsWindow(_preferences with { AlwaysOnTop = Topmost, StartWithWindows = StartupRegistration.IsEnabled, LaunchWithShortcut = StartMenuShortcut.Exists },
                IsShortcutAvailable, ShortcutStatus) { Owner = this };
            // Let the shortcut recorder receive WinMon's own combination; Save or the finally block takes it back.
            _hotkeyRetry?.Stop();
            _hotkey?.Unregister();
            if (dialog.ShowDialog() != true || dialog.Result is not { } result || _closing) return;
            SettingsButton.IsEnabled = false;
            ProcessCountSelector.IsEnabled = false;
            ProcessSortSelector.IsEnabled = false;
            _settingsWriteTask = ApplyPreferencesAsync(result);
            await _settingsWriteTask;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon preferences could not be saved: {0}", ex);
            if (!_closing) MessageBox.Show(this, "Settings could not be saved. Check access to your local application settings and startup registration.", "WinMon settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!_closing)
            {
                SettingsButton.IsEnabled = true;
                ProcessCountSelector.IsEnabled = true;
                ProcessSortSelector.IsEnabled = true;
                if (_hotkey is { Current: null } && _hotkeyStatus != HotkeyStatus.StartMenu) ApplyShortcut(StartMenuShortcut.Exists);
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
            await _preferenceStore.SaveAsync(result, _lifetime.Token);
        }
        catch
        {
            if (!startupChanged) throw;
            try { StartupRegistration.SetEnabled(wasEnabled); }
            catch (Exception rollbackError) { Trace.TraceError("WinMon startup registration rollback failed: {0}", rollbackError); }
            throw;
        }
        _preferences = PreferenceStore.Validate(result);
        SecondaryDiskMetric.Toggle.IsChecked = _preferences.ShowSecondaryDisk;
        ConfigureDiskLayout();
        _history.Resize(_preferences.RetentionSeconds);
        BuildRangeChips();
        ApplyTheme();
        ApplyShortcut(_preferences.LaunchWithShortcut);
        RefreshPresentation();
    }
    private void ApplyTheme()
    {
        var light = _preferences.Theme == "Light" || (_preferences.Theme == "System" && IsSystemLight());
        var app = Application.Current;
        if (app is null) return;
        OverallGraph.UseSeriesPatterns = _preferences.ChartPalette == "Colorblind";
        if (SystemParameters.HighContrast)
        {
            app.Resources["Surface"] = SystemColors.WindowBrush;
            app.Resources["Raised"] = SystemColors.ControlBrush;
            app.Resources["TitleBarSurface"] = SystemColors.WindowBrush;
            app.Resources["SectionHeaderSurface"] = SystemColors.WindowBrush;
            app.Resources["Text"] = SystemColors.WindowTextBrush;
            app.Resources["Muted"] = SystemColors.WindowTextBrush;
            app.Resources["Line"] = SystemColors.WindowTextBrush;
            app.Resources["Accent"] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent" }) app.Resources[key] = SystemColors.WindowTextBrush;
        }
        else
        {
            SetBrush("Surface", light ? "#F7F9FB" : "#15191E");
            SetBrush("Raised", light ? "#E8EDF2" : "#20262D");
            SetBrush("TitleBarSurface", light ? "#DDE1E6" : "#30343B");
            SetBrush("SectionHeaderSurface", light ? "#EBEDF0" : "#252A31");
            SetBrush("Text", light ? "#17212A" : "#EDF2F6");
            SetBrush("Muted", light ? "#465361" : "#B3BDC8");
            SetBrush("Line", light ? "#CBD3DC" : "#35404B");
            var colors = ChartPalettes.Colors(_preferences.ChartPalette, light);
            var keys = new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent" };
            for (var i = 0; i < keys.Length; i++) SetBrush(keys[i], colors[i]);
            SetBrush("Accent", colors[0]);
        }
        foreach (Window window in app.Windows) WindowTheme.Apply(window);
        foreach (var row in _rows) row.InvalidateVisual();
        _otherRow.InvalidateVisual();
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
        if (Application.Current is { } app)
            app.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void EnsureVisible()
    {
        // Keep any window that intersects a display; recover disconnected-monitor bounds to the primary work area.
        var dpi = VisualTreeHelper.GetDpi(this);
        var bounds = new System.Drawing.Rectangle((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY), (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY));
        var visible = Forms.Screen.AllScreens.Any(s => System.Drawing.Rectangle.Intersect(s.WorkingArea, bounds) is
        {
            Width: >= 100, Height: >= 80
        } && bounds.Top >= s.WorkingArea.Top && bounds.Top < s.WorkingArea.Bottom - 30);
        if (visible) return;
        var area = SystemParameters.WorkArea;
        Left = area.Left + 20;
        Top = area.Top + 20;
        Width = Math.Min(Width, Math.Max(MinWidth, area.Width - 40));
        Height = Math.Min(Height, Math.Max(MinHeight, area.Height - 40));
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!_closing) Dispatcher.BeginInvoke(() => { ApplyTheme(); RefreshPresentation(); });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_closing) Dispatcher.BeginInvoke(EnsureVisible);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _selectedSample is null || ProcessCountSelector.IsDropDownOpen ||
            ProcessSortSelector.IsDropDownOpen) return;
        ReturnToLive(); e.Handled = true;
    }

    // WPF requires a void event handler; shutdown failures are caught and logged below.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        try
        {
            await _lifetime.CancelAsync();
            if (_samplingTask is not null) await _samplingTask;
            try { await _settingsWriteTask; }
            catch (Exception settingsError) { Trace.TraceError("WinMon pending preference save failed during shutdown: {0}", settingsError); }
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!_diagnosticMode) await _preferenceStore.SaveAsync(_preferences with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, AlwaysOnTop = Topmost }, CancellationToken.None);
        }
        catch (Exception ex) { Trace.TraceError("WinMon shutdown or preference save failed: {0}", ex); }
        finally
        {
            _hotkeyRetry?.Stop();
            _hotkey?.Dispose();
            _tray?.ContextMenuStrip?.Dispose();
            _tray?.Dispose();
            _trayIcon?.Dispose();
            _lifetime.Dispose();
            _allowClose = true;
            Close();
        }
    }
}
