using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using MonCore.Monitoring;

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

            var checks = await CaptureUsageRankingAsync(fixture, output, cancellationToken);
            await CaptureUsageNavigationAsync(fixture, output, checks, cancellationToken);
            await CaptureUsageArchiveScopesAsync(fixture, output, cancellationToken);
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
