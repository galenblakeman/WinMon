namespace WinMon.Monitoring;

/// <summary>Client-session process-network totals adjusted from service-generation counters.</summary>
public sealed record ProcessNetworkFrame(long TimestampQpc, long Frequency, ProcessNetworkQuality Quality,
    IReadOnlyList<ProcessNetworkReading> Processes)
{
    /// <summary>Latest portable process metadata for current or retained network lifetimes.</summary>
    public IReadOnlyList<ProcessUsage> Owners { get; init; } = [];

    /// <summary>Wire lifetimes that are current; retained-only lifetimes keep totals but have no live rate.</summary>
    public IReadOnlySet<ProcessLifetimeIdentity>? CurrentLifetimes { get; init; }
}

/// <summary>
/// Converts service-generation counters into totals observed by one client monitoring session.
/// Rates remain service event-time rates; this class never derives them from snapshot arrival time.
/// </summary>
public sealed class ProcessNetworkSession
{
    private const int MaximumLifetimes = 8192;
    private static readonly IReadOnlySet<ProcessLifetimeIdentity> EmptyLifetimes = new HashSet<ProcessLifetimeIdentity>();
    private readonly Dictionary<ProcessLifetimeIdentity, Lifetime> _lifetimes = [];
    private readonly Dictionary<ProcessLifetimeIdentity, ProcessNetworkReading> _lastReadings = [];
    private Guid _generation;
    private Guid _subscriptionId;
    private long _lastSequence;
    private long _lastTimestampQpc;
    private long _frequency;
    private long _lastEventsLost;
    private long _lastDroppedEvents;
    private long _ratesBlockedUntilQpc;
    private ProcessNetworkQuality _lastQuality;
    private bool _hasSnapshot;
    private bool _incomplete;

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
            return Frame(snapshot, quality | ProcessNetworkQuality.Incomplete, _lastReadings.Values, nowQpc,
                forceNoRates: true, currentLifetimes: EmptyLifetimes);

        var generationChanged = _hasSnapshot && snapshot.Generation != _generation;
        var reconnected = _hasSnapshot && _subscriptionId != Guid.Empty && snapshot.SubscriptionId != Guid.Empty &&
            snapshot.SubscriptionId != _subscriptionId;
        var clockChanged = _hasSnapshot && snapshot.Frequency != _frequency;
        var older = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence < _lastSequence;
        var duplicate = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence == _lastSequence;
        if (older || duplicate)
            return Frame(snapshot, quality, _lastReadings.Values, nowQpc, forceNoRates: older,
                currentLifetimes: older ? EmptyLifetimes : SnapshotIdentities(snapshot));

        var gap = _hasSnapshot && !generationChanged && !reconnected && snapshot.Sequence != _lastSequence + 1;
        var qualityIssue = (snapshot.Quality & (ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded |
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
            foreach (var lifetime in _lifetimes.Values) lifetime.ResetGeneration();
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
            if (lifetime.HasBaseline && (reading.ReceivedBytes < lifetime.LastReceived || reading.SentBytes < lifetime.LastSent))
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
            if (!currentIdentities.Contains(identity)) readings.Add(reading with
            { ReceiveBytesPerSecond = null, SendBytesPerSecond = null });

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
        return Frame(snapshot, quality, readings, nowQpc, forceNoRates: generationChanged || reconnected || clockChanged,
            currentLifetimes: currentIdentities, warmingUp: initial);
    }

    /// <summary>Publishes retained session totals while the service has no usable snapshot.</summary>
    public ProcessNetworkFrame Unavailable(long nowQpc, long frequency)
    {
        if (nowQpc < 0) throw new ArgumentOutOfRangeException(nameof(nowQpc));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _incomplete = true;
        _ratesBlockedUntilQpc = Math.Max(_ratesBlockedUntilQpc, nowQpc);
        return new ProcessNetworkFrame(nowQpc, frequency,
            ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete,
            _lastReadings.Values.Select(reading => reading with
            { ReceiveBytesPerSecond = null, SendBytesPerSecond = null }).ToArray()) { CurrentLifetimes = EmptyLifetimes };
    }

    /// <summary>Starts a new client monitoring session without retaining observed traffic.</summary>
    public void Reset()
    {
        _lifetimes.Clear();
        _lastReadings.Clear();
        _generation = _subscriptionId = Guid.Empty;
        _lastSequence = _lastTimestampQpc = _frequency = _lastEventsLost = _lastDroppedEvents = _ratesBlockedUntilQpc = 0;
        _lastQuality = ProcessNetworkQuality.None;
        _hasSnapshot = _incomplete = false;
    }

    private ProcessNetworkFrame Frame(ProcessNetworkSnapshot snapshot, ProcessNetworkQuality quality,
        IEnumerable<ProcessNetworkReading> readings, long nowQpc, bool forceNoRates,
        IReadOnlySet<ProcessLifetimeIdentity> currentLifetimes, bool warmingUp = false)
    {
        var stale = nowQpc < snapshot.TimestampQpc || (nowQpc - snapshot.TimestampQpc) / (double)snapshot.Frequency > 3;
        var freshWindow = snapshot.WindowStartQpc >= _ratesBlockedUntilQpc && snapshot.WindowEndQpc > snapshot.WindowStartQpc;
        var ratesAvailable = !forceNoRates && !stale && freshWindow &&
            !quality.HasFlag(ProcessNetworkQuality.Unavailable) && !quality.HasFlag(ProcessNetworkQuality.WarmingUp);
        if (stale) quality |= ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete;
        else if (!freshWindow) quality |= warmingUp ? ProcessNetworkQuality.WarmingUp : ProcessNetworkQuality.Incomplete;
        else if (forceNoRates) quality |= ProcessNetworkQuality.Incomplete;
        if (_incomplete) quality |= ProcessNetworkQuality.Incomplete;
        return new ProcessNetworkFrame(snapshot.TimestampQpc, snapshot.Frequency, quality,
            readings.Select(reading => ratesAvailable && currentLifetimes.Contains(new ProcessLifetimeIdentity(
                reading.ProcessId, reading.CreationTicks, ProcessLifetimeIdentity.WindowsFileTime)) ? reading : reading with
            { ReceiveBytesPerSecond = null, SendBytesPerSecond = null }).ToArray()) { CurrentLifetimes = currentLifetimes };
    }

    private static IReadOnlySet<ProcessLifetimeIdentity> SnapshotIdentities(ProcessNetworkSnapshot snapshot) =>
        snapshot.Processes.Where(reading => reading is { ProcessId: > 0, CreationTicks: > 0 })
            .Select(reading => new ProcessLifetimeIdentity(reading.ProcessId, reading.CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime)).ToHashSet();

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

        internal void ResetGeneration() => HasBaseline = false;

        private static (long Total, bool Clipped) SaturatingAdd(long total, long delta) => delta > long.MaxValue - total
            ? (long.MaxValue, true) : (total + delta, false);
    }
}
