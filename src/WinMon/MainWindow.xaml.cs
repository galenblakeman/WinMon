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
using WinMon.Updates;
using Forms = System.Windows.Forms;
// ReSharper disable ConvertIfStatementToSwitchStatement
namespace WinMon;

/// <summary>Present a shared CPU recording session in compact and investigation layouts.</summary>
public partial class MainWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _diagnosticMode;
    private readonly PreferenceStore _preferenceStore;
    private readonly MonitorPreferences? _initialPreferences;
    private readonly ProcessRow[] _rows = [.. Enumerable.Range(0, 50).Select(_ => new ProcessRow())];
    private readonly ProcessRow _otherRow = new() { Visibility = Visibility.Collapsed };
    private readonly ProcessPins _processPins = new();
    private readonly List<ProcessRow> _pinnedRows = [];
    private Func<ProcessUsage, bool>? _retainPinned;
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
    private enum SamplingError { CpuUnavailable, ReadingsUnavailable }
    private SamplingError? _samplingError;
    private bool _wide;
    private const double WideWidth = 800;
    private const double InlineProcessToolbarWidth = 700;
    private bool _inlineProcessToolbar;
    private bool _inlinePinToolbar;
    private bool _closing;
    private bool _allowClose;
    private bool _preparingUpdateExit;
    private bool _updateReadySignaled;
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
    /// <param name="initialPreferences">Preferences already read before choosing the startup UI language.</param>
    /// <param name="preferenceStore">The store selected at startup, preserving the portable or installed storage mode.</param>
    internal MainWindow(bool diagnosticMode, MonitorPreferences? initialPreferences = null, PreferenceStore? preferenceStore = null)
    {
        _diagnosticMode = diagnosticMode;
        _initialPreferences = initialPreferences;
        _preferenceStore = preferenceStore ?? new PreferenceStore();
        InitializeComponent();
        InitializeCompactView();
        InitializeServices();
        ProcessActions.ActionRequested += OnProcessAction;
        VersionRun.Text = $" {AppInfo.Version}";
        ProcessCountSelector.ItemsSource = new[] { 5, 10, 15, 20, 50 };
        ProcessCountSelector.SelectedItem = _preferences.TopProcessCount;
        ProcessCountSelector.SelectionChanged += OnProcessCountChanged;
        // Item order matches ProcessSort so the selected index is the sort column.
        ProcessSortSelector.ItemsSource = new[] { Strings.CPU, Strings.GPU, Strings.Memory, Strings.IO, Strings.Threads, Strings.Handles };
        ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
        ProcessSortSelector.SelectionChanged += OnProcessSortChanged;
        ProcessSearchBox.TextChanged += OnProcessSearchChanged;
        AppsOnlyCheckBox.Checked += OnAppsOnlyChanged;
        AppsOnlyCheckBox.Unchecked += OnAppsOnlyChanged;
        PinnedOnlyCheckBox.Checked += OnPinnedOnlyChanged;
        PinnedOnlyCheckBox.Unchecked += OnPinnedOnlyChanged;
        IndividualChip.Checked += OnGroupingChanged;
        GroupedChip.Checked += OnGroupingChanged;
        // Column headings are shortcuts for the "processes by" dropdown, which stays the keyboard path.
        foreach (var (header, sort, _) in SortHeaders)
        {
            header.Cursor = Cursors.Hand;
            header.MouseEnter += (_, _) => header.TextDecorations = TextDecorations.Underline;
            header.MouseLeave += (_, _) => header.TextDecorations = null;
            header.MouseLeftButtonUp += (_, e) => { ProcessSortSelector.SelectedIndex = (int)sort; e.Handled = true; };
        }
        UpdateSortHeaderToolTips();
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
            EnableProcessInspection(row);
            row.EnablePinning(_processPins.Contains, ToggleProcessPin);
            ProcessRows.Children.Add(row);
        }
        ProcessRows.Children.Add(_otherRow);
        BuildRangeChips();
        // The process-type line gains a PID in a wide window; there is no separate expanded mode.
        SizeChanged += (_, _) => AlignTitleBar();
        DpiChanged += (_, _) => AlignTitleBar();
        SizeChanged += (_, _) =>
        {
            var wide = ActualWidth >= WideWidth;
            var inlineProcessToolbar = ActualWidth >= InlineProcessToolbarWidth;
            var inlinePinToolbar = inlineProcessToolbar;
            if ((wide == _wide && inlineProcessToolbar == _inlineProcessToolbar && inlinePinToolbar == _inlinePinToolbar) || _closing) return;
            _wide = wide;
            _inlineProcessToolbar = inlineProcessToolbar;
            _inlinePinToolbar = inlinePinToolbar;
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
            _preferences = _diagnosticMode ? new MonitorPreferences() : _initialPreferences ?? await _preferenceStore.LoadAsync(_lifetime.Token);
            _processPins.Restore(_preferences.PinnedProcesses);
            UpdateProcessRetention();
            PinnedOnlyCheckBox.IsChecked = _preferences.PinnedOnly;
            ProcessSortSelector.SelectedIndex = (int)_preferences.ProcessSort;
            GroupedChip.IsChecked = _preferences.GroupProcesses;
            IndividualChip.IsChecked = !_preferences.GroupProcesses;
            AppsOnlyCheckBox.IsChecked = _preferences.AppsOnly;
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
            if (_preferences.CompactMode) SetCompactView(true);
            BuildRangeChips();
            UpdatePinButton();
            AlignTitleBar();
            if (!_diagnosticMode)
            {
                CreateTray();
                _hotkey = new GlobalHotkey(this, ShowRequested);
                ApplyShortcut();
            }
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _samplingTask = Task.Run(() => CollectAsync(_lifetime.Token), _lifetime.Token);
            InitializeUpdates();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon initialization failed: {0}", ex);
            ShowError(Strings.InitializationFailedCloseAndRestartWinMon);
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        var sampler = new WindowsCpuSampler();
        var serviceTree = new ServiceTree();
        var services = new WindowsServices.Reader();
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
                    ServiceCatalog? serviceCatalog = null;
                    try { serviceCatalog = services.Capture(); }
                    catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException)
                    { Trace.TraceWarning("Service enumeration unavailable: {0}", ex.Message); }
                    SamplingError? cpuError = null;
                    try { sample = sampler.Sample(); }
                    catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
                    {
                        sampler.Reset();
                        cpuError = SamplingError.CpuUnavailable;
                        Trace.TraceWarning("CPU sampling unavailable: {0}", ex.Message);
                        sample = new CpuSample(DateTimeOffset.UtcNow, null, [], 0, false, Stopwatch.GetTimestamp());
                    }
                    var diskPreferences = Volatile.Read(ref _preferences);
                    var full = resources.Sample(sample, diskPreferences.PrimaryDrive, diskPreferences.SecondaryDrive);
                    Deliver(new PendingSample(ProcessRetention.Compact(full, retain: Volatile.Read(ref _retainPinned)), cpuError, full,
                        serviceTree.Capture(full, serviceCatalog)));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    Trace.TraceError("WinMon CPU sampling failed: {0}", ex);
                    sampler.Reset();
                    resources.Reset();
                    Deliver(new PendingSample(null, SamplingError.ReadingsUnavailable));
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
    private sealed record PendingSample(CpuSample? Sample, SamplingError? Error, CpuSample? Full = null, ServiceFrame? Services = null);

    // Capture never waits for the UI: a stalled dispatcher would otherwise delay the next capture and
    // stretch the sampling interval past the chart's gap threshold. Samples queue in pendingSamples and
    // are drained in order by at most one pending dispatcher callback.
    private void Deliver(PendingSample pending)
    {
        _pendingSamples.Enqueue(pending);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0)
            Dispatcher.BeginInvoke(DrainPendingSamples, DispatcherPriority.Background);
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
                    if (pending.Services is not null) _serviceHistory.Add(pending.Services);
                    if (!_diagnosticMode && !_updateReadySignaled && pending.Error is null)
                    {
                        _updateReadySignaled = true;
                        UpdateRuntime.SignalReady();
                        ShowCompletedUpdateNotice();
                    }
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
        System.Windows.Controls.Grid.SetRow(ProcessSearchBorder, _inlineProcessToolbar ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(ProcessSearchBorder, _inlineProcessToolbar ? 2 : 0);
        System.Windows.Controls.Grid.SetColumnSpan(ProcessSearchBorder, _inlineProcessToolbar ? 1 : 4);
        ProcessSearchBorder.Margin = _inlineProcessToolbar ? new Thickness(12, 0, 12, 0) : new Thickness(0, 6, 12, 0);
        System.Windows.Controls.Grid.SetRow(AppsOnlyCheckBox, _inlineProcessToolbar ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(AppsOnlyCheckBox, _inlineProcessToolbar ? 3 : 4);
        System.Windows.Controls.Grid.SetColumnSpan(AppsOnlyCheckBox, _inlineProcessToolbar ? 1 : 2);
        AppsOnlyCheckBox.Margin = _inlineProcessToolbar ? new Thickness(0, 0, 12, 0) : new Thickness(0, 6, 0, 0);
        ArrangePinToolbar();
        var samples = _history.Samples;
        var latest = samples.Count == 0 ? null : samples[^1];
        var visible = _selectedSample is null ? latest : samples.FirstOrDefault(s => ReferenceEquals(s, _selectedSample));
        var range = _preferences.EffectiveChartRange;
        CpuValue.Text = _samplingError is null && latest?.TotalCpuPercent is { } total ? Strings.Format(Strings.PercentOneDecimal, total) : Strings.MissingReading;
        var allUnavailable = _samplingError == SamplingError.ReadingsUnavailable;
        UpdateResources(allUnavailable ? null : latest);
        OverallGraph.SetData(samples, range, _selectedSample);
        RefreshCompactView();
        RefreshServices();
        UpdateSnapshot(_selectedSample is null ? null : visible);
        // The compact strip hides the process list; leaving compact mode refreshes it.
        if (!_compactMode) RefreshProcesses(samples, latest, visible);
        LiveCaption.Text = allUnavailable ? Strings.SystemResourcesUnavailable : Strings.SystemResources;
        LiveCaption.ToolTip = _selectedSample is not null ? Strings.SystemResourcesInspectingSnapshot : LiveCaption.Text;
        ShowError(_samplingError switch
        {
            SamplingError.CpuUnavailable => Strings.CPUUnavailableOtherResourcesContinueRecording,
            SamplingError.ReadingsUnavailable => Strings.ReadingsUnavailableRetryingEachSecond,
            _ => null
        });
        _presentedSample = latest;
        UpdateTrayText(latest);
    }

    private void RefreshProcesses(IReadOnlyList<CpuSample> samples, CpuSample? latest, CpuSample? visible)
    {
        var grouped = _preferences.GroupProcesses;
        var historyMetric = _preferences.ProcessSort switch
        {
            ProcessSort.Gpu => grouped ? Strings.BusiestMemberGPU : Strings.GPU,
            ProcessSort.Memory => grouped ? Strings.SummedPrivateMemory : Strings.MemoryLowercase,
            ProcessSort.Io => grouped ? Strings.SummedIO : Strings.IO,
            ProcessSort.Threads => Strings.Threads,
            ProcessSort.Handles => Strings.Handles,
            _ => grouped ? Strings.SummedCPU : Strings.CPU
        };
        ProcessHistoryHeader.ToolTip = Strings.Format(Strings.HistoryOnOneScaleSharedByEveryRowHoverA, historyMetric);
        SelectionLabel.Text = _selectedSample is not null ? visible is null ? Strings.HistoryExpiredThisSampleHasLeftTheRecordingWindowReturn : Strings.Format(Strings.ProcessesAtSAverage, visible.Timestamp, ProcessRanking.DefaultWindowSeconds, grouped ? Strings.IdleGroupMembersMayBeOmitted : "")
            : string.Empty;
        SelectionLabel.Visibility = _selectedSample is null ? Visibility.Collapsed : Visibility.Visible;
        var selectedIndex = -1;
        if (_selectedSample is not null)
            for (var i = 0; i < samples.Count; i++)
                if (ReferenceEquals(samples[i], _selectedSample)) { selectedIndex = i; break; }
        // Views over the history snapshot rather than copies of it, which run to 3,600 samples.
        IReadOnlyList<CpuSample> rowSamples = _selectedSample is null ? samples : new SampleList(samples, selectedIndex + 1);
        if (_selectedSample is null && _latestFull is not null && rowSamples.Count > 0 && ReferenceEquals(rowSamples[^1], _latestCompacted))
            rowSamples = new SampleList(samples, samples.Count, _latestFull);
        // Rank on a short average ending at the live or selected sample; single intervals are tick-quantized.
        var query = ProcessSearchBox.Text.Trim();
        var ranking = ProcessRanking.Rank(rowSamples, sort: _preferences.ProcessSort, grouped: grouped,
            includeInactive: query.Length > 0);
        // Pins and the selection need inactive processes too. A search already ranked them; otherwise rank once.
        var inclusive = query.Length > 0 ? ranking : null;
        ProcessRanking Inclusive() => inclusive ??= ProcessRanking.Rank(rowSamples, sort: _preferences.ProcessSort,
            grouped: grouped, includeInactive: true);
        UpdateSortHeaders();
        var appsOnly = _preferences.AppsOnly;
        var active = ProcessSearch.Filter(ranking.Processes, query, appsOnly).ToArray();
        var pinned = ResolvePinnedRows(rowSamples, grouped, query, appsOnly, Inclusive);
        var unpinned = active.Where(p => !_processPins.Contains(p, grouped)).ToArray();
        var pinnedOnly = PinnedOnlyCheckBox.IsChecked == true;
        var top = pinnedOnly ? [] : unpinned.Take(_preferences.TopProcessCount).ToArray();
        var rest = pinnedOnly ? [] : unpinned.Skip(top.Length).ToArray();
        var displayed = pinned.Select(p => p.Usage).Concat(top).ToArray();
        var processData = ProcessGraphData.Build(rowSamples, MonitorPreferences.ShortRangeSeconds, displayed, grouped, _preferences.ProcessSort);
        UpdatePinnedRows(pinned, processData, rowSamples, grouped);
        for (var i = 0; i < _rows.Length; i++)
        {
            _rows[i].Visibility = i < top.Length ? Visibility.Visible : Visibility.Collapsed;
            if (i < top.Length) _rows[i].Update(top[i], rowSamples, MonitorPreferences.ShortRangeSeconds, _wide, processData[pinned.Length + i], grouped);
        }
        RefreshProcessActions(rowSamples, grouped, Inclusive);
        _otherRow.Visibility = rest.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rest.Length > 0)
            // Memory ranking also covers idle processes compacted out of the sample.
            _otherRow.ShowRemainder(rest.Length + (query.Length == 0 && !appsOnly && _preferences.ProcessSort == ProcessSort.Memory && !grouped ? visible?.CompactedProcesses ?? 0 : 0),
                rest.Sum(p => p.CpuPercent),
                rest.Any(p => p.IoBytesPerSecond is not null) ? rest.Sum(p => p.IoBytesPerSecond ?? 0) : null, grouped);
        EmptyLabel.Visibility = displayed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLabel.Text = visible is null && _selectedSample is not null ? Strings.ReturnToLiveToInspectCurrentActivity : latest is null || visible?.TotalCpuPercent is null ? Strings.CollectingABaselineOrRecoveringAfterARecordingGap : appsOnly ? Strings.NoAppsMatchCurrentView : query.Length > 0 ? Strings.NoRankedProcessesMatchYourSearch : _preferences.ProcessSort switch
            {
                ProcessSort.Gpu => Strings.NoMeasurableGPUActivityAmongAccessibleProcessesInThisWindow,
                ProcessSort.Memory => Strings.NoMemoryReadingsAmongAccessibleProcesses,
                ProcessSort.Io => Strings.NoMeasurableIOAmongAccessibleProcessesInThisWindow,
                ProcessSort.Threads or ProcessSort.Handles => Strings.NoProcessCountReadings,
                _ => Strings.NoMeasurableActiveProcessesInThisWindow
            };
        if (pinnedOnly && displayed.Length == 0) EmptyLabel.Text = Strings.NoPinnedProcessesMatch;
        CoverageLabel.Text = visible is null ? "" : Coverage(visible.UnavailableProcesses, ranking.TotalCpuPercent, ranking.MeasuredCpuPercent);
        if (_history.WasMemoryLimited) CoverageLabel.Text += (CoverageLabel.Text.Length > 0 ? " · " : "") + Strings.HistoryShortenedByMemoryLimit;
        CoverageLabel.ToolTip = CoverageLabel.Text + Strings.UnattributedCPUIsTotalCPUMinusAllMeasuredProcessesKernel;
        if (_selectedSample is not null && visible is not null)
            _snapshotReport = new SnapshotReport(visible, displayed, grouped,
                SortHeaders.First(h => h.Sort == _preferences.ProcessSort).Text, query, appsOnly, CoverageLabel.Text,
                rest.Length == 0 ? null : new SnapshotRemainder(
                    rest.Length + (query.Length == 0 && !appsOnly && _preferences.ProcessSort == ProcessSort.Memory && !grouped ? visible.CompactedProcesses : 0),
                    rest.Sum(p => p.CpuPercent), rest.Any(p => p.IoBytesPerSecond is not null) ? rest.Sum(p => p.IoBytesPerSecond ?? 0) : null))
            { ProcessStates = [.. pinned.Select(p => ProcessRow.StateText(p.State)), .. top.Select(_ => "")] };
    }

    private void ShowError(string? message)
    {
        ErrorLabel.Text = message ?? "";
        ErrorLabel.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Summarize CPU that the measured processes do not cover, without inventing a process for it.</summary>
    private static string Coverage(int unavailableProcesses, double? totalCpu, double measuredCpu)
    {
        var unread = unavailableProcesses > 0 ? Strings.Format(Strings.ProcessesNeedAdministratorAccess, unavailableProcesses) : "";
        if (totalCpu is not { } total) return unread;
        var unattributed = Strings.Format(Strings.Unattributed, Math.Max(0, total - measuredCpu));
        return unread.Length > 0 ? $"{unattributed} · {unread}" : unattributed;
    }

    private void UpdateTrayText(CpuSample? latest = null)
    {
        if (_tray is null) return;
        latest ??= _history.Latest;
        _tray.Text = Strings.Format(Strings.TrayStatus, _samplingError is not null ? Strings.Unavailable : Percent(latest?.TotalCpuPercent) + Strings.CpuTraySuffix) +
            (_hotkeyStatus is HotkeyStatus.InUse or HotkeyStatus.Failed ? Strings.ShortcutUnavailable : "");
    }

    private void OnSeriesChanged(object sender, RoutedEventArgs e)
    {
        var mask = 0;
        for (var i = 0; i < Metrics.Length; i++) if (Metrics[i].Toggle.IsChecked == true) mask |= 1 << i;
        _preferences = _preferences with { VisibleMetrics = mask & 15, ShowGpu = (mask & 16) != 0, ShowSecondaryDisk = _preferences.SecondaryDrive is null ? _preferences.ShowSecondaryDisk : (mask & 32) != 0 };
        OverallGraph.VisibleMetrics = _preferences.SeriesMask;
        OverallGraph.InvalidateVisual();
    }

    private void OnProcessSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_closing) return;
        var hasText = ProcessSearchBox.Text.Length > 0;
        ProcessSearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearProcessSearchButton.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
    }

    private void OnClearProcessSearch(object sender, RoutedEventArgs e)
    {
        ProcessSearchBox.Clear();
        ProcessSearchBox.Focus();
    }

    private void OnAppsOnlyChanged(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _preferences = _preferences with { AppsOnly = AppsOnlyCheckBox.IsChecked == true };
        ProcessScroll.ScrollToTop();
        RefreshPresentation();
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
        (CpuHeader, ProcessSort.Cpu, Strings.CpuColumn), (GpuHeader, ProcessSort.Gpu, Strings.GpuColumn),
        (MemoryHeader, ProcessSort.Memory, Strings.Memory), (IoHeader, ProcessSort.Io, Strings.IOMBS),
        (ThreadsHeader, ProcessSort.Threads, Strings.Threads), (HandlesHeader, ProcessSort.Handles, Strings.Handles)
    ];

    private void UpdateSortHeaders()
    {
        foreach (var (header, sort, name) in SortHeaders)
        {
            var text = sort == ProcessSort.Memory ? $"{(_preferences.GroupProcesses ? Strings.Private : name)} MiB" : name;
            var active = sort == _preferences.ProcessSort;
            header.Text = active ? $"{text} ▾" : text;
            header.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            // Each new resource reference is resolved again, so set one only when the heading changes state.
            if (header.Tag is bool shown && shown == active) continue;
            header.Tag = active;
            header.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, active ? "Text" : "Muted");
        }
    }

    private void UpdateResources(CpuSample? latest)
    {
        var resource = latest?.Resources;
        GpuMetric.ValueLabel.Text = Percent(resource?.GpuPercent);
        GpuMetric.DetailLabel.Text = resource is { GpuMemoryUsedBytes: { } gpuUsed, GpuMemoryTotalBytes: > 0 }
            ? $"{gpuUsed / 1_073_741_824d:0.0} / {resource.GpuMemoryTotalBytes.Value / 1_073_741_824d:0.0} GiB" : "3D";
        GpuMetric.DetailLabel.ToolTip = resource?.GpuMemoryTotalBytes is { } gpuTotal
            ? Strings.Format(Strings.DedicatedVideoMemoryGiB, resource.GpuId, gpuTotal / 1_073_741_824d) : Strings.Unavailable;
        GpuMetric.ToolTip = resource?.GpuPercent is not null
            ? Strings.Format(Strings.AdapterUtilizationOfTheBusiestEngineAcrossAvailableGPUs, resource.GpuId, resource.GpuEngine)
            : Strings.GPUCountersUnavailableOrCollectingAFreshInterval;
        CpuMetric.DetailLabel.Text = resource?.CpuClockMhz is > 0
            ? $"{resource.CpuClockMhz.Value / 1000d:0.00} GHz" : Strings.Unavailable;
        CpuMetric.DetailLabel.ToolTip = Strings.CpuClockDescription;
        MemoryMetric.ValueLabel.Text = Percent(resource?.MemoryPercent);
        MemoryMetric.DetailLabel.Text = resource is { UsedMemoryBytes: { } used, TotalMemoryBytes: { } total }
            ? Strings.Format(Strings.GiB, used / 1_073_741_824d, total / 1_073_741_824d) : Strings.Unavailable;
        ConfigureDiskLayout();
        UpdateDiskMetric(DiskMetric, _preferences.PrimaryDrive,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskActivePercent : null,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is { } secondary)
            UpdateDiskMetric(SecondaryDiskMetric, secondary,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskActivePercent : null,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is not null) SecondaryDiskMetric.ToolTip += Strings.DrawnAsADashedLineOnTheChart;
        // Throughput leads: link utilization is almost always near 0% and says little on its own.
        if (resource?.ReceiveBytesPerSecond is { } receive)
        {
            var (number, unit) = RateText.Parts(receive * 8);
            NetworkMetric.ShowValue("↓" + number, unit);
        }
        else NetworkMetric.ValueLabel.Text = Strings.MissingReading;
        NetworkMetric.DetailLabel.Text = resource?.SendBytesPerSecond is { } tx
            ? "↑" + RateText.Format(tx * 8) : Strings.CollectingDisconnected;
        // Link speed and utilization live in the tooltip; the tiles show only the value and rates.
        var link = resource?.LinkBitsPerSecond is { } speed ? Strings.Format(Strings.Link, RateText.Format(speed)) : "";
        var utilization = resource?.NetworkPercent is not null ? Strings.Format(Strings.OfLink, Percent(resource.NetworkPercent)) : Strings.LinkSpeedUnavailable;
        NetworkMetric.ToolTip = resource?.NetworkName is { Length: > 0 } name
            ? Strings.Format(Strings.ReceiveSendTheValueIsTheBusierDirectionUtilizationIs, name, link, utilization)
            : Strings.NoConnectedEthernetOrWiFiInterfaceAvailable;
        OverallGraph.VisibleMetrics = _preferences.SeriesMask;
    }

    private void ConfigureDiskLayout()
    {
        var dual = _preferences.SecondaryDrive is not null;
        SecondaryDiskColumn.Width = dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SecondaryDiskMetric.Visibility = SecondaryDiskDivider.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = 836;
        if (Width < MinWidth) Width = MinWidth;
    }

    private static void UpdateDiskMetric(MetricReadout metric, string name, double? active, double? bytes)
    {
        metric.Label = Strings.Format(Strings.DiskLabel, name);
        metric.ValueLabel.Text = Percent(active);
        metric.DetailLabel.Text = bytes is { } rate ? Strings.Format(Strings.MBS, rate / 1_000_000) : Strings.Unavailable;
        metric.ToolTip = Strings.Format(Strings.VolumeActiveTimeNotSpaceUsedReadWriteThroughputIn, name);
    }
    private static string Percent(double? value) => value is { } number ? Strings.Format(Strings.PercentOneDecimal, number) : Strings.MissingReading;
    private static string Rate(ResourceSample resources) => CpuGraph.NetworkBitsPerSecond(resources) is { } bits ? RateText.Format(bits) : Strings.MissingReading;

    private void OnPin(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        UpdatePinButton();
    }

    private void UpdatePinButton()
    {
        // Keep the active toggle distinct from the surrounding title bar in every theme.
        PinButton.SetResourceReference(ForegroundProperty, Topmost ? "SelectedText" : "Text");
        if (Topmost)
        {
            PinButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "SelectedSurface");
            PinButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Accent");
        }
        else
        {
            PinButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            PinButton.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        }
        PinButton.ToolTip = Topmost ? Strings.KeptOnTopOfOtherWindowsClickToStop : Strings.KeepThisWindowOnTopOfOtherWindows;
        System.Windows.Automation.AutomationProperties.SetName(PinButton, Topmost ? Strings.KeepOnTopOn : Strings.KeepOnTopOff);
    }

    /// <summary>Align title-bar actions with the native caption buttons at each DPI.</summary>
    private void AlignTitleBar()
    {
        if (WindowTheme.CaptionButtonBounds(this) is not { } buttons || ActualWidth <= 0) return;
        var height = Math.Max(36, buttons.Bottom);
        TitleRow.Height = new GridLength(height);
        TitleBarArea.Margin = new Thickness(0, 0, Math.Max(0, ActualWidth - buttons.Left), 0);
        PinButton.Height = buttons.Height;
        PinButton.Margin = new Thickness(0, buttons.Top, 0, 0);
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is not { } chrome ||
            !(Math.Abs(chrome.CaptionHeight - height) > 0.5)) return;
        chrome.CaptionHeight = height;
        chrome.GlassFrameThickness = new Thickness(0, height, 0, 0);
    }

    /// <summary>Rebuild the range chips; choices longer than the retention setting are left out.</summary>
    private void BuildRangeChips()
    {
        RangeSelector.Children.Clear();
        foreach (var seconds in _preferences.ChartRanges)
        {
            var chip = new System.Windows.Controls.RadioButton
            {
                Content = Strings.Format(Strings.M, seconds / 60),
                Tag = seconds,
                GroupName = "ChartRange",
                IsChecked = seconds == _preferences.EffectiveChartRange,
                Style = (Style)FindResource("RangeChip"),
                ToolTip = Strings.Format(Strings.ShowTheLastMinutes, seconds / 60)
            };
            System.Windows.Automation.AutomationProperties.SetName(chip, Strings.Format(Strings.ShowTheLastMinutes, seconds / 60));
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
    private ProcessUsage[] CaptureEndTargets(ProcessUsage process) => ProcessTerminator.CaptureTargets(process,
        !_closing && _selectedSample is null, _preferences.GroupProcesses, _latestFull?.Processes ?? []);

    private bool CanEndProcess(ProcessUsage process) => CaptureEndTargets(process).Length > 0;

    private void EndProcess(ProcessUsage process)
    {
        var targets = CaptureEndTargets(process);
        if (targets.Length == 0) return;
        var grouped = _preferences.GroupProcesses;
        var message = grouped
            ? Strings.Format(Strings.EndGroupConfirmation, targets.Length, process.Name) + "\n\n" +
              string.Join("\n", targets.Select(p => $"{p.Name} (PID {p.ProcessId})"))
            : Strings.Format(Strings.EndPIDUnsavedWorkMayBeLostOnlyThisProcess, process.Name, process.ProcessId);
        if (new ProcessActionDialog(message, true) { Owner = this }.ShowDialog() != true ||
            _closing || _selectedSample is not null || grouped != _preferences.GroupProcesses) return;
        var errors = ProcessTerminator.EndAll(targets);
        if (errors.Count > 0)
            new ProcessActionDialog(string.Join("\n\n", errors), false) { Owner = this }.ShowDialog();
    }
    private void ReturnToLive() { _selectedSample = null; RefreshPresentation(); }

    private void CreateTray()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinMon.ico"))
            ?? throw new InvalidOperationException(Strings.TheEmbeddedTrayIconIsUnavailable);
        using (var stream = resource.Stream)
        using (var sourceIcon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize))
            _trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        _tray = new Forms.NotifyIcon { Text = Strings.WinMonCPUMonitor, Icon = _trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.ShowWinMon, null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add(Strings.Exit, null, (_, _) => Dispatcher.Invoke(Close));
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
        if (OwnedWindows.Count > 0) OwnedWindows[0]?.Activate();
    }

    /// <summary>Release any legacy Explorer hotkey and register the show shortcut directly.</summary>
    private void ApplyShortcut()
    {
        if (_hotkey is null) return;
        _hotkeyRetry?.Stop();
        var gesture = _preferences.ShowGesture;
        try
        {
            StartMenuShortcut.ClearHotkey();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidOperationException)
        {
            Trace.TraceError("WinMon legacy Start menu hotkey could not be cleared: {0}", ex);
        }
        // A cleanup failure must not prevent direct registration or conflict reporting.
        RegisterShortcut(gesture);
        UpdateTrayText();
    }

    private void RegisterShortcut(HotkeyGesture? gesture)
    {
        var hotkey = _hotkey!;
        _hotkeyStatus = hotkey.Register(gesture);
        if (_hotkeyStatus != HotkeyStatus.InUse) return;
        // Explorer may briefly retain a cleared legacy hotkey, so keep trying during the handoff.
        var attempts = 0;
        _hotkeyRetry = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (!_closing) _hotkeyStatus = hotkey.Register(gesture);
            if (_closing || _hotkeyStatus != HotkeyStatus.InUse || ++attempts >= 10) _hotkeyRetry?.Stop();
            UpdateTrayText();
        }, Dispatcher);
    }

    private bool IsShortcutAvailable(HotkeyGesture gesture) =>
        gesture == _hotkey?.Current || GlobalHotkey.IsAvailable(gesture);

    private string ShortcutStatus => _hotkeyStatus switch
    {
        HotkeyStatus.Registered => Strings.WorksFromAnyAppWhileWinMonIsRunning,
        HotkeyStatus.InUse => Strings.AnotherAppIsUsingThisShortcutChooseAnother,
        HotkeyStatus.Failed => Strings.ThisShortcutCouldNotBeRegisteredChooseAnother,
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
            var dialog = new SettingsWindow(_preferences with { AlwaysOnTop = Topmost, StartWithWindows = StartupRegistration.IsEnabled },
                IsShortcutAvailable, ShortcutStatus, async () => { await CheckForUpdatesAsync(true, _lifetime.Token); return UpdateStatusText.Text; }) { Owner = this };
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
            if (!_closing) WinMon.Controls.MessageDialog.ShowNotice(this, Strings.SettingsCouldNotBeSavedCheckAccessToYourLocal, Strings.WinMonSettings);
        }
        finally
        {
            if (!_closing)
            {
                SettingsButton.IsEnabled = true;
                ProcessCountSelector.IsEnabled = true;
                ProcessSortSelector.IsEnabled = true;
                if (_hotkey is { Current: null }) ApplyShortcut();
            }
        }
    }

    private async Task ApplyPreferencesAsync(MonitorPreferences result)
    {
        var wasEnabled = !_diagnosticMode && StartupRegistration.IsEnabled;
        var startupChanged = !_diagnosticMode && wasEnabled != result.StartWithWindows;
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
        ApplyTheme();
        ApplyLanguage();
        ApplyShortcut();
        RefreshPresentation();
    }

    private void ApplyLanguage()
    {
        AppLanguages.Apply(_preferences.Language);
        // Labels change, but ranking, history, selection, and scroll position stay intact.
        var selectedSort = _preferences.ProcessSort;
        ProcessSortSelector.SelectionChanged -= OnProcessSortChanged;
        try
        {
            ProcessSortSelector.ItemsSource = new[] { Strings.CPU, Strings.GPU, Strings.Memory, Strings.IO, Strings.Threads, Strings.Handles };
            ProcessSortSelector.SelectedIndex = (int)selectedSort;
        }
        finally { ProcessSortSelector.SelectionChanged += OnProcessSortChanged; }
        UpdateSortHeaderToolTips();
        UpdatePinButton();
        BuildRangeChips();
        foreach (var metric in Metrics) metric.RefreshLanguage();
        UpdateCompactButton();
        if (_tray?.ContextMenuStrip is { } menu)
        {
            menu.Items[0].Text = Strings.ShowWinMon;
            menu.Items[1].Text = Strings.Exit;
        }
        OverallGraph.RefreshLanguage();
        RefreshUpdateText();
        RefreshPresentation();
    }

    private void UpdateSortHeaderToolTips()
    {
        foreach (var (header, sort, text) in SortHeaders)
        {
            var description = sort switch
            {
                ProcessSort.Gpu => Strings.BusiestGPUEnginePerProcessAveragedOverReadableIntervalsIn,
                ProcessSort.Memory => Strings.ResidentWorkingSetIncludingSharedPages,
                ProcessSort.Io => Strings.ProcessReadWriteIOIncludesFileNetworkAndDevice,
                ProcessSort.Threads => Strings.ProcessThreadsExplanation,
                ProcessSort.Handles => Strings.ProcessHandlesExplanation,
                _ => Strings.AverageOverTheLast5SecondsWindowsCountsProcessCPU
            };
            header.ToolTip = Strings.Format(Strings.ClickToRankProcessesBy, description, text.Replace(" %", "").Replace(" MB/s", ""));
        }
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
            app.Resources["PlotSurface"] = SystemColors.WindowBrush;
            app.Resources["SearchSurface"] = SystemColors.WindowBrush;
            app.Resources["SelectedSurface"] = SystemColors.HighlightBrush;
            app.Resources["SelectedText"] = SystemColors.HighlightTextBrush;
            app.Resources["SnapshotAccent"] = SystemColors.HighlightBrush;
            app.Resources["ProcessPinAccent"] = SystemColors.WindowTextBrush;
            app.Resources["SnapshotAccentText"] = SystemColors.HighlightTextBrush;
            app.Resources["Text"] = SystemColors.WindowTextBrush;
            app.Resources["Muted"] = SystemColors.WindowTextBrush;
            app.Resources["Line"] = SystemColors.WindowTextBrush;
            app.Resources["Accent"] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent" }) app.Resources[key] = SystemColors.WindowTextBrush;
        }
        else
        {
            SetBrush("Surface", light ? "#F0F5FA" : "#15191E");
            SetBrush("Raised", light ? "#FFFFFF" : "#20262D");
            SetBrush("TitleBarSurface", light ? "#DCE7F1" : "#30343B");
            SetBrush("SectionHeaderSurface", light ? "#E5EEF7" : "#252A31");
            SetBrush("PlotSurface", light ? "#FFFFFF" : "#15191E");
            SetBrush("SearchSurface", light ? "#FFFFFF" : "#15191E");
            SetBrush("SelectedSurface", light ? "#1469B5" : "#20262D");
            SetBrush("SelectedText", light ? "#FFFFFF" : "#EDF2F6");
            SetBrush("SnapshotAccentText", "#07171B");
            SetBrush("Text", light ? "#20334A" : "#EDF2F6");
            SetBrush("Muted", light ? "#465D78" : "#B3BDC8");
            SetBrush("Line", light ? "#CCD9E5" : "#35404B");
            var colors = ChartPalettes.Colors(_preferences.ChartPalette, light);
            var keys = new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent" };
            for (var i = 0; i < keys.Length; i++) SetBrush(keys[i], colors[i]);
            SetBrush("Accent", light ? "#1469B5" : colors[0]);
            SetBrush("SnapshotAccent", light ? "#1469B5" : colors[0]);
            SetBrush("ProcessPinAccent", light ? "#1469B5" : colors[0]);
        }
        foreach (Window window in app.Windows) WindowTheme.Apply(window);
        foreach (var row in _rows) row.InvalidateVisual();
        _otherRow.InvalidateVisual();
        OverallGraph.InvalidateVisual();
    }

    internal static bool IsSystemLight()
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
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_compactMode) SetCompactView(false);
            if (ServicesTab.IsChecked == true)
            {
                ServiceView.FocusSearch();
                e.Handled = true;
                return;
            }
            ProcessSearchBox.Focus();
            ProcessSearchBox.SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && ServicesTab.IsChecked == true && ServiceView.ClearSearch())
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && ProcessSearchBox.IsKeyboardFocusWithin && ProcessSearchBox.Text.Length > 0)
        {
            ProcessSearchBox.Clear();
            e.Handled = true;
            return;
        }
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
        if (_closing || _preparingUpdateExit) return;
        // A restart may already have stopped its service. Let the bounded control operation finish
        // before cancelling the monitor lifetime or exiting its helper owner.
        if (_serviceActionBusy) { _closeAfterServiceAction = true; return; }
        if (_updateRestartPending)
        {
            _preparingUpdateExit = true;
            IsEnabled = false;
            try
            {
                await _settingsWriteTask;
                var savedBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                var savedSize = SavedFullViewSize(savedBounds);
                await _preferenceStore.SaveAsync(_preferences with { Width = savedSize.Width, Height = savedSize.Height, Left = savedBounds.Left, Top = savedBounds.Top, AlwaysOnTop = Topmost }, CancellationToken.None);
            }
            catch (Exception saveError)
            {
                Trace.TraceError("WinMon update cancelled because preferences could not be saved: {0}", saveError);
                AbortPendingUpdate();
                UpdateLauncher.Dispose();
                IsEnabled = true;
                ShowUpdateStatus(Strings.UpdateFailed, dismissible: true);
                _preparingUpdateExit = false;
                return;
            }
            _preparingUpdateExit = false;
        }
        _closing = true;
        _snapshotCopiedTimer.Stop();
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
            var size = SavedFullViewSize(bounds);
            if (!_diagnosticMode && !_updateRestartPending) await _preferenceStore.SaveAsync(_preferences with { Width = size.Width, Height = size.Height, Left = bounds.Left, Top = bounds.Top, AlwaysOnTop = Topmost }, CancellationToken.None);
        }
        catch (Exception ex) { AbortPendingUpdate(); Trace.TraceError("WinMon shutdown or preference save failed: {0}", ex); }
        finally
        {
            await ShutdownUpdatesAsync();
            _hotkeyRetry?.Stop();
            _hotkey?.Dispose();
            _tray?.ContextMenuStrip?.Dispose();
            _tray?.Dispose();
            _trayIcon?.Dispose();
            if (_updateRestartPending) UpdateLauncher.Commit();
            _lifetime.Dispose();
            _allowClose = true;
            Close();
        }
    }
}
