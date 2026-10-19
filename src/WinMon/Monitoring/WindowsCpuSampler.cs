using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>
/// Samples total-machine and per-process CPU use across successive calls.
/// </summary>
public sealed class WindowsCpuSampler : IDisposable
{
    private const double GapThresholdSeconds = 5;
    private readonly ICpuSnapshotProvider _provider;
    private CpuSnapshot? _previous;
    private bool _disposed;

    /// <summary>
    /// Initializes a sampler that uses Windows system and process CPU timing APIs.
    /// </summary>
    public WindowsCpuSampler()
        : this(new WindowsCpuSnapshotProvider())
    {
    }

    internal WindowsCpuSampler(ICpuSnapshotProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Captures the next interval-based CPU sample.
    /// </summary>
    /// <returns>A baseline or gap sample without CPU percentages, or an interval sample with total and process CPU use.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the sampler has been disposed.</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown when Windows cannot provide total CPU timing for all active processor groups.</exception>
    public CpuSample Sample()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _provider.Capture();

        if (_previous is null || IsGap(_previous, current))
        {
            _previous = current;
            return new CpuSample(current.Timestamp, null, [], current.UnavailableProcesses, true, current.MonotonicTimestamp);
        }

        var prior = _previous;
        _previous = current;
        var totalDelta = current.SystemTimes.TotalTicks - prior.SystemTimes.TotalTicks;
        var idleDelta = current.SystemTimes.IdleTicks - prior.SystemTimes.IdleTicks;
        var busyDelta = totalDelta - idleDelta;

        if (totalDelta <= 0 || idleDelta < 0 || busyDelta < 0 || busyDelta > totalDelta)
        {
            return new CpuSample(current.Timestamp, null, [], current.UnavailableProcesses, true, current.MonotonicTimestamp);
        }

        var usages = CreateProcessUsages(prior.Processes, current.Processes, totalDelta,
            (current.MonotonicTimestamp - prior.MonotonicTimestamp) / (double)Stopwatch.Frequency);
        return new CpuSample(
            current.Timestamp,
            Math.Clamp(100d * busyDelta / totalDelta, 0d, 100d),
            usages,
            current.UnavailableProcesses,
            false,
            current.MonotonicTimestamp);
    }

    /// <summary>
    /// Clears interval baselines so the next sample becomes a gap.
    /// </summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _previous = null;
    }

    /// <summary>
    /// Releases the sampler and prevents additional samples.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _previous = null;
    }

    private static bool IsGap(CpuSnapshot previousSnapshot, CpuSnapshot currentSnapshot)
    {
        var elapsed = (currentSnapshot.MonotonicTimestamp - previousSnapshot.MonotonicTimestamp) / (double)Stopwatch.Frequency;
        return elapsed is <= 0 or > GapThresholdSeconds;
    }

    private static IReadOnlyList<ProcessUsage> CreateProcessUsages(
        IReadOnlyList<ProcessCpuTimes> previousProcesses,
        IReadOnlyList<ProcessCpuTimes> currentProcesses,
        long totalDelta, double elapsedSeconds)
    {
        var baselines = previousProcesses.ToDictionary(
            process => (process.ProcessId, process.CreationTicks),
            process => process);
        var usages = new List<ProcessUsage>(currentProcesses.Count);

        foreach (var process in currentProcesses)
        {
            if (!baselines.TryGetValue((process.ProcessId, process.CreationTicks), out var priorCpuTicks))
            {
                continue;
            }

            var processDelta = process.CpuTicks - priorCpuTicks.CpuTicks;
            if (processDelta < 0)
            {
                continue;
            }

            usages.Add(new ProcessUsage(
                process.ProcessId,
                process.CreationTicks,
                process.Name,
                Math.Clamp(100d * processDelta / totalDelta, 0d, 100d),
                process.WorkingSetBytes,
                ResourceRates.PerSecond(priorCpuTicks.ReadBytes, process.ReadBytes, elapsedSeconds) +
                ResourceRates.PerSecond(priorCpuTicks.WriteBytes, process.WriteBytes, elapsedSeconds)));
        }

        return
        [
            .. usages
                .OrderByDescending(process => process.CpuPercent)
                .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(process => process.ProcessId)
        ];
    }
}


