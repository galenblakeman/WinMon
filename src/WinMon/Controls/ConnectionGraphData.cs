using System.Globalization;
using WinMon.Monitoring;

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

    internal static Dictionary<string, ProcessGraphData> Build(IReadOnlyList<ConnectionCapture> history,
        IReadOnlyList<ConnectionDisplayRow> rows)
    {
        if (rows.Count == 0) return [];
        var keys = rows.Where(row => row.Process is not null).Select(row => Key(row.Process!))
            .Distinct(StringComparer.Ordinal).ToArray();
        var values = keys.ToDictionary(key => key, _ => new double?[history.Count], StringComparer.Ordinal);
        var identities = rows.Where(row => row.Process is not null).Select(row => row.Process!.Identity).ToHashSet();
        var paths = rows.Where(row => row.Process is not null).Select(row => row.Process!.ImagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool Needed(ProcessUsage owner)
        {
            return owner.ImagePath.Length > 0
                ? paths.Contains(owner.ImagePath)
                : identities.Contains(owner.Identity);
        }

        var moments = history.Select(capture => capture.Sample).ToArray();
        var peak = 0d;
        for (var i = 0; i < history.Count; i++)
        {
            var capture = history[i];
            if (capture.Sample.IsGap || capture.Network is not { } network ||
                (network.Quality & (ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp)) !=
                0) continue;
            var owners = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
            foreach (var owner in network.Owners)
                if (Needed(owner))
                    owners[owner.Identity] = owner;
            foreach (var owner in capture.Owners)
                if (owner.ImagePath.Length > 0 || !owners.ContainsKey(owner.Identity))
                {
                    if (Needed(owner)) owners[owner.Identity] = owner;
                    else owners.Remove(owner.Identity);
                }

            var readings = new Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading>();
            foreach (var reading in network.Processes)
            {
                var identity = new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                    ProcessLifetimeIdentity.WindowsFileTime);
                if (owners.ContainsKey(identity)) readings.TryAdd(identity, reading);
            }

            var totals = new Dictionary<string, double?>(StringComparer.Ordinal);
            foreach (var owner in owners.Values)
            {
                if (network.CurrentLifetimes is not null &&
                    !network.CurrentLifetimes.Contains(owner.Identity)) continue;
                var key = Key(owner);
                if (!values.ContainsKey(key)) continue;
                var valid = readings.TryGetValue(owner.Identity, out var reading) &&
                            reading.ReceiveBytesPerSecond is { } receive && double.IsFinite(receive) && receive >= 0 &&
                            reading.SendBytesPerSecond is { } send && double.IsFinite(send) && send >= 0;
                var rate = valid ? reading!.ReceiveBytesPerSecond + reading.SendBytesPerSecond : null;
                totals[key] = totals.TryGetValue(key, out var total) ? total + rate : rate;
            }

            foreach (var (key, total) in totals)
                if (total is { } value && double.IsFinite(value))
                {
                    values[key][i] = value;
                    peak = Math.Max(peak, value);
                }
        }

        var scale = Math.Max(1, peak * 1.1);
        return values.ToDictionary(pair => pair.Key,
            pair => new ProcessGraphData(moments, pair.Value, scale), StringComparer.Ordinal);
    }
}