namespace WinMon.SensorService;

/// <summary>Internal, service-owned reading before it crosses the protected pipe boundary.</summary>
internal sealed record SensorReading(
    string HardwareId,
    string HardwareName,
    string SensorId,
    string SensorName,
    double? Celsius,
    string Source,
    long LastReadAt,
    string? AvailabilityReason = null);

internal enum SensorCollectorQuality
{
    Idle,
    Starting,
    Complete,
    Partial,
    Failed,
    Suspended
}

internal sealed record SensorCollectorSnapshot(
    Guid Generation,
    long Sequence,
    long UpdatedAt,
    long Frequency,
    SensorCollectorQuality Quality,
    SensorReading? CpuTemperature = null,
    string? FailureReason = null);
