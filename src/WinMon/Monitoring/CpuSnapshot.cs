namespace WinMon.Monitoring;

/// <summary>Carry raw native timing values for one monotonic collection instant.</summary>
/// <param name="Timestamp">Wall-clock label for the collection.</param>
/// <param name="MonotonicTimestamp">Stopwatch tick at collection.</param>
/// <param name="SystemTimes">Machine CPU timing counters.</param>
/// <param name="Processes">Readable process timing counters.</param>
/// <param name="UnavailableProcesses">Processes unavailable or omitted by the collection limit.</param>
internal sealed record CpuSnapshot(
    DateTimeOffset Timestamp,
    long MonotonicTimestamp,
    SystemCpuTimes SystemTimes,
    IReadOnlyList<ProcessCpuTimes> Processes,
    int UnavailableProcesses);