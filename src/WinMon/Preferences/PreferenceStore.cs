using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using WinMon.Shortcuts;

namespace WinMon.Preferences;

/// <summary>
/// Loads and saves monitor preferences in the current user's local application-data folder,
/// or beside the executable when portable.flag is present at startup.
/// </summary>
public sealed class PreferenceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientEnumConverter() }
    };

    private readonly string _settingsPath;

    // Set when a load could neither read the file nor copy it; the next save copies it first or fails.
    private bool _keepCopyBeforeReplace;

    /// <summary>Initializes a new instance of the <see cref="PreferenceStore"/> class.</summary>
    public PreferenceStore()
        : this(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal PreferenceStore(string applicationDirectory, string localApplicationDataDirectory)
        : this(File.Exists(Path.Combine(applicationDirectory, "portable.flag"))
            ? Path.Combine(applicationDirectory, "settings.json")
            : Path.Combine(localApplicationDataDirectory, "WinMon", "settings.json"))
    {
    }

    internal PreferenceStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        _settingsPath = settingsPath;
    }

    /// <summary>Where automatic capture reports go unless another folder is chosen: beside the settings file.</summary>
    internal string DefaultCaptureFolder => Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "Captures");
    internal string UsageArchivePath => Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "usage-history.json");
    internal string ResourceTrendArchivePath => Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".", "resource-trends.json.gz");

    /// <summary>Where the opt-in normal-exit history snapshot is stored.</summary>
    internal string NormalHistoryPath => Path.Combine(Path.GetDirectoryName(_settingsPath) ?? ".",
        RestartHistory.NormalFileName);

    /// <summary>Where an unreadable settings file is copied before defaults replace it on the next save.</summary>
    internal string InvalidCopyPath => Path.ChangeExtension(_settingsPath, ".invalid.json");

    /// <summary>Loads valid persisted preferences, returning defaults when the file is missing or invalid.</summary>
    /// <remarks>
    /// The next save overwrites the file, so one that exists but cannot be used is first copied to
    /// <see cref="InvalidCopyPath"/>. A brief sharing violation, such as a scanner reading it, is retried.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the asynchronous read.</param>
    /// <returns>Validated monitor preferences.</returns>
    public async Task<MonitorPreferences> LoadAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1;; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using FileStream stream = new(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var preferences = await JsonSerializer
                    .DeserializeAsync<MonitorPreferences>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                return Validate(preferences ?? new MonitorPreferences());
            }
            catch (FileNotFoundException)
            {
                Trace.TraceInformation("Preferences.DefaultsMissingFile");
                return new MonitorPreferences();
            }
            catch (DirectoryNotFoundException)
            {
                Trace.TraceInformation("Preferences.DefaultsMissingDirectory");
                return new MonitorPreferences();
            }
            catch (JsonException exception)
            {
                Trace.TraceWarning("Preferences.InvalidFile: {0}", exception);
                _keepCopyBeforeReplace = !KeepInvalidCopy();
                return new MonitorPreferences();
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                Trace.TraceWarning("Preferences.ReadFailed: {0}", exception);
                // A file locked by another program may be valid: never replace it until a copy is kept.
                _keepCopyBeforeReplace = !KeepInvalidCopy();
                return new MonitorPreferences();
            }
            catch (UnauthorizedAccessException exception)
            {
                Trace.TraceWarning("Preferences.AccessDenied: {0}", exception);
                return new MonitorPreferences();
            }
        }
    }

    private bool KeepInvalidCopy()
    {
        try
        {
            File.Copy(_settingsPath, InvalidCopyPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon could not keep a copy of its unreadable settings: {0}", ex.Message);
            return false;
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
        var directory = Path.GetDirectoryName(_settingsPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The settings path must include a directory.");

        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Validate(preferences), SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_keepCopyBeforeReplace && File.Exists(_settingsPath))
            {
                if (!KeepInvalidCopy()) throw new IOException("The unread settings file could not be preserved.");
                _keepCopyBeforeReplace = false;
            }

            File.Move(temporaryPath, _settingsPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal static string[] NormalizeServicePins(IEnumerable<string>? names)
    {
        return (names ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static MonitorPreferences Validate(MonitorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var primary = NormalizeDrive(preferences.PrimaryDrive) ?? new MonitorPreferences().PrimaryDrive;
        var secondary = NormalizeDrive(preferences.SecondaryDrive);
        var normalized = preferences with
        {
            PrimaryDrive = primary,
            SecondaryDrive = secondary == primary ? null : secondary,
            NetworkInterfaceId = string.IsNullOrWhiteSpace(preferences.NetworkInterfaceId)
                ? null
                : preferences.NetworkInterfaceId.Trim(),
            StartupView = NormalizeEnum(preferences.StartupView, StartupViewMode.LastUsed),
            Width = ClampFinite(preferences.Width, 600, 600, 2000),
            Height = ClampFinite(preferences.Height, 840, 160, 1500),
            ActivityPanelRatio = ClampFinite(preferences.ActivityPanelRatio, 1.25, 0.05, 20),
            SelectedActivityTab = NormalizeColumn(preferences.SelectedActivityTab, 3),
            Left = ValidateCoordinate(preferences.Left),
            Top = ValidateCoordinate(preferences.Top),
            Theme = NormalizeTheme(preferences.Theme),
            Language = AppLanguages.Normalize(preferences.Language),
            ChartPalette = ChartPalettes.Normalize(preferences.ChartPalette),
            GaugeStyle = NormalizeEnum(preferences.GaugeStyle, GaugeStyle.Bowl),
            GaugePeakWindowSeconds = preferences.GaugePeakWindowSeconds is 60 or 120 or 300 or 600
                ? preferences.GaugePeakWindowSeconds
                : MonitorPreferences.ShortRangeSeconds,
            ResourceChartType = NormalizeEnum(preferences.ResourceChartType, ResourceChartType.Overlay),
            FocusMetric = secondary is null && preferences.FocusMetric == 5
                ? 0
                : Math.Clamp(preferences.FocusMetric, 0, 5),
            RetentionSeconds = Math.Clamp(preferences.RetentionSeconds, 600, 3600),
            VisibleMetrics = preferences.VisibleMetrics & 15,
            TopProcessCount =
            NormalizeTopCount(preferences.TopProcessCount, 5),
            TopServiceCount = NormalizeTopCount(preferences.TopServiceCount, 20),
            ServiceSortColumn = NormalizeServiceSort(preferences.ServiceSortColumn, preferences.TopServiceCount),
            ServiceStatusFilter = NormalizeColumn(preferences.ServiceStatusFilter, 2),
            TopUsageCount = NormalizeTopCount(preferences.TopUsageCount, 20),
            UsageSortColumn = NormalizeColumn(preferences.UsageSortColumn, 4),
            TopConnectionCount = NormalizeTopCount(preferences.TopConnectionCount, 10),
            ConnectionSortColumn =
            NormalizeColumn(preferences.ConnectionSortColumn, 5),
            ConnectionTrafficSeconds = preferences.ConnectionTrafficSeconds is 0 or 60 or 300 or 1800
                ? preferences.ConnectionTrafficSeconds
                : 300,
            ProcessSort = preferences is { SortByGpu: true, ProcessSort: ProcessSort.Cpu } ? ProcessSort.Gpu
            : NormalizeEnum(preferences.ProcessSort, ProcessSort.Cpu),
            SortByGpu = false,
            PinnedProcesses = ProcessPins.Normalize(preferences.PinnedProcesses),
            PinnedServices = NormalizeServicePins(preferences.PinnedServices),
            PinnedConnections = ProcessPins.Normalize(preferences.PinnedConnections)
                .Where(pin => pin.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime &&
                              pin.Grouped == pin.ImagePath.Length > 0).ToArray(),
            // Persisted JSON can contain an explicit null despite the non-nullable declaration.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            AutoCapture = (preferences.AutoCapture ?? new AutoCapturePreferences()).Normalized(),
            ShowShortcut = HotkeyGesture.Normalize(preferences.ShowShortcut)
        };
        return normalized.RememberChartConfiguration();
    }

    internal static string? NormalizeDrive(string? drive)
    {
        var value = drive?.Trim().TrimEnd('\\').ToUpperInvariant();
        return value is [>= 'A' and <= 'Z', ':'] ? value : null;
    }

    private static double ClampFinite(double value, double defaultValue, double minimum, double maximum)
    {
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : defaultValue;
    }

    private static double? ValidateCoordinate(double? value)
    {
        return value is { } coordinate && double.IsFinite(coordinate) ? coordinate : null;
    }

    private static string NormalizeTheme(string? theme)
    {
        return theme?.Trim().ToLowerInvariant() switch
        {
            "light" => "Light",
            "system" => "System",
            "brushedsteel" or "brushednickel" => "Porcelain",
            "candy" => "Candy",
            "copper" => "Copper",
            "gunmetal" => "Gunmetal",
            "porcelain" => "Porcelain",
            "midnightink" => "MidnightInk",
            "evergreeninstrument" => "EvergreenInstrument",
            "frostedporcelain" => "FrostedPorcelain",
            "smokedsapphire" => "SmokedSapphire",
            "glassrim" => "Crystal",
            "crystal" => "Crystal",
            "slate" => "Slate",
            "prism" => "Prism",
            "wildberry" => "Wildberry",
            _ => "Dark"
        };
    }

    private static int NormalizeTopCount(int count, int fallback)
    {
        return TopCountChoices.IsSupported(count) ? count : fallback;
    }

    private static T NormalizeEnum<T>(T value, T fallback) where T : struct, Enum
    {
        return Enum.IsDefined(value) ? value : fallback;
    }

    private static int NormalizeColumn(int column, int maximum)
    {
        return column >= 0 && column <= maximum ? column : 0;
    }

    private static int NormalizeServiceSort(int column, int count)
    {
        return column is >= 0 and <= 2 || (column == 3 && count == 0) ? column : 0;
    }
}
