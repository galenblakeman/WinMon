using System.Diagnostics;

namespace WinMon.SensorService;

/// <summary>
/// Owns exactly one native provider. Native calls run only in this worker; timeout marks failure but never
/// aborts or replaces a potentially live call.
/// </summary>
internal sealed class SensorCollectorWorker : IAsyncDisposable
{
    private readonly ISensorProvider _provider;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _operationBudget;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _snapshotLock = new();
    private Guid _generation = Guid.NewGuid();
    private readonly Task _run;
    private SensorCollectorSnapshot _snapshot;
    private int _disposeRequested;
    private int _resourcesDisposed;
    private int _cleanupFailed;
    private int _timedOut;
    private long _timedOutAt;
    private long _cleanupCompletedAt;

    internal SensorCollectorWorker(ISensorProvider provider, TimeSpan sampleInterval, TimeSpan operationBudget)
    {
        _provider = provider;
        _sampleInterval = sampleInterval;
        _operationBudget = operationBudget;
        if (_sampleInterval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sampleInterval));
        if (_operationBudget <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(operationBudget));
        _snapshot = NewSnapshot(SensorCollectorQuality.Starting);
        _run = RunAsync();
    }

    internal SensorCollectorSnapshot Snapshot => Volatile.Read(ref _snapshot);
    internal bool IsCompleted => _run.IsCompleted;
    internal Task Completion => _run;
    internal bool TimedOut => Volatile.Read(ref _timedOut) != 0;
    internal bool CleanupFailed => Volatile.Read(ref _cleanupFailed) != 0;
    internal bool CleanupReturnedWithin(TimeSpan budget)
    {
        var timedOutAt = Volatile.Read(ref _timedOutAt);
        var completedAt = Volatile.Read(ref _cleanupCompletedAt);
        return timedOutAt != 0 && completedAt != 0 && Stopwatch.GetElapsedTime(timedOutAt, completedAt) <= budget;
    }

    internal void RequestStop(bool invalidate = true)
    {
        lock (_snapshotLock)
        {
            if (_resourcesDisposed != 0 || Interlocked.Exchange(ref _disposeRequested, 1) != 0) return;
            if (invalidate && Snapshot.Quality != SensorCollectorQuality.Failed)
            {
                // Invalidate before cancellation callbacks can run; preserve a latched failure diagnosis.
                _generation = Guid.NewGuid();
                PublishLocked(SensorCollectorQuality.Idle, null, "stopping");
            }
            _stop.Cancel();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            await RunOperationAsync(_provider.OpenAsync, "open").ConfigureAwait(false);
            while (!_stop.IsCancellationRequested)
            {
                var reading = await RunOperationAsync(_provider.ReadCpuTemperatureAsync, "update")
                    .ConfigureAwait(false);
                if (_stop.IsCancellationRequested || Volatile.Read(ref _timedOut) != 0) break;
                var quality = reading?.Celsius is null || reading.LastReadAt == 0
                    ? SensorCollectorQuality.Partial
                    : SensorCollectorQuality.Complete;
                Publish(quality, reading, reading?.AvailabilityReason);
                await Task.Delay(_sampleInterval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            if (Volatile.Read(ref _timedOut) == 0) Publish(SensorCollectorQuality.Idle, null, "stopped");
        }
        catch (Exception exception)
        {
            Publish(SensorCollectorQuality.Failed, null, FailureReason(exception));
        }
        finally
        {
            try
            {
                // The same worker is the only caller that may close native resources.
                await RunCloseAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Interlocked.Exchange(ref _cleanupFailed, 1);
                Publish(SensorCollectorQuality.Failed, null, "close: " + FailureReason(exception));
            }
            finally
            {
                Volatile.Write(ref _cleanupCompletedAt, Stopwatch.GetTimestamp());
            }
        }
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation, string name)
    {
        var task = Task.Run(async () => await operation(_stop.Token).ConfigureAwait(false), CancellationToken.None);
        await AwaitOperationAsync(task, name).ConfigureAwait(false);
    }

    private async Task<T> RunOperationAsync<T>(Func<CancellationToken, Task<T>> operation, string name)
    {
        var task = Task.Run(async () => await operation(_stop.Token).ConfigureAwait(false), CancellationToken.None);
        return await AwaitOperationAsync(task, name).ConfigureAwait(false);
    }

    private async Task RunCloseAsync()
    {
        var task = Task.Run(async () => await _provider.DisposeAsync().ConfigureAwait(false), CancellationToken.None);
        await AwaitOperationAsync(task, "close").ConfigureAwait(false);
    }

    private async Task<T> AwaitOperationAsync<T>(Task<T> task, string name)
    {
        if (await Task.WhenAny(task, Task.Delay(_operationBudget, CancellationToken.None)).ConfigureAwait(false) != task)
        {
            Interlocked.Exchange(ref _timedOut, 1);
            Volatile.Write(ref _timedOutAt, Stopwatch.GetTimestamp());
            Publish(SensorCollectorQuality.Failed, null, name + " timed out");
            RequestStop(invalidate: false);
            // A native call may still own handles. Do not start another worker until this task returns.
        }

        return await task.ConfigureAwait(false);
    }

    private async Task AwaitOperationAsync(Task task, string name)
    {
        if (await Task.WhenAny(task, Task.Delay(_operationBudget, CancellationToken.None)).ConfigureAwait(false) != task)
        {
            Interlocked.Exchange(ref _timedOut, 1);
            Volatile.Write(ref _timedOutAt, Stopwatch.GetTimestamp());
            Publish(SensorCollectorQuality.Failed, null, name + " timed out");
            RequestStop(invalidate: false);
        }

        await task.ConfigureAwait(false);
    }

    private void Publish(SensorCollectorQuality quality, SensorReading? reading, string? failure = null)
    {
        lock (_snapshotLock)
        {
            if ((quality is SensorCollectorQuality.Complete or SensorCollectorQuality.Partial) &&
                (_stop.IsCancellationRequested || Volatile.Read(ref _disposeRequested) != 0 ||
                 Volatile.Read(ref _timedOut) != 0)) return;
            PublishLocked(quality, reading, failure);
        }
    }

    private void PublishLocked(SensorCollectorQuality quality, SensorReading? reading, string? failure)
    {
        var current = Snapshot;
        Volatile.Write(ref _snapshot, new SensorCollectorSnapshot(_generation, current.Sequence + 1,
            Stopwatch.GetTimestamp(), Stopwatch.Frequency, quality, reading, failure));
    }

    private SensorCollectorSnapshot NewSnapshot(SensorCollectorQuality quality) =>
        new(_generation, 0, Stopwatch.GetTimestamp(), Stopwatch.Frequency, quality);

    private static string FailureReason(Exception exception)
    {
        return exception.GetType().Name + ": " + exception.Message[..Math.Min(exception.Message.Length, 160)];
    }

    public async ValueTask DisposeAsync()
    {
        RequestStop();
        await _run.ConfigureAwait(false);
        DisposeCompleted();
    }

    internal void DisposeCompleted()
    {
        if (!_run.IsCompleted) throw new InvalidOperationException("Native worker has not completed cleanup.");
        lock (_snapshotLock)
            if (Interlocked.Exchange(ref _resourcesDisposed, 1) == 0) _stop.Dispose();
    }
}
