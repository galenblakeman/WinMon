using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    /// <summary>
    /// Inject illustrative CPU and disk runs into a fixture window with automatic captures on, then check the
    /// markers, report files, Capture-mode selection, the card banner and the captures menu, and render them.
    /// </summary>
    internal static async Task CaptureAutoCaptureDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var reports = Path.Combine(output, "reports");
        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        fixture.Preferences = fixture.Preferences with
        {
            AutoCapture = new AutoCapturePreferences
            {
                Enabled = true, Folder = reports, CooldownMinutes = 1,
                Rules =
                [
                    .. CaptureRuleSetting.Defaults.Select(rule => rule.Metric switch
                    {
                        CaptureMetric.Cpu => rule with { Seconds = 5 },
                        CaptureMetric.PrimaryDisk => rule with { Enabled = true, Seconds = 5 },
                        _ => rule
                    })
                ]
            }
        };
        try
        {
            var anchor = DateTimeOffset.Now.AddSeconds(-120);
            for (var i = 0; i < 120; i++)
            {
                var cpuSpike = i is >= 30 and < 42;
                var diskSpike = i is >= 70 and < 82;
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

            var markers = fixture.CaptureMarkers;
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

            fixture.Show();
            fixture.RefreshVisiblePresentation();
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

            var menu = fixture.CreateCapturesMenu();
            if (menu.Items.Count != 6)
                throw new InvalidOperationException(
                    "The captures menu needs header, 2 captures, separator and 2 links.");

            fixture.Preferences = fixture.Preferences with { Theme = "Dark" };
            fixture.ApplyTheme();
            var settings = new SettingsWindow(fixture.Preferences, captureFolder: reports,
                checkForUpdates: () => Task.FromResult(Strings.UpToDate)) { Owner = fixture };
            try
            {
                settings.Show();
                foreach (var section in Enum.GetValues<SettingsSection>())
                {
                    settings.ShowSection(section);
                    if (settings.Section != section) continue;
                    settings.UpdateLayout();
                    await CaptureServiceWindowAsync(settings,
                        Path.Combine(output, $"settings-{section.ToString().ToLowerInvariant()}.png"),
                        cancellationToken);
                }
            }
            finally
            {
                settings.Close();
            }

            await File.WriteAllTextAsync(Path.Combine(output, "captures.json"), JsonSerializer.Serialize(new
            {
                Markers = markers.Select(m => new { m.Rule, m.ReportPath, Peak = m.Peak.Timestamp }),
                Reports = files.Select(Path.GetFileName)
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        finally
        {
            fixture.CloseForFixture();
        }
    }
}