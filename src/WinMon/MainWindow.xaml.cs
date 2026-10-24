using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Shortcuts;
using WinMon.Updates;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;
using SystemColors = System.Windows.SystemColors;

// ReSharper disable ConvertIfStatementToSwitchStatement
namespace WinMon;

/// <summary>Present a shared CPU recording session in compact and investigation layouts.</summary>
public partial class MainWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly bool _diagnosticMode;
    private readonly PreferenceStore _preferenceStore;
    private readonly MonitorPreferences? _initialPreferences;
    private readonly List<ProcessRow> _rows = [];
    private readonly ProcessRow _otherRow = new() { Visibility = Visibility.Collapsed };
    private readonly ProcessPins _processPins = new();
    private readonly List<ProcessRow> _pinnedRows = [];

    private Func<ProcessUsage, bool>? _retainPinned;
    private MonitorPreferences _preferences = new();
    private Task? _samplingTask;
    private Task _settingsWriteTask = Task.CompletedTask;
    private bool _preferencesLoaded;
    private Forms.NotifyIcon? _tray;
    private Icon? _trayIcon;

    private MonitorSample? _selectedSample;

    // The newest sample the visible presentation last drew; diagnostics check it stays put while hidden.
    private MonitorSample? _presentedSample;

    // The error the presentation shows, copied from the session after each drain; diagnostics set it directly.
    private SamplingError? _samplingError;
    private bool _wide;
    private const double WideWidth = 800;
    internal const double InlineProcessToolbarWidth = 700;
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
    private TextBlock CpuValue => CpuMetric.ValueLabel;

    private MetricReadout[] Metrics =>
        [CpuMetric, MemoryMetric, DiskMetric, NetworkMetric, GpuMetric, SecondaryDiskMetric];

    /// <summary>Create the CPU monitor and its shared live/history presentation.</summary>
    public MainWindow() : this(false)
    {
    }

    /// <summary>Create a monitor with isolated preferences for diagnostic validation.</summary>
    /// <param name="diagnosticMode">Whether to avoid reading or saving user preferences.</param>
    /// <param name="initialPreferences">Preferences already read before choosing the startup UI language.</param>
    /// <param name="preferenceStore">The store selected at startup, preserving the portable or installed storage mode.</param>
    internal MainWindow(bool diagnosticMode, MonitorPreferences? initialPreferences = null,
        PreferenceStore? preferenceStore = null)
    {
        _diagnosticMode = diagnosticMode;
        _initialPreferences = initialPreferences;
        _preferenceStore = preferenceStore ?? new PreferenceStore();
        Session = new MonitorSession(() => new WindowsFrameSource(), CurrentSamplingOptions,
            drain => Dispatcher.BeginInvoke(drain, DispatcherPriority.Background));
        Session.Recorded += OnSamplesRecorded;
        InitializeComponent();
        InitializeCompactView();
        InitializeServices();
        InitializeProcesses();
        UpdateActivityActionsToggle();
        VersionRun.Text = $" {AppInfo.Version}";
        WindowTheme.Attach(this);
        foreach (var metric in Metrics)
        {
            metric.Toggle.Checked += OnSeriesChanged;
            metric.Toggle.Unchecked += OnSeriesChanged;
        }

        BuildRangeChips();
        // The process-type line gains a PID in a wide window; there is no separate expanded mode.
        SizeChanged += (_, _) => AlignTitleBar();
        DpiChanged += (_, _) => AlignTitleBar();
        SizeChanged += (_, _) =>
        {
            var wide = ActualWidth >= WideWidth;
            var inlineProcessToolbar = ActualWidth >= InlineProcessToolbarWidth;
            if ((wide == _wide && inlineProcessToolbar == _inlineProcessToolbar &&
                 inlineProcessToolbar == _inlinePinToolbar) || _closing) return;
            _wide = wide;
            _inlineProcessToolbar = inlineProcessToolbar;
            _inlinePinToolbar = inlineProcessToolbar;
            RefreshPresentation();
        };
        OverallGraph.SampleSelected += (_, sample) =>
        {
            _selectedSample = sample;
            RefreshPresentation();
        };
        OverallGraph.LiveRequested += (_, _) => ReturnToLive();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_closing) RefreshPresentation();
        };
        StateChanged += (_, _) => OnWindowStateChanged();
    }

    internal async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _preferences = _diagnosticMode
                ? new MonitorPreferences()
                : _initialPreferences ?? await _preferenceStore.LoadAsync(_lifetime.Token);
            _preferencesLoaded = true;
            UpdateActivityActionsToggle();
            Processes.Load(ProcessListSettings.From(_preferences), _preferences.PinnedProcesses);
            ServiceView.TopServiceCount = _preferences.TopServiceCount;
            Width = Math.Max(MinWidth, _preferences.Width);
            Height = Math.Max(MinHeight, _preferences.Height);
            var visibleMask = _preferences.SeriesMask;
            for (var i = 0; i < Metrics.Length; i++) Metrics[i].Toggle.IsChecked = (visibleMask & (1 << i)) != 0;
            if (_preferences is { Left: { } left, Top: { } top })
            {
                Left = left;
                Top = top;
            }
            else
            {
                Left = SystemParameters.WorkArea.Left + 40;
                Top = SystemParameters.WorkArea.Top + 40;
            }

            EnsureVisible();
            Topmost = _preferences.AlwaysOnTop;
            Session.History.Resize(_preferences.RetentionSeconds);
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
            _samplingTask = Session.Run(_lifetime.Token);
            InitializeUpdates();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon initialization failed: {0}", ex);
            ShowError(Strings.InitializationFailedCloseAndRestartWinMon);
        }
    }

    /// <summary>Samples, the service history and the capture loop; the window only presents them.</summary>
    internal MonitorSession Session { get; }

    // Read once per capture on the sampling thread: one immutable preferences snapshot, so drives and the
    // retain-all choice always come from the same moment.
    internal SamplingOptions CurrentSamplingOptions()
    {
        var preferences = Volatile.Read(ref _preferences);
        return new SamplingOptions(preferences.PrimaryDrive, preferences.SecondaryDrive, Volatile.Read(ref _retainPinned),
            preferences.TopProcessCount == 0);
    }

    private void OnSamplesRecorded()
    {
        _samplingError = Session.Error;
        if (!_diagnosticMode && !_updateReadySignaled && Session.Latest is not null && Session.Error is null)
        {
            _updateReadySignaled = true;
            UpdateRuntime.SignalReady();
            ShowCompletedUpdateNotice();
        }

        RefreshPresentation();
    }

    internal void RefreshPresentation()
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            UpdateTrayText();
            return;
        }

        RefreshVisiblePresentation();
    }

    internal void RefreshVisiblePresentation()
    {
        Processes.ArrangeToolbar(_inlineProcessToolbar, _inlinePinToolbar);
        var samples = Session.History.Samples;
        var latest = samples.Count == 0 ? null : samples[^1];
        var visible = _selectedSample is null
            ? latest
            : samples.FirstOrDefault(s => ReferenceEquals(s, _selectedSample));
        var range = _preferences.EffectiveChartRange;
        CpuValue.Text = _samplingError is null && latest?.TotalCpuPercent is { } total
            ? Strings.Format(Strings.PercentOneDecimal, total)
            : Strings.MissingReading;
        var allUnavailable = _samplingError == SamplingError.ReadingsUnavailable;
        UpdateResources(allUnavailable ? null : latest);
        OverallGraph.SetData(samples, range, _selectedSample);
        RefreshCompactView();
        RefreshServices();
        UpdateSnapshot(_selectedSample is null ? null : visible);
        // The compact strip hides the process list; leaving compact mode refreshes it.
        if (!_compactMode &&
            Processes.Update(new ProcessListFrame(samples, latest, visible, _selectedSample, Session.LatestFull,
                Session.Latest, Session.History.WasMemoryLimited, _samplingError, _wide,
                ProcessListSettings.From(_preferences))) is { } report)
            _snapshotReport = report;
        LiveCaption.Text = allUnavailable ? Strings.SystemResourcesUnavailable : Strings.SystemResources;
        LiveCaption.ToolTip =
            _selectedSample is not null ? Strings.SystemResourcesInspectingSnapshot : LiveCaption.Text;
        ShowError(_samplingError switch
        {
            SamplingError.CpuUnavailable => Strings.CPUUnavailableOtherResourcesContinueRecording,
            SamplingError.ReadingsUnavailable => Strings.ReadingsUnavailableRetryingEachSecond,
            _ => null
        });
        _presentedSample = latest;
        UpdateTrayText(latest);
    }


    private void ShowError(string? message)
    {
        ErrorLabel.Text = message ?? "";
        ErrorLabel.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }


    private void UpdateTrayText(MonitorSample? latest = null)
    {
        if (_tray is null) return;
        latest ??= Session.History.Latest;
        _tray.Text = Strings.Format(Strings.TrayStatus,
                         _samplingError is not null
                             ? Strings.Unavailable
                             : Percent(latest?.TotalCpuPercent) + Strings.CpuTraySuffix) +
                     (_hotkeyStatus is HotkeyStatus.InUse or HotkeyStatus.Failed ? Strings.ShortcutUnavailable : "");
    }

    private void OnSeriesChanged(object sender, RoutedEventArgs e)
    {
        var mask = 0;
        for (var i = 0; i < Metrics.Length; i++)
            if (Metrics[i].Toggle.IsChecked == true)
                mask |= 1 << i;
        _preferences = _preferences with
        {
            VisibleMetrics = mask & 15, ShowGpu = (mask & 16) != 0,
            ShowSecondaryDisk = _preferences.SecondaryDrive is null
                ? _preferences.ShowSecondaryDisk
                : (mask & 32) != 0
        };
        OverallGraph.VisibleMetrics = _preferences.SeriesMask;
        OverallGraph.InvalidateVisual();
    }


    private void UpdateResources(MonitorSample? latest)
    {
        var resource = latest?.Resources;
        GpuMetric.ValueLabel.Text = Percent(resource?.GpuPercent);
        GpuMetric.DetailLabel.Text = resource is { GpuMemoryUsedBytes: { } gpuUsed, GpuMemoryTotalBytes: > 0 }
            ? $"{gpuUsed / 1_073_741_824d:0.0} / {resource.GpuMemoryTotalBytes.Value / 1_073_741_824d:0.0} GiB"
            : "3D";
        GpuMetric.DetailLabel.ToolTip = resource?.GpuMemoryTotalBytes is { } gpuTotal
            ? Strings.Format(Strings.DedicatedVideoMemoryGiB, resource.GpuId, gpuTotal / 1_073_741_824d)
            : Strings.Unavailable;
        GpuMetric.ToolTip = resource?.GpuPercent is not null
            ? Strings.Format(Strings.AdapterUtilizationOfTheBusiestEngineAcrossAvailableGPUs, resource.GpuId,
                resource.GpuEngine)
            : Strings.GPUCountersUnavailableOrCollectingAFreshInterval;
        CpuMetric.DetailLabel.Text = resource?.CpuClockMhz is > 0
            ? $"{resource.CpuClockMhz.Value / 1000d:0.00} GHz"
            : Strings.Unavailable;
        CpuMetric.DetailLabel.ToolTip = Strings.CpuClockDescription;
        MemoryMetric.ValueLabel.Text = Percent(resource?.MemoryPercent);
        MemoryMetric.DetailLabel.Text = resource is { UsedMemoryBytes: { } used, TotalMemoryBytes: { } total }
            ? Strings.Format(Strings.GiB, used / 1_073_741_824d, total / 1_073_741_824d)
            : Strings.Unavailable;
        ConfigureDiskLayout();
        UpdateDiskMetric(DiskMetric, _preferences.PrimaryDrive,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskActivePercent : null,
            resource?.DiskName == _preferences.PrimaryDrive ? resource.DiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is { } secondary)
            UpdateDiskMetric(SecondaryDiskMetric, secondary,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskActivePercent : null,
                resource?.SecondaryDiskName == secondary ? resource.SecondaryDiskBytesPerSecond : null);
        if (_preferences.SecondaryDrive is not null)
            SecondaryDiskMetric.ToolTip += Strings.DrawnAsADashedLineOnTheChart;
        // Throughput leads: link utilization is almost always near 0% and says little on its own.
        if (resource?.ReceiveBytesPerSecond is { } receive)
        {
            var (number, unit) = RateText.Parts(receive * 8);
            NetworkMetric.ShowValue("↓" + number, unit);
        }
        else
        {
            NetworkMetric.ValueLabel.Text = Strings.MissingReading;
        }

        NetworkMetric.DetailLabel.Text = resource?.SendBytesPerSecond is { } tx
            ? "↑" + RateText.Format(tx * 8)
            : Strings.CollectingDisconnected;
        // Link speed and utilization live in the tooltip; the tiles show only the value and rates.
        var link = resource?.LinkBitsPerSecond is { } speed ? Strings.Format(Strings.Link, RateText.Format(speed)) : "";
        var utilization = resource?.NetworkPercent is not null
            ? Strings.Format(Strings.OfLink, Percent(resource.NetworkPercent))
            : Strings.LinkSpeedUnavailable;
        NetworkMetric.ToolTip = resource?.NetworkName is { Length: > 0 } name
            ? Strings.Format(Strings.ReceiveSendTheValueIsTheBusierDirectionUtilizationIs, name, link, utilization)
            : Strings.NoConnectedEthernetOrWiFiInterfaceAvailable;
        OverallGraph.VisibleMetrics = _preferences.SeriesMask;
    }

    private void ConfigureDiskLayout()
    {
        var dual = _preferences.SecondaryDrive is not null;
        SecondaryDiskColumn.Width = dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SecondaryDiskMetric.Visibility =
            SecondaryDiskDivider.Visibility = dual ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = 836;
        if (Width < MinWidth) Width = MinWidth;
    }

    private static void UpdateDiskMetric(MetricReadout metric, string name, double? active, double? bytes)
    {
        metric.Label = Strings.Format(Strings.DiskLabel, name);
        metric.ValueLabel.Text = Percent(active);
        metric.DetailLabel.Text =
            bytes is { } rate ? Strings.Format(Strings.MBS, rate / 1_000_000) : Strings.Unavailable;
        metric.ToolTip = Strings.Format(Strings.VolumeActiveTimeNotSpaceUsedReadWriteThroughputIn, name);
    }

    internal static string Percent(double? value)
    {
        return value is { } number ? Strings.Format(Strings.PercentOneDecimal, number) : Strings.MissingReading;
    }

    private static string Rate(ResourceSample resources)
    {
        return CpuGraph.NetworkBitsPerSecond(resources) is { } bits ? RateText.Format(bits) : Strings.MissingReading;
    }

    internal void OnPin(object sender, RoutedEventArgs e)
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
            PinButton.SetResourceReference(BackgroundProperty, "SelectedSurface");
            PinButton.SetResourceReference(BorderBrushProperty, "Accent");
        }
        else
        {
            PinButton.ClearValue(BackgroundProperty);
            PinButton.ClearValue(BorderBrushProperty);
        }

        PinButton.ToolTip =
            Topmost ? Strings.KeptOnTopOfOtherWindowsClickToStop : Strings.KeepThisWindowOnTopOfOtherWindows;
        AutomationProperties.SetName(PinButton, Topmost ? Strings.KeepOnTopOn : Strings.KeepOnTopOff);
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
        if (WindowChrome.GetWindowChrome(this) is not { } chrome ||
            !(Math.Abs(chrome.CaptionHeight - height) > 0.5)) return;
        chrome.CaptionHeight = height;
        chrome.GlassFrameThickness = new Thickness(0, height, 0, 0);
    }

    /// <summary>Rebuild the range chips; choices longer than the retention setting are left out.</summary>
    internal void BuildRangeChips()
    {
        RangeSelector.Children.Clear();
        foreach (var seconds in _preferences.ChartRanges)
        {
            var chip = new RadioButton
            {
                Content = Strings.Format(Strings.M, seconds / 60),
                Tag = seconds,
                GroupName = "ChartRange",
                IsChecked = seconds == _preferences.EffectiveChartRange,
                Style = (Style)FindResource("RangeChip"),
                ToolTip = Strings.Format(Strings.ShowTheLastMinutes, seconds / 60)
            };
            AutomationProperties.SetName(chip, Strings.Format(Strings.ShowTheLastMinutes, seconds / 60));
            chip.Checked += OnRangeChecked;
            RangeSelector.Children.Add(chip);
        }
    }

    private void OnRangeChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: int seconds } || _closing) return;
        _preferences = _preferences with { ChartRangeSeconds = seconds };
        RefreshPresentation();
    }

    private void OnReturnLive(object sender, RoutedEventArgs e)
    {
        ReturnToLive();
    }

    private void OnHardwareInfo(object sender, RoutedEventArgs e)
    {
        if (!_closing) new HardwareInfoWindow { Owner = this }.ShowDialog();
    }


    internal void ReturnToLive()
    {
        _selectedSample = null;
        RefreshPresentation();
    }

    private void CreateTray()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WinMon.ico"))
                       ?? throw new InvalidOperationException(Strings.TheEmbeddedTrayIconIsUnavailable);
        using (var stream = resource.Stream)
        using (var sourceIcon = new Icon(stream, Forms.SystemInformation.SmallIconSize))
        {
            _trayIcon = (Icon)sourceIcon.Clone();
        }

        _tray = new Forms.NotifyIcon { Text = Strings.WinMonCPUMonitor, Icon = _trayIcon, Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Strings.ShowWinMon, null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add(Strings.Exit, null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(RestoreFromTray);
        };
    }

    private void OnWindowStateChanged()
    {
        if (_closing) return;
        if (WindowState != WindowState.Minimized)
        {
            _restoreState = WindowState;
            if (IsVisible) RefreshPresentation();
        }
        else if (_preferences.MinimizeToTray && _tray is not null)
        {
            Hide();
        }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
                                       or InvalidOperationException)
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

    private bool IsShortcutAvailable(HotkeyGesture gesture)
    {
        return gesture == _hotkey?.Current || GlobalHotkey.IsAvailable(gesture);
    }

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
            var dialog = new SettingsWindow(
                _preferences with { AlwaysOnTop = Topmost, StartWithWindows = StartupRegistration.IsEnabled },
                IsShortcutAvailable, ShortcutStatus, async () =>
                {
                    await CheckForUpdatesAsync(true, _lifetime.Token);
                    return UpdateStatusText.Text;
                }) { Owner = this };
            // Let the shortcut recorder receive WinMon's own combination; Save or the finally block takes it back.
            _hotkeyRetry?.Stop();
            _hotkey?.Unregister();
            if (dialog.ShowDialog() != true || dialog.Result is not { } result || _closing) return;
            SettingsButton.IsEnabled = false;
            Processes.RankingEnabled = false;
            _settingsWriteTask = ApplyPreferencesAsync(result);
            await _settingsWriteTask;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon preferences could not be saved: {0}", ex);
            if (!_closing)
                MessageDialog.ShowNotice(this, Strings.SettingsCouldNotBeSavedCheckAccessToYourLocal,
                    Strings.WinMonSettings);
        }
        finally
        {
            if (!_closing)
            {
                SettingsButton.IsEnabled = true;
                Processes.RankingEnabled = true;
                if (_hotkey is { Current: null }) ApplyShortcut();
            }
        }
    }

    internal async Task ApplyPreferencesAsync(MonitorPreferences result)
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
            try
            {
                StartupRegistration.SetEnabled(wasEnabled);
            }
            catch (Exception rollbackError)
            {
                Trace.TraceError("WinMon startup registration rollback failed: {0}", rollbackError);
            }

            throw;
        }

        // Choices made in the window while the file was written, such as grouping or a new pin, stay in effect.
        _preferences = PreferenceStore.Validate(SettingsWindow.ApplySettings(_preferences, result));
        SecondaryDiskMetric.Toggle.IsChecked = _preferences.ShowSecondaryDisk;
        ConfigureDiskLayout();
        Session.History.Resize(_preferences.RetentionSeconds);
        ApplyTheme();
        ApplyLanguage();
        ApplyShortcut();
        RefreshPresentation();
    }

    internal void ApplyLanguage()
    {
        AppLanguages.Apply(_preferences.Language);
        Processes.ApplyLanguage(ProcessListSettings.From(_preferences));
        UpdatePinButton();
        BuildRangeChips();
        foreach (var metric in Metrics) metric.RefreshLanguage();
        UpdateCompactButton();
        UpdateActivityActionsToggle();
        if (_tray?.ContextMenuStrip is { } menu)
        {
            menu.Items[0].Text = Strings.ShowWinMon;
            menu.Items[1].Text = Strings.Exit;
        }

        OverallGraph.RefreshLanguage();
        RefreshUpdateText();
        RefreshPresentation();
    }


    internal void ApplyTheme()
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
            foreach (var key in new[]
                     {
                         "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent"
                     }) app.Resources[key] = SystemColors.WindowTextBrush;
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
            var keys = new[]
                { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent" };
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
            using var key =
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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
        var bounds = new Rectangle((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY),
            (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY));
        var visible = Forms.Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, bounds) is
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
        if (!_closing)
            Dispatcher.BeginInvoke(() =>
            {
                ApplyTheme();
                RefreshPresentation();
            });
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

            Processes.FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && ServicesTab.IsChecked == true && ServiceView.ClearSearch())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && Processes.ClearFocusedSearch())
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape || _selectedSample is null || Processes.HasOpenDropDown) return;
        ReturnToLive();
        e.Handled = true;
    }

    // WPF requires a void event handler; shutdown failures are caught and logged below.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    internal async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing || _preparingUpdateExit) return;
        // A restart may already have stopped its service. Let the bounded control operation finish
        // before cancelling the monitor lifetime or exiting its helper owner.
        if (_serviceActionBusy)
        {
            _closeAfterServiceAction = true;
            return;
        }

        if (_updateRestartPending)
        {
            _preparingUpdateExit = true;
            IsEnabled = false;
            try
            {
                // An earlier failed Settings save was already reported; only this exit save decides the update.
                try
                {
                    await _settingsWriteTask;
                }
                catch (Exception settingsError)
                {
                    Trace.TraceError("WinMon earlier preference save failed: {0}", settingsError);
                }

                await _preferenceStore.SaveAsync(ExitPreferences(), CancellationToken.None);
            }
            catch (Exception saveError)
            {
                Trace.TraceError("WinMon update cancelled because preferences could not be saved: {0}", saveError);
                AbortPendingUpdate();
                UpdateLauncher.Dispose();
                IsEnabled = true;
                ShowUpdateStatus(Strings.UpdateFailed, true);
                _preparingUpdateExit = false;
                return;
            }

            _preparingUpdateExit = false;
        }

        _closing = true;
        Session.Close();
        Processes.Close();
        _snapshotCopiedTimer.Stop();
        IsEnabled = false;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        try
        {
            await _lifetime.CancelAsync();
            if (_samplingTask is not null) await _samplingTask;
            try
            {
                await _settingsWriteTask;
            }
            catch (Exception settingsError)
            {
                Trace.TraceError("WinMon pending preference save failed during shutdown: {0}", settingsError);
            }

            if (!_diagnosticMode && !_updateRestartPending)
                await _preferenceStore.SaveAsync(ExitPreferences(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            AbortPendingUpdate();
            Trace.TraceError("WinMon shutdown or preference save failed: {0}", ex);
        }
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

    /// <summary>The preferences to persist on exit, including the window's bounds and keep-on-top state.</summary>
    private MonitorPreferences ExitPreferences()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var size = SavedFullViewSize(bounds);
        return _preferences with
        {
            Width = size.Width, Height = size.Height, Left = bounds.Left, Top = bounds.Top, AlwaysOnTop = Topmost
        };
    }

    /// <summary>Save preferences synchronously while Windows signs out or shuts down.</summary>
    /// <remarks>
    /// WPF does not raise Closing when the session ends, and the process may end as soon as this returns, so the
    /// asynchronous save in <see cref="OnClosing"/> never runs. The save runs on the thread pool: blocking the UI
    /// thread on a continuation that needs it would deadlock.
    /// </remarks>
    internal void SaveForSessionEnd()
    {
        // Before OnLoaded has read them, the in-memory preferences are defaults that must not replace the saved ones.
        if (_diagnosticMode || !_preferencesLoaded || _closing || _preparingUpdateExit || _updateRestartPending) return;
        var preferences = ExitPreferences();
        try
        {
            if (!Task.Run(() => _preferenceStore.SaveAsync(preferences, CancellationToken.None))
                    .Wait(TimeSpan.FromSeconds(3)))
                Trace.TraceWarning("WinMon preferences were still saving when the session ended.");
        }
        catch (AggregateException ex)
        {
            Trace.TraceError("WinMon session-end preference save failed: {0}", ex.InnerException);
        }
    }
}
