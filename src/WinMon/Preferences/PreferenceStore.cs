using System.Diagnostics;
using System.Text.Json;
using WinMon.Monitoring;
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
                return new MonitorPreferences();
            }
            catch (DirectoryNotFoundException)
            {
                return new MonitorPreferences();
            }
            catch (JsonException)
            {
                KeepInvalidCopy();
                return new MonitorPreferences();
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // A file locked by another program may be valid: never replace it until a copy is kept.
                _keepCopyBeforeReplace = !KeepInvalidCopy();
                return new MonitorPreferences();
            }
            catch (UnauthorizedAccessException)
            {
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

    internal static MonitorPreferences Validate(MonitorPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var primary = NormalizeDrive(preferences.PrimaryDrive) ?? new MonitorPreferences().PrimaryDrive;
        var secondary = NormalizeDrive(preferences.SecondaryDrive);
        return preferences with
        {
            PrimaryDrive = primary,
            SecondaryDrive = secondary == primary ? null : secondary,
            NetworkInterfaceId = string.IsNullOrWhiteSpace(preferences.NetworkInterfaceId)
                ? null : preferences.NetworkInterfaceId.Trim(),
            StartupView = Enum.IsDefined(preferences.StartupView)
                ? preferences.StartupView : StartupViewMode.LastUsed,
            Width = ClampFinite(preferences.Width, 600, 600, 2000),
            Height = ClampFinite(preferences.Height, 840, 160, 1500),
            Left = ValidateCoordinate(preferences.Left),
            Top = ValidateCoordinate(preferences.Top),
            Theme = NormalizeTheme(preferences.Theme),
            Language = AppLanguages.Normalize(preferences.Language),
            ChartPalette = ChartPalettes.Normalize(preferences.ChartPalette),
            GaugeStyle = Enum.IsDefined(preferences.GaugeStyle) ? preferences.GaugeStyle : GaugeStyle.Bowl,
            ResourceChartType = Enum.IsDefined(preferences.ResourceChartType)
                ? preferences.ResourceChartType : ResourceChartType.Overlay,
            FocusMetric = secondary is null && preferences.FocusMetric == 5
                ? 0 : Math.Clamp(preferences.FocusMetric, 0, 5),
            RetentionSeconds = Math.Clamp(preferences.RetentionSeconds, 600, 3600),
            VisibleMetrics = preferences.VisibleMetrics & 15,
            TopProcessCount =
            TopCountChoices.IsSupported(preferences.TopProcessCount) ? preferences.TopProcessCount : 5,
            TopServiceCount = TopCountChoices.IsSupported(preferences.TopServiceCount)
                ? preferences.TopServiceCount
                : 20,
            TopUsageCount = TopCountChoices.IsSupported(preferences.TopUsageCount) ? preferences.TopUsageCount : 20,
            UsageSortColumn = preferences.UsageSortColumn is >= 0 and <= 4 ? preferences.UsageSortColumn : 0,
            ProcessSort = preferences is { SortByGpu: true, ProcessSort: ProcessSort.Cpu } ? ProcessSort.Gpu
            : Enum.IsDefined(preferences.ProcessSort) ? preferences.ProcessSort : ProcessSort.Cpu,
            SortByGpu = false,
            PinnedProcesses = ProcessPins.Normalize(preferences.PinnedProcesses),
            // Persisted JSON can contain an explicit null despite the non-nullable declaration.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            AutoCapture = (preferences.AutoCapture ?? new AutoCapturePreferences()).Normalized(),
            ShowShortcut = HotkeyGesture.Normalize(preferences.ShowShortcut)
        };
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
}
