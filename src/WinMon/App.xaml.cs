using System.Diagnostics;
using System.Windows;

namespace WinMon;

/// <summary>Run a single local monitoring session or an isolated diagnostic smoke run.</summary>
public partial class App
{
    private Mutex? _instance;
    private bool _ownsInstance;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--smoke-output" or "--feature-smoke-output", _])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var window = new MainWindow(true);
            MainWindow = window;
            window.Show();
            _ = RunDiagnosticsAsync(window, Path.GetFullPath(e.Args[1]), e.Args[0] == "--feature-smoke-output");
            return;
        }
        if (e.Args is ["--profile-output", _, ..])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var window = new MainWindow(true);
            MainWindow = window;
            window.Show();
            var seconds = e.Args.Length > 2 && int.TryParse(e.Args[2], out var requested) ? Math.Clamp(requested, 10, 3600) : 60;
            _ = RunProfileAsync(window, Path.GetFullPath(e.Args[1]), seconds);
            return;
        }
        _instance = new Mutex(true, @"Local\WinMon.CpuMonitor", out _ownsInstance);
        if (!_ownsInstance)
        {
            MessageBox.Show("WinMon is already running. Restore it from the notification area.", "WinMon", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task RunDiagnosticsAsync(MainWindow window, string output, bool featuresOnly)
    {
        var exitCode = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (featuresOnly) await window.CaptureFeatureDiagnosticsAsync(output, timeout.Token);
            else await window.CaptureDiagnosticAsync(output, timeout.Token);
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Trace.TraceError("WinMon diagnostic validation failed: {0}", ex);
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString(), CancellationToken.None);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
            Shutdown(exitCode);
        }
    }

    private async Task RunProfileAsync(MainWindow window, string output, int seconds)
    {
        var exitCode = 0;
        try { await window.CaptureProfileAsync(output, seconds, CancellationToken.None); }
        catch (Exception ex)
        {
            exitCode = 1;
            Trace.TraceError("WinMon profile failed: {0}", ex);
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString(), CancellationToken.None);
        }
        finally
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            window.Close();
            await closed.Task;
            Shutdown(exitCode);
        }
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}


