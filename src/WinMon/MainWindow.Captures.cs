using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using Forms = System.Windows.Forms;
using Path = System.Windows.Shapes.Path;

namespace WinMon;

/// <summary>Automatic captures: rules, report files, chart markers, the captures menu and notifications.</summary>
public partial class MainWindow
{
    /// <summary>One automatic capture while its firing sample is still in history; not saved across restarts.</summary>
    internal sealed class CaptureMarker(CaptureFired fired, Guid reportId, string rule)
    {
        internal CaptureFired Fired { get; } = fired;
        internal SampleKey FiredKey { get; } = SampleKey.Of(fired.Fired);
        internal SampleKey Peak { get; } = SampleKey.Of(fired.Peak);
        internal Guid ReportId { get; } = reportId;
        internal string Rule { get; } = rule;

        /// <summary>Null until the write succeeds, and when it failed.</summary>
        internal string? ReportPath { get; set; }

        internal bool WriteFailed { get; set; }
    }

    private readonly List<CaptureMarker> _captureMarkers = [];
    private CaptureRules? _captureRules;
    private AutoCapturePreferences? _captureRulesSource;
    private DateTime _lastCaptureNotification = DateTime.MinValue;
    private CaptureMarker? _notifiedMarker;
    private bool _balloonHooked;
    private bool _captureWriteErrorShown;

    /// <summary>Captures still on the chart, oldest first.</summary>
    internal IReadOnlyList<CaptureMarker> CaptureMarkers => _captureMarkers;

    private void InitializeCaptures()
    {
        Session.LiveSampleRecorded += full =>
        {
            // Diagnostics drive captures explicitly through ObserveForCaptures.
            if (!_diagnosticMode) ObserveForCaptures(full);
        };
        OverallGraph.MarkerSelected += (_, marker) => OpenCapture((CaptureMarker)marker.Tag);
    }

    /// <summary>Evaluate the rules on one live sample and record whatever fired.</summary>
    internal void ObserveForCaptures(MonitorSample full)
    {
        var settings = _preferences.AutoCapture;
        if (!settings.Enabled)
        {
            _captureRules = null;
            _captureRulesSource = null;
            return;
        }

        // A changed rule set starts afresh. Settings saves rebuild the preferences even when the rules are unchanged,
        // so compare by value: otherwise saving any setting would drop the runs and the cooldown.
        if (_captureRulesSource is not { } source || source.CooldownMinutes != settings.CooldownMinutes ||
            !source.Rules.SequenceEqual(settings.Rules))
            _captureRules = new CaptureRules(settings.Rules, TimeSpan.FromMinutes(settings.CooldownMinutes),
                WindowsCpuClock.LogicalProcessorCount);
        _captureRulesSource = settings;

        foreach (var fired in _captureRules!.Observe(full)) RecordCapture(fired, settings);
    }

    private void RecordCapture(CaptureFired fired, AutoCapturePreferences settings)
    {
        var history = Session.History.Samples;
        // Refreshes prune too, but none run while the window is hidden, and each marker holds two full samples.
        PruneCaptureMarkers(history);
        var marker = new CaptureMarker(fired, Guid.NewGuid(), CaptureReportText.Rule(fired));
        _captureMarkers.Add(marker);
        var snapshot = CaptureReportText.Snapshot(fired, history);
        var text = CaptureReportText.Report(fired, marker.ReportId, snapshot);
        var folder = settings.Folder ?? _preferenceStore.DefaultCaptureFolder;
        _ = WriteCaptureAsync(marker, folder, text);
        if (settings.Notify) NotifyCapture(marker, CaptureReportText.Notification(fired, snapshot));
    }

