using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace WinMon.AppExitHost;

internal static class DiagnosticHost
{
    internal static int Run(string mode, string root)
    {
        if (mode is not ("gauges" or "charts" or "examples" or "compact" or "search" or "snapshots" or
            "features" or "full" or "all" or "services" or "recover" or "locked")) return 64;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(root).StartsWith("WinMon.DiagnosticsHost-", StringComparison.Ordinal)) return 64;
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "output");
        File.WriteAllText(output, "owned output obstruction");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var events = new List<string>();
        using var trace = new HostTrace(mode, output, events);
        Trace.Listeners.Add(trace);
        var app = new DiagnosticOnlyApp(mode, root, output, events)
            { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Match the existing owned-window fixtures; no native or rendered appearance is under test.
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            app.Resources[key] = new Style();
        app.Resources["TitleBarButtonText"] = new DataTemplate();
        app.Resources[typeof(Button)] = new Style(typeof(Button));
        var runCode = app.Run();
        Trace.Listeners.Remove(trace);
        var task = app.Operation;
        var failurePath = Path.Combine(output, "failure.txt");
        var taskError = task?.Exception?.GetBaseException();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Mode = mode, RunCode = runCode, ProcessId = Environment.ProcessId,
            AppPath = typeof(global::WinMon.App).Assembly.Location,
            AppMvid = typeof(global::WinMon.App).Module.ModuleVersionId,
            Events = events, OperationCompleted = task?.IsCompleted ?? false,
            OperationSucceeded = task?.IsCompletedSuccessfully ?? false,
            ErrorType = taskError?.GetType().FullName,
            TraceErrorType = trace.Original?.GetType().FullName,
            SameError = ReferenceEquals(taskError, trace.Original),
            WindowClosed = app.WindowClosed, RemainingWindows = app.Windows.Count,
            SamplingStarted = app.SamplingStarted, ProductStartupSkipped = true,
            Failure = File.Exists(failurePath) ? File.ReadAllText(failurePath) : null,
            OriginalError = trace.Original?.ToString(),
            Obstruction = File.Exists(output) ? File.ReadAllText(output) : null,
            LogAttached = ProductField(app, "_log") is not null,
            InstanceOwned = (bool)ProductField(app, "_ownsInstance")!,
            ActivationAttached = ProductField(app, "_activation") is not null,
            SettingsWritten = File.Exists(Path.Combine(root, "settings.json"))
        }));
        return runCode;
    }

    private static object? ProductField(object target, string name) => typeof(global::WinMon.App)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);

    private sealed class DiagnosticOnlyApp(string mode, string root, string output, List<string> events)
        : global::WinMon.App
    {
        internal Task? Operation { get; private set; }
        internal bool WindowClosed { get; private set; }
        internal bool SamplingStarted { get; private set; }
        private global::WinMon.MainWindow? _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Never call product startup: it owns real logging, activation, mutexes and sampling.
            events.Add("host.startup");
            var constructor = typeof(global::WinMon.MainWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(value => value.GetParameters().Length == 3 && value.GetParameters()[0].ParameterType == typeof(bool));
            var storeType = constructor.GetParameters()[2].ParameterType;
            var store = storeType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(string)], null)!.Invoke([Path.Combine(root, "settings.json")]);
            _window = (global::WinMon.MainWindow)constructor.Invoke([true, null, store]);
            var loaded = typeof(global::WinMon.MainWindow).GetMethod("OnLoaded",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null,
                [typeof(object), typeof(RoutedEventArgs)], null)!;
            _window.Loaded -= loaded.CreateDelegate<RoutedEventHandler>(_window);
            _window.Closed += (_, _) =>
            {
                WindowClosed = true;
                events.Add("window.closed");
            };
            var method = typeof(global::WinMon.App).GetMethod(mode == "services"
                ? "RunServicesDiagnosticsAsync" : "RunDiagnosticsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var selected = mode is "recover" or "locked" ? "features" : mode;
            object?[] arguments = mode == "services" ? [_window, output, "--services-smoke-output"] :
                [_window, output, selected is "features" or "all", selected is "snapshots" or "all",
                    selected is "search" or "all", selected is "compact" or "all", selected is "examples" or "all",
                    selected is "charts" or "all", selected is "gauges" or "all"];
            Operation = (Task)method.Invoke(this, arguments)!;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SamplingStarted = _window is not null && typeof(global::WinMon.MainWindow)
                .GetProperty("SamplingTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window) is not null;
            events.Add("app.exit:" + e.ApplicationExitCode);
            base.OnExit(e);
        }
    }

    private sealed class HostTrace(string mode, string output, List<string> events) : TraceListener
    {
        internal Exception? Original { get; private set; }
        private FileStream? _locked;
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
            int id, string? format, params object?[]? args)
        {
            if (format != "WinMon diagnostic validation failed: {0}") return;
            Original = (Exception)args![0]!;
            events.Add("diagnostic.error");
            if (mode is not ("recover" or "locked")) return;
            // Deterministically model a filesystem obstruction removed before error reporting retries.
            File.Move(output, output + ".sentinel");
            Directory.CreateDirectory(output);
            if (mode != "locked") return;
            var path = Path.Combine(output, "failure.txt");
            File.WriteAllText(path, "owned failure report sentinel");
            _locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _locked?.Dispose();
            base.Dispose(disposing);
        }
    }
}
