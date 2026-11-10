using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

/// <summary>Builds the read-only Connections terminal table from aligned monitor observations.</summary>
public static class ConnectionsTab
{
    /// <summary>Builds all matching rows; callers apply pins and the configured top limit afterwards.</summary>
    public static TerminalTable Build(NetworkConnectionsFrame? frame, IReadOnlyList<ProcessUsage> processes,
        ProcessNetworkFrame? network, IReadOnlyList<ConnectionCapture> history, ConnectionCapture? capture,
        ConnectionQueryOptions options)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(options);
        var rows = ConnectionQuery.Build(frame, processes, network, history, capture, options)
            .Select(row => new TerminalRow(row.Key, () => Cells(row, options.NetworkEnabled), () => Details(row, options.NetworkEnabled))
                { PinIdentity = CliPin.Executable(row.Process?.ImagePath), PinName = DisplayName(row.Process) })
            .ToArray();
        return new TerminalTable(Columns(options.NetworkEnabled), rows, Note(frame, options.NetworkEnabled));
    }

    private static string[] Columns(bool networkEnabled)
    {
        var columns = new List<string>
        {
            Strings.ProcessColumn, Strings.ConnectionsTcpCount, Strings.ConnectionsUdpCount
        };
        if (networkEnabled)
        {
            columns.Add(Strings.ConnectionsReceiveRate);
            columns.Add(Strings.ConnectionsSendRate);
            columns.Add(Strings.ConnectionsReceived);
            columns.Add(Strings.ConnectionsSent);
        }

        return columns.ToArray();
    }

    private static string[] Cells(ConnectionQueryRow row, bool networkEnabled)
    {
        var process = DisplayName(row.Process);
        var pid = row.Connections.FirstOrDefault()?.ProcessId ?? row.Process?.ProcessId;
        var identity = row.Members.Count > 1
            ? $"{process} ({string.Join(", ", row.Members.Select(member => member.ProcessId.ToString(CultureInfo.InvariantCulture)))})"
            : $"{process} ({(pid?.ToString(CultureInfo.InvariantCulture) ?? Strings.MissingReading)})";
        var cells = new List<string>
        {
            identity,
            row.TcpCount.ToString(CultureInfo.CurrentCulture),
            row.UdpCount.ToString(CultureInfo.CurrentCulture)
        };
        if (networkEnabled)
        {
            cells.Add(Rate(row.Network.RatesAvailable ? row.Network.ReceiveBytesPerSecond : null));
            cells.Add(Rate(row.Network.RatesAvailable ? row.Network.SendBytesPerSecond : null));
            cells.Add(Traffic(row.Traffic.ReceivedBytes, row.Traffic.Available, row.Traffic.Incomplete));
            cells.Add(Traffic(row.Traffic.SentBytes, row.Traffic.Available, row.Traffic.Incomplete));
        }

        return cells.ToArray();
    }

    private static string Details(ConnectionQueryRow row, bool networkEnabled)
    {
        var text = new StringBuilder();
        text.AppendLine(DisplayName(row.Process));
        text.AppendLine($"PID: {string.Join(", ", row.Connections.Select(connection => connection.ProcessId).Distinct())}");
        text.AppendLine($"{Strings.ProcessPathLabel}: {(row.Process?.ImagePath is { Length: > 0 } path ? path : Strings.ExecutablePathUnavailable)}");
        text.AppendLine($"PID\t{Strings.ConnectionsProtocol}\t{Strings.ConnectionsLocalEndpoint}\t{Strings.ConnectionsRemoteEndpoint}\t{Strings.ConnectionsState}");
        foreach (var connection in row.Connections)
        {
            text.Append(connection.ProcessId.ToString(CultureInfo.InvariantCulture));
            text.Append('\t');
            text.Append(connection.Protocol == TransportProtocol.Tcp ? "TCP" : "UDP");
            text.Append('\t');
            text.Append(ConnectionQuery.Endpoint(connection.LocalEndpoint));
            text.Append('\t');
            text.Append(connection.RemoteEndpoint is { } remote ? ConnectionQuery.Endpoint(remote) : Strings.MissingReading);
            text.Append('\t');
            text.Append(State(connection.State));
            text.AppendLine();
        }

        if (networkEnabled) AppendNetworkDetails(text, row);
        return text.ToString().TrimEnd();
    }

    private static void AppendNetworkDetails(StringBuilder text, ConnectionQueryRow row)
    {
        text.AppendLine();
        text.AppendLine(Strings.ConnectionsNetworkOwnerTotals);
        text.AppendLine($"↓ {Rate(row.Network.RatesAvailable ? row.Network.ReceiveBytesPerSecond : null)}  ↑ {Rate(row.Network.RatesAvailable ? row.Network.SendBytesPerSecond : null)}");
        text.AppendLine($"{Strings.DetailReceivedSent}: {Totals(row.Network)}");
        text.AppendLine(Strings.ConnectionsRateTiming);
        var state = NetworkState(row.Network.Quality);
        if (state.Length > 0) text.AppendLine(state);
        text.AppendLine(Strings.ConnectionsTrafficHelp);
        if (!row.Traffic.Available)
        {
            text.Append(Strings.NetworkProcessMonitoringUnavailable);
            return;
        }

        text.Append(string.Format(CultureInfo.CurrentCulture, Strings.ConnectionsTrafficCoverage,
            (int)row.Traffic.CoveredSeconds));
        if (row.Traffic.Incomplete) text.AppendLine().Append(Strings.NetworkProcessMonitoringIncomplete);
    }

    private static string Note(NetworkConnectionsFrame? frame, bool networkEnabled)
    {
        if (frame is null) return Strings.ConnectionsUnavailable;
        if (!frame.Available) return Strings.ConnectionsUnavailable;
        if (frame.PartiallyAvailable) return Strings.ConnectionsPartial;
        return networkEnabled ? "" : Strings.NetworkProcessMonitoringDisabled;
    }

    private static string DisplayName(ProcessUsage? process) => process?.AppName is { Length: > 0 } appName
        ? appName
        : process?.Name is { Length: > 0 } name ? name : Strings.ConnectionsUnknownProcess;

    private static string Rate(double? rate) => rate is { } value
        ? Controls.TableMetricText.IoRate(value)
        : Strings.MissingReading;

    private static string Traffic(long bytes, bool available, bool incomplete) => !available
        ? Strings.MissingReading
        : (incomplete ? "≈ " : "") + Bytes(bytes);

    private static string Totals(ConnectionNetworkSummary network) => network.HasTotals
        ? $"{Bytes(network.ReceivedBytes)} / {Bytes(network.SentBytes)}"
        : Strings.MissingReading;

    private static string Bytes(double bytes) => Controls.UsageText.Bytes(bytes);

    private static string State(TcpState? state) => state switch
    {
        TcpState.Closed => Strings.ConnectionsTcpStateClosed,
        TcpState.Listen => Strings.ConnectionsTcpStateListen,
        TcpState.SynSent => Strings.ConnectionsTcpStateSynSent,
        TcpState.SynReceived => Strings.ConnectionsTcpStateSynReceived,
        TcpState.Established => Strings.ConnectionsTcpStateEstablished,
        TcpState.FinWait1 => Strings.ConnectionsTcpStateFinWait1,
        TcpState.FinWait2 => Strings.ConnectionsTcpStateFinWait2,
        TcpState.CloseWait => Strings.ConnectionsTcpStateCloseWait,
        TcpState.Closing => Strings.ConnectionsTcpStateClosing,
        TcpState.LastAck => Strings.ConnectionsTcpStateLastAck,
        TcpState.TimeWait => Strings.ConnectionsTcpStateTimeWait,
        TcpState.DeleteTcb => Strings.ConnectionsTcpStateDeleteTcb,
        _ => Strings.MissingReading
    };

    private static string NetworkState(ProcessNetworkQuality quality)
    {
        if (quality.HasFlag(ProcessNetworkQuality.Unavailable)) return Strings.NetworkProcessMonitoringUnavailable;
        if (quality.HasFlag(ProcessNetworkQuality.Incomplete) || quality.HasFlag(ProcessNetworkQuality.Discontinuity) ||
            quality.HasFlag(ProcessNetworkQuality.CapacityExceeded)) return Strings.NetworkProcessMonitoringIncomplete;
        if (quality.HasFlag(ProcessNetworkQuality.WarmingUp)) return Strings.NetworkProcessMonitoringWarmingUp;
        return "";
    }
}
