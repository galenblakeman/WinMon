using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

// ReSharper disable StringLiteralTypo

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureServiceWindowAsync(Window window, string path, CancellationToken cancellationToken)
    {
        try
        {
            await CaptureExampleWindowAsync(window, path, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("must be foreground", StringComparison.Ordinal))
        {
            // Content-only QA remains useful for the controls when Windows denies foreground activation.
            // Keep it clearly separate from native-composed screenshots; never substitute it in the gallery.
            var contentPath = Path.Combine(Path.GetDirectoryName(path)!,
                Path.GetFileNameWithoutExtension(path) + "-content.png");
            await CaptureVisualAsync(window, contentPath, 1, cancellationToken);
            await File.AppendAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!, "content-capture-notes.txt"),
                Path.GetFileName(contentPath) + ": WPF content capture; native caption not verified.\n",
                cancellationToken);
        }
    }

    internal async Task CaptureServicesDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        var live = window.Session.Services.Frames.LastOrDefault();
        if (live is null || !live.Available || !live.Groups.Any(g => g.Host is not null))
            throw new InvalidOperationException(
                "Live SCM enumeration and process mapping did not produce service readings.");
        var catalog = await Task.Run(WindowsServices.Capture, cancellationToken);
        using var sampler = new WindowsCpuSampler();
        sampler.Sample();
        await Task.Delay(1100, cancellationToken);
        var current = sampler.Sample().Processes.Single(p => p.ProcessId == Environment.ProcessId);
        if (current.ParentProcessId <= 0) throw new InvalidOperationException("Native parent PID was not captured.");
        await File.WriteAllTextAsync(Path.Combine(output, "live-services.json"), JsonSerializer.Serialize(new
        {
            Services = catalog.Services.Count,
            MappedHosts = live.Groups.Count(g => g.Host is not null),
            SharedHosts = live.Groups.Count(g => g.Shared),
            WorkerProcesses = live.Groups.Sum(g => Math.Max(0, g.Processes.Count - 1)),
            current.ProcessId, current.ParentProcessId, current.CreationTicks,
            ServiceActionsExecuted = false
        }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        window.ServicesTab.IsChecked = true;
        var liveTotal = (uint)live.Groups.Sum(group => group.Services.Count);
        var liveRunning = (uint)live.Groups.Sum(group => group.Services.Count(service => service.State == 4));
        if (window.ServiceTotalValue.Text != MainWindow.CompactCount(liveTotal) ||
            window.ServiceRunningValue.Text != MainWindow.CompactCount(liveRunning) ||
            window.ServiceCounts.Visibility != Visibility.Visible ||
            window.SystemCounts.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Services tab did not show total and running service counts.");
        window.Width = 1200;
        window.Height = 950;
        await CaptureServiceWindowAsync(window, Path.Combine(output, "services-live.png"), cancellationToken);

        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Left = SystemParameters.WorkArea.Left + 20;
        fixture.Top = SystemParameters.WorkArea.Top + 20;
        try
        {
            SeedServicesScenario(fixture);
            var agentRow = PrepareServicesScenarioPins(fixture);
            await CaptureServicesScenarioThemesAsync(fixture, output, cancellationToken);
            await CaptureServicesScenarioFiltersAsync(fixture, agentRow, output, cancellationToken);
            AppLanguages.Apply("de");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-header-german.png"),
                cancellationToken);
            VerifyServicesScenarioToolbar(fixture.ServiceView);
            AppLanguages.Apply("en");
            await CaptureServicesScenarioHistoryAsync(fixture, output, cancellationToken);
            await CaptureServicesScenarioDialogsAsync(fixture, output, cancellationToken);
            CompleteServicesScenario(fixture);
        }
        finally
        {
            fixture.Closing -= fixture.OnClosing;
            fixture.Close();
        }

        return;
    }

}
