using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace WinMon.Diagnostics;

/// <summary>Bounded, privacy-conscious file collection for existing Trace events.</summary>
public sealed class DiagnosticLog : TraceListener
{
    private const int FileLimit = 2 * 1024 * 1024;
    private const int FileCount = 5;
    private const int EntryLimit = 16 * 1024;
    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Dictionary<string, Repetition> _repetitions = new();
    private readonly object _gate = new();
    private readonly FileStream _lease;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly string _component;
    private readonly int _fileLimit;
    private readonly Task _writer;
    private FileStream? _file;
    private long _lost;
    private int _stopped;

    /// <summary>The actual writable folder, including a fallback when necessary.</summary>
    public string Folder { get; }
    /// <summary>The folder receiving this process's app logs, or null when logging could not start.</summary>
    public static string? AppFolder { get; private set; }
    /// <inheritdoc/>
    public override bool IsThreadSafe => true;

    internal DiagnosticLog(string folder, string component, int fileLimit = FileLimit)
    {
        Folder = folder;
        _component = component;
        _fileLimit = fileLimit;
        Directory.CreateDirectory(folder);
        _lease = new FileStream(Path.Combine(folder, "writer.lock"), FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.Read);
        try { Open(); }
        catch { _lease.Dispose(); throw; }
        _writer = Task.Run(WriteAsync);
    }

    /// <summary>Collect app errors from before preferences load; prefer provisioned ProgramData.</summary>
    public static DiagnosticLog? StartApp()
    {
        try { return StartAppCore(); }
        catch (Exception ex) when (IsStorageFailure(ex)) { return null; }
    }

    private static DiagnosticLog? StartAppCore()
    {
        if (LogDirectories.IsElevated)
        {
            var elevated = Attach(new DiagnosticLog(LogDirectories.PrepareElevatedAppPath(), "App"));
            AppFolder = elevated.Folder;
            return elevated;
        }
        var log = CreateApp(LogDirectories.AppPath, LogDirectories.LocalPath, LogDirectories.ValidateAppPath);
        if (log is null) return null;
        Attach(log);
        AppFolder = log.Folder;
        if (string.Equals(log.Folder, LogDirectories.LocalPath, StringComparison.OrdinalIgnoreCase))
            Trace.TraceInformation("Logging.LocalAppDataFallback");
        return log;
    }

    internal static DiagnosticLog? CreateApp(string shared, string local, Action validateShared)
    {
        Exception sharedFailure;
        try
        {
            validateShared();
            return new DiagnosticLog(shared, "App");
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { sharedFailure = ex; }
        try
        {
            var log = new DiagnosticLog(local, "App");
            log.TraceEvent(null, "App", TraceEventType.Warning, 0, "Logging.SharedDestinationUnavailable: {0}", sharedFailure);
            return log;
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return null; }
    }

    /// <summary>Never let a privileged service write to an untrusted or user-writable folder.</summary>
    public static DiagnosticLog? StartService()
    {
        try
        {
            LogDirectories.ValidateServicePath();
            return Attach(new DiagnosticLog(LogDirectories.ServicePath, "Service"));
        }
        catch (Exception ex) when (IsStorageFailure(ex)) { return null; }
    }

    private static DiagnosticLog Attach(DiagnosticLog log)
    {
        Trace.Listeners.Add(log);
        AppDomain.CurrentDomain.UnhandledException += log.OnUnhandled;
        TaskScheduler.UnobservedTaskException += log.OnUnobserved;
        return log;
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs e) => Fatal(e.ExceptionObject as Exception);
    private void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Trace.TraceError("Task.Unobserved: {0}", e.Exception);

    /// <summary>Record a fatal failure without changing the application's exception policy.</summary>
    public void Fatal(Exception? exception)
    {
        TraceEvent(null, _component, TraceEventType.Critical, 0, "Unhandled.Exception: {0}", exception);
        Stop();
    }

    /// <inheritdoc/>
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
        int id, string? message) => Record(eventType, message ?? "Trace", "");

