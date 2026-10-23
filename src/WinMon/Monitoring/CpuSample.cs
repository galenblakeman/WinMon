namespace WinMon.Monitoring;

/// <summary>
/// Captures the CPU measurements available for one sampling instant.
/// </summary>
/// <param name="Timestamp">Identifies when the observation was collected.</param>
/// <param name="TotalCpuPercent">Provides total machine CPU consumption, or <see langword="null"/> when a baseline or gap prevents a reliable value.</param>
/// <param name="Processes">Provides accessible processes with interval CPU attribution.</param>
/// <param name="UnavailableProcesses">Counts processes whose CPU timing could not be read.</param>
/// <param name="MonotonicTimestamp">Identifies the monotonic collection instant, or zero for wall-time-only fixtures.</param>
/// <param name="IsGap">Indicates that this sample cannot be connected to the preceding sample.</param>
public sealed record CpuSample(
    DateTimeOffset Timestamp,
    double? TotalCpuPercent,
    IReadOnlyList<ProcessUsage> Processes,
    int UnavailableProcesses,
    bool IsGap = false,
    long MonotonicTimestamp = 0)
{
    /// <summary>Resource measurements captured in the same collection pass.</summary>
    public ResourceSample Resources { get; init; } = new();

    /// <summary>
    /// Idle processes left out of <see cref="Processes"/> to bound history memory: no CPU, I/O or GPU reading,
    /// and outside the largest working sets. Treat them as zero, not unavailable.
    /// </summary>
    public int CompactedProcesses { get; init; }
}