using System.Globalization;
using MonCore.Monitoring;

namespace WinMon.Controls;

internal static class ConnectionGraphData
{
    internal static string Key(ProcessUsage process)
    {
        return process.ImagePath.Length > 0
            ? "image:" + process.ImagePath.ToUpperInvariant()
            : string.Create(CultureInfo.InvariantCulture,
                $"lifetime:{process.ProcessId}:{process.CreationTicks}:{process.Identity.Domain}");
    }

    /// <summary>
    ///     The previous refresh's series. A capture's values depend only on that capture and the set of rows, so while
    ///     the rows stay the same, captures both windows share are copied instead of resolved again.
    /// </summary>
    internal sealed class Reuse
    {
        internal ConnectionCapture[] History = [];
        internal HashSet<ProcessLifetimeIdentity> Identities = [];
        internal HashSet<string> Paths = [];
        internal Dictionary<string, double?[]> Values = [];
    }

    internal static Dictionary<string, ProcessGraphData> Build(IReadOnlyList<ConnectionCapture> history,
        IReadOnlyList<ConnectionDisplayRow> rows, Reuse? reuse = null)
    {
        if (rows.Count == 0) return [];
        var keys = rows.Where(row => row.Process is not null).Select(row => Key(row.Process!))
            .Distinct(StringComparer.Ordinal).ToArray();
        var values = keys.ToDictionary(key => key, _ => new double?[history.Count], StringComparer.Ordinal);
        var identities = rows.Where(row => row.Process is not null).Select(row => row.Process!.Identity).ToHashSet();
        var paths = rows.Where(row => row.Process is not null).Select(row => row.Process!.ImagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var captures = history as ConnectionCapture[] ?? [.. history];
        var shared = Shared(reuse, captures, values, identities, paths);

        var moments = captures.Select(capture => capture.Sample).ToArray();
        // Reused across captures: the traffic window holds hundreds of captures and this runs every refresh.
        var owners = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        var readings = new Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading>();
        var totals = new Dictionary<string, double?>(StringComparer.Ordinal);
        var imageKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = shared; i < captures.Length; i++)
        {
            var capture = captures[i];
            if (capture.Sample.IsGap || capture.Network is not { } network ||
                (network.Quality & (ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp)) !=
                0) continue;
            owners.Clear();
            readings.Clear();
            totals.Clear();
            ResolveCaptureOwnersAndReadings(capture, network, paths, identities, owners, readings);
            AccumulateOwnerRates(network, owners, readings, values, imageKeys, totals);

            foreach (var (key, total) in totals)
                if (total is { } value && double.IsFinite(value))
                    values[key][i] = value;
        }

        if (reuse is not null)
            (reuse.History, reuse.Identities, reuse.Paths, reuse.Values) = (captures, identities, paths, values);
        var peak = 0d;
        foreach (var series in values.Values)
        foreach (var value in series)
            if (value > peak) peak = value.Value;
        var scale = Math.Max(1, peak * 1.1);
        return values.ToDictionary(pair => pair.Key,
            pair => new ProcessGraphData(moments, pair.Value, scale), StringComparer.Ordinal);
    }

    /// <summary>Copy the leading captures the previous window also held, returning how many were copied.</summary>
    private static int Shared(Reuse? reuse, ConnectionCapture[] captures, Dictionary<string, double?[]> values,
        HashSet<ProcessLifetimeIdentity> identities, HashSet<string> paths)
    {
        // Which owners count towards a key can depend on the other rows, so only an unchanged row set is reused.
        if (reuse is null || captures.Length == 0 || reuse.Values.Count != values.Count ||
            !values.Keys.All(reuse.Values.ContainsKey) || !identities.SetEquals(reuse.Identities) ||
            !paths.SetEquals(reuse.Paths)) return 0;
        var previous = reuse.History;
        var shift = 0;
        // References, not record equality: a capture's contents are fixed once it is recorded.
        while (shift < previous.Length && !ReferenceEquals(previous[shift], captures[0])) shift++;
        var count = 0;
        while (shift + count < previous.Length && count < captures.Length &&
               ReferenceEquals(previous[shift + count], captures[count])) count++;
        foreach (var (key, series) in values) Array.Copy(reuse.Values[key], shift, series, 0, count);
        return count;
    }

    private static void ResolveCaptureOwnersAndReadings(ConnectionCapture capture, ProcessNetworkFrame network,
        HashSet<string> paths, HashSet<ProcessLifetimeIdentity> identities,
        Dictionary<ProcessLifetimeIdentity, ProcessUsage> owners,
        Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> readings)
    {
        bool Needed(ProcessUsage owner)
        {
            return owner.ImagePath.Length > 0
                ? paths.Contains(owner.ImagePath)
                : identities.Contains(owner.Identity);
        }

        foreach (var owner in network.Owners)
            if (Needed(owner))
                owners[owner.Identity] = owner;
        foreach (var owner in capture.Owners)
            if (owner.ImagePath.Length > 0 || !owners.ContainsKey(owner.Identity))
            {
                if (Needed(owner)) owners[owner.Identity] = owner;
                else owners.Remove(owner.Identity);
            }

        foreach (var reading in network.Processes)
        {
            var identity = new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime);
            if (owners.ContainsKey(identity)) readings.TryAdd(identity, reading);
        }
    }

    private static void AccumulateOwnerRates(ProcessNetworkFrame network,
        Dictionary<ProcessLifetimeIdentity, ProcessUsage> owners,
        Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> readings, Dictionary<string, double?[]> values,
        Dictionary<string, string> imageKeys, Dictionary<string, double?> totals)
    {
        foreach (var owner in owners.Values)
        {
            if (network.CurrentLifetimes is not null &&
                !network.CurrentLifetimes.Contains(owner.Identity)) continue;
            var key = owner.ImagePath.Length == 0 ? Key(owner)
                : imageKeys.TryGetValue(owner.ImagePath, out var cached) ? cached
                : imageKeys[owner.ImagePath] = Key(owner);
            if (!values.ContainsKey(key)) continue;
            var valid = readings.TryGetValue(owner.Identity, out var reading) &&
                        reading.ReceiveBytesPerSecond is { } receive && double.IsFinite(receive) && receive >= 0 &&
                        reading.SendBytesPerSecond is { } send && double.IsFinite(send) && send >= 0;
            var rate = valid ? reading!.ReceiveBytesPerSecond + reading.SendBytesPerSecond : null;
            totals[key] = totals.TryGetValue(key, out var total) ? total + rate : rate;
        }
    }
}
