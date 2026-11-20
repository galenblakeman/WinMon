using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        await ValidateArchiveRecoveryAsync(output, cancellationToken);
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
                        CpuClockMhz = 4300,
                        NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                        ReceiveBytesPerSecond = 800_000 + 600_000 * factor, SendBytesPerSecond = 130_000 * factor,
                        UsedMemoryBytes = 21_000_000_000, TotalMemoryBytes = 32UL * 1073741824, DiskName = "C:",
                        DiskActivePercent = 8 + factor * 4, DiskBytesPerSecond = 54_000_000, GpuPercent = 11,
                        GpuEngine = "3D", GpuId = "illustrative"
                    }
                };
                fixture.Session.Record(ProcessRetention.Compact(full), full);
            }

            fixture.Show();
            var processTarget = fixture.Session.LatestFull!.Processes.First(process => process.AppName == "Google Chrome");
            fixture.Processes.ShowGroup(processTarget);
            fixture.RefreshVisiblePresentation();
            var usageLink = fixture.Processes.ProcessActions.CreateMoreMenu()!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.ShowInUsage));
            if (!usageLink.IsEnabled)
                throw new InvalidOperationException("Process navigation must work before Usage has ever been opened.");
            usageLink.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            if (fixture.UsageTab.IsChecked != true || fixture.UsageView.Rows.SelectedItem is not UsageDisplayRow
                { App.ImagePath: var targetPath } || targetPath != processTarget.ImagePath)
                throw new InvalidOperationException("Process navigation must reveal the same executable in Usage.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-from-processes.png"), cancellationToken);
            fixture.UsageTab.IsChecked = true;
            var apps = fixture.Session.Usage.Apps;
            if (fixture.UsageTotalValue.Text != MainWindow.CompactCount((uint)apps.Count) ||
                fixture.UsageActiveValue.Text != MainWindow.CompactCount((uint)apps.Count(app => app.Running > 0)) ||
                fixture.UsageCounts.Visibility != Visibility.Visible ||
                fixture.SystemCounts.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Usage tab did not show total and active app counts.");
            var view = fixture.UsageView;
            view.Rows.SelectedItem = view.Rows.Items.Cast<UsageDisplayRow>().First();
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
            fixture.SelectedSample = null;
            fixture.ShowUsageInProcesses(chrome);
            if (fixture.SelectedSample is not null)
                throw new InvalidOperationException("Show in Processes must stay live for a running app.");

            fixture.SelectedSample = fixture.Session.History.Samples[59];
            fixture.UsageTab.IsChecked = true;
            fixture.RefreshVisiblePresentation();
            var period = fixture.CurrentUsageFrame();
            if (!period.Capturing || period.Apps.Single(app => app.Name == "setup").Running != 1 ||
                period.Latest != fixture.SelectedSample.Timestamp || view.ResetUsage.IsEnabled ||
                period.Apps.Sum(app => app.CpuTicks) >= fixture.Session.Usage.Apps.Sum(app => app.CpuTicks))
                throw new InvalidOperationException("Capture Usage must show recorded period totals, not live totals.");
            checks["PeriodReport"] = UsageText.Report(period, view.Settings, "");
            string? copiedCapture = null;
            fixture.CopySnapshot(text => copiedCapture = text);
            if (copiedCapture is null || !copiedCapture.Contains(view.CaptureReport(period), StringComparison.Ordinal))
                throw new InvalidOperationException("Copy capture must append the selected period Usage view.");
            checks["CopiedCapture"] = copiedCapture;
            if (!fixture.CaptureCoverage.Text.Contains(Strings.HistorySelectedAvailable, StringComparison.Ordinal) ||
                !fixture.HistoryCoverageDetails().Contains(Strings.Services, StringComparison.Ordinal))
                throw new InvalidOperationException("Capture coverage must describe the active period and other views.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-recorded-period.png"), cancellationToken);
            var selected = fixture.SelectedSample;
            fixture.ShowUsageInProcesses(period.Apps.Single(app => app.AppName == "Google Chrome"));
            if (!ReferenceEquals(fixture.SelectedSample, selected))
                throw new InvalidOperationException("Navigating from period Usage must preserve the captured moment.");
            fixture.SelectedSample = null;
            fixture.UsageTab.IsChecked = true;
            fixture.RefreshVisiblePresentation();
            if (fixture.CurrentUsageFrame().Capturing || !view.ResetUsage.IsEnabled)
                throw new InvalidOperationException("Live Usage must restore session totals and Reset.");
            if (fixture.CaptureCoverage.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("Live view must hide Capture coverage.");
            var archive = new UsageArchive();
            foreach (var sample in fixture.Session.History.Samples) archive.Add(sample);
            archive.Add(fixture.Session.LatestFull! with { Timestamp = DateTimeOffset.Now.AddDays(-1), MonotonicTimestamp = 0 });
            fixture.DailyUsageArchive = archive;
            fixture.SelectedSample = null;
            fixture.UsageTab.IsChecked = true;
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture.Preferences = fixture.Preferences with { Theme = theme };
                fixture.ApplyTheme();
                view.ArchiveScope.SelectedIndex = 1;
                fixture.RefreshVisiblePresentation();
                var day = fixture.CurrentUsageFrame();
                if (day.Apps.Count == 0 || view.ResetUsage.IsEnabled || day.Notice is null)
                    throw new InvalidOperationException("Daily Usage must show saved observations without enabling session reset.");
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-day-" + theme + ".png"), cancellationToken);
                view.ArchiveScope.SelectedIndex = 2;
                fixture.RefreshVisiblePresentation();
                if (fixture.CurrentUsageFrame().Apps.Sum(a => a.CpuTicks) <= day.Apps.Sum(a => a.CpuTicks))
                    throw new InvalidOperationException("Weekly Usage must include the previous day's observations.");
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, "usage-week-" + theme + ".png"), cancellationToken);
            }
            view.ArchiveScope.SelectedIndex = 0;
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

    private static async Task ValidateArchiveRecoveryAsync(string output, CancellationToken cancellationToken)
    {
        foreach (var oversized in new[] { false, true })
        {
            var folder = Path.Combine(output, oversized ? "oversized-archive" : "invalid-archive");
            Directory.CreateDirectory(folder);
            var store = new Preferences.PreferenceStore(Path.Combine(folder, "settings.json"));
            var original = oversized ? new byte[UsageArchive.MaximumFileBytes + 1] :
                System.Text.Encoding.UTF8.GetBytes("{\"SchemaVersion\":2,\"TimeZoneId\":\"UTC\",\"Days\":[]}");
            await File.WriteAllBytesAsync(store.UsageArchivePath, original, cancellationToken);
            var fixture = new MainWindow(false, preferenceStore: store);
            try
            {
                await fixture.LoadUsageArchiveForFixtureAsync();
                if (fixture.DailyUsageArchive is not null ||
                    !(await File.ReadAllBytesAsync(store.UsageArchivePath, cancellationToken)).SequenceEqual(original))
                    throw new InvalidOperationException("Rejected Usage archives must remain untouched and disabled.");
                using var writerLock = new FileStream(store.UsageArchivePath + ".lock", FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
                fixture.Session.Record(new MonitorSample(DateTimeOffset.Now, 10, [], 0));
                if (fixture.Session.Latest is null)
                    throw new InvalidOperationException("Rejected Usage archives must not prevent recording.");
            }
            finally { fixture.CloseForFixture(); }
        }
        var saveStore = new Preferences.PreferenceStore(Path.Combine(output, "rejected-write", "settings.json"));
        await UsageArchive.SaveAsync(saveStore.UsageArchivePath, new UsageArchive().Export(), cancellationToken);
        var saved = await File.ReadAllBytesAsync(saveStore.UsageArchivePath, cancellationToken);
        var saveFixture = new MainWindow(false, preferenceStore: saveStore);
        try
        {
            await saveFixture.LoadUsageArchiveForFixtureAsync();
            saveFixture.DailyUsageArchive!.Add(new MonitorSample(DateTimeOffset.Now, 10,
                [new ProcessUsage(100, 100, "invalid-gpu", 10) { GpuPercent = double.PositiveInfinity }], 0)
                { IntervalSeconds = 1, IntervalCpuTicks = TimeSpan.TicksPerSecond });
            await saveFixture.SaveUsageArchiveForFixtureAsync();
            if (!(await File.ReadAllBytesAsync(saveStore.UsageArchivePath, cancellationToken)).SequenceEqual(saved))
                throw new InvalidOperationException("Rejected archive writes must preserve the saved file.");
        }
        finally { saveFixture.CloseForFixture(); }
        await File.WriteAllTextAsync(Path.Combine(output, "archive-recovery-passed.txt"),
            "Invalid and oversized archives allowed recording; rejected loads and writes preserved existing files.", cancellationToken);
    }
}
