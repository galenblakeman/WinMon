using System.Collections.Concurrent;
using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Why the latest capture is incomplete.</summary>
internal enum SamplingError
{
    CpuUnavailable,
    ReadingsUnavailable
}

/// <summary>The drives and retention rules one capture uses.</summary>
/// <param name="Retain">Processes kept in compacted history even when idle, such as pins and the selection.</param>
/// <param name="RetainAll">Keep every process, for the All choice of the process count.</param>
internal sealed record SamplingOptions(
    string? PrimaryDrive = null,
    string? SecondaryDrive = null,
    Func<ProcessUsage, bool>? Retain = null,
    bool RetainAll = false);

/// <summary>One complete capture: the full sample, its service frame, and a partial failure if there was one.</summary>
internal sealed record CapturedFrame(MonitorSample Full, ServiceFrame? Services = null, SamplingError? Error = null);

/// <summary>Reads one capture each tick on the sampling thread.</summary>
internal interface IFrameSource : IDisposable
{
    CapturedFrame Capture(SamplingOptions options);

    /// <summary>Drop baselines after a failed capture so the next one starts a new interval.</summary>
    void Reset();
}

/// <summary>
/// Captures a frame every second on the thread pool and records it in history on the UI thread.
/// </summary>
/// <remarks>
/// Capture never waits for the UI: a stalled dispatcher would otherwise delay the next capture and stretch the
/// sampling interval past the chart's gap threshold. Frames queue and are drained in order by at most one posted
/// callback. History, the latest samples and <see cref="Error"/> change only inside that callback, on the thread
/// <c>post</c> runs it on.
/// </remarks>
internal sealed class MonitorSession(
    Func<IFrameSource> createSource,
    Func<SamplingOptions> options,
    Action<Action> post,
    TimeSpan? interval = null)
{
    private readonly ConcurrentQueue<Pending> _pending = new();
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(1);
    private int _drainScheduled;
    private bool _closed;

    /// <summary>Compacted samples, oldest first.</summary>
    internal MonitorHistory History { get; } = new();

    /// <summary>Complete service frames for the last two minutes.</summary>
    internal ServiceHistory Services { get; } = new();

    /// <summary>The newest compacted sample, the same instance history ends with.</summary>
    internal MonitorSample? Latest { get; private set; }

    /// <summary>The newest sample before compaction, so live ranking and group totals are exact.</summary>
    internal MonitorSample? LatestFull { get; private set; }

    /// <summary>Why the most recent capture is incomplete, or null when it succeeded.</summary>
    internal SamplingError? Error { get; private set; }

    /// <summary>Raised after queued captures, including failed ones, have been recorded.</summary>
    internal event Action? Recorded;

    /// <summary>Capture until cancelled. Completes without throwing when cancelled.</summary>
    internal Task Run(CancellationToken cancellationToken)
    {
        return Task.Run(() => CollectAsync(cancellationToken), cancellationToken);
    }

    /// <summary>Ignore captures that are still queued or arrive later, while the owner shuts down.</summary>
    internal void Close()
    {
        _closed = true;
    }

    /// <summary>Record a frame as a drain would, without raising <see cref="Recorded"/>.</summary>
    internal void Record(MonitorSample compacted, MonitorSample? full = null, ServiceFrame? services = null)
    {
        History.Add(compacted);
        Latest = compacted;
        LatestFull = full;
        if (services is not null) Services.Add(services);
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        using var source = createSource();
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var current = options();
                    var frame = source.Capture(current);
                    Deliver(new Pending(
                        ProcessRetention.Compact(frame.Full, retain: current.Retain, retainAll: current.RetainAll),
                        frame.Error, frame.Full, frame.Services));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("WinMon CPU sampling failed: {0}", ex);
                    source.Reset();
                    Deliver(new Pending(null, SamplingError.ReadingsUnavailable));
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void Deliver(Pending pending)
    {
        _pending.Enqueue(pending);
        if (Interlocked.Exchange(ref _drainScheduled, 1) == 0) post(Drain);
    }

    private void Drain()
    {
        // Clear the flag before draining so a capture enqueued meanwhile schedules another drain.
        Volatile.Write(ref _drainScheduled, 0);
        var changed = false;
        while (_pending.TryDequeue(out var pending))
        {
            if (_closed) continue;
            try
            {
                Error = pending.Error;
                if (pending.Sample is not null) Record(pending.Sample, pending.Full, pending.Services);
                changed = true;
            }
            catch (Exception ex)
            {
                Trace.TraceError("WinMon could not record a sample: {0}", ex);
            }
        }

        if (changed) Recorded?.Invoke();
    }

    /// <summary>A compacted capture, or a failed pass when <see cref="Sample"/> is null, awaiting the drain.</summary>
    private sealed record Pending(
        MonitorSample? Sample,
        SamplingError? Error,
        MonitorSample? Full = null,
        ServiceFrame? Services = null);
}
