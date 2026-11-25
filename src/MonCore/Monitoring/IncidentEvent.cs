using System.Runtime.CompilerServices;

namespace WinMon.Monitoring;

/// <summary>The severity recorded by a Windows event.</summary>
public enum IncidentEventLevel
{
    Unknown = 0,
    Error = 2,
    Warning = 3,
    Information = 4
}

/// <summary>The kind of a structured Windows application failure.</summary>
public enum CrashKind
{
    Crash,
    Hang
}

/// <summary>Structured, path-free identity fields emitted by an application failure event.</summary>
public sealed record CrashSignature(
    CrashKind Kind,
    string Application,
    string ApplicationVersion,
    string? Module = null,
    string? ModuleVersion = null,
    uint? ExceptionCode = null,
    ulong? Offset = null);

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
    long? CreationTicks = null,
    CrashSignature? CrashSignature = null);

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

        // Only events that name a process need the lookup, and building it walks every retained process.
        Dictionary<ProcessLifetimeIdentity, ProcessUsage>? retained = null;
        if (result.Events.Any(item => item is { ProcessId: not null, CreationTicks: > 0 }))
        {
            retained = [];
            foreach (var sample in samples)
            foreach (var process in sample.Processes)
                if (process.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime) retained.TryAdd(process.Identity, process);
            if (extraRetained is not null)
            foreach (var process in extraRetained)
                if (process.LifetimeDomain == ProcessLifetimeIdentity.WindowsFileTime) retained.TryAdd(process.Identity, process);
        }

        var mapped = new IncidentEventMapping[result.Events.Count];
        var containing = new List<int>();
        for (var eventIndex = 0; eventIndex < result.Events.Count; eventIndex++)
        {
            var item = result.Events[eventIndex];
            var (nearest, distance, moment, approximate) = Locate(item.Timestamp, samples, containing);
            int? selected = nearest is { } index && distance <= SelectionSeconds ? index : null;
            mapped[eventIndex] = new IncidentEventMapping(item, moment, selected, approximate,
                retained is null ? null : LinkedProcess(item, retained));
        }

        return mapped;
    }

    /// <summary>
    ///     The nearest non-gap observation and the event's chart moment, in one pass over the samples. Wall clocks can
    ///     jump back and repeat an interval, so timestamps are not assumed sorted: every interval containing the event
    ///     is kept, the shortest places it, and any that disagree mark it approximate.
    /// </summary>
    private static (int? Nearest, double Distance, double Moment, bool Approximate) Locate(DateTimeOffset timestamp,
        IReadOnlyList<MonitorSample> samples, List<int> containing)
    {
        int? nearest = null;
        var distance = double.PositiveInfinity;
        var eventTicks = timestamp.UtcTicks;
        var pair = -1;
        var bestSpan = long.MaxValue;
        containing.Clear();
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            if (!sample.IsGap)
            {
                var candidate = Math.Abs((sample.Timestamp - timestamp).TotalSeconds);
                if (candidate < distance)
                {
                    nearest = i;
                    distance = candidate;
                }
            }

            if (i == samples.Count - 1) break;
            var start = sample.Timestamp.UtcTicks;
            var end = samples[i + 1].Timestamp.UtcTicks;
            var low = Math.Min(start, end);
            var high = Math.Max(start, end);
            var span = high - low;
            if (span == 0 || eventTicks < low || eventTicks > high) continue;
            containing.Add(i);
            if (span >= bestSpan) continue;
            pair = i;
            bestSpan = span;
        }

        if (samples.Count == 0) return (nearest, distance, 0, true);
        if (samples.Count == 1) return (nearest, distance, ProcessRanking.Moment(samples[0]), true);
        if (pair < 0) return (nearest, distance, ProcessRanking.Moment(samples[nearest ?? 0]), true);

        var (moment, approximate) = Place(timestamp, samples, containing, pair);
        return (nearest, distance, moment, approximate);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double Moment, bool Approximate) Place(DateTimeOffset timestamp,
        IReadOnlyList<MonitorSample> samples, List<int> containing, int pair)
    {
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
        foreach (var i in containing)
        {
            var left = samples[i];
            var right = samples[i + 1];
            var duration = (right.Timestamp - left.Timestamp).TotalSeconds;
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
