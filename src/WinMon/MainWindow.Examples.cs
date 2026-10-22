using System.Diagnostics;
using System.Windows;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    /// <summary>Render README examples with illustrative data and isolated, English preferences.</summary>
    internal static async Task CaptureExamplesAsync(string output, CancellationToken cancellationToken)
    {
        AppLanguages.Apply("en");
        var fixture = new MainWindow(true) { Width = 1000, Height = 840 };
        fixture._preferences = new MonitorPreferences { Theme = "Dark", PrimaryDrive = "C:", SecondaryDrive = "D:" };
        var chrome = Process(12000, "chrome", "Google Chrome", 8.7, 1260, 92, 1420);
        var code = Process(12003, "Code", "Visual Studio Code", 4.7, 840, 64, 980);
        ProcessUsage[] processes =
        [
            chrome, code,
            Process(12001, "chrome", "Google Chrome", 1.2, 360, 28, 420) with { HasAppWindow = false },
            Process(12006, "MsMpEng", "Antimalware Service Executable", 3.3, 286, 48, 820) with { HasAppWindow = false },
            Process(4, "System", "System", 2.3, 84, 240, 3200) with { HasAppWindow = false },
            Process(12004, "Taskmgr", "Task Manager", 2, 195, 22, 560),
            Process(12010, "OneDrive", "Microsoft OneDrive", 0.8, 175, 36, 740)
        ];
        var anchor = new DateTimeOffset(2026, 10, 22, 10, 24, 0, TimeSpan.FromHours(-5));
        for (var i = 0; i < 120; i++)
        {
            var pulse = Math.Exp(-Math.Pow((i - 92) / 3d, 2));
            fixture._history.Add(new CpuSample(anchor.AddSeconds(i - 119), 32.8 + 40 * pulse + 3 * Math.Sin(i),
                processes, 0, MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    UsedMemoryBytes = 23_056_165_683, TotalMemoryBytes = 32UL * 1_073_741_824,
                    DiskName = "C:", DiskActivePercent = 6.4 + 60 * pulse, DiskBytesPerSecond = 84_000_000,
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = 3.5 + 18 * pulse, SecondaryDiskBytesPerSecond = 28_000_000,
                    GpuId = "illustrative-adapter", GpuEngine = "3D", GpuPercent = 10.5 + 40 * pulse,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 955_000 + 10_000_000 * pulse, SendBytesPerSecond = 150_000
                }
            });
        }
        await Draw("individual");
        fixture.GroupedChip.IsChecked = true;
        await Draw("grouped");
        fixture.AppsOnlyCheckBox.IsChecked = true;
        fixture.ProcessSearchBox.Text = "chrome";
        await Draw("apps_only");
        fixture.AppsOnlyCheckBox.IsChecked = false;
        fixture.ProcessSearchBox.Clear();
        fixture.IndividualChip.IsChecked = true;
        fixture.ToggleProcessPin(code, false);
        fixture.ToggleProcessPin(chrome, false);
        await Draw("pinned", 1000, 940);
        fixture.PinnedOnlyCheckBox.IsChecked = true;
        await Draw("pinned_only");
        fixture.PinnedOnlyCheckBox.IsChecked = false;
        fixture._selectedSample = fixture._history.Samples[92];
        await Draw("snapshot", 1000, 940);
        fixture._selectedSample = null;
        fixture.ToggleProcessPin(code, false);
        fixture.ToggleProcessPin(chrome, false);
        fixture.GroupedChip.IsChecked = true;
        fixture._preferences = fixture._preferences with { Theme = "Light" };
        await Draw("light");
        fixture._preferences = fixture._preferences with { Theme = "Dark" };
        foreach (var palette in new[] { "Neon", "Arcade", "Tropical" })
        {
            fixture._preferences = fixture._preferences with { ChartPalette = palette };
            await Draw(palette.ToLowerInvariant());
        }
        fixture._preferences = fixture._preferences with { ChartPalette = "Neon" };
        fixture.ApplyTheme();
        var settings = new SettingsWindow(fixture._preferences, checkForUpdates: () => Task.FromResult(Strings.UpToDate));
        // Measure the complete real Settings content, including controls below the normal scroll viewport.
        var settingsVisual = (FrameworkElement)settings.Content;
        settingsVisual.Measure(new Size(480, double.PositiveInfinity));
        settingsVisual.Arrange(new Rect(new Point(), settingsVisual.DesiredSize));
        await CaptureVisualAsync(settingsVisual, Path.Combine(output, "example_settings.png"), 1, cancellationToken);
        fixture._preferences = fixture._preferences with { ChartPalette = "Classic" };
        fixture.SetCompactView(true);
        await Draw("compact", 1000, 156);
        fixture._preferences = fixture._preferences with { Theme = "Light" };
        await Draw("compact_light", 1000, 156);
        return;

        async Task Draw(string name, double width = 1000, double height = 840)
        {
            fixture._wide = width >= WideWidth;
            fixture._inlineProcessToolbar = width >= InlineProcessToolbarWidth;
            fixture._inlinePinToolbar = width >= 1000;
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            var visual = (FrameworkElement)fixture.Content;
            visual.Measure(new Size(width, height));
            visual.Arrange(new Rect(0, 0, width, height));
            await CaptureVisualAsync(visual, Path.Combine(output, $"example_{name}.png"), 1, cancellationToken);
        }

        static ProcessUsage Process(int pid, string name, string app, double cpu, ulong memoryMiB, long threads, long handles) =>
            new(pid, pid, name, cpu, memoryMiB * 1_048_576, cpu * 400_000)
            {
                AppName = app, HasAppWindow = true, PrivateBytes = memoryMiB * 800_000,
                ThreadCount = threads, HandleCount = handles, GpuPercent = cpu / 2,
                ImagePath = $@"C:\Apps\{name}.exe", Publisher = "Example publisher"
            };
    }
}
