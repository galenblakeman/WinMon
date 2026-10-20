using System.Text.Json.Serialization;
using WinMon.Monitoring;

namespace WinMon.Preferences;

/// <summary>
/// Stores user-configurable monitoring and window preferences.
/// </summary>
public sealed record MonitorPreferences
{
    /// <summary>Gets the compact window width in device-independent pixels.</summary>
    public double Width { get; init; } = 600;

    /// <summary>Gets the compact window height in device-independent pixels.</summary>
    public double Height { get; init; } = 840;

    /// <summary>Visible chart series: CPU=1, memory=2, disk=4, network=8.</summary>
    public int VisibleMetrics { get; init; } = 15;

    /// <summary>Show GPU utilization; separate from the legacy mask so existing settings enable it by default.</summary>
    public bool ShowGpu { get; init; } = true;

    public string PrimaryDrive { get; init; } = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
    public string? SecondaryDrive { get; init; }
    public bool ShowSecondaryDisk { get; init; } = true;

    internal int SeriesMask => VisibleMetrics | (ShowGpu ? 16 : 0) |
        (SecondaryDrive is not null && ShowSecondaryDisk ? 32 : 0);

    /// <summary>Gets the maximum number of CPU-ranked processes to display.</summary>
    public int TopProcessCount { get; init; } = 5;

    /// <summary>Gets the column that ranks the process rows.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ProcessSort>))]
    public ProcessSort ProcessSort { get; init; }

    /// <summary>Gets a value indicating whether processes of the same executable share one row.</summary>
    public bool GroupProcesses { get; init; }

    /// <summary>Show only rows classified as apps at the live or selected historical sample.</summary>
    public bool AppsOnly { get; init; }

    /// <summary>Legacy GPU-ranking flag, read once and migrated to <see cref="ProcessSort"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SortByGpu { get; init; }

    /// <summary>Gets the saved window left position, or <see langword="null"/> when no position is saved.</summary>
    public double? Left { get; init; }

    /// <summary>Gets the saved window top position, or <see langword="null"/> when no position is saved.</summary>
    public double? Top { get; init; }

    /// <summary>The short chart range and the process sparkline duration, in seconds.</summary>
    internal const int ShortRangeSeconds = 120;

    /// <summary>Gets the chosen chart range in seconds; anything outside <see cref="ChartRanges"/> means the short range.</summary>
    public int ChartRangeSeconds { get; init; }

    /// <summary>Chart range choices: 2, 10, 30 and 60 minutes, up to the retained history.</summary>
    internal int[] ChartRanges => [.. new[] { ShortRangeSeconds, 600, 1800, 3600 }.Where(seconds => seconds <= RetentionSeconds)];

    internal int EffectiveChartRange => ChartRanges.Contains(ChartRangeSeconds) ? ChartRangeSeconds : ShortRangeSeconds;

    /// <summary>Gets a value indicating whether the monitor stays above other windows.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>Gets the selected application theme: Dark, Light, or System.</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>UI language identifier, or System to follow the Windows display language on startup.</summary>
    public string Language { get; init; } = AppLanguages.SystemDefault;

    /// <summary>Resource chart colors, independent of the light/dark appearance.</summary>
    public string ChartPalette { get; init; } = "Classic";

    /// <summary>Gets the duration, in seconds, retained for historical inspection.</summary>
    public int RetentionSeconds { get; init; } = 600;

    /// <summary>Gets a value indicating whether minimizing the monitor hides it in the notification area.</summary>
    public bool MinimizeToTray { get; init; }

    /// <summary>Gets a value indicating whether the monitor starts with the current user session.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Gets the global shortcut that shows the monitor, such as "Ctrl+Shift+M", or "None".</summary>
    public string ShowShortcut { get; init; } = Shortcuts.HotkeyGesture.Default.ToString();

    /// <summary>Gets a value indicating whether the shortcut also starts the monitor; the Start menu shortcut's presence is authoritative.</summary>
    public bool LaunchWithShortcut { get; init; }

    /// <summary>Gets the usable show shortcut, or <see langword="null"/> when it is turned off.</summary>
    internal Shortcuts.HotkeyGesture? ShowGesture =>
        Shortcuts.HotkeyGesture.TryParse(ShowShortcut, out var gesture) && gesture?.Problem is null ? gesture : null;
}
