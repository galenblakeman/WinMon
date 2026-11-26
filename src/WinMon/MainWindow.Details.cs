using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private readonly Dictionary<MetricReadout, MetricDetails> _metricDetails = [];
    private MetricReadout? _openMetricDetails;

    private static string DetailText(string key)
    {
        return Strings.ResourceManager.GetString(key, Strings.Culture ?? CultureInfo.CurrentUICulture) ?? key;
    }

    private void InitializeMetricDetails()
    {
        foreach (var metric in Metrics)
        {
            var details = new MetricDetails { Accent = metric.ValueLabel.Foreground };
            _metricDetails.Add(metric, details);
            var count = metric == CpuMetric ? 8 : metric == MemoryMetric ? 11 : metric == NetworkMetric ? 5 :
                metric == GpuMetric ? 10 : 6;
            for (var i = 0; i < count; i++) details.Rows.Add(new DetailRow { Value = Strings.MissingReading });
            metric.ToolTip = details;
            metric.Focusable = true;
            metric.SetResourceReference(FocusVisualStyleProperty, "MetricTileFocusVisual");
            ToolTipService.SetShowsToolTipOnKeyboardFocus(metric, true);
            metric.Toggle.ToolTip = details;
            metric.ScaleLabel.ToolTip = details;
            ToolTipService.SetShowsToolTipOnKeyboardFocus(metric.Toggle, true);
            metric.ToolTipOpening += (_, _) => OpenDetails(metric);
            metric.Toggle.ToolTipOpening += (_, _) => OpenDetails(metric);
            metric.ToolTipClosing += (_, _) => CloseDetails(metric);
            metric.Toggle.ToolTipClosing += (_, _) => CloseDetails(metric);
        }

        RefreshMetricDetailsLanguage();
    }

    internal void OpenDetails(MetricReadout metric)
    {
        _openMetricDetails = metric;
        UpdateMetricDetails(Session.History.Latest);
    }

    internal void CloseDetails(MetricReadout metric)
    {
        if (_openMetricDetails == metric) _openMetricDetails = null;
    }

    private void RefreshMetricDetailsLanguage()
    {
        foreach (var (metric, details) in _metricDetails)
        {
            details.Title = metric.Label;
            details.Live = DetailText("DetailLive");
            details.Definition = DetailText(metric == CpuMetric ? "DetailCpuDefinition" :
                metric == MemoryMetric ? "DetailMemoryDefinition" :
                metric == NetworkMetric ? "DetailNetworkDefinition" :
                metric == GpuMetric ? "DetailGpuDefinition" : "DetailDiskDefinition");
            var keys = metric == CpuMetric
                ? new[]
                {
                    "DetailClockSpeed", "DetailUserKernel", "DetailProcessesThreads", "DetailHandles",
                    "DetailBusiestProcess", "DetailUpTime", "DetailPerformanceLimit", "DetailSystemThermalZones"
                }
                : metric == MemoryMetric
                    ?
                    [
                        "DetailInUseTotal", "DetailAvailable", "DetailCommittedLimit", "DetailSystemCache",
                        "DetailPagingReads", "DetailStandby", "DetailModified", "DetailFree",
                        "DetailCompressionStore", "DetailPagedNonPaged", "DetailLargestWorkingSet"
                    ]
                    : metric == NetworkMetric
                        ?
                        [
                            "DetailAdapter", "DetailLinkSpeed", "DetailUtilization", "DetailReceiveSend",
                            "DetailReceivedSent"
                        ]
                        : metric == GpuMetric
                            ?
                            [
                                "DetailAdapterName", "DetailBusiestEngine", "DetailDedicatedMemory",
                                "DetailSharedMemory", "DetailBusiestProcess", "DetailTemperature", "DetailFanSpeed",
                                "DetailPower", "DetailTemperaturePeak", "DetailOtherGpuAdapters"
                            ]
                            :
                            [
                                "DetailActiveTime", "DetailRead", "DetailWrite", "DetailAverageResponse",
                                "DetailQueueLength", "DetailFreeSpace"
                            ];
            for (var i = 0; i < keys.Length; i++) details.Rows[i].Label = DetailText(keys[i]);
            AutomationProperties.SetHelpText(metric.Toggle, details.Definition);
            AutomationProperties.SetHelpText(metric, details.Definition);
        }
    }

    private void UpdateMetricDetails(MonitorSample? latest)
    {
        var metric = _openMetricDetails;
        if (metric is null) return;
        var resource = _samplingError == SamplingError.ReadingsUnavailable ? null : latest?.Resources;
        var details = _metricDetails[metric];
        details.Title = metric.Label;
        details.Accent = metric.ValueLabel.Foreground;
        details.Peak = GaugePeakTooltip(metric, latest);
        AutomationProperties.SetHelpText(metric.Toggle, details.Definition + "\n" + details.Peak);
        AutomationProperties.SetHelpText(metric, details.Definition + "\n" + details.Peak);
        var rows = details.Rows;
        if (metric == CpuMetric)
        {
            rows[0].Value = resource?.CpuClockMhz is > 0
                ? F("{0:0.00} GHz", resource.CpuClockMhz.Value / 1000)
                : Dash;
            rows[1].Value = latest?.TotalCpuPercent is { } total && resource?.CpuKernelPercent is { } kernel
                ? F("{0:0.0}% / {1:0.0}%", Math.Max(0, total - kernel), kernel)
                : Dash;
            rows[2].Value = ProcessThreadCounts(resource);
            rows[3].Value = HandleCountText(resource);
            rows[4].Value = Busiest(CpuMetric, ProcessSort.Cpu);
            var upTime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            rows[5].Value = F("{0} d {1:00}:{2:00}", (int)upTime.TotalDays, upTime.Hours, upTime.Minutes);
            rows[6].Value = PerformanceLimitText(latest);
            rows[7].Value = ThermalZonesText(latest);
            rows[6].Visibility = resource?.CpuPerformanceLimitPercent is not null
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            rows[7].Visibility = resource?.ThermalZones.Count > 0
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
        else if (metric == MemoryMetric)
        {
            rows[0].Value = PairBytes(resource?.UsedMemoryBytes, resource?.TotalMemoryBytes);
            rows[1].Value = Bytes(resource?.AvailableMemoryBytes);
            rows[2].Value = PairBytes(resource?.CommittedBytes, resource?.CommitLimitBytes);
            rows[3].Value = Bytes(resource?.CachedBytes);
            rows[4].Value = resource?.PagingReadsPerSecond is { } reads ? F("{0:0.0}/s", reads) : Dash;
            rows[5].Value = Bytes(resource?.StandbyBytes);
            rows[6].Value = Bytes(resource?.ModifiedBytes);
            rows[7].Value = Bytes(resource?.FreeBytes);
            rows[8].Value = Bytes(resource?.CompressionStoreBytes);
            rows[9].Value = PairBytes(resource?.PagedPoolBytes, resource?.NonPagedPoolBytes);
            rows[10].Value = Busiest(MemoryMetric, ProcessSort.Memory);
        }
        else if (metric == DiskMetric)
        {
            var matches = resource?.DiskName == _preferences.PrimaryDrive;
            UpdateDiskDetails(metric, matches ? resource?.DiskActivePercent : null,
                matches ? resource?.DiskReadBytesPerSecond : null,
                matches ? resource?.DiskWriteBytesPerSecond : null,
                matches ? resource?.DiskResponseSeconds : null,
                matches ? resource?.DiskQueueLength : null,
                matches ? resource?.DiskFreeBytes : null,
                matches ? resource?.DiskTotalBytes : null);
        }
        else if (metric == SecondaryDiskMetric)
        {
            var matches = resource?.SecondaryDiskName == _preferences.SecondaryDrive;
            UpdateDiskDetails(metric, matches ? resource?.SecondaryDiskActivePercent : null,
                matches ? resource?.SecondaryDiskReadBytesPerSecond : null,
                matches ? resource?.SecondaryDiskWriteBytesPerSecond : null,
                matches ? resource?.SecondaryDiskResponseSeconds : null,
                matches ? resource?.SecondaryDiskQueueLength : null,
                matches ? resource?.SecondaryDiskFreeBytes : null,
                matches ? resource?.SecondaryDiskTotalBytes : null);
        }
        else if (metric == NetworkMetric)
        {
            rows[0].Value = string.IsNullOrEmpty(resource?.NetworkName) ? Dash : resource.NetworkName;
            rows[1].Value = resource?.LinkBitsPerSecond is { } speed ? RateText.LinkSpeed(speed) : Dash;
            rows[2].Value = Percent(resource?.NetworkPercent);
            rows[3].Value = resource is { ReceiveBytesPerSecond: { } rx, SendBytesPerSecond: { } tx }
                ? F("↓{0} / ↑{1}", RateText.Format(rx * 8), RateText.Format(tx * 8))
                : Dash;
            rows[4].Value = resource is { ReceivedBytesSinceStart: { } received, SentBytesSinceStart: { } sent }
                ? F("↓{0} / ↑{1}", Bytes(received), Bytes(sent))
                : Dash;
        }
        else if (metric == GpuMetric)
        {
            rows[0].Value = string.IsNullOrEmpty(resource?.GpuName) ? Dash : resource.GpuName;
            rows[1].Value = string.IsNullOrEmpty(resource?.GpuEngine) ? Dash : resource.GpuEngine;
            rows[2].Value = PairBytes(resource?.GpuMemoryUsedBytes, resource?.GpuMemoryTotalBytes);
            rows[3].Value = Bytes(resource?.GpuSharedMemoryUsedBytes);
            rows[4].Value = Busiest(GpuMetric, ProcessSort.Gpu);
            var thermal = PairedGpuThermal(resource);
            rows[5].Value = GpuTemperatureText(thermal);
            rows[6].Value = thermal?.FanRpm is { } fan ? Strings.Format(Strings.TemperatureFanRpmValue, fan) : Dash;
            rows[7].Value = Percent(thermal?.PowerPercent);
            rows[8].Value = GpuTemperaturePeakText(latest, thermal);
            rows[9].Value = OtherGpuTemperatures(resource, thermal);
            var pairedVisible = thermal is { TemperatureCelsius: not null } or { FanRpm: not null } or { PowerPercent: not null };
            rows[5].Visibility = thermal?.TemperatureCelsius is not null
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            rows[6].Visibility = thermal?.FanRpm is not null
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            rows[7].Visibility = thermal?.PowerPercent is not null
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            rows[8].Visibility = pairedVisible && latest is not null && GpuThermalPeak(latest, thermal!.AdapterId) is not null
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            rows[9].Visibility = resource?.GpuThermals.Any(item => item.AdapterId != thermal?.AdapterId &&
                item.TemperatureCelsius is not null) == true || pairedVisible
                ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }
    }

    private string PerformanceLimitText(MonitorSample? latest)
    {
        var duration = CpuPerformanceLimitDuration(latest);
        var resource = latest?.Resources;
        if (resource?.CpuPerformanceLimitPercent is not { } value) return Dash;
        return duration is { } elapsed && resource.CpuPerformanceLimitFlags is { } flags
            ? Strings.Format(Strings.TemperatureLimitedTo, value, elapsed, flags)
            : Percent(value);
    }

    private string ThermalZonesText(MonitorSample? latest)
    {
        var zones = latest?.Resources.ThermalZones;
        if (zones is not { Count: > 0 }) return Dash;
        return string.Join("\n", zones.OrderByDescending(zone => zone.Celsius).Select(zone =>
        {
            var peak = ThermalZonePeak(latest!, zone.Name);
            var detail = zone.Name + ": " + GaugeReadoutText.Temperature(zone.Celsius, _preferences.TemperatureUnit);
            if (peak is { } item) detail += " · " + Strings.Format(Strings.TemperaturePeakAt,
                GaugeReadoutText.Temperature(item.Value, _preferences.TemperatureUnit), item.Sample.Timestamp.ToLocalTime());
            if (zone.Unchanging) detail += " · " + Strings.DetailUnchangedFor10Minutes;
            return detail;
        })) + "\n" + Strings.DetailAcpiThermalZoneNotCpuDie;
    }

    private string GpuTemperatureText(GpuThermalReading? thermal)
    {
        if (thermal?.TemperatureCelsius is not { } value) return Dash;
        var temperature = GaugeReadoutText.Temperature(value, _preferences.TemperatureUnit);
        if (thermal.WarningCelsius is { } warning)
            return Strings.Format(Strings.TemperatureAtThreshold, temperature,
                GaugeReadoutText.Temperature(warning, _preferences.TemperatureUnit));
        return thermal.MaxCelsius is { } maximum
            ? Strings.Format(Strings.TemperatureMaxFormat, temperature,
                GaugeReadoutText.Temperature(maximum, _preferences.TemperatureUnit))
            : temperature;
    }

    private string GpuTemperaturePeakText(MonitorSample? latest, GpuThermalReading? thermal)
    {
        if (thermal is null || latest is null) return Dash;
        var peak = GpuThermalPeak(latest, thermal.AdapterId);
        return peak is { } item ? Strings.Format(Strings.TemperaturePeakAt,
            GaugeReadoutText.Temperature(item.Value, _preferences.TemperatureUnit), item.Sample.Timestamp.ToLocalTime()) : Dash;
    }

    private string OtherGpuTemperatures(ResourceSample? resource, GpuThermalReading? paired)
    {
        var others = resource?.GpuThermals.Where(reading => reading.AdapterId != paired?.AdapterId &&
                                                             reading.TemperatureCelsius is not null).ToArray();
        if (others is not { Length: > 0 }) return paired is null ? Dash : Strings.DetailSameAdapterAsUtilization;
        var text = string.Join("\n", others.Select(reading => reading.Name + ": " +
            GaugeReadoutText.Temperature(reading.TemperatureCelsius, _preferences.TemperatureUnit)));
        return paired is null ? text : text + "\n" + Strings.DetailSameAdapterAsUtilization;
    }

    private void UpdateDiskDetails(MetricReadout metric, double? active, double? read, double? write,
        double? response, double? queue, ulong? free, ulong? total)
    {
        var rows = _metricDetails[metric].Rows;
        rows[0].Value = Percent(active);
        rows[1].Value = read is { } r ? F("{0:0.0} MB/s", r / 1_000_000) : Dash;
        rows[2].Value = write is { } w ? F("{0:0.0} MB/s", w / 1_000_000) : Dash;
        rows[3].Value = response is { } s ? F("{0:0.0} ms", s * 1000) : Dash;
        rows[4].Value = queue is { } q ? F("{0:0.0}", q) : Dash;
        rows[5].Value = PairBytes(free, total);
    }

    private string Busiest(MetricReadout metric, ProcessSort sort)
    {
        if (_openMetricDetails != metric || Session.LatestFull is null) return Dash;
        var history = Session.History.Samples;
        var process = BusiestLive(history, Session.LatestFull, sort);
        if (process is null) return Dash;
        var value = sort switch
        {
            ProcessSort.Memory => Bytes(process.WorkingSetBytes),
            ProcessSort.Gpu => Percent(process.GpuPercent),
            _ => Percent(process.CpuPercent)
        };
        return F("{0}  {1}", process.Name, value);
    }

    internal static ProcessUsage? BusiestLive(IReadOnlyList<MonitorSample> history, MonitorSample? latestFull,
        ProcessSort sort)
    {
        if (history.Count == 0 || latestFull is null) return null;
        var live = new SampleList(history, history.Count, latestFull);
        return ProcessRanking.Rank(live, sort: sort, grouped: false, includeInactive: false)
            .Processes.FirstOrDefault();
    }

    private static string Dash => Strings.MissingReading;

    internal static string ProcessThreadCounts(ResourceSample? resource)
    {
        return resource is { ProcessCount: { } processes, ThreadCount: { } threads }
            ? F("{0:N0} · {1:N0}", processes, threads)
            : Dash;
    }

    internal static string HandleCountText(ResourceSample? resource)
    {
        return resource?.HandleCount is { } handles ? F("{0:N0}", handles) : Dash;
    }

    /// <summary>A count short enough for the activity tab strip: 4,812, 152K, 1.2M.</summary>
    internal static string CompactCount(uint? value)
    {
        return value switch
        {
            null => Dash,
            < 100_000 => F("{0:N0}", value),
            < 999_500 => F("{0:0}K", value / 1_000d),
            _ => F("{0:0.0}M", value / 1_000_000d)
        };
    }

    internal static string ExactCount(uint? value)
    {
        return value is { } count ? F("{0:N0}", count) : Dash;
    }

    private static string F(string format, params object[] values)
    {
        return Strings.Format(format, values);
    }

    private static string Bytes(ulong? value)
    {
        return value is { } bytes
            ? bytes >= 1_073_741_824
                ? F("{0:0.0} GiB", bytes / 1_073_741_824d)
                : F("{0:0.0} MiB", bytes / 1_048_576d)
            : Dash;
    }

    private static string PairBytes(ulong? first, ulong? second)
    {
        return first is { } a && second is { } b
            ? F("{0} / {1}", Bytes(a), Bytes(b))
            : Dash;
    }
}
