using System.Globalization;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

/// <summary>Builds the read-only Services table from the same retained observations as the desktop view.</summary>
public static class ServicesTab
{
    private static readonly string[] Columns =
    [
        Strings.ServiceProcess,
        Strings.ServiceStatus,
        "CPU %",
        Strings.Private,
        Strings.IO
    ];

    /// <summary>
    /// Returns current service groups, ordered and filtered like the desktop Services tab.
    /// CPU and I/O use ServiceRanking's gap-aware five-second window; private memory is current.
    /// </summary>
    public static TerminalTable Build(IReadOnlyList<ServiceFrame> history, int sort, string filter, int status,
        bool workers, int top)
    {
        if (history.Count == 0)
            return new TerminalTable(Columns, [], Strings.ServicesCollecting);

        var latest = history[^1];
        if (!latest.Available)
            return new TerminalTable(Columns, [], Strings.ServicesUnavailable);

        var ranking = ServiceRanking.Build(history, workers);
        var query = filter.Trim();
        var filtered = latest.Groups.Where(group => Matches(group, query) && MatchesStatus(group, status));
        IEnumerable<ServiceGroup> ordered = (sort switch
        {
            1 => filtered.OrderByDescending(group => Metrics(ranking, group).PrivateBytes),
            2 => filtered.OrderByDescending(group => Metrics(ranking, group).Io),
            3 when top == 0 => filtered.OrderBy(group => group.Services[0].DisplayName,
                StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderByDescending(group => Metrics(ranking, group).Cpu)
        }).ThenBy(group => group.Services[0].DisplayName, StringComparer.CurrentCultureIgnoreCase);

        if (top > 0) ordered = ordered.Take(top);

        var rows = ordered.Select(group => Row(group, Metrics(ranking, group), ranking, workers)).ToArray();
        var mode = workers ? Strings.ServiceWorkersIncluded : Strings.ServiceHostOnly;
        return new TerminalTable(Columns, rows,
            Strings.ServiceAverageSummary + " · " + mode);
    }

    private static ServiceMetrics Metrics(ServiceRanking ranking, ServiceGroup group) =>
        ranking.Groups.GetValueOrDefault(group.Key) ?? ServiceMetrics.None;

    private static bool MatchesStatus(ServiceGroup group, int status) => status switch
    {
        1 => group.Services.Any(service => service.State == 4),
        2 => group.Services.Any(service => service.State == 1),
        _ => true
    };

    // Keep matching behavior in lockstep with ServicesView.Matches: SCM/display names plus observed process names/PIDs.
    private static bool Matches(ServiceGroup group, string query)
    {
        return query.Length == 0 ||
               group.Services.Any(service =>
                   service.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   service.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
               group.Processes.Any(process =>
                   process.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || process.ProcessId
                       .ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal));
    }

    private static TerminalRow Row(ServiceGroup group, ServiceMetrics metrics, ServiceRanking ranking, bool workers)
    {
        var representative = group.Services[0];
        var name = group.Shared
            ? string.Format(CultureInfo.CurrentCulture, Strings.SharedServiceHost, group.Host?.Name ?? "",
                representative.ProcessId)
            : representative.DisplayName;
        var serviceStatus = group.Shared ? string.Empty : StateText(representative.State);
        return new TerminalRow(group.Key,
        () => [
            SnapshotReport.SafeText(name),
            SnapshotReport.SafeText(serviceStatus),
            SnapshotReport.Percent(metrics.Cpu),
            SnapshotReport.Memory(metrics.PrivateBytes),
            SnapshotReport.Rate(metrics.Io)
        ], () => Details(group, ranking, workers))
        {
            PinIdentity = "service:" + System.Text.Json.JsonSerializer.Serialize(
                group.Services.Select(service => service.Name).Order(StringComparer.OrdinalIgnoreCase))
        };
    }

    private static string Details(ServiceGroup group, ServiceRanking ranking, bool workers)
    {
        var services = string.Join("; ", group.Services.Select(service =>
            $"{service.Name} ({StateText(service.State)})"));
        var host = group.Host is { } process
            ? string.Format(CultureInfo.CurrentCulture, Strings.ServiceProcessPid, process.ProcessId) + " · " +
              process.Name
            : group.Services[0].HasHost
                ? string.Format(CultureInfo.CurrentCulture, Strings.ServiceProcessPid, group.Services[0].ProcessId) +
                  " · " + Strings.Unavailable
                : Strings.Unavailable;
        var membership = workers && group.Processes.Count > 1
            ? string.Format(CultureInfo.CurrentCulture, Strings.ServiceHostWorkers, group.Processes.Count - 1)
            : Strings.ServiceDedicatedHost;
        var grouping = group.Shared
            ? string.Format(CultureInfo.CurrentCulture, Strings.SharedServiceCount, group.Services.Count)
            : group.Services[0].Name;
        var details = $"{grouping} · {services} · {host} · {membership}";
        if (!workers || group.Processes.Count == 0) return details;

        var observed = group.Processes.OrderBy(process => process == group.Host ? 0 : 1)
            .ThenByDescending(process => ProcessMetrics(ranking, process).Cpu)
            .ThenBy(process => process.ProcessId);
        return details + "\n" + string.Join("\n", observed.Select(process => ProcessDetails(process,
            ProcessMetrics(ranking, process), process == group.Host)));
    }

    private static ServiceMetrics ProcessMetrics(ServiceRanking ranking, ProcessUsage process) =>
        ranking.Processes.GetValueOrDefault((process.ProcessId, process.CreationTicks)) ?? ServiceMetrics.None;

    private static string ProcessDetails(ProcessUsage process, ServiceMetrics metrics, bool host)
    {
        var role = host ? Strings.ServiceHostOnly : Strings.ServiceWorkers;
        var pid = string.Format(CultureInfo.CurrentCulture, Strings.ServiceProcessPid, process.ProcessId);
        return $"{role}: {process.Name} · {pid} · {Strings.CPU}: {SnapshotReport.Percent(metrics.Cpu)} · " +
               $"{Strings.ProcessPrivateMemoryLabel}: {SnapshotReport.Memory(metrics.PrivateBytes)} · " +
               $"{Strings.ReadWriteIO}: {SnapshotReport.Rate(metrics.Io)}";
    }

    private static string StateText(uint state) => state switch
    {
        1 => Strings.ServiceStopped,
        2 => Strings.ServiceStarting,
        3 => Strings.ServiceStopping,
        4 => Strings.ServiceRunning,
        5 => Strings.ServiceContinuing,
        6 => Strings.ServicePausing,
        7 => Strings.ServicePaused,
        _ => Strings.Unavailable
    };
}
