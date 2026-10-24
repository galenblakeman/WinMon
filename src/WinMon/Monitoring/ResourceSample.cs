namespace WinMon.Monitoring;

/// <summary>Immutable system resources for a single interval. Null means unavailable, never zero.</summary>
public sealed record ResourceSample
{
    public double? CpuClockMhz { get; init; }
    public double? CpuKernelPercent { get; init; }
    public ulong? AvailableMemoryBytes { get; init; }
    public ulong? CommittedBytes { get; init; }
    public ulong? CommitLimitBytes { get; init; }
    public ulong? CachedBytes { get; init; }
    public ulong? PagedPoolBytes { get; init; }
    public ulong? NonPagedPoolBytes { get; init; }
    public uint? ProcessCount { get; init; }
    public uint? ThreadCount { get; init; }
    public uint? HandleCount { get; init; }
    public double? GpuPercent { get; init; }
    public string GpuId { get; init; } = "";
    public string GpuEngine { get; init; } = "";
    public ulong? GpuMemoryUsedBytes { get; init; }
    public ulong? GpuMemoryTotalBytes { get; init; }
    public ulong? GpuSharedMemoryUsedBytes { get; init; }
    public string GpuName { get; init; } = "";
    public ulong? TotalMemoryBytes { get; init; }
    public ulong? UsedMemoryBytes { get; init; }

    public double? MemoryPercent => TotalMemoryBytes is > 0 && UsedMemoryBytes is { } used
        ? 100d * used / TotalMemoryBytes.Value
        : null;

    public string DiskName { get; init; } = "";
    public double? DiskActivePercent { get; init; }
    public double? DiskBytesPerSecond { get; init; }
    public double? DiskReadBytesPerSecond { get; init; }
    public double? DiskWriteBytesPerSecond { get; init; }
    public double? DiskResponseSeconds { get; init; }
    public double? DiskQueueLength { get; init; }
    public ulong? DiskFreeBytes { get; init; }
    public ulong? DiskTotalBytes { get; init; }
    public string SecondaryDiskName { get; init; } = "";
    public double? SecondaryDiskActivePercent { get; init; }
    public double? SecondaryDiskBytesPerSecond { get; init; }
    public double? SecondaryDiskReadBytesPerSecond { get; init; }
    public double? SecondaryDiskWriteBytesPerSecond { get; init; }
    public double? SecondaryDiskResponseSeconds { get; init; }
    public double? SecondaryDiskQueueLength { get; init; }
    public ulong? SecondaryDiskFreeBytes { get; init; }
    public ulong? SecondaryDiskTotalBytes { get; init; }
    public string NetworkName { get; init; } = "";
    public string NetworkId { get; init; } = "";
    public long? LinkBitsPerSecond { get; init; }
    public double? ReceiveBytesPerSecond { get; init; }

    public double? SendBytesPerSecond { get; init; }
    public ulong? ReceivedBytesSinceStart { get; init; }
    public ulong? SentBytesSinceStart { get; init; }

    // Full-duplex utilization is the busier direction, not the sum of both directions.
    public double? NetworkPercent =>
        LinkBitsPerSecond is > 0 && ReceiveBytesPerSecond is { } rx && SendBytesPerSecond is { } tx
            ? Math.Clamp(Math.Max(rx, tx) * 800d / LinkBitsPerSecond.Value, 0, 100)
            : null;
}

/// <summary>Validate cumulative counters before turning them into rates.</summary>
internal static class ResourceRates
{
    internal static double? PerSecond(ulong? before, ulong? after, double seconds)
    {
        return before is { } prior && after is { } current && current >= prior && seconds is > 0 and <= 5
            ? (current - prior) / seconds
            : null;
    }
}
