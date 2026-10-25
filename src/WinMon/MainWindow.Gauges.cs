using System.Diagnostics;
using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon;

public partial class MainWindow
{
    private void UpdateMetricGauges(IReadOnlyList<MonitorSample> samples, MonitorSample? latest)
    {
        if (_samplingError == SamplingError.ReadingsUnavailable || latest is null)
        {
            foreach (var metric in Metrics) ShowGaugeInActiveView(metric, null, null, 100);
            return;
        }

        var latestResources = latest.Resources;
        var cpu = Valid(latest.TotalCpuPercent);
        var memory = Valid(latestResources.MemoryPercent);
        var disk = latestResources.DiskName == _preferences.PrimaryDrive
            ? Valid(latestResources.DiskActivePercent) : null;
        var secondary = _preferences.SecondaryDrive is { } secondaryName &&
                        latestResources.SecondaryDiskName == secondaryName
            ? Valid(latestResources.SecondaryDiskActivePercent) : null;
        var network = Valid(latestResources.ReceiveBytesPerSecond * 8);
        var gpu = Valid(latestResources.GpuPercent);
        var peaks = TwoMinuteGaugePeaks(samples, latest, _preferences.PrimaryDrive,
            _preferences.SecondaryDrive);

        ShowGaugeInActiveView(CpuMetric, cpu, peaks.Cpu, 100);
        ShowGaugeInActiveView(MemoryMetric, memory, peaks.Memory, 100);
        ShowGaugeInActiveView(DiskMetric, disk, peaks.Disk, 100);
        ShowGaugeInActiveView(SecondaryDiskMetric, secondary, peaks.SecondaryDisk, 100);
        var networkScale = CpuGraph.NetworkScale(samples.Where(sample => InGaugeWindow(sample, latest)));
        ShowGaugeInActiveView(NetworkMetric, network, peaks.Network, networkScale, RateText.Short(networkScale));
        ShowGaugeInActiveView(GpuMetric, gpu, peaks.Gpu, 100);
    }

    private void ShowGaugeInActiveView(MetricReadout source, double? current, double? peak, double maximum,
        string? scale = null)
    {
        if (CompactMode)
            _compactMetrics[source].ShowGauge(current, peak, maximum, scale);
        else
            source.ShowGauge(current, peak, maximum, scale);
    }

    /// <summary>Draw every readout's gauge in the chosen style; the peak legend goes with the gauges.</summary>
    internal void ApplyGaugeStyle()
    {
        foreach (var metric in Metrics)
        {
            metric.GaugeStyle = _preferences.GaugeStyle;
            _compactMetrics[metric].GaugeStyle = _preferences.GaugeStyle;
        }
        PeakLegend.Visibility = _preferences.GaugeStyle == GaugeStyle.None ? Visibility.Collapsed : Visibility.Visible;
    }

    internal static (double? Cpu, double? Memory, double? Disk, double? SecondaryDisk, double? Network, double? Gpu)
        TwoMinuteGaugePeaks(IReadOnlyList<MonitorSample> samples, MonitorSample latest,
            string primaryDrive, string? secondaryDrive)
    {
        double? cpuPeak = null, memoryPeak = null, diskPeak = null;
        double? secondaryPeak = null, networkPeak = null, gpuPeak = null;

        for (var i = samples.Count - 1; i >= 0; i--)
        {
            var sample = samples[i];
            if (!InGaugeWindow(sample, latest)) break;
            if (sample.IsGap) continue;
            var resource = sample.Resources;
            cpuPeak = Max(cpuPeak, Valid(sample.TotalCpuPercent));
            memoryPeak = Max(memoryPeak, Valid(resource.MemoryPercent));
            if (resource.DiskName == primaryDrive)
                diskPeak = Max(diskPeak, Valid(resource.DiskActivePercent));
            if (secondaryDrive is { } drive && resource.SecondaryDiskName == drive)
                secondaryPeak = Max(secondaryPeak, Valid(resource.SecondaryDiskActivePercent));
            networkPeak = Max(networkPeak, Valid(resource.ReceiveBytesPerSecond * 8));
            gpuPeak = Max(gpuPeak, Valid(resource.GpuPercent));
        }

        return (cpuPeak, memoryPeak, diskPeak, secondaryPeak, networkPeak, gpuPeak);
    }

    private static double? Valid(double? value) => value is >= 0 && double.IsFinite(value.Value) ? value : null;

    private static bool InGaugeWindow(MonitorSample sample, MonitorSample latest)
    {
        var seconds = sample.MonotonicTimestamp > 0 && latest.MonotonicTimestamp > 0
            ? (latest.MonotonicTimestamp - sample.MonotonicTimestamp) / (double)Stopwatch.Frequency
            : (latest.Timestamp - sample.Timestamp).TotalSeconds;
        return seconds is >= 0 and <= MonitorPreferences.ShortRangeSeconds;
    }

    private static double? Max(double? left, double? right) => right is { } value
        ? left is { } previous ? Math.Max(previous, value) : value : left;
}
