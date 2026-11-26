using MonCore.Monitoring;

namespace WinMon.Cli;

internal sealed record ActivityReport(object Data, TerminalTable Table)
{
    internal static ActivityReport Create(string kind, IReadOnlyList<CapturedFrame> frames, UsageLedger ledger,
        int sort, string filter, int top)
    {
        var latest = frames[^1];
        var services = frames.Select(f => f.Services ?? new ServiceFrame(f.Full, [], false)).ToArray();
        var table = kind switch
        {
            "services" => ServicesTab.Build(latest.Services is null ? [] : services, sort, filter, 0, true, 0),
            "usage" => UsageTab.Build(ledger.Apps, ledger.Since, ledger.MachineBusyTicks, latest.Full.Timestamp, sort, filter, false, 0),
            _ => new TerminalTable([Strings.ProcessColumn, Strings.CliPid, Strings.ConnectionsTab], [])
        };
        object[] entries;
        int? matchingCount = null;
        var available = true;
        if (kind == "services")
        {
            available = latest.Services?.Available == true;
            var ranking = ServiceRanking.Build(services, true);
            var groups = latest.Services?.Groups ?? [];
            var rows = table.Rows.Select(row => groups.First(g => g.Key == row.Key)).ToArray();
            entries = rows.Select(group => (object)new
            {
                services = group.Services.Select(s => new { s.Name, s.DisplayName, s.State, pid = s.ProcessId }).ToArray(),
                sharedHost = group.Shared,
                processes = group.Processes.Select(p => new { pid = p.ProcessId, startToken = p.CreationTicks.ToString(System.Globalization.CultureInfo.InvariantCulture), domain = p.LifetimeDomain.ToString(), p.Name }).ToArray(),
                cpuPercent = ranking.Groups.GetValueOrDefault(group.Key)?.Cpu,
                privateBytes = ranking.Groups.GetValueOrDefault(group.Key)?.PrivateBytes,
                ioBytesPerSecond = ranking.Groups.GetValueOrDefault(group.Key)?.Io
            }).ToArray();
        }
        else if (kind == "usage")
        {
            available = frames.Any(f => UsageLedger.IsValid(f.Full));
            entries = UsageQuery.Select(ledger.Apps, sort, filter, false, 0, a => a.AppName.Length > 0 ? a.AppName : a.Name)
                .Select(a => (object)new { a.Name, a.AppName, a.CpuTicks, a.IoBytes, a.GpuSeconds, a.Lifetimes, a.Running, lastActiveUtc = a.LastActive?.Timestamp.ToUniversalTime() }).ToArray();
        }
        else
        {
            available = latest.Connections?.Available == true;
            var owners = new Dictionary<(int Pid, long Created), ProcessUsage>(latest.Full.Processes.Count);
            foreach (var process in latest.Full.Processes)
                if (process.LifetimeDomain == ProcessLifetimeDomain.WindowsFileTime)
                    owners.TryAdd((process.ProcessId, process.CreationTicks), process);
            var connections = (latest.Connections?.Connections ?? []).Select(c => (Connection: c,
                Owner: c.ProcessCreationTicks is { } created ? owners.GetValueOrDefault((c.ProcessId, created)) : null))
                .Where(row => filter.Length == 0 ||
                    row.Connection.ProcessId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    row.Owner?.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);
            var ordered = (sort == 0 ? connections.OrderBy(row => row.Connection.ProcessId) :
                connections.OrderBy(row => row.Owner?.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            matchingCount = ordered.Length;
            var selected = top > 0 ? ordered.Take(top) : ordered;
            entries = selected.Select(row =>
            {
                var c = row.Connection;
                return (object)new { pid = c.ProcessId, name = row.Owner?.Name,
                startToken = c.ProcessCreationTicks?.ToString(System.Globalization.CultureInfo.InvariantCulture), domain = c.ProcessCreationTicks is null ? null : "WindowsFileTime",
                protocol = c.Protocol.ToString(), state = c.State?.ToString(), localAddress = c.LocalEndpoint.Address.ToString(), localPort = c.LocalEndpoint.Port,
                remoteAddress = c.RemoteEndpoint?.Address.ToString(), remotePort = c.RemoteEndpoint?.Port };
            }).ToArray();
            table = new TerminalTable([Strings.ProcessColumn, Strings.CliPid, Strings.ConnectionsTab], selected.Select(row =>
                new TerminalRow("", [row.Owner?.Name ?? Strings.Unavailable, row.Connection.ProcessId.ToString(),
                    $"{row.Connection.Protocol} {row.Connection.LocalEndpoint} → {row.Connection.RemoteEndpoint}"], "")).ToArray());
        }
        var total = matchingCount ?? entries.Length;
        if (!available) table = table with { Note = Strings.Unavailable + " · " + table.Note };
        if (top > 0 && matchingCount is null) { entries = entries.Take(top).ToArray(); table = table with { Rows = table.Rows.Take(top).ToArray() }; }
        return new(new { schemaVersion = 1, kind, sinceUtc = ledger.Since.ToUniversalTime(), timestampUtc = latest.Full.Timestamp.ToUniversalTime(),
            available, isGap = latest.Full.IsGap, samplingError = latest.Error?.ToString(),
            intervalSeconds = latest.Full.IntervalSeconds, observedSeconds = frames.Where(f => UsageLedger.IsValid(f.Full)).Sum(f => f.Full.IntervalSeconds),
            ipv4 = latest.Connections?.Ipv4, ipv6 = latest.Connections?.Ipv6,
            machineBusyTicks = kind == "usage" ? (long?)ledger.MachineBusyTicks : null,
            returnedCount = entries.Length, truncated = entries.Length < total, entries }, table);
    }
}
