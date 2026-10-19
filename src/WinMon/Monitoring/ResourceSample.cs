namespace WinMon.Monitoring;

/// <summary>Immutable system resources for a single interval. Null means unavailable, never zero.</summary>
public sealed record ResourceSample
{
    public double? GpuPercent { get; init; }
    public string GpuId { get; init; } = "";
    public string GpuEngine { get; init; } = "";
    public ulong? TotalMemoryBytes { get; init; }
    public ulong? UsedMemoryBytes { get; init; }
    public double? MemoryPercent => TotalMemoryBytes is > 0 && UsedMemoryBytes is { } used
        ? 100d * used / TotalMemoryBytes.Value : null;
    public string DiskName { get; init; } = "";
    public double? DiskActivePercent { get; init; }
    public double? DiskBytesPerSecond { get; init; }
    public string NetworkName { get; init; } = "";
    public string NetworkId { get; init; } = "";
    public long? LinkBitsPerSecond { get; init; }
    public double? ReceiveBytesPerSecond { get; init; }
    public double? SendBytesPerSecond { get; init; }
    // Full-duplex utilization is the busier direction, not the sum of both directions.
    public double? NetworkPercent => LinkBitsPerSecond is > 0 && ReceiveBytesPerSecond is { } rx && SendBytesPerSecond is { } tx
        ? Math.Clamp(Math.Max(rx, tx) * 800d / LinkBitsPerSecond.Value, 0, 100) : null;
}

/// <summary>Validate cumulative counters before turning them into rates.</summary>
internal static class ResourceRates
{
    internal static double? PerSecond(ulong? before, ulong? after, double seconds) =>
        before is { } prior && after is { } current && current >= prior && seconds is > 0 and <= 5
        ? (current - prior) / seconds : null;
}
