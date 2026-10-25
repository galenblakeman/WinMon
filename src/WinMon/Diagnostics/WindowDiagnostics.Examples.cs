using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using WinMon.Monitoring;
using WinMon.Preferences;
using Point = System.Drawing.Point;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    /// <summary>Render README examples with illustrative data and isolated, English preferences.</summary>
    internal static async Task CaptureExamplesAsync(string output, CancellationToken cancellationToken)
    {
        AppLanguages.Apply("en");
        var fixture = new MainWindow(true) { Width = 1000, Height = 840 };
        // Show real chrome without starting the live sampler or replacing the illustrative preferences.
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        var chrome = Process(12000, "chrome", "Google Chrome", 8.7, 1260, 92, 1420);
        try
        {
            fixture.Preferences = new MonitorPreferences
                { Theme = "Dark", PrimaryDrive = "C:", SecondaryDrive = "D:" };
            var code = Process(12003, "Code", "Visual Studio Code", 4.7, 840, 64, 980);
            ProcessUsage[] processes =
            [
                chrome, code,
                Process(12001, "chrome", "Google Chrome", 1.2, 360, 28, 420) with { HasAppWindow = false },
                Process(12006, "MsMpEng", "Antimalware Service Executable", 3.3, 286, 48, 820) with
                {
                    HasAppWindow = false
                },
                Process(4, "System", "System", 2.3, 84, 240, 3200) with { HasAppWindow = false },
                Process(12004, "Taskmgr", "Task Manager", 2, 195, 22, 560),
                Process(12010, "OneDrive", "Microsoft OneDrive", 0.8, 175, 36, 740)
            ];
            var anchor = new DateTimeOffset(2026, 10, 23, 10, 24, 0, TimeSpan.FromHours(-5));
            for (var i = 0; i < 120; i++)
            {
                var pulse = Math.Exp(-Math.Pow((i - 92) / 3d, 2));
                fixture.Session.History.Add(new MonitorSample(anchor.AddSeconds(i - 119),
                    32.8 + 40 * pulse + 3 * Math.Sin(i),
                    processes, 0, MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
                {
                    Resources = new ResourceSample
                    {
                        UsedMemoryBytes = 23_056_165_683, TotalMemoryBytes = 32UL * 1_073_741_824,
                        DiskName = "C:", DiskActivePercent = 6.4 + 60 * pulse, DiskBytesPerSecond = 84_000_000,
                        SecondaryDiskName = "D:", SecondaryDiskActivePercent = 3.5 + 18 * pulse,
                        SecondaryDiskBytesPerSecond = 28_000_000,
                        GpuId = "illustrative-adapter", GpuEngine = "3D", GpuPercent = 10.5 + 40 * pulse,
                        NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                        ReceiveBytesPerSecond = 955_000 + 10_000_000 * pulse, SendBytesPerSecond = 150_000
                    }
                });
            }

            await Draw("individual");
            fixture.Processes.GroupedChip.IsChecked = true;
            await Draw("grouped");
            fixture.Processes.AppsOnlyCheckBox.IsChecked = true;
            fixture.Processes.ProcessSearchBox.Text = "chrome";
            await Draw("apps_only");
            fixture.Processes.AppsOnlyCheckBox.IsChecked = false;
            fixture.Processes.ProcessSearchBox.Clear();
            fixture.Processes.IndividualChip.IsChecked = true;
            fixture.Processes.ToggleProcessPin(code, false);
            fixture.Processes.ToggleProcessPin(chrome, false);
            await Draw("pinned");
            fixture.Processes.PinnedOnlyCheckBox.IsChecked = true;
            await Draw("pinned_only");
            fixture.Processes.PinnedOnlyCheckBox.IsChecked = false;
            fixture.SelectedSample = fixture.Session.History.Samples[92];
            await Draw("snapshot");
            fixture.SelectedSample = null;
            fixture.Processes.ToggleProcessPin(code, false);
            fixture.Processes.ToggleProcessPin(chrome, false);
            fixture.Processes.GroupedChip.IsChecked = true;
            fixture.Preferences = fixture.Preferences with { Theme = "Light" };
            await Draw("light");
            await Draw("narrow", 836);
            fixture.Preferences = fixture.Preferences with { Theme = "Dark" };
            foreach (var palette in new[] { "Neon", "Arcade", "Tropical" })
            {
                fixture.Preferences = fixture.Preferences with { ChartPalette = palette };
                await Draw(palette.ToLowerInvariant());
            }

            fixture.Preferences = fixture.Preferences with { ChartPalette = "Neon" };
            fixture.ApplyTheme();
            var settings = new SettingsWindow(fixture.Preferences,
                checkForUpdates: () => Task.FromResult(Strings.UpToDate))
            {
                Owner = fixture
            };
            try
            {
                settings.Show();
                settings.ShowSection(SettingsSection.Appearance);
                await CaptureExampleWindowAsync(settings, Path.Combine(output, "example_settings.png"),
                    cancellationToken);
            }
            finally
            {
                settings.Close();
            }

            fixture.Preferences = fixture.Preferences with { ChartPalette = "Classic" };
            fixture.SetCompactView(true);
            await Draw("compact", 1000, 156);
            fixture.Preferences = fixture.Preferences with { Theme = "Light" };
            await Draw("compact_light", 1000, 156);
        }
        finally
        {
            // This fixture never initialized sampling, updates, tray state or preference writes.
            fixture.SnapshotCopiedTimer.Stop();
            fixture.AllowClose = true;
            fixture.Close();
            fixture.Lifetime.Dispose();
        }

        return;

        async Task Draw(string name, double width = 1000, double height = 940)
        {
            fixture.Width = width;
            fixture.Height = height;
            if (!fixture.IsVisible) fixture.Show();
            fixture.ApplyTheme();
            fixture.Processes.Selection.Select(chrome, fixture.Preferences.GroupProcesses);
            fixture.Processes.UpdateRetention();
            fixture.RefreshVisiblePresentation();
            await CaptureExampleWindowAsync(fixture, Path.Combine(output, $"example_{name}.png"), cancellationToken);
        }

        static ProcessUsage Process(int pid, string name, string app, double cpu, ulong memoryMiB, long threads,
            long handles)
        {
            return new ProcessUsage(pid, pid, name, cpu, memoryMiB * 1_048_576, cpu * 400_000)
            {
                AppName = app, HasAppWindow = true, PrivateBytes = memoryMiB * 800_000,
                ThreadCount = threads, HandleCount = handles, GpuPercent = cpu / 2,
                ImagePath = $@"C:\Apps\{name}.exe", Publisher = "Example publisher"
            };
        }
    }

    private static async Task CaptureExampleWindowAsync(Window window, string path, CancellationToken cancellationToken)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var foreground = false;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            window.Activate();
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
            // Allow Windows to compose the native caption after activation, theme and size changes.
            await Task.Delay(200, cancellationToken);
            if (GetForegroundWindow() != handle) continue;
            foreground = true;
            break;
        }

        if (!foreground || !GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException("Example window must be foreground to capture its native caption.");
        var screen = Screen.FromHandle(handle).Bounds;
        var rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        if (!screen.Contains(rectangle))
            throw new InvalidOperationException("Example window must fit on screen for a complete capture.");
        using var bitmap = new Bitmap(rectangle.Width, rectangle.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rectangle.Location, Point.Empty, rectangle.Size);
        bitmap.Save(path, ImageFormat.Png);
    }
}