    private async Task WriteCaptureAsync(CaptureMarker marker, string folder, string text)
    {
        try
        {
            marker.ReportPath = await Task.Run(() =>
            {
                var path = CaptureReports.Write(folder, marker.Fired.Fired.Timestamp, marker.Fired.Rule.Metric, text);
                CaptureReports.Prune(folder);
                return path;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException)
        {
            marker.WriteFailed = true;
            Trace.TraceError("WinMon could not save an automatic capture report: {0}", ex);
            // Once per session: a full disk must not interrupt the user every few minutes.
            if (!_captureWriteErrorShown && !IsClosing)
            {
                _captureWriteErrorShown = true;
                ShowCaptureNotice(Strings.Format(Strings.AutoCaptureWriteFailed, folder));
            }
        }

        if (!IsClosing) RefreshPresentation();
    }

    private void NotifyCapture(CaptureMarker marker, string body)
    {
        if (_tray is null || DateTime.UtcNow - _lastCaptureNotification < TimeSpan.FromMinutes(1)) return;
        _lastCaptureNotification = DateTime.UtcNow;
        _notifiedMarker = marker;
        if (!_balloonHooked)
        {
            _balloonHooked = true;
            _tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() =>
            {
                RestoreFromTray();
                if (_notifiedMarker is { } clicked) OpenCapture(clicked);
            });
        }

        _tray.ShowBalloonTip(5000, marker.Rule, body, Forms.ToolTipIcon.Info);
    }

    /// <summary>Show a capture: its peak in Capture mode, else its report, else say neither remains.</summary>
    internal void OpenCapture(CaptureMarker marker)
    {
        var samples = Session.History.Samples;
        MonitorSample? peak = null;
        for (var i = samples.Count - 1; i >= 0; i--)
            if (marker.Peak.Matches(samples[i]))
            {
                peak = samples[i];
                break;
            }

        if (peak is not null)
        {
            // Widen the chart to the smallest range that shows the peak.
            var age = CpuGraph.Moment(samples[^1]) - marker.Peak.Moment;
            if (age > _preferences.EffectiveChartRange &&
                _preferences.ChartRanges.FirstOrDefault(candidate => candidate >= age) is var range and > 0)
            {
                _preferences = _preferences with { ChartRangeSeconds = range };
                BuildRangeChips();
            }

            SelectedSample = peak;
            RefreshPresentation();
            OverallGraph.Focus();
            return;
        }

        if (!OpenCaptureReport(marker))
            MessageDialog.ShowNotice(this, Strings.AutoCaptureGone, Strings.AutoCaptureHeading);
    }

    private bool OpenCaptureReport(CaptureMarker marker)
    {
        if (marker.ReportPath is not { } path || !File.Exists(path)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageDialog.ShowNotice(this, ex.Message, Strings.AutoCaptureOpenReport);
            return true;
        }
    }

    private void OnOpenCaptureReport(object sender, RoutedEventArgs e)
    {
        if (SelectedCapture() is { } marker && !OpenCaptureReport(marker))
            MessageDialog.ShowNotice(this, Strings.AutoCaptureReportMissing, Strings.AutoCaptureOpenReport);
    }

    private CaptureMarker? SelectedCapture()
    {
        return SelectedSample is { } selected ? _captureMarkers.LastOrDefault(m => m.Peak.Matches(selected)) : null;
    }

    /// <summary>A dismissible footer line, unless the footer is offering an update.</summary>
    private void ShowCaptureNotice(string text)
    {
        if (UpdateFooter.Visibility == Visibility.Visible && UpdateNowButton.Visibility == Visibility.Visible)
            MessageDialog.ShowNotice(this, text, Strings.AutoCaptureHeading);
        else ShowUpdateStatus(text, true);
    }

    /// <summary>Drop captures whose firing sample left history; returns the oldest moment still in it.</summary>
    private double PruneCaptureMarkers(IReadOnlyList<MonitorSample> samples)
    {
        var oldest = samples.Count == 0 ? double.MaxValue : CpuGraph.Moment(samples[0]);
        _captureMarkers.RemoveAll(m => m.FiredKey.Moment < oldest);
        return oldest;
    }

    /// <summary>Drop captures whose firing sample left history, then update the markers, button and card.</summary>
    private void RefreshCaptures(IReadOnlyList<MonitorSample> samples)
    {
        var enabled = _preferences.AutoCapture.Enabled;
        if (!enabled && _captureMarkers.Count == 0)
        {
            // The common case: nothing to draw or offer, so nothing to rebuild each refresh.
            if (OverallGraph.Markers.Count > 0) OverallGraph.Markers = [];
            CapturesButton.Visibility = Visibility.Collapsed;
            CaptureBanner.Visibility = Visibility.Collapsed;
            return;
        }

        var oldest = PruneCaptureMarkers(samples);
        var selected = SelectedCapture();
        var markers = new ChartMarker[_captureMarkers.Count];
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = _captureMarkers[i];
            var inHistory = samples.Count > 0 && marker.Peak.Moment >= oldest;
            var tooltip = marker.Rule + "\n" + Strings.Format(Strings.AutoCaptureMarkerTooltip,
                              marker.FiredKey.Timestamp,
                              CaptureReportText.Value(marker.Fired.Rule.Metric, marker.Fired.PeakValue),
                              marker.Peak.Timestamp) + "\n" +
                          (inHistory ? Strings.AutoCaptureMarkerHint : Strings.AutoCapturePeakGone);
            markers[i] = new ChartMarker(marker.FiredKey.Moment, CaptureReportText.BrushKey(marker.Fired.Rule.Metric),
                tooltip, ReferenceEquals(marker, selected), marker);
        }

        OverallGraph.Markers = markers;
        CapturesButton.Visibility = enabled || _captureMarkers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var capturesLabel = _captureMarkers.Count == 0
            ? Strings.AutoCaptureShort
            : Strings.Format(Strings.AutoCaptureShortCount, _captureMarkers.Count);
        var capturesDescription = _captureMarkers.Count == 0
            ? Strings.AutoCaptureNone
            : Strings.Format(Strings.AutoCaptureCount, _captureMarkers.Count);
        if (!Equals(CapturesButton.Content, capturesLabel)) CapturesButton.Content = capturesLabel;
        if (CapturesButton.ToolTip is not string buttonTooltip || buttonTooltip != capturesDescription)
            CapturesButton.ToolTip = capturesDescription;
        if (AutomationProperties.GetName(CapturesButton) != capturesDescription)
            AutomationProperties.SetName(CapturesButton, capturesDescription);
        CaptureBanner.Visibility = selected is null ? Visibility.Collapsed : Visibility.Visible;
        if (selected is null) return;
        CaptureBannerText.Text = Strings.Format(Strings.AutoCaptureBanner, selected.Rule);
        CaptureBannerText.ToolTip = CaptureBannerText.Text;
        CaptureBannerMarker.SetResourceReference(Shape.FillProperty,
            CaptureReportText.BrushKey(selected.Fired.Rule.Metric));
        var exists = selected.ReportPath is { } path && File.Exists(path);
        OpenReportButton.IsEnabled = exists;
        OpenReportButton.ToolTip = exists ? selected.ReportPath :
            selected.WriteFailed ? Strings.AutoCaptureReportFailed :
            selected.ReportPath is null ? Strings.AutoCaptureReportPending : Strings.AutoCaptureReportMissing;
    }

