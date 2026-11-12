using System.Globalization;
using System.Text;
using WinMon.Monitoring;
using WinMon.Preferences;

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
            CaptureMetric.Network => RateText.Format(value * 1_000_000),
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
    internal static string Report(CaptureFired fired, Guid id, SnapshotReport snapshot,
        ConnectionCapture? networkCapture = null, int topConnectionCount = 20,
        IReadOnlyList<ConnectionCapture>? connectionHistory = null)
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
        if (metric == CaptureMetric.Network) text.Append(NetworkLeaders(networkCapture, topConnectionCount));
        text.Append(ConnectionLeaders(networkCapture, topConnectionCount, history: connectionHistory));
        return text.ToString();
    }

    internal static string NetworkLeaders(ConnectionCapture? capture, int topCount = 20)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.ExportTraffic);
        text.AppendLine(Strings.ConnectionsNetworkOwnerTotals);
        text.AppendLine(Strings.ConnectionsRateTiming);
        text.AppendLine(Strings.AutoCaptureNetworkScope);
        var network = capture?.Network;
        if (network is null || network.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
        {
            text.AppendLine(Strings.NetworkProcessMonitoringUnavailable);
            return text.ToString();
        }

        if ((network.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.Discontinuity |
                               ProcessNetworkQuality.CapacityExceeded)) != 0)
            text.AppendLine(Strings.NetworkProcessMonitoringIncomplete);
        if (network.Quality.HasFlag(ProcessNetworkQuality.WarmingUp))
            text.AppendLine(Strings.NetworkProcessMonitoringWarmingUp);
        var owners = capture!.Owners.Concat(network.Owners).DistinctBy(owner => owner.Identity)
            .ToDictionary(owner => owner.Identity);
        var groups = network.Processes.Where(reading => network.CurrentLifetimes is null ||
                network.CurrentLifetimes.Contains(new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                    ProcessLifetimeIdentity.WindowsFileTime)))
            .Select(reading => new
            {
                Reading = reading,
                Owner = owners.GetValueOrDefault(new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                    ProcessLifetimeIdentity.WindowsFileTime))
            })
            .GroupBy(row => row.Owner is { ImagePath.Length: > 0 } owner
                ? owner.ImagePath : $"{row.Reading.ProcessId}:{row.Reading.CreationTicks}", StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.First().Owner is { } owner ? DisplayName(owner) :
                    Strings.Format(Strings.PID, group.First().Reading.ProcessId),
                Receive = SumRates(group.Select(row => row.Reading.ReceiveBytesPerSecond)),
                Send = SumRates(group.Select(row => row.Reading.SendBytesPerSecond))
            })
            .OrderByDescending(row => row.Receive + row.Send ?? -1)
            .ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(topCount == 0 ? int.MaxValue : topCount).ToArray();
        foreach (var row in groups)
            text.AppendLine($"{row.Name}: ↓ {ConnectionsView.NetworkRate(row.Receive)}  ↑ {ConnectionsView.NetworkRate(row.Send)}");
        if (groups.Length == 0) text.AppendLine(Strings.MissingReading);
        return text.ToString();
    }

    /// <summary>Traffic groups and their endpoint observations aligned with one captured resource sample.</summary>
    internal static string ConnectionLeaders(ConnectionCapture? capture, int topCount = 20,
        ConnectionQueryOptions? options = null, IReadOnlySet<string>? pinnedKeys = null, bool pinnedOnly = false,
        IReadOnlyList<ConnectionCapture>? history = null)
    {
        var text = new StringBuilder();
        text.AppendLine();
        text.AppendLine(Strings.ConnectionsDetails);
        if (capture is null || (capture.Connections?.Available != true &&
                                capture.Network is not { FlowTrafficSupported: true, Flows.Count: > 0 }))
        {
            text.AppendLine(Strings.ConnectionsHistoryUnavailable);
            return text.ToString();
        }

        var automatic = options is null;
        options ??= new ConnectionQueryOptions(ConnectionSortMetric.Receive, NetworkEnabled: capture.Network is not null);
        var all = ConnectionQuery.Build(capture.Connections, capture.Owners, capture.Network, history ?? [capture], capture,
            options);
        var pinned = all.Where(group => pinnedKeys?.Contains(group.Key) == true).ToArray();
        var unpinned = pinnedOnly ? [] : all.Where(group => pinnedKeys?.Contains(group.Key) != true);
        var ranked = automatic
            ? unpinned.OrderByDescending(group => group.Network.ReceiveBytesPerSecond + group.Network.SendBytesPerSecond ?? -1)
                .ThenBy(group => group.Process is { } owner ? DisplayName(owner) : group.Key,
                    StringComparer.CurrentCultureIgnoreCase)
            : unpinned;
        var groups = pinned.Concat(TopCountChoices.Limit(ranked, topCount)).ToArray();
        if (capture.Network is not { } network)
        {
            text.AppendLine(Strings.NetworkProcessMonitoringUnavailable);
        }
        else
        {
            if (network.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
                text.AppendLine(Strings.NetworkProcessMonitoringUnavailable);
            else if ((network.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.Discontinuity |
                                         ProcessNetworkQuality.CapacityExceeded)) != 0)
                text.AppendLine(Strings.NetworkProcessMonitoringIncomplete);
            else if (network.Quality.HasFlag(ProcessNetworkQuality.WarmingUp))
                text.AppendLine(Strings.NetworkProcessMonitoringWarmingUp);
            if (!network.FlowTrafficSupported) text.AppendLine(Strings.CaptureConnectionTrafficUnavailable);
            else
            {
                if ((network.FlowQuality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.Discontinuity |
                                            ProcessNetworkQuality.CapacityExceeded | ProcessNetworkQuality.Unavailable)) != 0)
                    text.AppendLine(Strings.CaptureFlowIncomplete);
                text.AppendLine(TrafficWindow(network));
            }
        }

        text.AppendLine(Strings.ConnectionsNetworkOwnerTotals);
        text.AppendLine(Strings.ConnectionsRateTiming);
        var endpointRows = 0;
        foreach (var group in groups)
        {
            var processId = group.Connections.FirstOrDefault()?.ProcessId ?? group.Flows.FirstOrDefault()?.ProcessId;
            var name = group.Process is { } owner ? DisplayName(owner) : processId is { } pid
                ? Strings.Format(Strings.PID, pid) : Strings.ConnectionsUnknownProcess;
            text.AppendLine($"{name}: ↓ {ConnectionsView.NetworkRate(group.Network.ReceiveBytesPerSecond)}  ↑ {ConnectionsView.NetworkRate(group.Network.SendBytesPerSecond)}");
            text.AppendLine($"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}\t{Strings.ConnectionsReceiveRate}\t{Strings.ConnectionsSendRate}\t{Strings.ConnectionsReceived}\t{Strings.ConnectionsSent}");
            var reportedFlows = new HashSet<ProcessNetworkFlowIdentity>();
            foreach (var connection in group.Connections.OrderBy(connection => connection.ProcessId)
                         .ThenBy(connection => connection.Protocol)
                         .ThenBy(connection => ConnectionQuery.Endpoint(connection.LocalEndpoint), StringComparer.Ordinal))
            {
                endpointRows++;
                var flow = Flow(capture.Network, connection);
                if (flow is not null) reportedFlows.Add(FlowIdentity(flow));
                text.AppendLine($"{connection.ProcessId}\t{Protocol(connection.Protocol)}\t" +
                                $"{ConnectionQuery.Endpoint(connection.LocalEndpoint)}\t" +
                                $"{(connection.RemoteEndpoint is { } remote ? ConnectionQuery.Endpoint(remote) : Strings.MissingReading)}\t" +
                                $"{State(connection.State)}\t{ConnectionsView.NetworkRate(flow?.ReceiveBytesPerSecond)}\t" +
                                $"{ConnectionsView.NetworkRate(flow?.SendBytesPerSecond)}\t" +
                                $"{Bytes(flow?.ReceivedBytes)}\t{Bytes(flow?.SentBytes)}");
            }
            foreach (var flow in group.Flows.Where(flow => !reportedFlows.Contains(FlowIdentity(flow)))
                         .OrderByDescending(flow => flow.ReceiveBytesPerSecond + flow.SendBytesPerSecond ?? -1)
                         .ThenBy(flow => flow.LocalEndpoint, StringComparer.Ordinal)
                         .ThenBy(flow => flow.RemoteEndpoint, StringComparer.Ordinal))
            {
                endpointRows++;
                text.AppendLine($"{flow.ProcessId}\t{Protocol(flow.Protocol)}\t{flow.LocalEndpoint}\t" +
                                $"{flow.RemoteEndpoint}\t{Strings.CaptureObservedFlowMissingEndpoint}\t" +
                                $"{ConnectionsView.NetworkRate(flow.ReceiveBytesPerSecond)}\t" +
                                $"{ConnectionsView.NetworkRate(flow.SendBytesPerSecond)}\t" +
                                $"{Bytes(flow.ReceivedBytes)}\t{Bytes(flow.SentBytes)}");
            }
        }

        var eligibleGroups = pinnedOnly ? pinned.Length : all.Count;
        text.AppendLine(Strings.Format(Strings.CaptureReportConnectionCount,
            topCount == 0 ? Strings.AllEntries : topCount.ToString(CultureInfo.CurrentCulture),
            groups.Length, eligibleGroups, eligibleGroups - groups.Length, endpointRows));
        if (groups.Length == 0) text.AppendLine(Strings.MissingReading);
        return text.ToString();
    }

    private static string Protocol(TransportProtocol protocol)
    {
        return protocol == TransportProtocol.Tcp ? "TCP" : "UDP";
    }

    private static string State(System.Net.NetworkInformation.TcpState? state)
    {
        return state?.ToString() ?? Strings.MissingReading;
    }

    private static ProcessNetworkFlowReading? Flow(ProcessNetworkFrame? network, NetworkConnection connection)
    {
        if (network is not { FlowTrafficSupported: true } || connection.ProcessCreationTicks is not { } created ||
            connection.RemoteEndpoint is not { } remote) return null;
        var local = ConnectionQuery.Endpoint(connection.LocalEndpoint);
        var remoteText = ConnectionQuery.Endpoint(remote);
        return network.Flows.FirstOrDefault(flow => flow.ProcessId == connection.ProcessId &&
            flow.CreationTicks == created && flow.Protocol == connection.Protocol &&
            flow.LocalEndpoint == local && flow.RemoteEndpoint == remoteText);
    }

    private static ProcessNetworkFlowIdentity FlowIdentity(ProcessNetworkFlowReading flow)
    {
        return new ProcessNetworkFlowIdentity(flow.ProcessId, flow.CreationTicks, flow.Protocol,
            flow.LocalEndpoint, flow.RemoteEndpoint);
    }

    private static string Bytes(long? value)
    {
        return value is { } bytes ? UsageText.Bytes(bytes) : Strings.MissingReading;
    }

    private static string TrafficWindow(ProcessNetworkFrame network)
    {
        if (network.Frequency <= 0 || network.WindowEndQpc < network.WindowStartQpc) return Strings.CaptureConnectionTrafficScope;
        var seconds = (network.WindowEndQpc - network.WindowStartQpc) / (double)network.Frequency;
        var age = Math.Max(0, network.TimestampQpc - network.WindowEndQpc) / (double)network.Frequency;
        return Strings.CaptureConnectionTrafficScope + " " +
               Strings.Format(Strings.CaptureConnectionTrafficWindow, seconds, age);
    }

    private static double? SumRates(IEnumerable<double?> rates)
    {
        var total = 0d;
        foreach (var rate in rates)
        {
            if (rate is not { } value || !double.IsFinite(value) || value < 0) return null;
            total += value;
        }

        return double.IsFinite(total) ? total : null;
    }

    /// <summary>The peak as Copy capture would report it: top 20 individual processes, sorted by the rule's resource.</summary>
    internal static SnapshotReport Snapshot(CaptureFired fired, IReadOnlyList<MonitorSample> history, int topCount = 20)
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
        var processes = TopCountChoices.Limit(ProcessRanking.Rank(window, sort: sort).Processes, topCount).ToList();
        var triggerOutsideTop = false;
        if (fired.Process is { } trigger)
        {
            var row = processes.FindIndex(p =>
                p.ProcessId == trigger.ProcessId && p.CreationTicks == trigger.CreationTicks);
            var first = row >= 0 ? processes[row] : trigger;
            triggerOutsideTop = row < 0;
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
            null) { TopCount = topCount, TriggerIncludedOutsideTop = triggerOutsideTop };
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
