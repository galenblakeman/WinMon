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

    internal int SeriesMask => VisibleMetrics | (ShowGpu ? 16 : 0);

    /// <summary>Gets the maximum number of CPU-ranked processes to display.</summary>
    public int TopProcessCount { get; init; } = 5;

    /// <summary>Gets the column that ranks the process rows.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ProcessSort>))]
    public ProcessSort ProcessSort { get; init; }

    /// <summary>Legacy GPU-ranking flag, read once and migrated to <see cref="ProcessSort"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SortByGpu { get; init; }

    /// <summary>Gets the saved window left position, or <see langword="null"/> when no position is saved.</summary>
    public double? Left { get; init; }

    /// <summary>Gets the saved window top position, or <see langword="null"/> when no position is saved.</summary>
    public double? Top { get; init; }

    /// <summary>Gets a value indicating whether the investigation view is open.</summary>
    public bool Expanded { get; init; }

    /// <summary>Gets a value indicating whether the monitor stays above other windows.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>Gets the selected application theme: Dark, Light, or System.</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>Gets the duration, in seconds, shown by the compact graph.</summary>
    public int CompactHistorySeconds { get; init; } = 120;

    /// <summary>Gets the duration, in seconds, retained for historical inspection.</summary>
    public int RetentionSeconds { get; init; } = 600;

    /// <summary>Gets a value indicating whether closing the monitor hides it in the notification area.</summary>
    public bool CloseToTray { get; init; }

    /// <summary>Gets a value indicating whether the monitor starts with the current user session.</summary>
    public bool StartWithWindows { get; init; }
}
