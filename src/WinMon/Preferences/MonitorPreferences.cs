using System.Text.Json.Serialization;
using WinMon.Monitoring;
using WinMon.Shortcuts;

namespace WinMon.Preferences;

/// <summary>
/// Stores user-configurable monitoring and window preferences.
/// </summary>
public sealed record MonitorPreferences
{
    /// <summary>Open the compact resource gauges while retaining full-view window dimensions.</summary>
    public bool CompactMode { get; init; }

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

    /// <summary>Maximum ranked processes to display; zero shows all available entries.</summary>
    public int TopProcessCount { get; init; } = 5;

    /// <summary>Maximum ranked service groups to display; zero shows all matching groups.</summary>
    public int TopServiceCount { get; init; } = 20;

    /// <summary>Maximum apps the Usage tab lists; zero shows all matching apps.</summary>
    public int TopUsageCount { get; init; } = 20;

    /// <summary>The Usage tab's ranking column, by position: CPU time, I/O, GPU time, processes, last active.</summary>
    public int UsageSortColumn { get; init; }

    /// <summary>Gets a value indicating whether the Usage tab lists only apps observed with a window.</summary>
    public bool UsageAppsOnly { get; init; }

    /// <summary>Gets the column that ranks the process rows.</summary>
    public ProcessSort ProcessSort { get; init; }

    /// <summary>Gets a value indicating whether processes of the same executable share one row.</summary>
    public bool GroupProcesses { get; init; }

    /// <summary>Show only rows classified as apps at the live or selected historical sample.</summary>
    public bool AppsOnly { get; init; }

    /// <summary>Individual process lifetimes and executable groups pinned in insertion order.</summary>
    public SavedProcessPin[] PinnedProcesses { get; init; } = [];

    /// <summary>Restore the pinned-only process filter on startup.</summary>
    public bool PinnedOnly { get; init; }

    /// <summary>Keep both process and service action bars collapsed between launches. Retains the original JSON key.</summary>
    public bool ProcessActionsHidden { get; init; }

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

    /// <summary>Full-view resource history layout.</summary>
    public ResourceChartType ResourceChartType { get; init; }

    /// <summary>Resource enlarged in the focus layout, in chart metric order.</summary>
    public int FocusMetric { get; init; }

    /// <summary>Chart range choices: 2, 10, 30 and 60 minutes, up to the retained history.</summary>
    internal int[] ChartRanges =>
    [
        .. new[] { ShortRangeSeconds, 600, 1800, 3600 }.Where(seconds => seconds <= RetentionSeconds)
    ];

    internal int EffectiveChartRange => ChartRanges.Contains(ChartRangeSeconds) ? ChartRangeSeconds : ShortRangeSeconds;

    /// <summary>Gets a value indicating whether the monitor stays above other windows.</summary>
    public bool AlwaysOnTop { get; init; }

    /// <summary>Gets the selected application theme identifier.</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>UI language identifier, or System to follow the Windows display language on startup.</summary>
    public string Language { get; init; } = AppLanguages.SystemDefault;

    /// <summary>Resource chart colors, independent of the light/dark appearance.</summary>
    public string ChartPalette { get; init; } = "Classic";

    /// <summary>Full-view resource gauge drawing; settings saved before the choice existed load as the bowl.</summary>
    public GaugeStyle GaugeStyle { get; init; }

    /// <summary>Gets the duration, in seconds, retained for historical inspection.</summary>
    public int RetentionSeconds { get; init; } = 600;

    /// <summary>Save retained resource and process samples across ordinary exits.</summary>
    public bool KeepHistoryAcrossRestarts { get; init; }

    /// <summary>Gets a value indicating whether minimizing the monitor hides it in the notification area.</summary>
    public bool MinimizeToTray { get; init; }

    /// <summary>Gets a value indicating whether the monitor starts with the current user session.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Gets a value indicating whether WinMon checks GitHub for signed releases while it runs.</summary>
    public bool AutomaticallyCheckForUpdates { get; init; } = true;

    /// <summary>Gets the global shortcut that shows the monitor, such as "Ctrl+Shift+M", or "None".</summary>
    public string ShowShortcut { get; init; } = HotkeyGesture.Default.ToString();

    /// <summary>Gets the usable show shortcut, or <see langword="null"/> when it is turned off.</summary>
    internal HotkeyGesture? ShowGesture =>
        HotkeyGesture.TryParse(ShowShortcut, out var gesture) && gesture?.Problem is null ? gesture : null;

    /// <summary>Gets the automatic capture rules and where their reports go; off by default.</summary>
    public AutoCapturePreferences AutoCapture { get; init; } = new();
}

/// <summary>Automatic captures: one rule per resource, a cooldown, the reports folder and notifications.</summary>
public sealed record AutoCapturePreferences
{
    /// <summary>Gets a value indicating whether any rule may fire; reports contain process names, so off by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets one rule per <see cref="CaptureMetric"/>, in display order.</summary>
    public CaptureRuleSetting[] Rules { get; init; } = CaptureRuleSetting.Defaults;

    /// <summary>Gets the minutes a rule waits after firing before it may fire again.</summary>
    public int CooldownMinutes { get; init; } = 5;

    /// <summary>Gets the reports folder, or <see langword="null"/> for the default beside the settings file.</summary>
    public string? Folder { get; init; }

    /// <summary>Gets a value indicating whether a saved capture also shows a notification.</summary>
    public bool Notify { get; init; }

    /// <summary>A copy with one valid rule per metric, missing ones at their defaults, and the cooldown in range.</summary>
    internal AutoCapturePreferences Normalized()
    {
        // Deserialized settings can contain null despite the non-nullable declarations.
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var rules = Rules ?? [];
        return this with
        {
            Rules =
            [
                .. CaptureRuleSetting.Defaults.Select(fallback =>
                    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                    (rules.FirstOrDefault(rule => rule?.Metric == fallback.Metric) ?? fallback).Normalized())
            ],
            CooldownMinutes = Math.Clamp(CooldownMinutes, 1, 1440),
            Folder = string.IsNullOrWhiteSpace(Folder) ? null : Folder.Trim()
        };
    }
}
