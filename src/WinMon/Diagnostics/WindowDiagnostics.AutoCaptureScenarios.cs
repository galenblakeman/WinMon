// Exact stored layout values require exact comparison.
// ReSharper disable CompareOfFloatsByEqualityOperator

using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static void SeedAutoCaptureScenario(MainWindow fixture)
    {
        fixture.RefreshVisiblePresentation();
        if (!Equals(fixture.CapturesButton.Content, Strings.AutoCaptureShort) ||
            AutomationProperties.GetName(fixture.CapturesButton) != Strings.AutoCaptureNone)
            throw new InvalidOperationException(
                "The empty captures button needs a short label and full description.");
        var anchor = DateTimeOffset.Now.AddSeconds(-120);
        for (var i = 0; i < 120; i++)
        {
            var cpuSpike = i is >= 30 and < 42;
            // Leave the disk incident's follow-up open so the shutdown path must finish it.
            var diskSpike = i >= 111;
            ProcessUsage[] processes =
            [
                new(12000, 1, "chrome", cpuSpike ? 60 + i % 3 : 6, 900_000_000, 400_000)
                {
                    AppName = "Google Chrome", ImagePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe"
                },
                new(15288, 2, "node", cpuSpike ? 30 : 2, 300_000_000, 100_000) { ImagePath = @"C:\Tools\node.exe" },
                new(4120, 3, "MsMpEng", 2, 280_000_000, diskSpike ? 40_000_000 : 1_000_000)
                    { AppName = "Antimalware Service Executable" }
            ];
            var full = new MonitorSample(anchor.AddSeconds(i), processes.Sum(p => p.CpuPercent) + 4, processes, 0,
                MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
            {
                IntervalSeconds = 1, IntervalCpuTicks = Environment.ProcessorCount * TimeSpan.TicksPerSecond,
                Resources = new ResourceSample
                {
                    UsedMemoryBytes = 21_000_000_000, TotalMemoryBytes = 32UL * 1073741824, DiskName = "C:",
                    DiskActivePercent = diskSpike ? 97 + i % 3 : 6, DiskBytesPerSecond = diskSpike ? 180e6 : 9e6,
                    GpuPercent = 11, GpuEngine = "3D", GpuId = "illustrative"
                }
            };
            fixture.Session.Record(ProcessRetention.Compact(full), full);
            fixture.ObserveForCaptures(full);
        }
    }

    private static async Task<IReadOnlyList<string>> VerifyAutoCaptureReportsAsync(IReadOnlyList<MainWindow.CaptureMarker> markers, string reports, CancellationToken cancellationToken)
    {
        if (markers.Count != 2 || markers[0].Fired.Rule.Metric != CaptureMetric.Cpu ||
            markers[1].Fired.Rule.Metric != CaptureMetric.PrimaryDisk)
            throw new InvalidOperationException("Expected one CPU and one disk capture, in order.");
        for (var wait = 0; wait < 100 && markers.Any(m => m.ReportPath is null && !m.WriteFailed); wait++)
            await Task.Delay(50, cancellationToken);
        var files = CaptureReports.Find(reports);
        if (files.Count != 2 || markers.Any(m => m.ReportPath is null))
            throw new InvalidOperationException("Both reports must be written and found by the pruning rules.");
        var diskReport = await File.ReadAllTextAsync(markers[1].ReportPath!, cancellationToken);
        if (!diskReport.StartsWith(CaptureReports.FirstLine(markers[1].ReportId), StringComparison.Ordinal) ||
            !diskReport.Contains(Strings.AutoCaptureIoDisclaimer, StringComparison.Ordinal))
            throw new InvalidOperationException("The disk report needs its first line and attribution disclaimer.");

        return files;
    }

    private static async Task CaptureAutoCaptureToolbarAsync(MainWindow fixture, string output, CancellationToken cancellationToken)
    {
        fixture.Show();
        fixture.RefreshVisiblePresentation();
        if (!Equals(fixture.CapturesButton.Content, Strings.Format(Strings.AutoCaptureShortCount, 2)) ||
            AutomationProperties.GetName(fixture.CapturesButton) != Strings.Format(Strings.AutoCaptureCount, 2))
            throw new InvalidOperationException("The captures button needs its count and full description.");
        var captureRight = fixture.ChartModeSwitch.TranslatePoint(
            new Point(fixture.ChartModeSwitch.ActualWidth, 0), fixture).X;
        var historyLeft = fixture.FlightRecorderButton.TranslatePoint(new Point(), fixture).X;
        var historyRight = fixture.FlightRecorderButton.TranslatePoint(
            new Point(fixture.FlightRecorderButton.ActualWidth, 0), fixture).X;
        var autoLeft = fixture.CapturesButton.TranslatePoint(new Point(), fixture).X;
        if (Math.Abs(historyLeft - captureRight - 8) > 1.5 || Math.Abs(autoLeft - historyRight - 8) > 1.5 ||
            fixture.ChartTypeSelector.ActualWidth != 150 ||
            !ReferenceEquals(ButtonIcon.GetData(fixture.FlightRecorderButton), ButtonIcon.History))
            throw new InvalidOperationException(
                "History must follow Capture with its clock icon, then Auto, while the chart type selector stays narrow.");
        fixture.Width = 836;
        fixture.UpdateLayout();
        var autoRight = fixture.CapturesButton.TranslatePoint(
            new Point(fixture.CapturesButton.ActualWidth, 0), fixture).X;
        var rangeLeft = fixture.RangeSelector.TranslatePoint(new Point(), fixture).X;
        if (autoRight + 8 > rangeLeft)
            throw new InvalidOperationException("Auto overlaps the time range controls at minimum width.");
        VerifyAutoCaptureScenarioToolbar(fixture);
        if (fixture.ResourceHeader.ActualHeight > 44)
            throw new InvalidOperationException("The English chart toolbar must stay on one row at minimum width.");
        await CaptureVisualAsync((FrameworkElement)fixture.Content,
            Path.Combine(output, "captures-toolbar-narrow.png"), 1, cancellationToken);
        var previousCulture = Strings.Culture;
        try
        {
            Strings.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("de"));
            fixture.RefreshVisiblePresentation();
            fixture.UpdateLayout();
            VerifyAutoCaptureScenarioToolbar(fixture);
            await CaptureVisualAsync((FrameworkElement)fixture.Content,
                Path.Combine(output, "captures-toolbar-german-narrow.png"), 1, cancellationToken);
        }
        finally
        {
            Strings.SetCulture(previousCulture);
            fixture.RefreshVisiblePresentation();
        }
    }

    private static async Task CaptureAutoCaptureMarkersAsync(MainWindow fixture, IReadOnlyList<MainWindow.CaptureMarker> markers, string output,
        CancellationToken cancellationToken)
    {
        fixture.Width = 1200;
        fixture.UpdateLayout();
        var graph = fixture.OverallGraph;
        var plot = graph.PlotBounds;
        var end = CpuGraph.Moment(fixture.Session.History.Samples[^1]);
        var x = plot.Right - (end - markers[0].FiredKey.Moment) / MonitorPreferences.ShortRangeSeconds * plot.Width;
        if (graph.MarkerAt(new Point(x, 2))?.Tag != markers[0])
            throw new InvalidOperationException("The CPU marker must be hit-testable at the chart's top edge.");
        if (!graph.Markers.Select(m => m.BrushKey).SequenceEqual(["CpuAccent", "DiskAccent"]))
            throw new InvalidOperationException("Markers must use their resource colors.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "captures-live.png"), cancellationToken);

        fixture.OpenCapture(markers[0]);
        if (fixture.SelectedSample is not { } selected || !markers[0].Peak.Matches(selected) ||
            fixture.CaptureBanner.Visibility != Visibility.Visible || !fixture.OpenReportButton.IsEnabled)
            throw new InvalidOperationException(
                "Opening a capture must select its exact peak and show the banner with Open report.");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"capture-card-{theme.ToLowerInvariant()}.png"), cancellationToken);
        }
    }

    private static async Task<Dictionary<string, double>> CaptureAutoCaptureSettingsAsync(MainWindow fixture, string reports, string output, CancellationToken cancellationToken)
    {
        var menu = fixture.CreateCapturesMenu();
        if (menu.Items.Count != 6)
            throw new InvalidOperationException(
                "The captures menu needs header, 2 captures, separator and 2 links.");

        var overflow = new Dictionary<string, double>();
        SettingsSection? openedSection = null;
        _ = fixture.Dispatcher.BeginInvoke(() =>
        {
            var dialog = fixture.OwnedWindows.OfType<SettingsWindow>().Single();
            openedSection = dialog.Section;
            dialog.Close();
        });
        ((System.Windows.Controls.MenuItem)menu.Items[menu.Items.Count - 1]).RaiseEvent(
            new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
        if (openedSection != SettingsSection.AutomaticCaptures)
            throw new InvalidOperationException("The captures menu must open Automatic captures settings.");

        foreach (var theme in new[] { "Dark", "Light" })
            await CaptureAutoCaptureSettingsThemeAsync(fixture, reports, output, theme, overflow, cancellationToken);
        return overflow;
    }

    private static async Task CaptureAutoCaptureSettingsThemeAsync(MainWindow fixture, string reports, string output, string theme,
        Dictionary<string, double> overflow, CancellationToken cancellationToken)
    {
        fixture.Preferences = fixture.Preferences with { Theme = theme };
        fixture.ApplyTheme();
        var networkChange = new TaskCompletionSource<NetworkMonitoringChangeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? networkSave = null;
        var scoped = fixture.Preferences.AutoCapture with
        {
            Rules = [.. fixture.Preferences.AutoCapture.Rules,
                new(CaptureMetric.ApplicationCpu, true, 70, 10) { Target = "renderer.exe" },
                new(CaptureMetric.ApplicationCpu, true, 95, 30) { Target = "worker.exe" }]
        };
        var settings = new SettingsWindow(fixture.Preferences with { AutoCapture = scoped.Normalized() }, captureFolder: reports,
            checkForUpdates: () => Task.FromResult(Strings.UpToDate),
            changeNetworkMonitoring: (_, _) => networkChange.Task)
        {
            Owner = fixture,
            GetGpuSample = () => new ResourceSample { GpuName = "Example GPU" }
        };
        try
        {
            settings.Show();
            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                settings.ShowSection(section);
                if (settings.Section != section) continue;
                settings.UpdateLayout();
                overflow[$"{theme} {section}"] = Math.Round(settings.PageOverflow, 1);
                // These sections must show every setting without scrolling at full height.
                if (section is SettingsSection.Appearance or SettingsSection.Monitoring
                        or SettingsSection.Devices && settings.PageOverflow > 0.5 &&
                    !settings.HeightLimitedByWorkArea)
                    throw new InvalidOperationException(
                        $"Settings {section} scrolls by {settings.PageOverflow:F0} DIPs in {theme}.");
                var name = theme == "Dark"
                    ? $"settings-{section.ToString().ToLowerInvariant()}.png"
                    : $"settings-light-{section.ToString().ToLowerInvariant()}.png";
                await CaptureServiceWindowAsync(settings, Path.Combine(output, name), cancellationToken);
                if (section == SettingsSection.AutomaticCaptures)
                {
                    settings.ScrollSettingsPageToBottom();
                    settings.UpdateLayout();
                    await CaptureServiceWindowAsync(settings, Path.Combine(output, "settings-scoped-rules-" + theme + ".png"), cancellationToken);
                    settings.ScrollSettingsPageToTop();
                }
                if (section == SettingsSection.Services)
                {
                    settings.RemoveNetworkMonitoringServiceButton.BringIntoView();
                    settings.UpdateLayout();
                    var networkMonitoringName = theme == "Dark"
                        ? "settings-network-monitoring.png"
                        : "settings-light-network-monitoring.png";
                    await CaptureServiceWindowAsync(settings, Path.Combine(output, networkMonitoringName),
                        cancellationToken);
                    if (NetworkSetupBroker.Supported)
                    {
                        await CaptureAutoCaptureNetworkUpdateAsync(settings, theme, output, cancellationToken);
                        settings.RefreshNetworkMonitoringState(NetworkMonitoringState.Disabled);
                        settings.ProcessNetworkMonitoringCheckBox.IsChecked = true;
                        networkSave = settings.SaveAsync();
                        await CaptureAutoCaptureNetworkBusyAsync(settings, networkChange, networkSave, theme, output, cancellationToken);
                    }

                    settings.ScrollSettingsPageToTop();
                }
            }
        }
        finally
        {
            networkChange.TrySetResult(
                new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled));
            if (networkSave is not null) await networkSave;
            settings.Close();
        }
    }

    private static async Task CaptureAutoCaptureNetworkUpdateAsync(SettingsWindow settings, string theme, string output, CancellationToken cancellationToken)
    {
        settings.RefreshNetworkMonitoringState(NetworkMonitoringState.UpdateRequired);
        if (settings.ProcessNetworkMonitoringStatus.Text !=
            Strings.NetworkProcessMonitoringUpdateRequired)
            throw new InvalidOperationException(
                "Incompatible service needs the localized update-required state.");
        await CaptureServiceWindowAsync(settings, Path.Combine(output,
                theme == "Dark"
                    ? "settings-network-update-required.png"
                    : "settings-light-network-update-required.png"),
            cancellationToken);
    }

    private static async Task CaptureAutoCaptureNetworkBusyAsync(SettingsWindow settings, TaskCompletionSource<NetworkMonitoringChangeResult> networkChange,
        Task networkSave, string theme, string output, CancellationToken cancellationToken)
    {
        await Dispatcher.Yield(
            DispatcherPriority.Background);
        if (settings.Navigation.IsEnabled || settings.SaveButton.IsEnabled)
            throw new InvalidOperationException(
                "Setup must disable navigation and repeated saves.");
        await CaptureServiceWindowAsync(settings, Path.Combine(output,
                theme == "Dark"
                    ? "settings-network-busy.png"
                    : "settings-light-network-busy.png"),
            cancellationToken);
        networkChange.TrySetResult(
            new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled));
        await networkSave;
    }

    private static void VerifyAutoCaptureScenarioToolbar(MainWindow fixture)
    {
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(fixture.ResourceHeader)
            .TransformBounds(new Rect(new Point(), element.RenderSize));
        var actions = Bounds(fixture.ChartInvestigationActions);
        if (actions.Left < 0 || actions.Right > fixture.ResourceHeader.ActualWidth ||
            actions.IntersectsWith(Bounds(fixture.ChartTypeSelector)) ||
            actions.IntersectsWith(Bounds(fixture.RangeSelector)))
            throw new InvalidOperationException("Chart investigation actions overlap or leave the toolbar at minimum width.");
    }
}
