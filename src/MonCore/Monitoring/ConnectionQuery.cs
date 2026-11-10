using System.Globalization;
using System.Net;

namespace WinMon.Monitoring;

/// <summary>Column used to order grouped connection observations. The values match the desktop Connections tab.</summary>
public enum ConnectionSortMetric
{
    Receive = 0,
    Send = 1,
    Tcp = 2,
    Udp = 3,
    Received = 4,
    Sent = 5
}

/// <summary>Read-only filters and display choices for a grouped connection query.</summary>
public sealed record ConnectionQueryOptions(
    ConnectionSortMetric Sort = ConnectionSortMetric.Receive,
    bool AppsOnly = false,
    string Search = "",
    bool NetworkEnabled = false,
    int TrafficSeconds = 300,
    string UnknownProcessName = "Unknown process");

/// <summary>Observed process-network counters for one grouped connection row.</summary>
public sealed record ConnectionNetworkSummary(
    long ReceivedBytes,
    long SentBytes,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond,
    ProcessNetworkQuality Quality,
    bool Enabled,
    bool HasTotals)
{
    public static ConnectionNetworkSummary Disabled { get; } =
        new(0, 0, null, null, ProcessNetworkQuality.None, false, false);

    public static ConnectionNetworkSummary Unavailable { get; } =
        new(0, 0, null, null, ProcessNetworkQuality.Unavailable, true, false);

    public bool RatesAvailable => HasTotals && !Quality.HasFlag(ProcessNetworkQuality.Unavailable);
}

/// <summary>Traffic accumulated for the requested period for one grouped connection row.</summary>
public sealed record ConnectionTrafficSummary(
    long ReceivedBytes,
    long SentBytes,
    bool Available,
    bool Incomplete,
    double CoveredSeconds);

/// <summary>A grouped executable or process-lifetime row suitable for a Connections display.</summary>
public sealed record ConnectionQueryRow(
    string Key,
    ProcessUsage? Process,
    IReadOnlyList<ProcessUsage> Members,
    IReadOnlyList<NetworkConnection> Connections,
    ConnectionNetworkSummary Network,
    ConnectionTrafficSummary Traffic)
{
    public int TcpCount => Connections.Count(connection => connection.Protocol == TransportProtocol.Tcp);
    public int UdpCount => Connections.Count(connection => connection.Protocol == TransportProtocol.Udp);
}

/// <summary>Shared desktop-equivalent grouping, filtering, traffic attribution and ordering for Connections displays.</summary>
public static class ConnectionQuery
{
    /// <summary>
    /// Builds all visible connection rows. Callers apply their own pins and top limit after this query so those controls
    /// do not affect the shared desktop ordering.
    /// </summary>
    public static IReadOnlyList<ConnectionQueryRow> Build(NetworkConnectionsFrame? frame,
        IReadOnlyList<ProcessUsage> processes, ProcessNetworkFrame? network,
        IReadOnlyList<ConnectionCapture> history, ConnectionCapture? capture, ConnectionQueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(history);
        options ??= new ConnectionQueryOptions();
        ArgumentOutOfRangeException.ThrowIfNegative(options.TrafficSeconds);
        if (frame?.Available != true) return [];

        var groups = Entries(frame, processes)
            .GroupBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => Group.Create(group.Key, group))
            .Where(group => Visible(group, options))
            .ToArray();
        var networkMembers = NetworkMembers(processes, network);
        var readings = Readings(network);
        var totals = options.NetworkEnabled
            ? ConnectionTrafficTotals.Build(history, capture, options.TrafficSeconds)
            : null;
        var effectiveSort = !options.NetworkEnabled && IsTrafficSort(options.Sort)
            ? ConnectionSortMetric.Tcp
            : options.Sort;

