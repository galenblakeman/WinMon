// Deserialized payloads can contain null despite non-nullable model annotations.
// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract

namespace WinMon.Monitoring;

/// <summary>Client-session process-network totals adjusted from service-generation counters.</summary>
public sealed record ProcessNetworkFrame(
    long TimestampQpc,
    long Frequency,
    ProcessNetworkQuality Quality,
    IReadOnlyList<ProcessNetworkReading> Processes)
{
    /// <summary>Observed endpoint-pair traffic; consult <see cref="FlowTrafficSupported"/> to distinguish unsupported from empty.</summary>
    public IReadOnlyList<ProcessNetworkFlowReading> Flows { get; init; } = [];

    /// <summary>Event-time bounds for the delayed rate window represented by this frame.</summary>
    public long WindowStartQpc { get; init; }
    public long WindowEndQpc { get; init; }

    /// <summary>Latest portable process metadata for current or retained network lifetimes.</summary>
    public IReadOnlyList<ProcessUsage> Owners { get; init; } = [];

    /// <summary>Wire lifetimes that are current; retained-only lifetimes keep totals but have no live rate.</summary>
    public IReadOnlySet<ProcessLifetimeIdentity>? CurrentLifetimes { get; init; }

    /// <summary>Flow identities that were present in the current service snapshot.</summary>
    public IReadOnlySet<ProcessNetworkFlowIdentity>? CurrentFlows { get; init; }

    /// <summary>Whether the connected service can provide endpoint-pair traffic.</summary>
    public bool FlowTrafficSupported { get; init; }

    /// <summary>Coverage state for endpoint-pair traffic, separate from process totals.</summary>
    public ProcessNetworkQuality FlowQuality { get; init; }
}

/// <summary>Stable endpoint-pair identity within one Windows process lifetime.</summary>
public sealed record ProcessNetworkFlowIdentity(int ProcessId, long CreationTicks, TransportProtocol Protocol,
    string LocalEndpoint, string RemoteEndpoint);

/// <summary>
/// Converts service-generation counters into totals observed by one client monitoring session.
/// Rates remain service event-time rates; this class never derives them from snapshot arrival time.
/// </summary>
public sealed class ProcessNetworkSession
{
    private const int MaximumLifetimes = 8192;
    private const int FlowRetentionSeconds = 30;

    private static readonly IReadOnlySet<ProcessLifetimeIdentity> EmptyLifetimes =
        new HashSet<ProcessLifetimeIdentity>();
    private static readonly IReadOnlySet<ProcessNetworkFlowIdentity> EmptyFlows =
        new HashSet<ProcessNetworkFlowIdentity>();

    private readonly Dictionary<ProcessLifetimeIdentity, Lifetime> _lifetimes = [];
    private readonly Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> _lastReadings = [];
    private readonly Dictionary<ProcessNetworkFlowIdentity, FlowLifetime> _flows = [];
    private readonly Dictionary<ProcessNetworkFlowIdentity, ProcessNetworkFlowReading> _lastFlows = [];
    private Guid _generation;
    private Guid _subscriptionId;
    private long _lastSequence;
    private long _lastTimestampQpc;
    private long _frequency;
    private long _lastEventsLost;
    private long _lastDroppedEvents;
    private long _ratesBlockedUntilQpc;
    private long _flowRatesBlockedUntilQpc;
    private ProcessNetworkQuality _lastQuality;
    private bool _hasSnapshot;
    private bool _incomplete;
    private bool _flowTrafficSupported;

