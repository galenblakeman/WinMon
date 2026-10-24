using System.Globalization;
using System.Text;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Text for automatic captures: rule descriptions, report files and notifications.</summary>
internal static class CaptureReportText
{
    /// <summary>The resource color of a rule's marker, matching that resource's chart trace.</summary>
    internal static string BrushKey(CaptureMetric metric)
    {
        return metric switch
        {
            CaptureMetric.Memory => "MemoryAccent", CaptureMetric.PrimaryDisk => "DiskAccent",
            CaptureMetric.SecondaryDisk => "SecondaryDiskAccent", CaptureMetric.Network => "NetworkAccent",
            CaptureMetric.Gpu => "GpuAccent", _ => "CpuAccent"
        };
    }

    /// <summary>Disk and network process lists cannot be attributed to that drive or adapter.</summary>
    internal static bool AttributesProcesses(CaptureMetric metric)
    {
        return metric is not (CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk or CaptureMetric.Network);
    }

    internal static string MetricName(CaptureMetric metric, ResourceSample resources)
    {
        return metric switch
        {
            CaptureMetric.Cpu => Strings.CPU, CaptureMetric.Memory => Strings.Memory,
            CaptureMetric.PrimaryDisk => resources.DiskName.Length > 0
                ? Strings.Format(Strings.DiskLabel, resources.DiskName)
                : Strings.Disk,
            CaptureMetric.SecondaryDisk => resources.SecondaryDiskName.Length > 0
                ? Strings.Format(Strings.DiskLabel, resources.SecondaryDiskName)
                : Strings.SecondDrive,
            CaptureMetric.Network => Strings.Network, CaptureMetric.Gpu => Strings.GPU,
            _ => Strings.AutoCaptureAnyProcess
        };
    }

    internal static string Value(CaptureMetric metric, double value)
    {
        return metric switch
        {
            CaptureMetric.Network => string.Create(CultureInfo.CurrentCulture, $"{value:0.0} Mbps"),
            CaptureMetric.Process => Strings.Format(Strings.AutoCapturePercentOfCoreValue, value),
            _ => Strings.Format(Strings.PercentOneDecimal, value)
        };
    }

    /// <summary>"CPU above 90% for 10 s", or for a process rule, the process's name in place of the resource.</summary>
    internal static string Rule(CaptureFired fired)
    {
        var name = fired.Process is { } process
            ? DisplayName(process)
            : MetricName(fired.Rule.Metric, fired.Fired.Resources);
        return Strings.Format(Strings.AutoCaptureRuleText, name, Value(fired.Rule.Metric, fired.Rule.Threshold),
            fired.Rule.Seconds);
    }

    /// <summary>The report saved to the folder: the trigger header, then the Copy capture text of the peak.</summary>
    /// <param name="fired">The rule trigger and its peak reading.</param>
    /// <param name="id">The capture identifier written into the report header.</param>
    /// <param name="snapshot">The peak's <see cref="Snapshot"/>, shared with <see cref="Notification"/>.</param>
    internal static string Report(CaptureFired fired, Guid id, SnapshotReport snapshot)
    {
        var metric = fired.Rule.Metric;
        var text = new StringBuilder();
        text.AppendLine(CaptureReports.FirstLine(id));
        text.AppendLine(Strings.Format(Strings.AutoCaptureReportTitle, Rule(fired)));
        text.AppendLine(Strings.Format(Strings.AutoCaptureReportFired, fired.Fired.Timestamp,
            Value(metric, fired.PeakValue), fired.Peak.Timestamp));
        text.AppendLine(Strings.Format(Strings.AutoCaptureReportRun, MetricName(metric, fired.Peak.Resources),
            string.Join(' ', fired.Run.Select(v => v.ToString("0.0", CultureInfo.CurrentCulture)))));
        if (!AttributesProcesses(metric)) text.AppendLine(Strings.AutoCaptureIoDisclaimer);
        text.AppendLine();
        text.Append(SnapshotText.Format(snapshot));
        text.AppendLine();
        return text.ToString();
    }

    /// <summary>The peak as Copy capture would report it: top 20 individual processes, sorted by the rule's resource.</summary>
    internal static SnapshotReport Snapshot(CaptureFired fired, IReadOnlyList<MonitorSample> history)
    {
        var key = SampleKey.Of(fired.Peak);
        var index = -1;
        for (var i = history.Count - 1; i >= 0; i--)
            if (key.Matches(history[i]))
            {
                index = i;
                break;
            }

        // Rank on the window ending at the peak, with the peak's own uncompacted values, as live ranking does.
        IReadOnlyList<MonitorSample>
            window = index >= 0 ? new SampleList(history, index + 1, fired.Peak) : [fired.Peak];
        var metric = fired.Rule.Metric;
        var sort = metric switch
        {
            CaptureMetric.Memory => ProcessSort.Memory, CaptureMetric.Gpu => ProcessSort.Gpu,
            CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk or CaptureMetric.Network => ProcessSort.Io,
            _ => ProcessSort.Cpu
        };
        var processes = ProcessRanking.Rank(window, sort: sort).Processes.Take(20).ToList();
        if (fired.Process is { } trigger)
        {
            var row = processes.FindIndex(p =>
                p.ProcessId == trigger.ProcessId && p.CreationTicks == trigger.CreationTicks);
            var first = row >= 0 ? processes[row] : trigger;
            if (row >= 0) processes.RemoveAt(row);
            processes.Insert(0, first);
        }

        var label = AttributesProcesses(metric)
            ? sort switch
            {
                ProcessSort.Memory => Strings.Memory, ProcessSort.Gpu => Strings.GPU, _ => Strings.CPU
            }
            : Strings.AutoCaptureTotalIo;
        return new SnapshotReport(index >= 0 ? history[index] : fired.Peak, processes, false, label, "", false, "",
            null);
    }

    /// <summary>The notification body: the peak, and the busiest process where the rule can attribute one.</summary>
    /// <param name="fired">The rule trigger and its peak reading.</param>
    /// <param name="snapshot">The peak's <see cref="Snapshot"/>, ranked by the rule's resource.</param>
    internal static string Notification(CaptureFired fired, SnapshotReport snapshot)
    {
        var metric = fired.Rule.Metric;
        var peak = Strings.Format(Strings.AutoCaptureNotificationPeak, Value(metric, fired.PeakValue),
            fired.Peak.Timestamp);
        if (!AttributesProcesses(metric) || snapshot.Processes.FirstOrDefault() is not { } busiest) return peak;
        // The process's own share of the resource it was ranked by. A process rule's peak is already that process's.
        var value = metric switch
        {
            CaptureMetric.Process => null,
            CaptureMetric.Memory => busiest.WorkingSetBytes is { } bytes ? UsageText.Bytes(bytes) : null,
            CaptureMetric.Gpu => busiest.GpuPercent is { } gpu ? Strings.Format(Strings.PercentOneDecimal, gpu) : null,
            _ => Strings.Format(Strings.PercentOneDecimal, busiest.CpuPercent)
        };
        return value is null ? $"{peak} · {DisplayName(busiest)}" : $"{peak} · {DisplayName(busiest)} {value}";
    }

    private static string DisplayName(ProcessUsage process)
    {
        return process.AppName.Length > 0 ? process.AppName : process.Name;
    }
}