    private void OnCapturesMenu(object sender, RoutedEventArgs e)
    {
        var menu = CreateCapturesMenu();
        menu.PlacementTarget = CapturesButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    internal ContextMenu CreateCapturesMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "Raised");
        menu.SetResourceReference(ForegroundProperty, "Text");
        var settings = _preferences.AutoCapture;
        var watching = settings.Enabled
            ? string.Join(", ", settings.Rules.Where(r => r.Enabled).Select(r =>
                CaptureReportText.MetricName(r.Metric, Session.History.Latest?.Resources ?? new ResourceSample())))
            : Strings.AutoCaptureOff;
        menu.Items.Add(new MenuItem
        {
            Header = Strings.Format(Strings.AutoCaptureWatching, watching), IsEnabled = false,
            FontWeight = FontWeights.SemiBold
        });
        if (_captureMarkers.Count == 0)
            menu.Items.Add(new MenuItem { Header = Strings.AutoCaptureNoneYet, IsEnabled = false });
        var latest = Session.History.Latest?.Timestamp ?? DateTimeOffset.Now;
        foreach (var marker in Enumerable.Reverse(_captureMarkers))
        {
            var detail = Strings.Format(Strings.AutoCaptureMenuDetail, marker.FiredKey.Timestamp,
                CaptureReportText.Value(marker.Fired.Rule.Metric, marker.Fired.PeakValue),
                UsageText.Duration(latest - marker.FiredKey.Timestamp));
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = marker.Rule });
            var line = new TextBlock { Text = detail, FontSize = 11 };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            header.Children.Add(line);
            var triangle = new Path
            {
                Data = Geometry.Parse("M 0,0 L 10,0 L 5,8 Z"), Width = 10, Height = 8, Stretch = Stretch.Fill
            };
            triangle.SetResourceReference(Shape.FillProperty, CaptureReportText.BrushKey(marker.Fired.Rule.Metric));
            var item = new MenuItem { Header = header, Icon = triangle };
            AutomationProperties.SetName(item, $"{marker.Rule}, {detail}");
            item.Click += (_, _) => OpenCapture(marker);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var folder = settings.Folder ?? _preferenceStore.DefaultCaptureFolder;
        var open = ProcessRow.ActionItem(Strings.Format(Strings.AutoCaptureOpenFolderCount, "…"), ButtonIcon.Folder);
        _ = ShowReportCountAsync(open, folder);
        open.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                    { UseShellExecute = false, Arguments = $"\"{folder}\"" })?.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           Win32Exception or InvalidOperationException)
            {
                MessageDialog.ShowNotice(this, ex.Message, Strings.AutoCaptureFolder);
            }
        };
        menu.Items.Add(open);
        var settingsItem = ProcessRow.ActionItem(Strings.AutoCaptureSettings, ButtonIcon.Settings);
        settingsItem.Click += (_, _) => OnSettings(this, new RoutedEventArgs());
        menu.Items.Add(settingsItem);
        return menu;
    }

    /// <summary>Fill in the report count once it is known; an unreadable folder keeps the placeholder.</summary>
    /// <remarks>Counting opens every report to check its first line, so a slow or offline folder must not block the menu.</remarks>
    private static async Task ShowReportCountAsync(MenuItem item, string folder)
    {
        try
        {
            var count = await Task.Run(() => CaptureReports.Find(folder).Count);
            item.Header = Strings.Format(Strings.AutoCaptureOpenFolderCount, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException or SecurityException)
        {
            Trace.TraceWarning("WinMon could not count capture reports in {0}: {1}", folder, ex.Message);
        }
    }
}
