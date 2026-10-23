using System.Diagnostics;
using System.Globalization;
using System.Windows;
using WinMon.Preferences;
using WinMon.Shortcuts;
using WinMon.Updates;
// ReSharper disable ConvertIfStatementToSwitchStatement

namespace WinMon;

/// <summary>Run a single local monitoring session or an isolated diagnostic smoke run.</summary>
public partial class App
{
    private Mutex? _instance;
    private bool _ownsInstance;
    private IDisposable? _activation;

    /// <inheritdoc/>
    // WPF requires a void startup override; awaiting preferences preserves the UI synchronization context.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--service-action", var action, var service, var pidText, var createdText])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var result = 87;
            if (action is "start" or "stop" or "restart" && int.TryParse(pidText, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) &&
                long.TryParse(createdText, NumberStyles.None, CultureInfo.InvariantCulture, out var created))
            {
                try
                {
                    await Task.Run(() => Monitoring.WindowsServices.ExecuteAsync(service, action, pid, created, CancellationToken.None));
                    result = 0;
                }
                catch (System.ComponentModel.Win32Exception ex) { result = ex.NativeErrorCode; }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { result = 1237; }
            }
            Shutdown(result);
            return;
        }
        if (e.Args is ["--apply-update", var directory, var token])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = await Task.Run(() => UpdateRuntime.RunHelper(directory, token));
            Shutdown(code);
            return;
        }
        // On Windows, the initial UI culture is the user's display language, independently of regional formats.
        // Keep newly created threads on that language too; ResourceManager supplies English when unavailable.
        AppLanguages.InitializeSystemDisplayCulture(CultureInfo.CurrentUICulture);
        AppLanguages.Apply(AppLanguages.SystemDefault);

        if (e.Args is ["--services-smoke-output", var servicesOutput])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var window = new MainWindow(true);
            MainWindow = window;
            window.Show();
            _ = RunServicesDiagnosticsAsync(window, Path.GetFullPath(servicesOutput));
            return;
        }
        if (e.Args is ["--smoke-output" or "--feature-smoke-output" or "--snapshot-smoke-output" or "--search-smoke-output" or "--compact-smoke-output" or "--examples-output", _])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            var window = new MainWindow(true, preferenceStore: new PreferenceStore(
                Path.Combine(Path.GetFullPath(e.Args[1]), "diagnostic-settings.json")));
            MainWindow = window;
            window.Show();
            _ = RunDiagnosticsAsync(window, Path.GetFullPath(e.Args[1]), e.Args[0] == "--feature-smoke-output", e.Args[0] == "--snapshot-smoke-output", e.Args[0] == "--search-smoke-output", e.Args[0] == "--compact-smoke-output", e.Args[0] == "--examples-output");
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
        // Read once before any localized windows are created.
        var preferenceStore = new PreferenceStore();
        var preferences = await preferenceStore.LoadAsync();
        AppLanguages.Apply(preferences.Language);
        UpdateRuntime.AcceptResultArguments(e.Args);
        if (UpdateRuntime.IsUpdating() && !UpdateRuntime.IsExpectedRestart)
        {
            Shutdown();
            return;
        }
        _instance = new Mutex(true, @"Local\WinMon.CpuMonitor", out _ownsInstance);
        if (!_ownsInstance)
        {
            if (!InstanceActivation.SignalRunningInstance())
                WinMon.Controls.MessageDialog.ShowNotice(null, Strings.WinMonIsAlreadyRunningRestoreItFromTheNotificationArea, Strings.WinMon);
            Shutdown();
            return;
        }
        _activation = InstanceActivation.Listen(() => Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowRequested()));
        base.OnStartup(e);
        MainWindow = new MainWindow(false, preferences, preferenceStore);
        MainWindow.Show();
    }

    private async Task RunDiagnosticsAsync(MainWindow window, string output, bool featuresOnly, bool snapshotsOnly = false, bool searchOnly = false, bool compactOnly = false, bool examplesOnly = false)
    {
        var exitCode = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (examplesOnly)
            {
                Directory.CreateDirectory(output);
                await WinMon.MainWindow.CaptureExamplesAsync(output, timeout.Token);
            }
            else if (compactOnly)
            {
                Directory.CreateDirectory(output);
                await Task.Delay(TimeSpan.FromSeconds(4), timeout.Token);
                await window.CaptureCompactDiagnosticsAsync(output, timeout.Token);
            }
            else if (searchOnly)
            {
                Directory.CreateDirectory(output);
                await Task.Delay(TimeSpan.FromSeconds(8), timeout.Token);
                await window.CaptureSearchDiagnosticsAsync(output, timeout.Token);
            }
            else if (snapshotsOnly)
            {
                Directory.CreateDirectory(output);
                await WinMon.MainWindow.CaptureDesignFixtureAsync(output, timeout.Token);
                await WinMon.MainWindow.CapturePinsFixtureAsync(output, timeout.Token);
            }
            else if (featuresOnly) await window.CaptureFeatureDiagnosticsAsync(output, timeout.Token);
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
        _activation?.Dispose();
        UpdateLauncher.Dispose();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
