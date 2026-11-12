using WinMon.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Attributes delayed network events to bounded Windows process lifetimes.</summary>
internal sealed class NetworkTrafficLedger
{
    private const int RateWindowSeconds = 5;
    private const int DeliveryHoldbackSeconds = 3;
    private const int RetentionSeconds = 30;
    private const int MaximumFlows = 8_192;

    private readonly long _frequency;
    private readonly int _maximumLifetimes;
    private readonly Dictionary<int, Lifetime> _active = [];
    private readonly List<Lifetime> _lifetimes = [];
    private readonly Dictionary<int, List<Lifetime>> _lifetimesByProcess = [];
    private readonly Dictionary<int, Tombstone> _tombstones = [];
    private Guid _generation;
    private long _sequence;
    private long _startedQpc;
    private long _lastSnapshotQpc = long.MinValue;
    private long _prunedBucket = -1;
    private long _lastEventsLost;
    private long _eventsLost;
    private long _lateEvents;
    private long _ratesUnavailableUntilQpc;
    private bool _incomplete;
    private bool _capacityExceeded;
    private bool _discontinuity;
    private int _flowCount;
    private ProcessNetworkIncompleteReason _incompleteReasons;

    internal NetworkTrafficLedger(long frequency, long startedQpc, int maximumLifetimes = 8192)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (startedQpc < 0) throw new ArgumentOutOfRangeException(nameof(startedQpc));
        if (maximumLifetimes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLifetimes));
        _frequency = frequency;
        _startedQpc = startedQpc;
        _maximumLifetimes = maximumLifetimes;
        _generation = Guid.NewGuid();
    }

    internal bool Register(int processId, long creationTicks, string ownerSid, long startedQpc)
    {
        ValidateIdentity(processId, creationTicks, ownerSid, startedQpc);
        Prune(startedQpc);
        if (_tombstones.TryGetValue(processId, out var previous) &&
            (startedQpc < previous.TimestampQpc ||
             (startedQpc == previous.TimestampQpc && creationTicks == previous.CreationTicks)))
        {
            MarkIncomplete(startedQpc, ProcessNetworkIncompleteReason.AmbiguousLifetime);
            return false;
        }

        if (_lifetimes.Count >= _maximumLifetimes)
        {
            _capacityExceeded = true;
            MarkIncomplete(startedQpc, ProcessNetworkIncompleteReason.CapacityExceeded);
            return false;
        }

        if (_active.TryGetValue(processId, out var active))
        {
            if (startedQpc <= active.StartedQpc || creationTicks == active.CreationTicks)
            {
                MarkIncomplete(startedQpc, ProcessNetworkIncompleteReason.AmbiguousLifetime);
                return false;
            }

            active.EndedQpc = startedQpc;
            _active.Remove(processId);
            RecordTombstone(processId, startedQpc, active.CreationTicks);
        }

        var lifetime = new Lifetime(processId, creationTicks, ownerSid, startedQpc);
        _active.Add(processId, lifetime);
        _lifetimes.Add(lifetime);
        if (!_lifetimesByProcess.TryGetValue(processId, out var processLifetimes))
        {
            processLifetimes = [];
            _lifetimesByProcess.Add(processId, processLifetimes);
        }

        processLifetimes.Add(lifetime);
        RecordTombstone(processId, startedQpc, creationTicks);
        return true;
    }

    internal void End(int processId, long eventQpc)
    {
        if (processId <= 0 || eventQpc < 0)
            throw new ArgumentOutOfRangeException(processId <= 0 ? nameof(processId) : nameof(eventQpc));
        if (!_active.TryGetValue(processId, out var lifetime) || eventQpc <= lifetime.StartedQpc)
        {
            MarkIncomplete(eventQpc);
            return;
        }

        _active.Remove(processId);
        lifetime.EndedQpc = eventQpc;
        RecordTombstone(processId, eventQpc, lifetime.CreationTicks);
    }

    internal bool Add(int processId, long eventQpc, long arrivalQpc, bool send, int bytes,
        TransportProtocol? protocol = null, string localEndpoint = "", string remoteEndpoint = "")
    {
        if (processId <= 0 || eventQpc < 0 || arrivalQpc < 0 || bytes <= 0)
        {
            MarkIncomplete(Math.Max(0, arrivalQpc), ProcessNetworkIncompleteReason.InvalidEvent);
            return false;
        }

        if (eventQpc > arrivalQpc || arrivalQpc - eventQpc > RetentionTicks)
        {
            MarkIncomplete(arrivalQpc, ProcessNetworkIncompleteReason.EventTimestamp);
            return false;
        }

        if (arrivalQpc - eventQpc > HoldbackTicks) _lateEvents++;
        var lifetime = Resolve(processId, eventQpc);
        if (lifetime is null)
        {
            MarkIncomplete(arrivalQpc);
            return false;
        }

        var canTrackFlow = protocol is null || localEndpoint.Length == 0 || remoteEndpoint.Length == 0 ||
                           lifetime.HasFlow(protocol.Value, localEndpoint, remoteEndpoint) || _flowCount < MaximumFlows;
        if (!canTrackFlow) lifetime.FlowCapacityExceeded = true;
        if (lifetime.Add(eventQpc / _frequency, send, bytes, protocol, localEndpoint, remoteEndpoint, canTrackFlow))
            _flowCount++;
        return true;
    }

    internal void ReportLoss(long cumulativeEventsLost, long observedAtQpc)
    {
        if (cumulativeEventsLost < 0 || observedAtQpc < 0)
            throw new ArgumentOutOfRangeException(cumulativeEventsLost < 0
                ? nameof(cumulativeEventsLost)
                : nameof(observedAtQpc));
        if (cumulativeEventsLost < _lastEventsLost)
        {
            Discontinue(observedAtQpc, ProcessNetworkIncompleteReason.NativeLoss);
            _lastEventsLost = cumulativeEventsLost;
            _eventsLost = cumulativeEventsLost;
            return;
        }

        if (cumulativeEventsLost == _lastEventsLost) return;
        _eventsLost = cumulativeEventsLost;
        _lastEventsLost = cumulativeEventsLost;
        MarkIncomplete(observedAtQpc, ProcessNetworkIncompleteReason.NativeLoss);
        _ratesUnavailableUntilQpc = Math.Max(_ratesUnavailableUntilQpc, observedAtQpc);
    }

    internal void MarkIncomplete(long observedAtQpc,
        ProcessNetworkIncompleteReason reason = ProcessNetworkIncompleteReason.AmbiguousLifetime)
    {
        if (observedAtQpc < 0) throw new ArgumentOutOfRangeException(nameof(observedAtQpc));
        _incomplete = true;
        _incompleteReasons |= reason;
        _ratesUnavailableUntilQpc = Math.Max(_ratesUnavailableUntilQpc, observedAtQpc);
    }

    /// <summary>Checks an event-time lifetime without treating a late event as a current live PID.</summary>
    internal bool HasLifetime(int processId, long eventQpc)
    {
        return processId > 0 && eventQpc >= 0 && Resolve(processId, eventQpc) is not null;
    }

    /// <summary>Checks whether an event belongs to a retained, ended lifetime rather than the current PID.</summary>
    internal bool HasExitedLifetime(int processId, long eventQpc)
    {
        return processId > 0 && eventQpc >= 0 &&
               Resolve(processId, eventQpc)?.EndedQpc is not null;
    }

    internal ProcessNetworkSnapshot Snapshot(string ownerSid, long nowQpc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSid);
        if (nowQpc < 0) throw new ArgumentOutOfRangeException(nameof(nowQpc));
        if (_lastSnapshotQpc != long.MinValue && nowQpc < _lastSnapshotQpc) Discontinue(nowQpc);
        _lastSnapshotQpc = nowQpc;
        Prune(nowQpc);

        var windowEnd = Math.Max(0, nowQpc / _frequency * _frequency - HoldbackTicks);
        var windowStart = Math.Max(0, windowEnd - RateWindowTicks);
        var ratesAvailable = windowEnd > _startedQpc + RateWindowTicks && windowStart >= _ratesUnavailableUntilQpc;
        var quality = ProcessNetworkQuality.Provisional;
        if (!ratesAvailable) quality |= ProcessNetworkQuality.WarmingUp;
        if (_incomplete) quality |= ProcessNetworkQuality.Incomplete;
        if (_capacityExceeded) quality |= ProcessNetworkQuality.CapacityExceeded;
        if (_discontinuity) quality |= ProcessNetworkQuality.Discontinuity;

        var owned = _lifetimes.Where(lifetime => string.Equals(lifetime.OwnerSid, ownerSid, StringComparison.Ordinal)).ToArray();
        var rows = owned.Where(lifetime => lifetime.ReceivedBytes != 0 || lifetime.SentBytes != 0)
            .Select(lifetime => lifetime.Snapshot(windowStart, windowEnd, ratesAvailable, _frequency))
            .OrderBy(row => row.ProcessId).ThenBy(row => row.CreationTicks).ToArray();
        var flowQuality = quality;
        if (owned.Any(lifetime => lifetime.FlowCapacityExceeded))
            flowQuality |= ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded;
        return new ProcessNetworkSnapshot(_generation, checked(++_sequence), nowQpc, _frequency, windowStart, windowEnd,
            quality, _eventsLost, _lateEvents, rows)
        {
            IncompleteReasons = _incompleteReasons,
            FlowTrafficSupported = true,
            FlowQuality = flowQuality,
            Flows = owned.SelectMany(lifetime => lifetime.FlowSnapshot(windowStart, windowEnd, ratesAvailable, _frequency))
                .OrderBy(row => row.ProcessId).ThenBy(row => row.CreationTicks).ThenBy(row => row.Protocol)
                .ThenBy(row => row.LocalEndpoint, StringComparer.Ordinal).ThenBy(row => row.RemoteEndpoint, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private Lifetime? Resolve(int processId, long eventQpc)
    {
        if (_active.TryGetValue(processId, out var active) && active.Contains(eventQpc)) return active;
        if (!_lifetimesByProcess.TryGetValue(processId, out var lifetimes)) return null;
        foreach (var lifetime in lifetimes)
            if (lifetime.Contains(eventQpc))
                return lifetime;
        return null;
    }

    private void Prune(long nowQpc)
    {
        if (nowQpc < _startedQpc) return;
        var oldestBucket = Math.Max(0, nowQpc / _frequency - RetentionSeconds);
        // Buckets only expire when the retention second advances; Register calls this per process start.
        var pruneBuckets = oldestBucket > _prunedBucket;
        if (pruneBuckets) _prunedBucket = oldestBucket;
        for (var index = _lifetimes.Count - 1; index >= 0; index--)
        {
            var lifetime = _lifetimes[index];
            if (pruneBuckets) lifetime.PruneBuckets(oldestBucket);
            if (lifetime.EndedQpc is { } ended && nowQpc - ended > RetentionTicks)
            {
                _lifetimes.RemoveAt(index);
                _flowCount -= lifetime.FlowCount;
                var processLifetimes = _lifetimesByProcess[lifetime.ProcessId];
                processLifetimes.Remove(lifetime);
                if (processLifetimes.Count == 0) _lifetimesByProcess.Remove(lifetime.ProcessId);
            }
        }
    }

    private void RecordTombstone(int processId, long timestampQpc, long creationTicks)
    {
        if (!_tombstones.ContainsKey(processId) && _tombstones.Count >= _maximumLifetimes)
        {
            var oldest = _tombstones.MinBy(entry => entry.Value.TimestampQpc);
            _tombstones.Remove(oldest.Key);
            // Expired PID history cannot affect the retained traffic window.
            if (timestampQpc - oldest.Value.TimestampQpc <= RetentionTicks)
            {
                _capacityExceeded = true;
                MarkIncomplete(timestampQpc, ProcessNetworkIncompleteReason.CapacityExceeded);
            }
        }

        if (!_tombstones.TryGetValue(processId, out var existing) || timestampQpc >= existing.TimestampQpc)
            _tombstones[processId] = new Tombstone(timestampQpc, creationTicks);
    }

    private void Discontinue(long nowQpc,
        ProcessNetworkIncompleteReason reason = ProcessNetworkIncompleteReason.ClockDiscontinuity)
    {
        _generation = Guid.NewGuid();
        _discontinuity = true;
        _incomplete = true;
        _incompleteReasons |= reason;
        _active.Clear();
        _lifetimes.Clear();
        _lifetimesByProcess.Clear();
        _tombstones.Clear();
        _flowCount = 0;
        _startedQpc = nowQpc;
        _lastEventsLost = 0;
        _eventsLost = 0;
        _lateEvents = 0;
        _ratesUnavailableUntilQpc = nowQpc;
        _prunedBucket = -1;
    }

    private long HoldbackTicks => checked(DeliveryHoldbackSeconds * _frequency);
    private long RateWindowTicks => checked(RateWindowSeconds * _frequency);
    private long RetentionTicks => checked(RetentionSeconds * _frequency);

    private static void ValidateIdentity(int processId, long creationTicks, string ownerSid, long startedQpc)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (creationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(creationTicks));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSid);
        if (startedQpc < 0) throw new ArgumentOutOfRangeException(nameof(startedQpc));
    }

    private sealed class Lifetime(int processId, long creationTicks, string ownerSid, long startedQpc)
    {
        private readonly Dictionary<long, Bytes> _buckets = [];
        private readonly Dictionary<FlowKey, Flow> _flows = [];
        internal int ProcessId { get; } = processId;
        internal long CreationTicks { get; } = creationTicks;
        internal string OwnerSid { get; } = ownerSid;
        internal long StartedQpc { get; } = startedQpc;
        internal long? EndedQpc { get; set; }
        internal long ReceivedBytes { get; private set; }
        internal long SentBytes { get; private set; }
        internal bool FlowCapacityExceeded { get; set; }
        internal int FlowCount => _flows.Count;

        internal bool Contains(long eventQpc)
        {
            return eventQpc > StartedQpc && (EndedQpc is null || eventQpc < EndedQpc);
        }

        internal bool Add(long second, bool send, int bytes, TransportProtocol? protocol, string localEndpoint,
            string remoteEndpoint, bool trackFlow)
        {
            if (!_buckets.TryGetValue(second, out var bucket))
            {
                bucket = new Bytes();
                _buckets.Add(second, bucket);
            }

            if (send)
            {
                SentBytes = checked(SentBytes + bytes);
                bucket.Sent = checked(bucket.Sent + bytes);
            }
            else
            {
                ReceivedBytes = checked(ReceivedBytes + bytes);
                bucket.Received = checked(bucket.Received + bytes);
            }

            if (!trackFlow || protocol is not { } transport || localEndpoint.Length == 0 || remoteEndpoint.Length == 0)
                return false;
            var key = new FlowKey(transport, localEndpoint, remoteEndpoint);
            if (!_flows.TryGetValue(key, out var flow))
            {
                flow = new Flow(key);
                _flows.Add(key, flow);
                flow.Add(second, send, bytes);
                return true;
            }

            flow.Add(second, send, bytes);
            return false;
        }

        internal bool HasFlow(TransportProtocol protocol, string localEndpoint, string remoteEndpoint)
        {
            return _flows.ContainsKey(new FlowKey(protocol, localEndpoint, remoteEndpoint));
        }

        internal void PruneBuckets(long oldestSecond)
        {
            // Dictionary allows Remove during enumeration since .NET Core 3.0.
            foreach (var second in _buckets.Keys)
                if (second < oldestSecond)
                    _buckets.Remove(second);
            foreach (var flow in _flows.Values) flow.PruneBuckets(oldestSecond);
            // shortcut: retain flow keys until owning lifetime expires to preserve cumulative identity; recycle with per-flow epoch if sustained churn exhausts the cap.
        }

        internal ProcessNetworkReading Snapshot(long windowStartQpc, long windowEndQpc, bool ratesAvailable,
            long frequency)
        {
            long received = 0;
            long sent = 0;
            if (ratesAvailable)
            {
                var startSecond = windowStartQpc / frequency;
                var endSecond = windowEndQpc / frequency;
                foreach (var pair in _buckets)
                {
                    if (pair.Key < startSecond || pair.Key >= endSecond) continue;
                    received = checked(received + pair.Value.Received);
                    sent = checked(sent + pair.Value.Sent);
                }
            }

            const double windowSeconds = RateWindowSeconds;
            return new ProcessNetworkReading(ProcessId, CreationTicks, ReceivedBytes, SentBytes,
                ratesAvailable ? received / windowSeconds : null, ratesAvailable ? sent / windowSeconds : null);
        }

        internal IEnumerable<ProcessNetworkFlowReading> FlowSnapshot(long windowStartQpc, long windowEndQpc,
            bool ratesAvailable, long frequency)
        {
            foreach (var flow in _flows.Values)
                yield return flow.Snapshot(ProcessId, CreationTicks, windowStartQpc, windowEndQpc, ratesAvailable,
                    frequency);
        }

        internal sealed class Bytes
        {
            internal long Received;
            internal long Sent;
        }

        private readonly record struct FlowKey(TransportProtocol Protocol, string LocalEndpoint, string RemoteEndpoint);

        private sealed class Flow(FlowKey key)
        {
            private readonly Dictionary<long, Bytes> _buckets = [];
            private long _received;
            private long _sent;

            internal void Add(long second, bool send, int bytes)
            {
                if (!_buckets.TryGetValue(second, out var bucket))
                {
                    bucket = new Bytes();
                    _buckets.Add(second, bucket);
                }

                if (send) { _sent = checked(_sent + bytes); bucket.Sent = checked(bucket.Sent + bytes); }
                else { _received = checked(_received + bytes); bucket.Received = checked(bucket.Received + bytes); }
            }

            internal void PruneBuckets(long oldestSecond)
            {
                foreach (var second in _buckets.Keys)
                    if (second < oldestSecond) _buckets.Remove(second);
            }

            internal ProcessNetworkFlowReading Snapshot(int processId, long creationTicks, long windowStartQpc,
                long windowEndQpc, bool ratesAvailable, long frequency)
            {
                long received = 0, sent = 0;
                if (ratesAvailable)
                    foreach (var pair in _buckets)
                        if (pair.Key >= windowStartQpc / frequency && pair.Key < windowEndQpc / frequency)
                        {
                            received = checked(received + pair.Value.Received);
                            sent = checked(sent + pair.Value.Sent);
                        }
                return new ProcessNetworkFlowReading(processId, creationTicks, key.Protocol, key.LocalEndpoint,
                    key.RemoteEndpoint, _received, _sent,
                    ratesAvailable ? received / (double)RateWindowSeconds : null,
                    ratesAvailable ? sent / (double)RateWindowSeconds : null);
            }
        }
    }

    private readonly record struct Tombstone(long TimestampQpc, long CreationTicks);
}
