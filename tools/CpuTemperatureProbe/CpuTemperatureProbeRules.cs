readonly record struct CpuTemperatureReading(string HardwareId, string HardwareName, string SensorId,
    string SensorName, double? Celsius);

static class CpuTemperatureRules
{
    public static double? Normalize(float? value) => value is >= 1 and <= 150 ? value : null;

    // Preserve the library's meaning. Never choose the hottest core or relabel Tctl as Tdie.
    // Multi-package systems require an explicit package choice before a single gauge can use one.
    public static CpuTemperatureReading? SelectPackageCandidate(IEnumerable<CpuTemperatureReading> readings)
    {
        var valid = readings.Where(reading => reading.Celsius is >= 1 and <= 150).ToArray();
        if (valid.Select(reading => reading.HardwareId).Distinct(StringComparer.Ordinal).Count() != 1) return null;
        foreach (var name in new[] { "CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "Core (Tctl)" })
        {
            var matches = valid.Where(reading => reading.SensorName.Equals(name, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) return null;
        }
        return null;
    }
}
