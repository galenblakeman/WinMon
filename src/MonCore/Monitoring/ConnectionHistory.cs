using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>A connection observation aligned with one resource sample. It is unavailable when collection did not complete.</summary>
public sealed record ConnectionCapture(
    MonitorSample Sample,
    NetworkConnectionsFrame? Connections,
    IReadOnlyList<ProcessUsage> Owners,
    ProcessNetworkFrame? Network)
{
    public bool Available => Connections?.Available == true;
}

/// <summary>Bounded, in-memory connection observations for Capture mode. They are intentionally not restart-persisted.</summary>
public sealed class ConnectionHistory
{
    private readonly Queue<ConnectionCapture> _captures = [];
    private readonly int _entryBudget;
    private int _entries;

    public ConnectionHistory(int capacity = 600, int entryBudget = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(entryBudget, 1);
        Capacity = capacity;
        _entryBudget = entryBudget;
    }

    public int Capacity { get; private set; }
    public bool WasMemoryLimited { get; private set; }
    public ConnectionCapture? Latest { get; private set; }
    public IReadOnlyList<ConnectionCapture> Captures => _captures.ToArray();

    /// <summary>Returns only an exactly aligned capture; callers must not substitute current live readings.</summary>
    public ConnectionCapture? Find(long monotonicTimestamp)
    {
        return monotonicTimestamp <= 0
            ? null
            : _captures.FirstOrDefault(capture =>
                capture.Sample.MonotonicTimestamp == monotonicTimestamp);
    }

    /// <summary>Finds the capture for this retained resource sample, including wall-clock-only test fixtures.</summary>
    public ConnectionCapture? Find(MonitorSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.MonotonicTimestamp > 0
            ? Find(sample.MonotonicTimestamp)
            : _captures.FirstOrDefault(capture => ReferenceEquals(capture.Sample, sample));
    }

    internal void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        Trim(_captures.LastOrDefault()?.Sample);
    }

    internal void Add(MonitorSample sample, NetworkConnectionsFrame? connections, IReadOnlyList<ProcessUsage> owners,
        ProcessNetworkFrame? network)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(owners);
        var count = EntryCount(connections, owners, network);
        // A huge one-off table is represented as unavailable instead of evicting all useful Capture history.
        if (count > _entryBudget)
        {
            WasMemoryLimited = true;
            connections = null;
            owners = [];
            network = null;
            count = 0;
        }

        Latest = new ConnectionCapture(sample, connections, owners, network);
        _captures.Enqueue(Latest);
        _entries += count;
        Trim(sample);
    }

    private void Trim(MonitorSample? latest)
    {
        while (_captures.Count > 0 && latest is not null)
        {
            var oldest = _captures.Peek();
            var age = latest.MonotonicTimestamp > 0 && oldest.Sample.MonotonicTimestamp > 0
                ? (latest.MonotonicTimestamp - oldest.Sample.MonotonicTimestamp) / (double)Stopwatch.Frequency
                : (latest.Timestamp - oldest.Sample.Timestamp).TotalSeconds;
            if (_captures.Count <= Capacity && age < Capacity && _entries <= _entryBudget) break;
            WasMemoryLimited |= _entries > _entryBudget;
            _entries -= EntryCount(oldest.Connections, oldest.Owners, oldest.Network);
            _captures.Dequeue();
        }
    }

    private static int EntryCount(NetworkConnectionsFrame? connections, IReadOnlyList<ProcessUsage> owners,
        ProcessNetworkFrame? network)
    {
        return checked((connections?.Connections.Count ?? 0) + owners.Count +
                       (network?.Processes.Count ?? 0));
    }
}