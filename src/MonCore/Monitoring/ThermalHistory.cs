using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonCore.Monitoring;

/// <summary>Bounded historical sensor observations. No acquisition clock or live service lease is restored.</summary>
public sealed record ThermalHistory(CpuTemperatureReading? Cpu, double? CpuLimit, uint? CpuLimitFlags,
    ThermalZoneReading[] Zones, GpuThermalReading[] Gpus, double? Disk, double? SecondaryDisk)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GpuGraphicsClockMhz { get; init; }

    public static ThermalHistory From(ResourceSample r) => new(r.CpuTemperature, r.CpuPerformanceLimitPercent,
        r.CpuPerformanceLimitFlags, r.ThermalZones?.ToArray()!, r.GpuThermals?.ToArray()!,
        r.DiskTemperatureCelsius, r.SecondaryDiskTemperatureCelsius) { GpuGraphicsClockMhz = r.GpuGraphicsClockMhz };

    public ResourceSample Apply(ResourceSample r) => r with
    {
        CpuTemperature = Cpu, CpuPerformanceLimitPercent = CpuLimit, CpuPerformanceLimitFlags = CpuLimitFlags,
        ThermalZones = Zones, GpuThermals = Gpus, DiskTemperatureCelsius = Disk, SecondaryDiskTemperatureCelsius = SecondaryDisk,
        GpuGraphicsClockMhz = GpuGraphicsClockMhz
    };

    public static bool IsTemperature(double? value) => value is >= 1 and <= 150;
    private static bool Temperature(double? value) => value is null || IsTemperature(value);
    private static bool Name(string? value) => value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl);
    private static bool GpuName(string? value) => value is { Length: <= 1024 } && !value.Any(char.IsControl);
    private static bool Nonnegative(double? value) => value is null || value is >= 0 and <= 1_000_000;

    public bool Valid() => Temperature(Disk) && Temperature(SecondaryDisk) && Nonnegative(GpuGraphicsClockMhz) &&
        (CpuLimit is null || CpuLimit is >= 0 and <= 100) &&
        (Cpu is null || (IsTemperature(Cpu.Celsius) && Cpu.Generation != Guid.Empty && Name(Cpu.HardwareId) &&
            Name(Cpu.HardwareName) && Name(Cpu.SensorId) && Name(Cpu.SensorName) && Name(Cpu.Source))) &&
        Zones is { Length: <= 64 } && Zones.All(z => z is not null && Name(z.Name) && IsTemperature(z.Celsius)) &&
        Zones.Select(z => z.Name).Distinct(StringComparer.Ordinal).Count() == Zones.Length &&
        Gpus is { Length: <= 64 } && Gpus.All(g => g is not null && Name(g.AdapterId) && GpuName(g.Name) &&
            Temperature(g.TemperatureCelsius) && Temperature(g.WarningCelsius) && Temperature(g.MaxCelsius) &&
            Nonnegative(g.FanRpm) && Nonnegative(g.PowerPercent)) &&
        Gpus.Select(g => g.AdapterId).Distinct(StringComparer.Ordinal).Count() == Gpus.Length;

    internal static void Write(BinaryWriter writer, ResourceSample resources)
    {
        if (resources.CpuTemperature is null && resources.CpuPerformanceLimitPercent is null && resources.CpuPerformanceLimitFlags is null &&
            resources.ThermalZones is { Count: 0 } && resources.GpuThermals is { Count: 0 } &&
            resources.DiskTemperatureCelsius is null && resources.SecondaryDiskTemperatureCelsius is null &&
            resources.GpuGraphicsClockMhz is null)
        {
            writer.Write(0);
            return;
        }
        var value = From(resources);
        if (!value.Valid()) throw new InvalidDataException("Invalid historical sensors.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 65536) throw new InvalidDataException("Historical sensors exceed size limit.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    internal static ResourceSample Read(BinaryReader reader, ResourceSample resources)
    {
        var length = reader.ReadInt32();
        if (length == 0) return resources;
        if (length is < 1 or > 65536) throw new InvalidDataException("Invalid historical sensor size.");
        var value = JsonSerializer.Deserialize<ThermalHistory>(reader.ReadBytes(length));
        if (value is null || !value.Valid()) throw new InvalidDataException("Invalid historical sensors.");
        return value.Apply(resources);
    }
}

/// <summary>Shared projection for charts, recording analysis and rules; temperatures are always absolute Celsius.</summary>
public static class ThermalMetrics
{
    public static bool IsThermal(CaptureMetric metric) => metric is CaptureMetric.CpuTemperature or CaptureMetric.GpuTemperature or
        CaptureMetric.PrimaryDiskTemperature or CaptureMetric.SecondaryDiskTemperature or CaptureMetric.CpuPerformanceLimited or CaptureMetric.GpuThermalLimited;
    public static bool IsTemperature(CaptureMetric metric) => IsThermal(metric) && metric is not (CaptureMetric.CpuPerformanceLimited or CaptureMetric.GpuThermalLimited);
    public static GpuThermalReading? Gpu(ResourceSample r)
    {
        for (var index = 0; index < r.GpuThermals.Count; index++)
            if (r.GpuThermals[index].AdapterId == r.GpuId) return r.GpuThermals[index];
        return null;
    }
    public static double? Temperature(double? value) => ThermalHistory.IsTemperature(value) ? value : null;
    public static string Identity(CaptureMetric metric, ResourceSample r) => metric switch
    {
        CaptureMetric.CpuTemperature => r.CpuTemperature is { } cpu ? $"{cpu.Generation}/{cpu.HardwareId}/{cpu.SensorId}" : "",
        CaptureMetric.GpuTemperature or CaptureMetric.GpuThermalLimited => r.GpuId,
        CaptureMetric.PrimaryDiskTemperature => r.DiskName,
        CaptureMetric.SecondaryDiskTemperature => r.SecondaryDiskName,
        _ => ""
    };
    public static double? Value(CaptureMetric metric, ResourceSample r) => metric switch
    {
        CaptureMetric.CpuTemperature => Temperature(r.CpuTemperature?.Celsius),
        CaptureMetric.GpuTemperature => Temperature(Gpu(r)?.TemperatureCelsius),
        CaptureMetric.PrimaryDiskTemperature => r.DiskName.Length > 0 ? Temperature(r.DiskTemperatureCelsius) : null,
        CaptureMetric.SecondaryDiskTemperature => r.SecondaryDiskName.Length > 0 ? Temperature(r.SecondaryDiskTemperatureCelsius) : null,
        CaptureMetric.CpuPerformanceLimited => r.CpuPerformanceLimitPercent is >= 0 and <= 100 and var limit ? (limit < 100 ? 1 : 0) : null,
        CaptureMetric.GpuThermalLimited => Gpu(r) is { TemperatureCelsius: { } temperature, WarningCelsius: { } warning } &&
            ThermalHistory.IsTemperature(temperature) && ThermalHistory.IsTemperature(warning) ? (temperature >= warning ? 1 : 0) : null,
        _ => null
    };
}
