using System.Diagnostics;
using System.Windows;

namespace WinMon;

/// <summary>Run a single local monitoring session or an isolated diagnostic smoke run.</summary>
public partial class App : Application
{
    private Mutex? instance;
    private bool ownsInstance;
    private Task? diagnostics;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-output")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var window = new MainWindow(true);
            MainWindow = window;
            window.Show();
            diagnostics = RunDiagnosticsAsync(window, Path.GetFullPath(e.Args[1]));
            return;
        }
        instance = new Mutex(true, @"Local\WinMon.CpuMonitor", out ownsInstance);
        if (!ownsInstance)
        {
            MessageBox.Show("WinMon is already running. Restore it from the notification area.", "WinMon", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private async Task RunDiagnosticsAsync(MainWindow window, string output)
    {
        var exitCode = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await window.CaptureDiagnosticAsync(output, timeout.Token); }
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

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}


