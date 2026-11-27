using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon;

public partial class MainWindow
{
    private void SetGaugeDetailsHeight(double height, bool settled)
    {
        var opening = FullGaugeDetails.DetailsHeight == 0 && height > 0;
        FullGaugeDetails.DetailsHeight = CompactGaugeDetails.DetailsHeight = height;
        if (CompactMode) SizeCompactWindow();
        else MinHeight = FullMinimumHeight + height;
        if (opening) RefreshPresentation();
        if (settled && CompactMode) EnsureVisible();
    }

    private void UpdateGaugeDetails(ResourceSample? resource, GpuThermalReading? gpuThermal)
    {
        var strip = CompactMode ? CompactGaugeDetails : FullGaugeDetails;
        if (strip.DetailsHeight == 0) return;

        var sensors = _preferences.TemperatureChartsEnabled;
        var cpuFan = sensors ? resource?.HardwareFans.FirstOrDefault(fan => fan.IsCpu) : null;
        var watts = sensors ? CpuPackageWatts(resource) : null;
        GaugeDetailsStrip.Column cpu = new(
            sensors
                ? new(Strings.GaugeDetailsFan, FanSpeed(cpuFan?.Rpm), Strings.DetailCpuFans)
                : new(Strings.GaugeDetailsProcesses, CompactCount(resource?.ProcessCount)),
            sensors
                ? new(Strings.DetailPower, watts is { } power ? F("{0:0} W", power) : Dash,
                    Strings.DetailCpuPackagePower)
                : new(Strings.Threads, CompactCount(resource?.ThreadCount)),
            new(Strings.GaugeDetailsKernel, Percent(resource?.CpuKernelPercent)));
        GaugeDetailsStrip.Column memory = new(
            new(Strings.GaugeDetailsCommit, Percent(resource is { CommittedBytes: { } committed, CommitLimitBytes: > 0 }
                ? 100d * committed / resource.CommitLimitBytes.Value : null), Strings.DetailCommittedLimit),
            new(Strings.DetailCached, Bytes(resource?.CachedBytes)),
            new(Strings.GaugeDetailsPaging, resource?.PagingReadsPerSecond is { } reads ? F("{0:0.0}/s", reads) : Dash,
                Strings.DetailPagingReads));

        var primary = resource?.DiskName == _preferences.PrimaryDrive ? resource : null;
        var disk = DiskGaugeDetails(primary?.DiskResponseSeconds, primary?.DiskQueueLength, primary?.DiskFreeBytes);
        GaugeDetailsStrip.Column? secondary = null;
        if (_preferences.SecondaryDrive is { } drive)
        {
            var observed = resource?.SecondaryDiskName == drive ? resource : null;
            secondary = DiskGaugeDetails(observed?.SecondaryDiskResponseSeconds, observed?.SecondaryDiskQueueLength,
                observed?.SecondaryDiskFreeBytes);
        }

        GaugeDetailsStrip.Column network = new(
            new(Strings.GaugeDetailsSession, Bytes(resource?.ReceivedBytesSinceStart), Strings.GaugeDetailsReceivedSession),
            new(Strings.GaugeDetailsLink, resource?.LinkBitsPerSecond is > 0 and { } link ? RateText.LinkSpeed(link) : Dash,
                Strings.DetailLinkSpeed),
            new(Strings.DetailAdapter, ReadingOrDash(resource?.NetworkName)));

        GaugeDetailsStrip.Reading vram = new(Strings.GaugeDetailsVram,
            PairBytes(resource?.GpuMemoryUsedBytes, resource?.GpuMemoryTotalBytes), Strings.DetailDedicatedMemory);
        GaugeDetailsStrip.Reading shared = new(Strings.GaugeDetailsShared, Bytes(resource?.GpuSharedMemoryUsedBytes),
            Strings.DetailSharedMemory);
        GaugeDetailsStrip.Column gpu = sensors
            ? new(new(Strings.GaugeDetailsFan, FanSpeed(gpuThermal?.FanRpm), Strings.DetailFanSpeed), vram, shared)
            : new(vram, shared, new(Strings.GaugeDetailsEngine, ReadingOrDash(resource?.GpuEngine), Strings.DetailBusiestEngine));
        strip.Update(cpu, memory, disk, secondary, network, gpu);
    }

    private double? CpuPackageWatts(ResourceSample? resource) =>
        resource is null ? null : _diagnosticMode ? CpuPackageWattsForFixture :
        _preferences.CpuTemperatureMonitoringEnabled ? _cpuTemperature?.PackageWatts : null;

    private static GaugeDetailsStrip.Column DiskGaugeDetails(double? response, double? queue, ulong? free) => new(
        new(Strings.GaugeDetailsLatency, response is { } seconds ? F("{0:0.0} ms", seconds * 1000) : Dash,
            Strings.DetailAverageResponse),
        new(Strings.GaugeDetailsQueue, queue is { } length ? F("{0:0.##}", length) : Dash, Strings.DetailQueueLength),
        new(Strings.GaugeDetailsFree, free is >= 1_099_511_627_776 ? F("{0:0.0} TiB", free.Value / 1_099_511_627_776d) : Bytes(free),
            Strings.DetailFreeSpace));

    private static string FanSpeed(double? rpm) =>
        rpm is { } speed ? Strings.Format(Strings.TemperatureFanRpmValue, speed) : Dash;

    private static string ReadingOrDash(string? text) => string.IsNullOrEmpty(text) ? Dash : text;
}