    /// <summary>Adds the most recent service snapshot using the service QPC clock for staleness.</summary>
    public ProcessNetworkFrame Add(ProcessNetworkSnapshot snapshot, long nowQpc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (nowQpc < 0) throw new ArgumentOutOfRangeException(nameof(nowQpc));

        var quality = snapshot.Quality;
        if (snapshot.Frequency <= 0 || snapshot.TimestampQpc < 0 || snapshot.WindowStartQpc < 0 ||
            snapshot.WindowEndQpc < snapshot.WindowStartQpc || snapshot.WindowEndQpc > snapshot.TimestampQpc ||
            snapshot.Processes is null)
            return Unavailable(nowQpc, snapshot.Frequency > 0 ? snapshot.Frequency : _frequency > 0 ? _frequency : 1);

        var initial = !_hasSnapshot;
        var olderTimestamp = _hasSnapshot && snapshot.TimestampQpc < _lastTimestampQpc;
        if (olderTimestamp)
            return Frame(snapshot, quality | ProcessNetworkQuality.Incomplete,
                snapshot.FlowTrafficSupported ? snapshot.FlowQuality | ProcessNetworkQuality.Incomplete :
                ProcessNetworkQuality.None, _lastReadings.Values, _lastFlows.Values, nowQpc, true,
                EmptyLifetimes, EmptyFlows);

        var generationChanged = _hasSnapshot && snapshot.Generation != _generation;
        var reconnected = _hasSnapshot && _subscriptionId != Guid.Empty && snapshot.SubscriptionId != Guid.Empty &&
                          snapshot.SubscriptionId != _subscriptionId;
        var clockChanged = _hasSnapshot && snapshot.Frequency != _frequency;
        var older = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence < _lastSequence;
        var duplicate = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence == _lastSequence;
        if (older || duplicate)
            return Frame(snapshot, quality,
                snapshot.FlowTrafficSupported ? snapshot.FlowQuality : ProcessNetworkQuality.None,
                _lastReadings.Values, _lastFlows.Values, nowQpc, older,
                older ? EmptyLifetimes : SnapshotIdentities(snapshot),
                older ? EmptyFlows : SnapshotFlowIdentities(snapshot));

        var gap = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence != _lastSequence + 1;
        var qualityIssue = (snapshot.Quality & (ProcessNetworkQuality.Incomplete |
                                                ProcessNetworkQuality.CapacityExceeded |
                                                ProcessNetworkQuality.Discontinuity)) != 0;
        var discontinuityStarted = snapshot.Quality.HasFlag(ProcessNetworkQuality.Discontinuity) &&
                                   !_lastQuality.HasFlag(ProcessNetworkQuality.Discontinuity);
        var interrupted = generationChanged || reconnected || clockChanged || gap || discontinuityStarted ||
                          snapshot.EventsLost != _lastEventsLost || snapshot.DroppedEvents != _lastDroppedEvents;
        if (interrupted)
        {
            _incomplete = true;
            _ratesBlockedUntilQpc = Math.Max(_ratesBlockedUntilQpc, snapshot.TimestampQpc);
            quality |= ProcessNetworkQuality.Incomplete;
        }

        if (qualityIssue)
        {
            _incomplete = true;
            quality |= ProcessNetworkQuality.Incomplete;
        }

        if (generationChanged || reconnected || clockChanged)
            quality |= ProcessNetworkQuality.Discontinuity;

        if (generationChanged || reconnected)
        {
            foreach (var lifetime in _lifetimes.Values)
                lifetime.ResetGeneration();
            foreach (var flow in _flows.Values)
                flow.Totals.ResetGeneration();
        }

        var readings = new List<ProcessNetworkReading>(snapshot.Processes.Count);
        var currentIdentities = new HashSet<ProcessLifetimeIdentity>();
        foreach (var reading in snapshot.Processes)
        {
            if (reading is null || reading.ProcessId <= 0 || reading.CreationTicks <= 0 ||
                reading.ReceivedBytes < 0 || reading.SentBytes < 0)
            {
                quality |= ProcessNetworkQuality.Incomplete;
                _incomplete = true;
                continue;
            }

            var identity = new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime);
            currentIdentities.Add(identity);
            if (!_lifetimes.TryGetValue(identity, out var lifetime))
            {
                if (_lifetimes.Count == MaximumLifetimes)
                {
                    quality |= ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded;
                    _incomplete = true;
                    continue;
                }

                lifetime = new Lifetime();
                _lifetimes.Add(identity, lifetime);
            }

            if (lifetime.HasBaseline &&
                (reading.ReceivedBytes < lifetime.LastReceived || reading.SentBytes < lifetime.LastSent))
            {
                _incomplete = true;
                quality |= ProcessNetworkQuality.Incomplete;
                _ratesBlockedUntilQpc = Math.Max(_ratesBlockedUntilQpc, snapshot.TimestampQpc);
                lifetime.ResetGeneration();
            }

            if (lifetime.Add(reading.ReceivedBytes, reading.SentBytes))
            {
                _incomplete = true;
                quality |= ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded;
            }

            var adjusted = new ProcessNetworkReading(reading.ProcessId, reading.CreationTicks,
                lifetime.Received, lifetime.Sent, reading.ReceiveBytesPerSecond, reading.SendBytesPerSecond);
            _lastReadings[identity] = adjusted;
            readings.Add(adjusted);
        }

