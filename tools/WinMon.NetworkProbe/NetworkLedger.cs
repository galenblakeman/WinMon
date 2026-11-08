namespace WinMon.NetworkProbe;

/// <summary>Bounds and attributes prototype network events to process lifetimes.</summary>
internal sealed class NetworkLedger
{
    private readonly Dictionary<int, Lifetime> _active = [];
    private readonly List<Lifetime> _lifetimes = [];
    private readonly Dictionary<int, DateTime> _latestLifecycleByProcess = [];
    private readonly int _maximumLifetimes;
    private readonly TimeSpan _exitedGrace;

    internal long CapacityRejectedStarts { get; private set; }
    internal long OutOfOrderStarts { get; private set; }
    internal long UnknownProcessEvents { get; private set; }
    internal long OutOfLifetimeEvents { get; private set; }
    internal long InvalidEvents { get; private set; }

    internal NetworkLedger(int maximumLifetimes = 8192, TimeSpan? exitedGrace = null)
    {
        if (maximumLifetimes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumLifetimes));
        _maximumLifetimes = maximumLifetimes;
        _exitedGrace = exitedGrace ?? TimeSpan.FromSeconds(10);
        if (_exitedGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(exitedGrace));
    }

    /// <summary>Registers a process lifetime. Older starts for an existing PID are ignored.</summary>
    internal bool Start(int processId, long creationTicks, DateTime timestampUtc)
    {
        Validate(processId, creationTicks, timestampUtc);
        Prune(timestampUtc);
        if (_latestLifecycleByProcess.TryGetValue(processId, out var latest) && timestampUtc <= latest)
        {
            OutOfOrderStarts++;
            return false;
        }
        if (_active.TryGetValue(processId, out var active))
        {
            if (active.CreationTicks == creationTicks || timestampUtc <= active.StartedAtUtc)
            {
                OutOfOrderStarts++;
                return false;
            }
            active.ExitedAtUtc = timestampUtc;
            _active.Remove(processId);
        }

        if (_lifetimes.Count >= _maximumLifetimes)
        {
            CapacityRejectedStarts++;
            return false;
        }
        var lifetime = new Lifetime(processId, creationTicks, timestampUtc);
        _active.Add(processId, lifetime);
        _lifetimes.Add(lifetime);
        RecordLifecycleTimestamp(processId, timestampUtc);
        return true;
    }

    /// <summary>Marks a known process as exited. Earlier exit events are ignored.</summary>
    internal bool Stop(int processId, DateTime timestampUtc)
    {
        ValidateTimestamp(timestampUtc);
        if (!_active.TryGetValue(processId, out var active) || timestampUtc < active.StartedAtUtc) return false;
        _active.Remove(processId);
        active.ExitedAtUtc = timestampUtc;
        RecordLifecycleTimestamp(processId, timestampUtc);
        return true;
    }

    /// <summary>Adds a send or receive event only when it falls within an unambiguous registered lifetime.</summary>
    internal bool Add(int processId, DateTime timestampUtc, bool tcp, bool ipv6, bool send, int bytes)
    {
        ValidateTimestamp(timestampUtc);
        if (bytes <= 0)
        {
            InvalidEvents++;
            return false;
        }
        if (!_active.TryGetValue(processId, out var active))
        {
            UnknownProcessEvents++;
            return false;
        }
        if (timestampUtc <= active.StartedAtUtc)
        {
            OutOfLifetimeEvents++;
            return false;
        }
        active.Add(tcp, ipv6, send, bytes);
        return true;
    }

    /// <summary>Prunes exited lifetimes after their delivery grace period; active lifetimes are retained.</summary>
    internal void Prune(DateTime timestampUtc)
    {
        ValidateTimestamp(timestampUtc);
        for (var index = _lifetimes.Count - 1; index >= 0; index--)
        {
            var lifetime = _lifetimes[index];
            if (lifetime.ExitedAtUtc is not { } exited || timestampUtc - exited <= _exitedGrace) continue;
            _lifetimes.RemoveAt(index);
        }
    }

    internal IReadOnlyList<NetworkLedgerRow> Snapshot(DateTime timestampUtc)
    {
        Prune(timestampUtc);
        return _lifetimes.SelectMany(lifetime => lifetime.Rows()).ToArray();
    }

    private static void Validate(int processId, long creationTicks, DateTime timestampUtc)
    {
        if (processId <= 0) throw new ArgumentOutOfRangeException(nameof(processId));
        if (creationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(creationTicks));
        ValidateTimestamp(timestampUtc);
    }

    private static void ValidateTimestamp(DateTime timestampUtc)
    {
        if (timestampUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(timestampUtc));
    }

    private void RecordLifecycleTimestamp(int processId, DateTime timestampUtc)
    {
        // shortcut: evicted tombstones cannot reject very late starts; add an eviction quality flag before production reuse.
        if (!_latestLifecycleByProcess.TryGetValue(processId, out var previous) &&
            _latestLifecycleByProcess.Count >= _maximumLifetimes)
        {
            var oldest = _latestLifecycleByProcess.MinBy(entry => entry.Value).Key;
            _latestLifecycleByProcess.Remove(oldest);
        }
        if (!_latestLifecycleByProcess.TryGetValue(processId, out previous) || timestampUtc > previous)
            _latestLifecycleByProcess[processId] = timestampUtc;
    }

    private sealed class Lifetime(int processId, long creationTicks, DateTime startedAtUtc)
    {
        private readonly Dictionary<(bool Tcp, bool Ipv6, bool Send), long> _bytes = [];
        internal int ProcessId { get; } = processId;
        internal long CreationTicks { get; } = creationTicks;
        internal DateTime StartedAtUtc { get; } = startedAtUtc;
        internal DateTime? ExitedAtUtc { get; set; }

        internal void Add(bool tcp, bool ipv6, bool send, int bytes)
        {
            var key = (tcp, ipv6, send);
            _bytes[key] = checked(_bytes.GetValueOrDefault(key) + bytes);
        }

        internal IEnumerable<NetworkLedgerRow> Rows() => _bytes.Select(entry => new NetworkLedgerRow(ProcessId, CreationTicks,
            entry.Key.Tcp, entry.Key.Ipv6, entry.Key.Send, entry.Value));
    }
}

internal sealed record NetworkLedgerRow(int ProcessId, long CreationTicks, bool Tcp, bool Ipv6, bool Send, long Bytes);