    /// <inheritdoc/>
    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType,
        int id, string? format, params object?[]? args)
    {
        // Templates are code-owned; string values and exception messages can contain paths, endpoints or tokens.
        var template = format ?? "Trace";
        var values = args?.Select(SafeValue).ToArray() ?? [];
        var rendered = Regex.Replace(template, @"\{(\d+)(?:[^}]*)\}", match =>
            int.TryParse(match.Groups[1].Value, out var index) && index < values.Length ? values[index] : "[omitted]");
        Record(eventType, template, rendered);
    }

    private static string SafeValue(object? value) => value switch
    {
        Exception exception => ExceptionDetails(exception),
        Enum enumeration => enumeration.ToString(),
        bool boolean => boolean.ToString(),
        Version version => version.ToString(),
        byte or short or int or long or uint or ulong or float or double or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        _ => "[omitted]"
    };

    private static string ExceptionDetails(Exception exception)
    {
        var text = new StringBuilder();
        for (var depth = 0; exception is not null && depth < 8; depth++, exception = exception.InnerException!)
        {
            text.Append(exception.GetType().Name).Append(" hresult=").Append(exception.HResult);
            if (exception is Win32Exception native) text.Append(" win32=").Append(native.NativeErrorCode);
            // Method names retain useful call sites without source paths or exception-controlled text.
            foreach (var frame in (new StackTrace(exception, false).GetFrames() ?? []).Take(24))
            {
                var method = frame.GetMethod();
                text.Append(" at ").Append(method?.DeclaringType?.FullName).Append('.').Append(method?.Name);
            }
            text.Append("; ");
        }
        return text.ToString();
    }

    private void Record(TraceEventType level, string template, string details)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (level is TraceEventType.Error or TraceEventType.Warning)
            {
                if (_repetitions.TryGetValue(template, out var repetition))
                {
                    repetition.Count++;
                    return;
                }
                if (_repetitions.Count == 128) Summarize(now, true);
                _repetitions[template] = new Repetition(now);
            }
            Enqueue(now, level, details.Length == 0 ? template : details);
        }
    }

    private void Enqueue(DateTimeOffset now, TraceEventType level, string text)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ');
        if (text.Length > 4000) text = text[..4000] + " [truncated]";
        if (!_queue.Writer.TryWrite($"{now:O} {level} {_component} session={_session} {text}\n"))
            Interlocked.Increment(ref _lost);
    }

    private void Summarize(DateTimeOffset now, bool all = false)
    {
        foreach (var (template, repetition) in _repetitions.ToArray())
        {
            if (!all && now - repetition.Since < TimeSpan.FromMinutes(1)) continue;
            if (repetition.Count > 0)
                Enqueue(now, TraceEventType.Warning, $"Repeated count={repetition.Count} event={template}");
            _repetitions.Remove(template);
        }
    }

    private async Task WriteAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            do
            {
                lock (_gate) Summarize(DateTimeOffset.UtcNow);
                Drain();
                if (Volatile.Read(ref _stopped) != 0) break;
                if (_queue.Reader.Completion.IsCompleted) break;
            } while (await timer.WaitForNextTickAsync().ConfigureAwait(false));
        }
        finally
        {
            CloseFile();
            _lease.Dispose();
        }
    }

    private void Drain()
    {
        var lost = Interlocked.Exchange(ref _lost, 0);
        try
        {
            if (lost > 0) WriteEntry($"{DateTimeOffset.UtcNow:O} Warning {_component} session={_session} Logging.Dropped count={lost}\n");
            for (var count = 0; count < 512 && _queue.Reader.TryRead(out var line); count++)
            {
                try { WriteEntry(line); }
                catch (Exception ex) when (IsStorageFailure(ex)) { Interlocked.Increment(ref _lost); throw; }
            }
            _file?.Flush();
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            Interlocked.Add(ref _lost, lost);
            CloseFile();
        }
    }

    private void WriteEntry(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line);
        if (bytes.Length > Math.Min(EntryLimit, _fileLimit)) return;
        if (_file is null) Open();
        if (_file!.Length + bytes.Length > _fileLimit)
        {
            _file.Dispose();
            _file = null;
            File.Delete(FileName(FileCount - 1));
            for (var index = FileCount - 2; index >= 0; index--)
                if (File.Exists(FileName(index))) File.Move(FileName(index), FileName(index + 1), true);
            Open();
        }
        _file!.Write(bytes);
    }

    private string FileName(int index) => Path.Combine(Folder, index == 0 ? "winmon.log" : $"winmon.{index}.log");
    private void CloseFile()
    {
        try { _file?.Dispose(); }
        catch (Exception ex) when (IsStorageFailure(ex)) { }
        finally { _file = null; }
    }
    private void Open()
    {
        // A protected service directory prevents links being planted by an unelevated user.
        foreach (var path in Directory.EnumerateFiles(Folder, "*.log"))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Reparse log file.");
        _file = new FileStream(FileName(0), FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read | FileShare.Delete);
        _file.Seek(0, SeekOrigin.End);
    }

    /// <summary>Drain and close within two seconds; callers never wait indefinitely for a disk.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        Trace.Listeners.Remove(this);
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
        lock (_gate) Summarize(DateTimeOffset.UtcNow, true);
        _queue.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing) Stop();
        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override void Write(string? message) => Record(TraceEventType.Information, "Trace.Write [omitted]", "");
    /// <inheritdoc/>
    public override void WriteLine(string? message) => Write(message);

    private static bool IsStorageFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException or NotSupportedException;

    private sealed class Repetition(DateTimeOffset since)
    {
        internal DateTimeOffset Since { get; } = since;
        internal int Count { get; set; }
    }
}
