namespace WinMon.Monitoring;

/// <summary>
/// Describes CPU consumed by a process during one sampling interval.
/// </summary>
/// <param name="ProcessId">Identifies the process for the sampled interval.</param>
/// <param name="CreationTicks">Identifies this specific lifetime of the process.</param>
/// <param name="Name">Provides the executable name captured during the sample.</param>
/// <param name="CpuPercent">Provides CPU consumed as a percentage of total machine capacity.</param>
public sealed record ProcessUsage(
    int ProcessId,
    long CreationTicks,
    string Name,
    double CpuPercent,
    ulong? WorkingSetBytes = null,
    double? IoBytesPerSecond = null)
{
    /// <summary>Observed parent PID; ancestry must also be checked against process creation times.</summary>
    public int ParentProcessId { get; init; }

    /// <summary>Number of threads at the latest observation, or null when unavailable.</summary>
    public long? ThreadCount { get; init; }

    /// <summary>Number of open handles at the latest observation, or null when unavailable.</summary>
    public long? HandleCount { get; init; }

    /// <summary>Busiest GPU engine utilization for this process interval; null means unavailable.</summary>
    public double? GpuPercent { get; init; }

    public string GpuEngine { get; init; } = "";
    public string GpuId { get; init; } = "";

    /// <summary>Private committed memory; summed for groups because shared pages make working sets overlap.</summary>
    public ulong? PrivateBytes { get; init; }

    /// <summary>Full executable path, or empty when it could not be read; groups processes by executable.</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>Friendly application description from executable version information, when available.</summary>
    public string AppName { get; init; } = "";

    /// <summary>Product name from executable version information, when available.</summary>
    public string ProductName { get; init; } = "";

    /// <summary>Publisher from executable company metadata, when available; not a verified signing identity.</summary>
    public string Publisher { get; init; } = "";

    /// <summary>Whether a visible, unowned top-level window was observed at sampling time; null when unavailable.</summary>
    public bool? HasAppWindow { get; init; }

    // Keep later background observations too, so compaction cannot leave an old App label in the averaging window.
    public bool HasHadAppWindow { get; init; }

    /// <summary>Processes a grouped row represents; 1 for an individual process.</summary>
    public int MemberCount { get; init; } = 1;

    /// <summary>Process IDs a grouped row represents, lowest first; empty for an individual process.</summary>
    public IReadOnlyList<int> MemberIds { get; init; } = [];
}