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
    SensorServiceQuality Quality, CpuSensorReading? CpuTemperature, string? FailureReason = null)
{
    public IReadOnlyList<HardwareFanReading>? Fans { get; init; }
    public CpuPackagePowerReading? CpuPackagePower { get; init; }
    public IReadOnlyList<HardwareSensorReading>? Sensors { get; init; }
}
public sealed record CpuPackagePowerReading(double Watts, long LastReadAt);
public enum HardwareSensorKind
{
    BoardTemperature, BoardVoltage, GpuTemperature, GpuPower, GpuFan, NvmeEnduranceUsed, NvmeAvailableSpare
}
public sealed record HardwareSensorReading(HardwareSensorKind Kind, string? Target, string? Name, double Value, long LastReadAt);
public sealed record CpuSensorReading(string HardwareId, string HardwareName, string SensorId, string SensorName,
    double? Celsius, string Source, long LastReadAt, string? AvailabilityReason = null);

/// <summary>Validates untrusted metadata and maps only recent successful observations into portable samples.</summary>
public static class SensorSnapshotValidation
{
    public const int MaximumFans = 16;
    public const int MaximumCpuPackageWatts = 2000;
    public const int MaximumHardwareSensors = 128;

    public static void Validate(SensorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(snapshot.Quality) || snapshot.Sequence < 0 || snapshot.UpdatedAt < 0 ||
            snapshot.Frequency != System.Diagnostics.Stopwatch.Frequency ||
            snapshot.FailureReason?.Length > 1024 ||
            snapshot.Quality is SensorServiceQuality.Complete && snapshot.CpuTemperature is null ||
            snapshot.CpuTemperature is { } reading && !IsValid(reading, snapshot.UpdatedAt) ||
            snapshot.Fans is { } fans && !ValidFans(fans) ||
            snapshot.CpuPackagePower is { } power && !IsValid(power, snapshot.UpdatedAt) ||
            snapshot.Sensors is { } sensors && (sensors.Count > MaximumHardwareSensors ||
                sensors.Any(sensor => !IsValidHardwareSensor(sensor, snapshot.UpdatedAt))))
            throw new InvalidDataException("Sensor service snapshot is invalid.");
        if ((snapshot.CpuTemperature is not null || snapshot.Fans is { Count: > 0 } ||
             snapshot.CpuPackagePower is not null || snapshot.Sensors is { Count: > 0 }) && snapshot.Generation == Guid.Empty)
            throw new InvalidDataException("Sensor service generation is missing.");
    }

    private static bool IsValid(CpuSensorReading reading, long updatedAt) =>
        Text(reading.HardwareId) && Text(reading.HardwareName) && Text(reading.SensorId) &&
        Text(reading.SensorName) && Text(reading.Source) && reading.LastReadAt >= 0 &&
        reading.LastReadAt <= updatedAt && reading.AvailabilityReason?.Length is not > 1024 &&
        (reading.Celsius is null || reading.Celsius is >= 1 and <= 150);

    private static bool IsValid(CpuPackagePowerReading reading, long updatedAt) =>
        reading.Watts is > 0 and <= MaximumCpuPackageWatts && reading.LastReadAt > 0 &&
        reading.LastReadAt <= updatedAt;

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 &&
        !value.Any(char.IsControl);

    public static bool IsValidHardwareSensor(HardwareSensorReading? sensor, long updatedAt) =>
        sensor is { LastReadAt: > 0 } && sensor.LastReadAt <= updatedAt && (sensor.Kind switch
        {
            HardwareSensorKind.BoardTemperature => sensor.Target is null && Text(sensor.Name) && sensor.Value is >= -40 and <= 150,
            HardwareSensorKind.BoardVoltage => sensor.Target is null && Text(sensor.Name) && sensor.Value is >= -30 and <= 30,
            HardwareSensorKind.GpuTemperature => IsGpuSensor(sensor) && sensor.Value is > 0 and <= 150,
            HardwareSensorKind.GpuPower => IsGpuSensor(sensor) && sensor.Value is > 0 and <= MaximumCpuPackageWatts,
            HardwareSensorKind.GpuFan => IsGpuSensor(sensor) && sensor.Value is >= 0 and <= 30000,
            HardwareSensorKind.NvmeEnduranceUsed => IsNvmeSensor(sensor) && sensor.Value is >= 0 and <= 255,
            HardwareSensorKind.NvmeAvailableSpare => IsNvmeSensor(sensor) && sensor.Value is >= 0 and <= 100,
            _ => false
        });

    private static bool IsVolume(string? value) => value is { Length: 2 } && char.IsAsciiLetter(value[0]) && value[1] == ':';
    private static bool IsGpuSensor(HardwareSensorReading sensor) => Text(sensor.Target) && !IsVolume(sensor.Target) && Text(sensor.Name);
    private static bool IsNvmeSensor(HardwareSensorReading sensor) => IsVolume(sensor.Target) && sensor.Name is null &&
        double.IsInteger(sensor.Value);

    public static IReadOnlyList<HardwareSensorReading> GetFreshHardwareSensors(SensorSnapshot? snapshot, long now)
    {
        if (snapshot is not { Quality: SensorServiceQuality.Complete or SensorServiceQuality.Partial,
                Sensors: { Count: > 0 and <= MaximumHardwareSensors } sensors } ||
            snapshot.Generation == Guid.Empty || snapshot.UpdatedAt > now ||
            snapshot.Frequency != System.Diagnostics.Stopwatch.Frequency ||
            (now - snapshot.UpdatedAt) / (double)snapshot.Frequency > 5)
            return [];

        return sensors.Where(sensor => IsValidHardwareSensor(sensor, snapshot.UpdatedAt) &&
            (now - sensor.LastReadAt) / (double)snapshot.Frequency <=
            (sensor.Kind is HardwareSensorKind.NvmeEnduranceUsed or HardwareSensorKind.NvmeAvailableSpare ? 35 : 5)).ToArray();
    }

    private static bool ValidFans(IReadOnlyList<HardwareFanReading> fans) =>
        fans.Count <= MaximumFans && fans.All(fan => fan is not null && Text(fan.Name) && fan.Rpm is >= 0 and <= 30000);

    public static IReadOnlyList<HardwareFanReading> GetFreshFans(SensorSnapshot? snapshot, long now) =>
        snapshot is { Quality: SensorServiceQuality.Complete or SensorServiceQuality.Partial,
            Fans: { Count: > 0 } fans, UpdatedAt: > 0 } && snapshot.Generation != Guid.Empty &&
        snapshot.UpdatedAt <= now && snapshot.Frequency == System.Diagnostics.Stopwatch.Frequency &&
        (now - snapshot.UpdatedAt) / (double)snapshot.Frequency <= 5 && ValidFans(fans) ? fans : [];

    public static double? GetFreshCpuPackageWatts(SensorSnapshot? snapshot, long now) =>
        snapshot is { Quality: SensorServiceQuality.Complete or SensorServiceQuality.Partial,
            CpuPackagePower: { } power } && snapshot.Generation != Guid.Empty &&
        snapshot.UpdatedAt <= now && snapshot.Frequency == System.Diagnostics.Stopwatch.Frequency &&
        IsValid(power, snapshot.UpdatedAt) && (now - power.LastReadAt) / (double)snapshot.Frequency <= 5
            ? power.Watts : null;

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
