namespace WinMon.Monitoring;

/// <summary>The severity recorded by a Windows event.</summary>
public enum IncidentEventLevel
{
    Unknown = 0,
    Error = 2,
    Warning = 3,
    Information = 4
}

/// <summary>The outcome of reading one Windows event log for an incident.</summary>
public enum IncidentEventCoverageStatus
{
    Unknown,
    Complete,
    Unreadable,
    TimedOut
}

/// <summary>A bounded, display-safe Windows event retained with an incident.</summary>
public sealed record IncidentEvent(
    DateTimeOffset Timestamp,
    string Provider,
    int EventId,
    IncidentEventLevel Level,
    string Message,
    int? ProcessId = null,
    long? CreationTicks = null);

/// <summary>Whether a Windows event log was fully covered while collecting an incident.</summary>
public sealed record IncidentEventLogCoverage(string Log, IncidentEventCoverageStatus Status);

/// <summary>The bounded Windows events associated with one history range.</summary>
public sealed record IncidentEventResult(
    IReadOnlyList<IncidentEvent> Events,
    IReadOnlyList<IncidentEventLogCoverage> Coverage,
    bool LimitReached)
{
    /// <summary>Events are unavailable or were not collected, rather than known to be absent.</summary>
    public static IncidentEventResult Unknown { get; } = new(
        [],
        [new("Application", IncidentEventCoverageStatus.Unknown), new("System", IncidentEventCoverageStatus.Unknown)],
        false);
}

/// <summary>Places one event on an incident's monotonic chart timeline.</summary>
public sealed record IncidentEventMapping(
    IncidentEvent Event,
    double Moment,
    int? ObservationIndex,
    bool Approximate,
    ProcessUsage? LinkedProcess);

/// <summary>Maps wall-clock Windows events onto retained monitor observations.</summary>
public static class IncidentEventMapper
{
    private const double SelectionSeconds = 2;
    private const double ClockDivergenceSeconds = 1;

    /// <summary>Maps each event, preserving its order, without inferring a process lifetime from a PID alone.</summary>
    public static IReadOnlyList<IncidentEventMapping> Map(IncidentEventResult result,
        IReadOnlyList<MonitorSample> samples, IEnumerable<ProcessUsage>? extraRetained = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(samples);

        var retained = new Dictionary<ProcessLifetimeIdentity, ProcessUsage>();
        foreach (var sample in samples)
        foreach (var process in sample.Processes)
            if (process.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime) retained.TryAdd(process.Identity, process);
        if (extraRetained is not null)
        foreach (var process in extraRetained)
            if (process.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime) retained.TryAdd(process.Identity, process);

        var mapped = new IncidentEventMapping[result.Events.Count];
        for (var eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
        {
            var item = result.Events[eventIndex];
            var nearest = NearestObservation(item.Timestamp, samples);
            var (moment, approximate) = Interpolate(item.Timestamp, samples, nearest.Index);
            int? selected = nearest.Index is { } index && nearest.DistanceSeconds <= SelectionSeconds
                ? index
                : null;
            mapped[eventIndex] = new IncidentEventMapping(item, moment, selected, approximate,
                LinkedProcess(item, retained));
        }

        return mapped;
    }

    private static (int? Index, double DistanceSeconds) NearestObservation(DateTimeOffset timestamp,
        IReadOnlyList<MonitorSample> samples)
    {
        int? index = null;
        var distance = double.PositiveInfinity;
        for (var i = 0; i < samples.Count; i++)
        {
            if (samples[i].IsGap) continue;
            var candidate = Math.Abs((samples[i].Timestamp - timestamp).TotalSeconds);
            if (candidate < distance)
            {
                index = i;
                distance = candidate;
            }
        }

        return (index, distance);
    }

    private static (double Moment, bool Approximate) Interpolate(DateTimeOffset timestamp,
        IReadOnlyList<MonitorSample> samples, int? nearest)
    {
        if (samples.Count == 0) return (0, true);
        if (samples.Count == 1) return (ProcessRanking.Moment(samples[0]), true);

        var eventTicks = timestamp.UtcTicks;
        var pair = -1;
        var bestSpan = long.MaxValue;
        for (var i = 0; i < samples.Count - 1; i++)
        {
            var start = samples[i].Timestamp.UtcTicks;
            var end = samples[i + 1].Timestamp.UtcTicks;
            var low = Math.Min(start, end);
            var high = Math.Max(start, end);
            var span = high - low;
            if (span == 0 || eventTicks < low || eventTicks > high || span >= bestSpan) continue;
            pair = i;
            bestSpan = span;
        }

        if (pair < 0)
        {
            var index = nearest ?? 0;
            return (ProcessRanking.Moment(samples[index]), true);
        }

        var first = samples[pair];
        var second = samples[pair + 1];
        var wallSeconds = (second.Timestamp - first.Timestamp).TotalSeconds;
        var ratio = Math.Clamp((timestamp - first.Timestamp).TotalSeconds / wallSeconds, 0, 1);
        var firstMoment = ProcessRanking.Moment(first);
        var secondMoment = ProcessRanking.Moment(second);
        var monotonicSeconds = secondMoment - firstMoment;
        var clockJump = wallSeconds <= 0 || monotonicSeconds < 0 ||
            Math.Abs(wallSeconds - monotonicSeconds) > ClockDivergenceSeconds;
        var moment = firstMoment + (secondMoment - firstMoment) * ratio;
        var ambiguous = false;
        for (var i = 0; i < samples.Count - 1; i++)
        {
            var left = samples[i];
            var right = samples[i + 1];
            var duration = (right.Timestamp - left.Timestamp).TotalSeconds;
            if (duration == 0 || eventTicks < Math.Min(left.Timestamp.UtcTicks, right.Timestamp.UtcTicks) ||
                eventTicks > Math.Max(left.Timestamp.UtcTicks, right.Timestamp.UtcTicks)) continue;
            var candidate = ProcessRanking.Moment(left) + (ProcessRanking.Moment(right) - ProcessRanking.Moment(left)) *
                Math.Clamp((timestamp - left.Timestamp).TotalSeconds / duration, 0, 1);
            if (Math.Abs(candidate - moment) > .001) { ambiguous = true; break; }
        }
        return (moment, first.IsGap || second.IsGap || clockJump || ambiguous);
    }

    private static ProcessUsage? LinkedProcess(IncidentEvent item,
        IReadOnlyDictionary<ProcessLifetimeIdentity, ProcessUsage> retained)
    {
        if (item.ProcessId is not { } pid || item.CreationTicks is not { } creation || creation <= 0) return null;
        return retained.GetValueOrDefault(new ProcessLifetimeIdentity(pid, creation, ProcessLifetimeIdentity.WindowsFileTime));
    }
}
