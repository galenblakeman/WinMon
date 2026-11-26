using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>Fixed local endpoint and bounded wire format for the optional CPU sensor service.</summary>
public static class SensorServiceProtocol
{
    public const int Version = 1;
    public const string ServiceName = "WinMonSensorMonitor";
    public const string PipeName = "WinMonSensorMonitor.v1";
    public const int MaximumMessageBytes = LocalServiceWire.MaximumMessageBytes;
    public const int MaximumRequestBytes = LocalServiceWire.MaximumRequestBytes;
    public static string InstallDirectory => ProtectedServiceEndpoints.Sensor.InstallDirectory;
    public static string InstalledExecutable => ProtectedServiceEndpoints.Sensor.InstalledServiceExecutable;

    public static Task WriteAsync<T>(Stream stream, T value, CancellationToken token) =>
        LocalServiceWire.WriteAsync(stream, value, token);
    public static Task<T> ReadAsync<T>(Stream stream, CancellationToken token,
        int maximumBytes = MaximumMessageBytes) => LocalServiceWire.ReadAsync<T>(stream, token, maximumBytes);
}

public enum SensorServiceQuality { Idle, Starting, Complete, Partial, Failed, Suspended }

public sealed record SensorServiceRequest(int Version, string Operation);
public sealed record SensorServiceResponse(int Version, string Status, SensorSnapshot? Snapshot = null);
public sealed record SensorSnapshot(Guid Generation, long Sequence, long UpdatedAt, long Frequency,
    SensorServiceQuality Quality, CpuSensorReading? CpuTemperature, string? FailureReason = null);
public sealed record CpuSensorReading(string HardwareId, string HardwareName, string SensorId, string SensorName,
    double? Celsius, string Source, long LastReadAt, string? AvailabilityReason = null);

/// <summary>Validates untrusted metadata and maps only recent successful observations into portable samples.</summary>
public static class SensorSnapshotValidation
{
    public static void Validate(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(snapshot.Quality) || snapshot.Sequence < 0 || snapshot.UpdatedAt < 0 ||
            snapshot.Frequency != System.Diagnostics.Stopwatch.Frequency ||
            snapshot.FailureReason?.Length > 1024 ||
            snapshot.Quality is SensorServiceQuality.Complete && snapshot.CpuTemperature is null ||
            snapshot.CpuTemperature is { } reading && !IsValid(reading, snapshot.UpdatedAt))
            throw new InvalidDataException("Sensor service snapshot is invalid.");
        if (snapshot.CpuTemperature is not null && snapshot.Generation == Guid.Empty)
            throw new InvalidDataException("Sensor service generation is missing.");
    }

    private static bool IsValid(CpuSensorReading reading, long updatedAt) =>
        Text(reading.HardwareId) && Text(reading.HardwareName) && Text(reading.SensorId) &&
        Text(reading.SensorName) && Text(reading.Source) && reading.LastReadAt >= 0 &&
        reading.LastReadAt <= updatedAt && reading.AvailabilityReason?.Length is not > 1024 &&
        (reading.Celsius is null || reading.Celsius is >= 1 and <= 150);

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl);

    public static CpuTemperatureReading? TryGetReading(SensorSnapshot? snapshot, long now)
    {
        if (snapshot is null) return null;
        try { Validate(snapshot); }
        catch (InvalidDataException) { return null; }
        if (snapshot.Quality != SensorServiceQuality.Complete || snapshot.CpuTemperature is not
            { Celsius: { } value, LastReadAt: > 0, AvailabilityReason: null } reading ||
            snapshot.UpdatedAt > now || reading.LastReadAt > now ||
            (now - snapshot.UpdatedAt) / (double)snapshot.Frequency > 5 ||
            (now - reading.LastReadAt) / (double)snapshot.Frequency > 5)
            return null;
        return new(snapshot.Generation, reading.HardwareId, reading.HardwareName, reading.SensorId,
            reading.SensorName, value, reading.Source);
    }
}
