using MonCore.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private readonly record struct TemperaturePeak(double Value, MonitorSample Sample);

    private TimeSpan? CpuPerformanceLimitDuration(MonitorSample? latest)
    {
        return CpuPerformanceLimited(Session.History.Samples, latest, out var duration) ? duration : null;
    }

    internal static bool CpuPerformanceLimited(IReadOnlyList<MonitorSample> samples, MonitorSample? latest,
        out TimeSpan duration)
    {
        duration = default;
        if (latest?.Resources.CpuPerformanceLimitPercent is not (>= 0 and < 100) || latest.IsGap)
            return false;
        var first = latest;
        var newer = latest;
        var count = 0;
        for (var i = samples.Count - 1; i >= 0; i--)
        {
            var sample = samples[i];
            if (sample.IsGap || sample.Resources.CpuPerformanceLimitPercent is not (>= 0 and < 100) ||
                count > 0 && GaugeSampleAge(sample, newer) is not (> 0 and <= 5)) break;
            first = sample;
            newer = sample;
            count++;
        }

        if (count < 2) return false;
        duration = TimeSpan.FromSeconds(GaugeSampleAge(first, latest));
        return true;
    }

    private TemperaturePeak? ThermalZonePeak(MonitorSample latest, string name)
    {
        TemperaturePeak? peak = null;
        foreach (var sample in Session.History.Samples.Reverse())
        {
            if (!InTemperatureWindow(sample, latest)) break;
            if (sample.IsGap) break;
            var zone = sample.Resources.ThermalZones.FirstOrDefault(item => item.Name == name);
            if (zone is not null && (peak is null || zone.Celsius > peak.Value.Value))
                peak = new TemperaturePeak(zone.Celsius, sample);
        }

        return peak;
    }

    private TemperaturePeak? GpuThermalPeak(MonitorSample latest, string adapterId)
    {
        TemperaturePeak? peak = null;
        foreach (var sample in Session.History.Samples.Reverse())
        {
            if (!InTemperatureWindow(sample, latest)) break;
            if (sample.IsGap) break;
            var temperature = sample.Resources.GpuThermals.FirstOrDefault(item => item.AdapterId == adapterId)
                ?.TemperatureCelsius;
            if (temperature is { } value && (peak is null || value > peak.Value.Value))
                peak = new TemperaturePeak(value, sample);
        }

        return peak;
    }

    private static bool InTemperatureWindow(MonitorSample sample, MonitorSample latest)
    {
        return GaugeSampleAge(sample, latest) is var age && age >= 0 && age <= 600;
    }
}
