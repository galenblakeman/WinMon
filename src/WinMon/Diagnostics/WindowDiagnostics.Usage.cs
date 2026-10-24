using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Monitoring;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    /// <summary>
    /// Render the Usage tab from an illustrative two-minute ledger in both themes, and check ranking, the copied
    /// report and Show in Processes for a running and an exited app.
    /// </summary>
    internal static async Task CaptureUsageDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        try
        {
            var anchor = DateTimeOffset.Now.AddSeconds(-120);
            fixture.Session.Usage.Reset(anchor);
            for (var i = 0; i < 120; i++)
            {
                var factor = 0.6 + 0.4 * Math.Abs(Math.Sin(i / 9d));
                List<ProcessUsage> processes =
                [
                    App(12000, 1, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Google Chrome",
                        6 * factor, 400_000, 4, true),
                    App(12001, 2, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "Google Chrome",
                        2 * factor, 50_000, 1, false),
                    App(12003, 3, "Code", @"C:\Users\Example\AppData\Local\Programs\Microsoft VS Code\Code.exe",
                        "Visual Studio Code", 3 * factor, 120_000, null, true),
                    App(4120, 4, "MsMpEng", @"C:\ProgramData\Microsoft\Windows Defender\Platform\MsMpEng.exe",
                        "Antimalware Service Executable", 2.5, 4_000_000, null, false),
                    App(1880, 5, "dwm", @"C:\Windows\System32\dwm.exe", "Desktop Window Manager", 0.8, 0, 9, false),
                    App(4, 6, "System", "", "", 1.2, 900_000, null, false)
                ];
                // An installer that ran for the first minute, so Show in Processes opens it in Capture.
                if (i < 60)
                    processes.Add(App(15288, 7, "setup", @"C:\Users\Example\Downloads\setup.exe", "Example Setup",
                        11, 9_000_000, null, true));
                // Machine CPU slightly above the attributed total, as short-lived processes would leave it.
                var full = new MonitorSample(anchor.AddSeconds(i), processes.Sum(p => p.CpuPercent) + 2.5, processes, 0,
                    MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
                {
                    IntervalSeconds = 1,
                    IntervalCpuTicks = Environment.ProcessorCount * TimeSpan.TicksPerSecond,
                    Resources = new ResourceSample
                    {
                        UsedMemoryBytes = 21_000_000_000, TotalMemoryBytes = 32UL * 1073741824, DiskName = "C:",
                        DiskActivePercent = 8 + factor * 4, DiskBytesPerSecond = 54_000_000, GpuPercent = 11,
                        GpuEngine = "3D", GpuId = "illustrative"
                    }
                };
                fixture.Session.Record(ProcessRetention.Compact(full), full);
            }

            fixture.Show();
            fixture.UsageTab.IsChecked = true;
            var view = fixture.UsageView;
            var checks = new Dictionary<string, object>();
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture.Preferences = fixture.Preferences with { Theme = theme };
                fixture.ApplyTheme();
                fixture.RefreshVisiblePresentation();
                await CaptureServiceWindowAsync(fixture,
                    Path.Combine(output, $"usage-{theme.ToLowerInvariant()}.png"), cancellationToken);
            }

            var rows = view.Rows.Items.Cast<UsageDisplayRow>().ToArray();
            if (rows.Length != 6 || rows[0].App?.AppName != "Google Chrome")
                throw new InvalidOperationException("Usage must list every app, Google Chrome first by CPU time.");
            checks["Rows"] = rows.Select(r => new { r.Name, r.CpuTime, r.Share, r.Processes, r.LastActive }).ToArray();

            view.IoHeader.RaiseEvent(new MouseButtonEventArgs(
                Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent
            });
            if (view.Rows.Items.Cast<UsageDisplayRow>().First().App?.AppName != "Example Setup")
                throw new InvalidOperationException("Ranking by I/O must put the installer first.");
            if (fixture.Preferences.UsageSortColumn != (int)UsageSort.Io)
                throw new InvalidOperationException("The Usage ranking must be stored in preferences.");
            checks["Report"] = UsageText.Report(new UsageFrame(fixture.Session.Usage.Apps,
                fixture.Session.Usage.MachineBusyTicks, fixture.Session.Usage.NotAttributedCpuTicks,
                fixture.Session.Usage.Since, fixture.Session.LatestFull?.Timestamp, false), view.Settings, "");

            var setup = fixture.Session.Usage.Apps.Single(a => a.Name == "setup");
            fixture.ShowUsageInProcesses(setup);
            if (fixture.ProcessesTab.IsChecked != true || fixture.SelectedSample is not { } moment ||
                setup.LastActive?.Matches(moment) != true || fixture.Processes.GroupedChip.IsChecked != true)
                throw new InvalidOperationException(
                    "Show in Processes must open Capture at the exited app's last active sample, grouped.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-show-exited.png"), cancellationToken);

            var chrome = fixture.Session.Usage.Apps.Single(a => a.AppName == "Google Chrome");
            fixture.ShowUsageInProcesses(chrome);
            if (fixture.SelectedSample is not null)
                throw new InvalidOperationException("Show in Processes must stay live for a running app.");
            await File.WriteAllTextAsync(Path.Combine(output, "usage.json"),
                JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }
        finally
        {
            fixture.CloseForFixture();
        }

        return;

        static ProcessUsage App(int pid, long created, string name, string path, string appName, double cpu,
            double io, double? gpu, bool window)
        {
            return new ProcessUsage(pid, created, name, cpu, 200_000_000, io)
            {
                ImagePath = path, AppName = appName, GpuPercent = gpu, HasAppWindow = window,
                HasHadAppWindow = window
            };
        }
    }
}