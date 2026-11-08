using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Observed counter differences, ending at the displayed capture, without interpolating missing traffic.</summary>
public sealed record ConnectionTrafficTotals(
    IReadOnlyDictionary<ProcessLifetimeIdentity, (long Received, long Sent)> Processes,
    double CoveredSeconds,
    bool Available,
    bool Incomplete)
{
    public static ConnectionTrafficTotals Build(IReadOnlyList<ConnectionCapture> history,
        ConnectionCapture? end, int seconds)
    {
        if (end?.Network is not { } network || end.Sample.IsGap ||
            (network.Quality & (ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp)) != 0)
            return new(new Dictionary<ProcessLifetimeIdentity, (long, long)>(), 0, false, true);

        static double Moment(MonitorSample sample)
        {
            return sample.MonotonicTimestamp > 0
                ? sample.MonotonicTimestamp / (double)Stopwatch.Frequency
                : sample.Timestamp.ToUnixTimeMilliseconds() / 1000d;
        }

        var finish = Moment(end.Sample);
        ConnectionCapture? start = null;
        var incomplete = (network.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded |
                                             ProcessNetworkQuality.Discontinuity | ProcessNetworkQuality.WarmingUp)) !=
                         0;
        var lastMoment = double.NaN;
        foreach (var capture in history)
        {
            var moment = Moment(capture.Sample);
            if (moment > finish || (seconds > 0 && moment < finish - seconds)) continue;
            if (!double.IsNaN(lastMoment) && moment - lastMoment > 2) incomplete = true;
            lastMoment = moment;
            if (capture.Network is { } observed && (observed.Quality & (ProcessNetworkQuality.Incomplete |
                                                                        ProcessNetworkQuality.CapacityExceeded |
                                                                        ProcessNetworkQuality.Discontinuity)) !=
                0) incomplete = true;
            if (capture.Sample.IsGap || capture.Network is null ||
                capture.Network.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
            {
                incomplete = true;
                continue;
            }

            start ??= capture;
        }

        var covered = start is null ? 0 : Math.Max(0, finish - Moment(start.Sample));
        if (seconds > 0 && (start is null || covered < seconds - 1)) incomplete = true;
        if (seconds > 0 && start is null)
            return new(new Dictionary<ProcessLifetimeIdentity, (long, long)>(), 0, false, true);

        var baseline = new Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading>();
        if (seconds > 0)
            foreach (var reading in start!.Network!.Processes)
                baseline.TryAdd(Identity(reading), reading);
        var totals = new Dictionary<ProcessLifetimeIdentity, (long, long)>();
        foreach (var reading in network.Processes)
        {
            var identity = Identity(reading);
            baseline.TryGetValue(identity, out var first);
            if (reading.ReceivedBytes < 0 || reading.SentBytes < 0 ||
                (first is not null && (first.ReceivedBytes < 0 || first.SentBytes < 0 ||
                                       reading.ReceivedBytes < first.ReceivedBytes ||
                                       reading.SentBytes < first.SentBytes)))
            {
                incomplete = true;
                continue;
            }

            totals.TryAdd(identity, (reading.ReceivedBytes - (first?.ReceivedBytes ?? 0),
                reading.SentBytes - (first?.SentBytes ?? 0)));
        }

        if (baseline.Keys.Any(identity => !totals.ContainsKey(identity))) incomplete = true;
        return new(totals, covered, network.Processes.Count == 0 || totals.Count > 0, incomplete);
    }

    private static ProcessLifetimeIdentity Identity(ProcessNetworkReading reading)
    {
        return new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
            ProcessLifetimeIdentity.WindowsFileTime);
    }

    public (long Received, long Sent) Sum(IEnumerable<ProcessLifetimeIdentity>? identities = null)
    {
        var received = 0L;
        var sent = 0L;
        foreach (var identity in identities?.Distinct() ?? Processes.Keys)
            if (Processes.TryGetValue(identity, out var total))
            {
                received = Add(received, total.Received);
                sent = Add(sent, total.Sent);
            }

        return (received, sent);
    }

    private static long Add(long total, long value)
    {
        return value > long.MaxValue - total ? long.MaxValue : total + value;
    }
}