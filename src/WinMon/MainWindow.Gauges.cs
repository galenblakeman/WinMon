using System.Diagnostics;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    internal readonly record struct GaugePeak(double? Value, MonitorSample? Sample);

    private (GaugePeak Cpu, GaugePeak Memory, GaugePeak Disk, GaugePeak SecondaryDisk,
        GaugePeak Network, GaugePeak Gpu) _gaugePeaks;

    private double _networkGaugeScale;

    private void UpdateMetricGauges(IReadOnlyList<MonitorSample> samples, MonitorSample? latest)
    {
        if (_samplingError == SamplingError.ReadingsUnavailable || latest is null)
        {
            _gaugePeaks = default;
            foreach (var metric in Metrics) ShowGaugeInActiveView(metric, null, default, 100);
            return;
        }

        var resource = latest.Resources;
        _gaugePeaks = GaugePeaks(samples, latest, _preferences.PrimaryDrive,
            _preferences.SecondaryDrive, _preferences.GaugePeakWindowSeconds);
        ShowGaugeInActiveView(CpuMetric, Valid(latest.TotalCpuPercent), _gaugePeaks.Cpu, 100);
        ShowGaugeInActiveView(MemoryMetric, Valid(resource.MemoryPercent), _gaugePeaks.Memory, 100);
        ShowGaugeInActiveView(DiskMetric, resource.DiskName == _preferences.PrimaryDrive
            ? Valid(resource.DiskActivePercent)
            : null, _gaugePeaks.Disk, 100);
        ShowGaugeInActiveView(SecondaryDiskMetric, _preferences.SecondaryDrive is { } drive &&
                                                   resource.SecondaryDiskName == drive
                ? Valid(resource.SecondaryDiskActivePercent)
                : null,
            _gaugePeaks.SecondaryDisk, 100);
        var networkScale = CpuGraph.NetworkScale(samples.Where(sample =>
            InGaugeWindow(sample, latest, _preferences.GaugePeakWindowSeconds) &&
            sample.Resources.NetworkId == resource.NetworkId));
        _networkGaugeScale = networkScale;
        ShowGaugeInActiveView(NetworkMetric, Valid(resource.ReceiveBytesPerSecond * 8),
            _gaugePeaks.Network, networkScale);
        ShowGaugeInActiveView(GpuMetric, Valid(resource.GpuPercent), _gaugePeaks.Gpu, 100);
    }

    private void ShowGaugeInActiveView(MetricReadout source, double? current, GaugePeak peak, double maximum)
    {
        var showCorner = _preferences.GaugeStyle is GaugeStyle.Bowl or GaugeStyle.Needle or GaugeStyle.SegmentedArc;
        string? full = null, shortText = null;
        if (showCorner)
            if (peak.Value is { } value)
            {
                if (source == NetworkMetric)
                {
                    full = Strings.Format(Strings.GaugePeakValue, RateText.Format(value));
                    shortText = RateText.Short(value);
                }
                else
                {
                    var native = GaugePeakNativeValue(source, peak.Sample?.Resources);
                    full = native is null
                        ? Strings.Format(Strings.GaugePeak, Math.Clamp(value, 0, 100))
                        : Strings.Format(Strings.GaugePeakValue, native);
                    shortText = native ?? Strings.Format(Strings.GaugePeakShort, Math.Clamp(value, 0, 100));
                }
            }

        var target = CompactMode ? _compactMetrics[source] : source;
        target.ShowGauge(current, peak.Value, maximum, full, shortText, true);
    }

    private string? GaugePeakNativeValue(MetricReadout metric, ResourceSample? resource)
    {
        if (resource is null) return null;
        if (metric == MemoryMetric && resource.UsedMemoryBytes is { } memory)
            return TableMetricText.Memory(memory);
        if (metric == GpuMetric && resource.GpuMemoryUsedBytes is { } gpuMemory)
            return TableMetricText.Memory(gpuMemory);
        var rate = metric == DiskMetric ? resource.DiskBytesPerSecond
            : metric == SecondaryDiskMetric ? resource.SecondaryDiskBytesPerSecond : null;
        return Valid(rate) is { } bytes ? TableMetricText.IoRate(bytes) : null;
    }

    private GaugePeak PeakFor(MetricReadout metric)
    {
        return metric == CpuMetric ? _gaugePeaks.Cpu
            : metric == MemoryMetric ? _gaugePeaks.Memory
            : metric == DiskMetric ? _gaugePeaks.Disk
            : metric == SecondaryDiskMetric ? _gaugePeaks.SecondaryDisk
            : metric == NetworkMetric ? _gaugePeaks.Network : _gaugePeaks.Gpu;
    }

    private string GaugePeakTooltip(MetricReadout metric, MonitorSample? latest)
    {
        var heading = Strings.Format(Strings.GaugePeakOverWindow, _preferences.GaugePeakWindowSeconds / 60);
        var peak = PeakFor(metric);
        var current = CompactMode ? _compactMetrics[metric] : metric;
        if (latest is null || peak.Sample is not { } sample || peak.Value is not { } value ||
            current.Gauge.Level is null)
            return heading + "\n" + Strings.MissingReading;
        var measurement = metric == NetworkMetric
            ? Strings.GaugePeakDownload + ": " + RateText.Format(value)
            : metric.Label + ": " + Percent(value);
        if (metric == NetworkMetric)
            measurement += "\n" + Strings.Format(Strings.NetworkScale0, RateText.Format(_networkGaugeScale));
        if (GaugePeakNativeValue(metric, sample.Resources) is { } native)
        {
            var name = metric == MemoryMetric ? Strings.GaugePeakRamUsed
                : metric == GpuMetric ? Strings.GaugePeakGpuMemoryUsed : Strings.GaugePeakDiskThroughput;
            measurement += "\n" + Strings.Format(Strings.GaugePeakNativeAtPeak, name, native);
        }

        var age = Math.Max(0, (int)GaugeSampleAge(sample, latest));
        return heading + "\n" + measurement + "\n" +
               Strings.Format(Strings.GaugePeakAtTime, sample.Timestamp.ToLocalTime(), age);
    }

    /// <summary>Draw every readout's gauge in the chosen style.</summary>
    internal void ApplyGaugeStyle()
    {
        foreach (var metric in Metrics)
        {
            metric.GaugeStyle = _preferences.GaugeStyle;
            _compactMetrics[metric].GaugeStyle = _preferences.GaugeStyle;
        }
    }

    internal static (GaugePeak Cpu, GaugePeak Memory, GaugePeak Disk, GaugePeak SecondaryDisk,
        GaugePeak Network, GaugePeak Gpu) GaugePeaks(IReadOnlyList<MonitorSample> samples, MonitorSample latest,
            string primaryDrive, string? secondaryDrive, int windowSeconds = 120)
    {
        GaugePeak cpu = default,
            memory = default,
            disk = default,
            secondary = default,
            network = default,
            gpu = default;
        for (var i = samples.Count - 1; i >= 0; i--)
        {
            var sample = samples[i];
            if (!InGaugeWindow(sample, latest, windowSeconds)) break;
            if (sample.IsGap) continue;
            var resource = sample.Resources;
            KeepPeak(ref cpu, sample.TotalCpuPercent, sample);
            KeepPeak(ref memory, resource.MemoryPercent, sample);
            if (resource.DiskName == primaryDrive) KeepPeak(ref disk, resource.DiskActivePercent, sample);
            if (secondaryDrive is { } drive && resource.SecondaryDiskName == drive)
                KeepPeak(ref secondary, resource.SecondaryDiskActivePercent, sample);
            if (resource.NetworkId == latest.Resources.NetworkId)
                KeepPeak(ref network, resource.ReceiveBytesPerSecond * 8, sample);
            KeepPeak(ref gpu, resource.GpuPercent, sample);
        }

        return (cpu, memory, disk, secondary, network, gpu);
    }

    private static void KeepPeak(ref GaugePeak peak, double? value, MonitorSample sample)
    {
        // Walking newest first keeps the most recent occurrence when peaks tie.
        if (Valid(value) is { } valid && (peak.Value is null || valid > peak.Value))
            peak = new GaugePeak(valid, sample);
    }

    private static double? Valid(double? value)
    {
        return value is >= 0 && double.IsFinite(value.Value) ? value : null;
    }

    private static double GaugeSampleAge(MonitorSample sample, MonitorSample latest)
    {
        return sample.MonotonicTimestamp > 0 && latest.MonotonicTimestamp > 0
            ? (latest.MonotonicTimestamp - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency
            : (latest.Timestamp - sample.Timestamp).TotalSeconds;
    }

    private static bool InGaugeWindow(MonitorSample sample, MonitorSample latest, int seconds)
    {
        return GaugeSampleAge(sample, latest) is var age && age >= 0 && age <= seconds;
    }
}