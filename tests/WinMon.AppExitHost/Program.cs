using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace WinMon.AppExitHost;

/// <summary>Runs only application exit in a disposable process; product startup is never invoked.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--diagnostic-host-fixture", var mode, var root])
            return DiagnosticHost.Run(mode, root);
        if (args.Length != 3 || args[0] != "--app-exit-fixture" ||
            args[1] is not ("normal" or "mutate" or "reenter" or "throw") ||
            !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested)) return 64;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var events = new List<string>();
        var traces = new List<string>();
        using var listener = new ExitTrace(events, traces);
        Trace.Listeners.Add(listener);
        var expected = new IOException("owned exit handler failure");
        var app = new ExitOnlyApp(requested, events, expected) { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var eventCalls = 0;
        app.Exit += (_, e) =>
        {
            eventCalls++;
            events.Add("event.enter:" + e.ApplicationExitCode);
            if (args[1] == "mutate") e.ApplicationExitCode = requested + 5;
            if (args[1] == "reenter") app.Shutdown(requested + 5);
            if (args[1] == "throw") throw expected;
            events.Add("event.leave:" + e.ApplicationExitCode);
        };
        var result = app.Run();
        events.Add("run.return:" + result);
        Trace.Listeners.Remove(listener);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Requested = requested, Mode = args[1], RunCode = result, EventCalls = eventCalls,
            CaughtType = app.Caught?.GetType().FullName, CaughtSameException = ReferenceEquals(expected, app.Caught),
            Events = events, Traces = traces, WindowCounts = app.WindowCounts,
            ProcessId = Environment.ProcessId, AppMvid = typeof(App).Module.ModuleVersionId,
            AppPath = typeof(App).Assembly.Location
        }));
        return result;
    }

    private sealed class ExitOnlyApp(int code, List<string> events, Exception expected) : App
    {
        public Exception? Caught { get; private set; }
        public List<int> WindowCounts { get; } = [];

        protected override void OnStartup(StartupEventArgs e)
        {
            // Calling product startup here would acquire the real instance/logging/monitoring resources.
            WindowCounts.Add(Windows.Count);
            events.Add("host.startup");
            Shutdown(code);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            WindowCounts.Add(Windows.Count);
            events.Add("exit.before");
            try { base.OnExit(e); }
            catch (Exception error) when (ReferenceEquals(error, expected))
            {
                Caught = error;
                e.ApplicationExitCode = 71;
                events.Add("exit.caught");
            }
            finally
            {
                WindowCounts.Add(Windows.Count);
                events.Add("exit.after");
            }
        }
    }

    private sealed class ExitTrace(List<string> events, List<string> traces) : TraceListener
    {
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id,
            string? format, params object?[]? args)
        {
            var text = args is null ? format ?? "" : string.Format(CultureInfo.InvariantCulture, format ?? "", args);
            if (!text.StartsWith("App.Exit code=", StringComparison.Ordinal)) return;
            traces.Add(text);
            events.Add("trace:" + text);
        }
    }
}