        return groups.Select(group => CreateRow(group, network, networkMembers, readings, totals, options.NetworkEnabled))
            .OrderByDescending(row => SortValue(row, effectiveSort))
            .ThenBy(row => DisplayName(row.Process, options.UnknownProcessName), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Formats an endpoint consistently for searching and detail rendering.</summary>
    public static string Endpoint(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{endpoint.Address}]:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
            : $"{endpoint.Address}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Returns the stable row key used by Connections for an owner with no observed endpoint yet.</summary>
    public static string OwnerKey(ProcessUsage owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.ImagePath.Length > 0
            ? "image:" + ProcessRanking.GroupKey(owner).ToUpperInvariant()
            : LifetimeKey(owner.ProcessId, owner.CreationTicks);
    }

    private static ConnectionQueryRow CreateRow(Group group, ProcessNetworkFrame? network,
        IReadOnlyDictionary<string, IReadOnlyList<ProcessUsage>> networkMembers,
        IReadOnlyDictionary<ProcessLifetimeIdentity, ProcessNetworkReading> readings,
        ConnectionTrafficTotals? totals, bool networkEnabled)
    {
        var members = MembersFor(group, networkMembers);
        var networkSummary = CollectNetworkSummary(members, network, readings, networkEnabled);
        var identities = members.Select(member => member.Identity).Distinct().ToArray();
        var available = totals?.Available == true && identities.Any(totals.Processes.ContainsKey);
        var traffic = totals?.Sum(identities) ?? (0L, 0L);
        return new ConnectionQueryRow(group.Key, group.Process, group.Members,
            group.Entries.Select(entry => entry.Connection).ToArray(), networkSummary,
            new ConnectionTrafficSummary(traffic.Received, traffic.Sent, available,
                totals?.Incomplete == true, totals?.CoveredSeconds ?? 0));
    }

    private static double SortValue(ConnectionQueryRow row, ConnectionSortMetric sort)
    {
        return sort switch
        {
            ConnectionSortMetric.Receive => row.Network.ReceiveBytesPerSecond ?? double.NegativeInfinity,
            ConnectionSortMetric.Send => row.Network.SendBytesPerSecond ?? double.NegativeInfinity,
            ConnectionSortMetric.Tcp => row.TcpCount,
            ConnectionSortMetric.Udp => row.UdpCount,
            ConnectionSortMetric.Received => row.Traffic.Available ? row.Traffic.ReceivedBytes : double.NegativeInfinity,
            ConnectionSortMetric.Sent => row.Traffic.Available ? row.Traffic.SentBytes : double.NegativeInfinity,
            _ => double.NegativeInfinity
        };
    }

    private static bool IsTrafficSort(ConnectionSortMetric sort)
    {
        return sort is ConnectionSortMetric.Receive or ConnectionSortMetric.Send or ConnectionSortMetric.Received or
            ConnectionSortMetric.Sent;
    }

    private static ConnectionNetworkSummary CollectNetworkSummary(IReadOnlyList<ProcessUsage> members,
        ProcessNetworkFrame? network, IReadOnlyDictionary<ProcessLifetimeIdentity, ProcessNetworkReading> readings,
        bool networkEnabled)
    {
        if (!networkEnabled) return ConnectionNetworkSummary.Disabled;
        if (members.Count == 0 || network is null) return ConnectionNetworkSummary.Unavailable;
        var received = 0L;
        var sent = 0L;
        var found = 0;
        var rateCount = 0;
        var receiveRate = 0d;
        var sendRate = 0d;
        var ratesAvailable = true;
        foreach (var member in members)
        {
            if (!readings.TryGetValue(member.Identity, out var reading)) continue;
            found++;
            if (reading.ReceivedBytes < 0 || reading.SentBytes < 0) return ConnectionNetworkSummary.Unavailable;
            received = SaturatingAdd(received, reading.ReceivedBytes);
            sent = SaturatingAdd(sent, reading.SentBytes);
            if (network.CurrentLifetimes is not null && !network.CurrentLifetimes.Contains(member.Identity)) continue;
            rateCount++;
            if (!ValidRate(reading.ReceiveBytesPerSecond) || !ValidRate(reading.SendBytesPerSecond) ||
                reading.ReceiveBytesPerSecond is null || reading.SendBytesPerSecond is null)
            {
                ratesAvailable = false;
                continue;
            }

            receiveRate += reading.ReceiveBytesPerSecond.Value;
            sendRate += reading.SendBytesPerSecond.Value;
        }

        if (found == 0) return ConnectionNetworkSummary.Unavailable;
        ratesAvailable &= double.IsFinite(receiveRate) && double.IsFinite(sendRate);
        return new ConnectionNetworkSummary(received, sent,
            ratesAvailable && rateCount > 0 ? receiveRate : null,
            ratesAvailable && rateCount > 0 ? sendRate : null,
            network.Quality | (found == members.Count ? ProcessNetworkQuality.None : ProcessNetworkQuality.Incomplete),
            true, true);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ProcessUsage>> NetworkMembers(
        IReadOnlyList<ProcessUsage> processes, ProcessNetworkFrame? network)
    {
        var known = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        foreach (var owner in network?.Owners ?? []) known[owner.Identity] = owner;
        foreach (var process in processes)
            if (!known.TryGetValue(process.Identity, out var owner) || process.ImagePath.Length > 0 ||
                owner.ImagePath.Length == 0)
                known[process.Identity] = process;

        return known.Values.Where(process => process.ImagePath.Length > 0)
            .GroupBy(process => process.ImagePath, ProcessIdentityPolicy.Windows.Comparer)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ProcessUsage>)group.ToArray(),
                ProcessIdentityPolicy.Windows.Comparer);
    }

