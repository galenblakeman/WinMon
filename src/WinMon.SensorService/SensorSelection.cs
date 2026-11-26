namespace WinMon.SensorService;

internal static class SensorSelection
{
    internal static double? Normalize(float? value) => value is >= 1 and <= 150 ? value : null;

    internal static double? FreshCelsius(float? value, float? minimum, float? maximum)
    {
        var normalized = Normalize(value);
        return normalized is not null && minimum is { } min && maximum is { } max && min == value && max == value
            ? normalized
            : null;
    }

    internal static SensorReading? SelectPackageCandidate(IEnumerable<SensorReading> readings)
    {
        var all = readings.ToArray();
        if (all.Select(reading => reading.HardwareId).Distinct(StringComparer.Ordinal).Count() != 1) return null;
        var valid = all.Where(reading => reading.Celsius is >= 1 and <= 150).ToArray();
        foreach (var name in new[] { "CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "Core (Tctl)" })
        {
            var matches = valid.Where(reading => reading.SensorName.Equals(name, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) return null;
        }

        return null;
    }

    internal static SensorReading? SelectFixedCandidate(IEnumerable<SensorReading> readings, string hardwareId,
        string sensorId, string sensorName) => readings.SingleOrDefault(reading =>
        reading.HardwareId == hardwareId && reading.SensorId == sensorId && reading.SensorName == sensorName);
}
