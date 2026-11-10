using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.Cli;

public sealed class CliSettingsStore(string? path = null)
{
    private const long MaximumFileBytes = 1024 * 1024;
    public string Path { get; } = System.IO.Path.GetFullPath(path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", "cli-settings.json"));
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true,
        MaxDepth = 16, Converters = { new JsonStringEnumConverter() }
    };

    public CliPreferences Load(Action? warn = null)
    {
        try { return Read(); }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            Trace.TraceWarning("CLI settings load failed: {0}", exception);
            warn?.Invoke();
            return new();
        }
    }

    private CliPreferences Read()
    {
        try
        {
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException("CLI settings are too large.");
            var value = JsonSerializer.Deserialize<CliPreferences>(stream, JsonOptions) ?? throw new JsonException("Missing settings.");
            value.Normalize();
            return value;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
    }

    public void Update(Action<CliPreferences> update, bool reset = false)
    {
        var mutexName = "Local\\WinMon.Cli.Settings." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        var acquired = false;
        string? temporary = null;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("CLI settings are busy.");
            CliPreferences preferences;
            try { preferences = reset ? new() : Read(); }
            catch (JsonException)
            {
                File.Copy(Path, Path + ".invalid-" + Guid.NewGuid().ToString("N"));
                preferences = new();
            }
            update(preferences);
            preferences.Normalize();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences, JsonOptions);
                if (stream.Length > MaximumFileBytes) throw new InvalidDataException("CLI settings are too large.");
                stream.Flush(true);
            }
            File.Move(temporary, Path, true);
        }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            if (acquired) mutex.ReleaseMutex();
        }
    }

    internal static bool IsSettingsError(Exception exception) => exception is IOException or UnauthorizedAccessException
        or JsonException or System.Security.SecurityException or WaitHandleCannotBeOpenedException;
}

public sealed class CliSettingsSession(CliSettingsStore store, InteractiveState state, Action warn)
{
    private CliPreferences _observed = state.CapturePreferences();
    private CliPreferences? _before, _after;
    private long _changedAt;
    public bool Failed { get; private set; }

    public void Observe()
    {
        var next = state.CapturePreferences();
        if (CliPreferences.Serialize(next) == CliPreferences.Serialize(_observed)) return;
        _before ??= _observed;
        _after = _observed = next;
        _changedAt = Stopwatch.GetTimestamp();
    }

    public void Flush(bool force = false)
    {
        if (Failed || _before is not { } before || _after is not { } after ||
            !force && Stopwatch.GetElapsedTime(_changedAt) < TimeSpan.FromMilliseconds(500)) return;
        try
        {
            store.Update(current => CliPreferences.Merge(before, after, current));
            _before = _after = null;
        }
        catch (Exception exception) when (CliSettingsStore.IsSettingsError(exception))
        {
            Trace.TraceWarning("CLI settings save failed: {0}", exception);
            Failed = true;
            warn();
        }
    }
}
