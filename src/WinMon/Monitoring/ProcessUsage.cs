namespace WinMon.Monitoring;

/// <summary>
/// Describes CPU consumed by a process during one sampling interval.
/// </summary>
/// <param name="ProcessId">Identifies the process for the sampled interval.</param>
/// <param name="CreationTicks">Identifies this specific lifetime of the process.</param>
/// <param name="Name">Provides the executable name captured during the sample.</param>
/// <param name="CpuPercent">Provides CPU consumed as a percentage of total machine capacity.</param>
public sealed record ProcessUsage(int ProcessId, long CreationTicks, string Name, double CpuPercent,
    ulong? WorkingSetBytes = null, double? IoBytesPerSecond = null)
{
    /// <summary>Busiest GPU engine utilization for this process interval; null means unavailable.</summary>
    public double? GpuPercent { get; init; }
    public string GpuEngine { get; init; } = "";
    public string GpuId { get; init; } = "";
}