    private static IReadOnlyDictionary<ProcessLifetimeIdentity, ProcessNetworkReading> Readings(ProcessNetworkFrame? network)
    {
        var readings = new Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading>();
        foreach (var reading in network?.Processes ?? [])
        {
            var identity = new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime);
            readings.TryAdd(identity, reading);
        }

        return readings;
    }

    private static IReadOnlyList<ProcessUsage> MembersFor(Group group,
        IReadOnlyDictionary<string, IReadOnlyList<ProcessUsage>> networkMembers)
    {
        var path = group.Members.Append(group.Process).OfType<ProcessUsage>()
            .FirstOrDefault(process => process.ImagePath.Length > 0)?.ImagePath;
        return path is not null && networkMembers.TryGetValue(path, out var members) ? members : group.Members;
    }

    private static IEnumerable<Entry> Entries(NetworkConnectionsFrame frame, IReadOnlyList<ProcessUsage> processes)
    {
        var owners = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        foreach (var process in processes) owners[process.Identity] = process;
        var executableKeys = new Dictionary<string, string>(ProcessIdentityPolicy.Windows.Comparer);
        foreach (var connection in frame.Connections)
        {
            ProcessUsage? owner = null;
            if (connection.ProcessCreationTicks is { } created)
                owners.TryGetValue(new ProcessLifetimeIdentity(connection.ProcessId, created,
                    ProcessLifetimeIdentity.WindowsFileTime), out owner);
            string key;
            if (owner is { ImagePath.Length: > 0 })
            {
                if (!executableKeys.TryGetValue(owner.ImagePath, out key!))
                {
                    key = OwnerKey(owner);
                    executableKeys.Add(owner.ImagePath, key);
                }
            }
            else
            {
                key = LifetimeKey(connection.ProcessId, connection.ProcessCreationTicks);
            }

            yield return new Entry(connection, owner, key);
        }
    }

    private static bool Visible(Group group, ConnectionQueryOptions options)
    {
        var query = options.Search.Trim();
        if (!Matches(group, query, options.UnknownProcessName)) return false;
        return !options.AppsOnly || group.Members.Append(group.Process).Any(process =>
            process is not null && (process.HasHadAppWindow || process.HasAppWindow == true));
    }

    private static bool Matches(Group group, string query, string unknownProcessName)
    {
        return group.Entries.Any(entry => query.Length == 0 ||
            DisplayName(entry.Process, unknownProcessName).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            entry.Connection.ProcessId.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal) ||
            Endpoint(entry.Connection.LocalEndpoint).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (entry.Connection.RemoteEndpoint is { } remote &&
             Endpoint(remote).Contains(query, StringComparison.OrdinalIgnoreCase)));
    }

    private static string DisplayName(ProcessUsage? process, string unknownProcessName = "Unknown process")
    {
        return process?.AppName is { Length: > 0 } appName ? appName :
            process?.Name is { Length: > 0 } name ? name : unknownProcessName;
    }

    private static string LifetimeKey(int processId, long? creationTicks)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"lifetime:{processId}:{creationTicks?.ToString(CultureInfo.InvariantCulture) ?? "?"}");
    }

    private static bool ValidRate(double? rate) => rate is null || (double.IsFinite(rate.Value) && rate.Value >= 0);

    private static long SaturatingAdd(long total, long value) => value > long.MaxValue - total ? long.MaxValue : total + value;

    private sealed record Entry(NetworkConnection Connection, ProcessUsage? Process, string Key);

    private sealed record Group(string Key, IReadOnlyList<Entry> Entries, IReadOnlyList<ProcessUsage> Members,
        ProcessUsage? Process)
    {
        public static Group Create(string key, IEnumerable<Entry> source)
        {
            var entries = source.ToArray();
            var members = entries.Select(entry => entry.Process).OfType<ProcessUsage>().DistinctBy(process => process.Identity)
                .ToArray();
            var process = members.OrderBy(member => DisplayName(member), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(member => member.ProcessId).FirstOrDefault();
            return new Group(key, entries, members, process);
        }
    }
}