        foreach (var (identity, reading) in _lastReadings)
            if (!currentIdentities.Contains(identity))
                readings.Add(reading with
                {
                    ReceiveBytesPerSecond = null, SendBytesPerSecond = null
                });

        var currentFlows = SnapshotFlowIdentities(snapshot);
        var flowQuality = snapshot.FlowTrafficSupported ? snapshot.FlowQuality : ProcessNetworkQuality.None;
        var flows = AddFlows(snapshot, currentFlows, ref flowQuality);

        if (!_hasSnapshot) _ratesBlockedUntilQpc = Math.Max(_ratesBlockedUntilQpc, snapshot.TimestampQpc);
        _generation = snapshot.Generation;
        if (snapshot.SubscriptionId != Guid.Empty) _subscriptionId = snapshot.SubscriptionId;
        _lastSequence = snapshot.Sequence;
        _lastTimestampQpc = snapshot.TimestampQpc;
        _frequency = snapshot.Frequency;
        _lastEventsLost = snapshot.EventsLost;
        _lastDroppedEvents = snapshot.DroppedEvents;
        _lastQuality = snapshot.Quality;
        _hasSnapshot = true;
        _flowTrafficSupported = snapshot.FlowTrafficSupported;
        return Frame(snapshot, quality, flowQuality, readings, flows, nowQpc,
            generationChanged || reconnected || clockChanged, currentIdentities, currentFlows, initial);
    }

    /// <summary>Publishes retained session totals while the service has no usable snapshot.</summary>
    public ProcessNetworkFrame Unavailable(long nowQpc, long frequency)
    {
        if (nowQpc < 0) throw new ArgumentOutOfRangeException(nameof(nowQpc));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _incomplete = true;
        _ratesBlockedUntilQpc = Math.Max(_ratesBlockedUntilQpc, nowQpc);
        _flowRatesBlockedUntilQpc = Math.Max(_flowRatesBlockedUntilQpc, nowQpc);
        return new ProcessNetworkFrame(nowQpc, frequency,
            ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete,
            _lastReadings.Values.Select(reading => reading with
            {
                ReceiveBytesPerSecond = null, SendBytesPerSecond = null
            }).ToArray())
        {
            Flows = _lastFlows.Values.Select(reading => reading with
            {
                ReceiveBytesPerSecond = null, SendBytesPerSecond = null
            }).ToArray(),
            CurrentLifetimes = EmptyLifetimes,
            CurrentFlows = EmptyFlows,
            FlowTrafficSupported = _flowTrafficSupported,
            FlowQuality = ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete
        };
    }

    /// <summary>Starts a new client monitoring session without retaining observed traffic.</summary>
    public void Reset()
    {
        _lifetimes.Clear();
        _lastReadings.Clear();
        _flows.Clear();
        _lastFlows.Clear();
        _generation = _subscriptionId = Guid.Empty;
        _lastSequence = _lastTimestampQpc =
            _frequency = _lastEventsLost = _lastDroppedEvents = _ratesBlockedUntilQpc = 0;
        _flowRatesBlockedUntilQpc = 0;
        _lastQuality = ProcessNetworkQuality.None;
        _hasSnapshot = _incomplete = false;
        _flowTrafficSupported = false;
    }

    private ProcessNetworkFrame Frame(ProcessNetworkSnapshot snapshot, ProcessNetworkQuality quality,
        ProcessNetworkQuality flowQuality,
        IEnumerable<ProcessNetworkReading> readings, IEnumerable<ProcessNetworkFlowReading> flows, long nowQpc,
        bool forceNoRates, IReadOnlySet<ProcessLifetimeIdentity> currentLifetimes,
        IReadOnlySet<ProcessNetworkFlowIdentity> currentFlows, bool warmingUp = false)
    {
        var stale = nowQpc < snapshot.TimestampQpc || (nowQpc - snapshot.TimestampQpc) / (double)snapshot.Frequency > 3;
        var freshWindow = snapshot.WindowStartQpc >= _ratesBlockedUntilQpc &&
                          snapshot.WindowEndQpc > snapshot.WindowStartQpc;
        var ratesAvailable = !forceNoRates && !stale && freshWindow &&
                             !quality.HasFlag(ProcessNetworkQuality.Unavailable) &&
                             !quality.HasFlag(ProcessNetworkQuality.WarmingUp);
        if (stale) quality |= ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete;
        else if (!freshWindow)
            quality |= warmingUp ? ProcessNetworkQuality.WarmingUp : ProcessNetworkQuality.Incomplete;
        else if (forceNoRates) quality |= ProcessNetworkQuality.Incomplete;
        if (_incomplete) quality |= ProcessNetworkQuality.Incomplete;
        var flowRatesAvailable = ratesAvailable && snapshot.FlowTrafficSupported &&
                                 snapshot.WindowStartQpc >= _flowRatesBlockedUntilQpc &&
                                 !flowQuality.HasFlag(ProcessNetworkQuality.Unavailable) &&
                                 !flowQuality.HasFlag(ProcessNetworkQuality.WarmingUp);
        if (stale) flowQuality |= ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete;
        else if (!flowRatesAvailable && snapshot.FlowTrafficSupported)
            flowQuality |= ProcessNetworkQuality.Incomplete;
        return new ProcessNetworkFrame(snapshot.TimestampQpc, snapshot.Frequency, quality,
            readings.Select(reading => ratesAvailable && currentLifetimes.Contains(new ProcessLifetimeIdentity(
                reading.ProcessId, reading.CreationTicks, ProcessLifetimeIdentity.WindowsFileTime))
                ? reading
                : reading with
                {
                    ReceiveBytesPerSecond = null, SendBytesPerSecond = null
                }).ToArray())
        {
            Flows = flows.Select(reading => flowRatesAvailable && currentFlows.Contains(FlowIdentity(reading))
                ? reading
                : reading with { ReceiveBytesPerSecond = null, SendBytesPerSecond = null }).ToArray(),
            WindowStartQpc = snapshot.WindowStartQpc,
            WindowEndQpc = snapshot.WindowEndQpc,
            CurrentLifetimes = currentLifetimes,
            CurrentFlows = currentFlows,
            FlowTrafficSupported = snapshot.FlowTrafficSupported,
            FlowQuality = snapshot.FlowTrafficSupported ? flowQuality : ProcessNetworkQuality.None
        };
    }

    private static IReadOnlySet<ProcessLifetimeIdentity> SnapshotIdentities(ProcessNetworkSnapshot snapshot)
    {
        return snapshot.Processes.Where(reading => reading is { ProcessId: > 0, CreationTicks: > 0 })
            .Select(reading => new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime)).ToHashSet();
    }

    private IReadOnlyList<ProcessNetworkFlowReading> AddFlows(ProcessNetworkSnapshot snapshot,
        IReadOnlySet<ProcessNetworkFlowIdentity> currentFlows, ref ProcessNetworkQuality flowQuality)
    {
        if (!snapshot.FlowTrafficSupported || snapshot.Flows is null) return [];
        var flows = new List<ProcessNetworkFlowReading>(snapshot.Flows.Count);
        foreach (var reading in snapshot.Flows)
        {
            if (reading is null || reading.ProcessId <= 0 || reading.CreationTicks <= 0 ||
                !Enum.IsDefined(reading.Protocol) || reading.LocalEndpoint.Length == 0 ||
                reading.RemoteEndpoint.Length == 0 ||
                reading.ReceivedBytes < 0 || reading.SentBytes < 0)
            {
                flowQuality |= ProcessNetworkQuality.Incomplete;
                continue;
            }

            var identity = FlowIdentity(reading);
            if (!_flows.TryGetValue(identity, out var flow))
            {
                if (_flows.Count == MaximumLifetimes)
                {
                    flowQuality |= ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded;
                    continue;
                }

                flow = new FlowLifetime();
                _flows.Add(identity, flow);
                // The service rate includes traffic before this client baseline for a newly observed endpoint pair.
                _flowRatesBlockedUntilQpc = Math.Max(_flowRatesBlockedUntilQpc, snapshot.TimestampQpc);
            }

            if (flow.Totals.HasBaseline &&
                (reading.ReceivedBytes < flow.Totals.LastReceived || reading.SentBytes < flow.Totals.LastSent))
            {
                flowQuality |= ProcessNetworkQuality.Incomplete;
                _flowRatesBlockedUntilQpc = Math.Max(_flowRatesBlockedUntilQpc, snapshot.TimestampQpc);
                flow.Totals.ResetGeneration();
            }

            if (flow.Totals.Add(reading.ReceivedBytes, reading.SentBytes))
            {
                flowQuality |= ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded;
            }

            flow.SeenAtQpc = snapshot.TimestampQpc;
            var adjusted = reading with { ReceivedBytes = flow.Totals.Received, SentBytes = flow.Totals.Sent };
            _lastFlows[identity] = adjusted;
            flows.Add(adjusted);
        }

        PruneFlows(snapshot.TimestampQpc, snapshot.Frequency);
        foreach (var (identity, reading) in _lastFlows)
            if (!currentFlows.Contains(identity))
                flows.Add(reading with { ReceiveBytesPerSecond = null, SendBytesPerSecond = null });
        return flows;
    }

    private static IReadOnlySet<ProcessNetworkFlowIdentity> SnapshotFlowIdentities(ProcessNetworkSnapshot snapshot)
    {
        if (!snapshot.FlowTrafficSupported) return EmptyFlows;
        return (snapshot.Flows ?? []).Where(reading => reading is { ProcessId: > 0, CreationTicks: > 0,
                LocalEndpoint.Length: > 0, RemoteEndpoint.Length: > 0 } && Enum.IsDefined(reading.Protocol))
            .Select(FlowIdentity).ToHashSet();
    }

    private static ProcessNetworkFlowIdentity FlowIdentity(ProcessNetworkFlowReading reading)
    {
        return new ProcessNetworkFlowIdentity(reading.ProcessId, reading.CreationTicks, reading.Protocol,
            reading.LocalEndpoint, reading.RemoteEndpoint);
    }

    private void PruneFlows(long timestampQpc, long frequency)
    {
        var retention = checked(frequency * FlowRetentionSeconds);
        foreach (var (identity, flow) in _flows)
            if (timestampQpc - flow.SeenAtQpc > retention)
            {
                _flows.Remove(identity);
                _lastFlows.Remove(identity);
            }
    }

    private sealed class FlowLifetime
    {
        internal Lifetime Totals { get; } = new();
        internal long SeenAtQpc { get; set; }
    }

    private sealed class Lifetime
    {
        internal long LastReceived { get; private set; }
        internal long LastSent { get; private set; }
        internal long Received { get; private set; }
        internal long Sent { get; private set; }
        internal bool HasBaseline { get; private set; }

        internal bool Add(long received, long sent)
        {
            var clipped = false;
            if (HasBaseline)
            {
                (Received, clipped) = SaturatingAdd(Received, received - LastReceived);
                var sentResult = SaturatingAdd(Sent, sent - LastSent);
                Sent = sentResult.Total;
                clipped |= sentResult.Clipped;
            }

            LastReceived = received;
            LastSent = sent;
            HasBaseline = true;
            return clipped;
        }

        internal void ResetGeneration()
        {
            HasBaseline = false;
        }

        private static (long Total, bool Clipped) SaturatingAdd(long total, long delta)
        {
            return delta > long.MaxValue - total
                ? (long.MaxValue, true)
                : (total + delta, false);
        }
    }
}
