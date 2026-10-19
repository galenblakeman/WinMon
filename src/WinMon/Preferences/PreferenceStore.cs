using System.Text.Json;

namespace WinMon.Preferences;

/// <summary>
/// Loads and saves monitor preferences in the current user's local application-data folder.
/// </summary>
public sealed class PreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string settingsPath;

    /// <summary>Initializes a new instance of the <see cref="PreferenceStore"/> class.</summary>
    public PreferenceStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", "settings.json"))
    {
    }

    internal PreferenceStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = settingsPath;
    }

    /// <summary>Loads valid persisted preferences, returning defaults when the file is missing or invalid.</summary>
    /// <param name="cancellationToken">Cancels the asynchronous read.</param>
    /// <returns>Validated monitor preferences.</returns>
    public async Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using FileStream stream = new(settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            MonitorPreferences? preferences = await JsonSerializer.DeserializeAsync<MonitorPreferences>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return Validate(preferences ?? new MonitorPreferences());
        }
        catch (FileNotFoundException)
        {
            return new MonitorPreferences();
        }
        catch (DirectoryNotFoundException)
        {
            return new MonitorPreferences();
        }
        catch (JsonException)
        {
            return new MonitorPreferences();
        }
        catch (IOException)
        {
            return new MonitorPreferences();
        }
        catch (UnauthorizedAccessException)
        {
            return new MonitorPreferences();
        }
    }

    /// <summary>Atomically saves validated monitor preferences.</summary>
    /// <param name="preferences">Preferences to save.</param>
    /// <param name="cancellationToken">Cancels the asynchronous write.</param>
    /// <returns>A task that completes after the file is replaced.</returns>
    public async Task SaveAsync(MonitorPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        cancellationToken.ThrowIfCancellationRequested();
        string? directory = Path.GetDirectoryName(settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The settings path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Validate(preferences), SerializerOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static MonitorPreferences Validate(MonitorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences with
        {
            Width = ClampFinite(preferences.Width, 600, 600, 2000),
            Height = ClampFinite(preferences.Height, 840, 800, 1500),
            Left = ValidateCoordinate(preferences.Left),
            Top = ValidateCoordinate(preferences.Top),
            Theme = NormalizeTheme(preferences.Theme),
            CompactHistorySeconds = NormalizeCompactHistory(preferences.CompactHistorySeconds),
            RetentionSeconds = Math.Clamp(preferences.RetentionSeconds, 600, 3600),
            VisibleMetrics = preferences.VisibleMetrics & 15,
            TopProcessCount = preferences.TopProcessCount is 5 or 10 or 15 or 20 ? preferences.TopProcessCount : 5,
        };
    }

    private static double ClampFinite(double value, double defaultValue, double minimum, double maximum) => double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : defaultValue;

    private static double? ValidateCoordinate(double? value) => value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;

    private static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "light" => "Light",
        "system" => "System",
        _ => "Dark",
    };

    private static int NormalizeCompactHistory(int seconds) => seconds switch
    {
        <= 90 => 60,
        <= 210 => 120,
        _ => 300,
    };
}
