using System.Diagnostics;

namespace WinMon.SensorService;

/// <summary>Shares one CPU-only sensor worker across authenticated pipe subscriptions.</summary>
internal sealed class SensorCollectorManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _allowedSids;
    private readonly Func<ISensorProvider> _createProvider;
    private readonly TimeSpan _idleGrace;
    private readonly TimeSpan _sampleInterval;
    private readonly TimeSpan _operationBudget;
    private readonly TimeSpan _retryBackoff;
    private readonly TimeSpan _recoveryBudget;
    private SensorCollectorWorker? _worker;
    private CancellationTokenSource? _idle;
    private int _subscriptions;
    private bool _suspended;
    private bool _restartRequired;
    private bool _disposed;
    private long _retryAt;

    internal SensorCollectorManager(IReadOnlyCollection<string> allowedSids, Func<ISensorProvider> createProvider,
        TimeSpan? idleGrace = null, TimeSpan? sampleInterval = null, TimeSpan? operationBudget = null,
        TimeSpan? retryBackoff = null, TimeSpan? recoveryBudget = null)
    {
        _allowedSids = new HashSet<string>(allowedSids, StringComparer.Ordinal);
        if (_allowedSids.Count == 0) throw new ArgumentException("At least one provisioned SID is required.", nameof(allowedSids));
        _createProvider = createProvider;
        _idleGrace = idleGrace ?? TimeSpan.FromSeconds(2);
        _sampleInterval = sampleInterval ?? TimeSpan.FromSeconds(2);
        _operationBudget = operationBudget ?? TimeSpan.FromSeconds(10);
        _retryBackoff = retryBackoff ?? TimeSpan.FromSeconds(30);
        _recoveryBudget = recoveryBudget ?? TimeSpan.FromSeconds(10);
        if (_idleGrace < TimeSpan.Zero || _sampleInterval < TimeSpan.Zero || _operationBudget <= TimeSpan.Zero ||
            _retryBackoff < TimeSpan.Zero || _recoveryBudget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(idleGrace));
    }

    internal async Task<IAsyncDisposable> SubscribeAsync(string sid, CancellationToken token)
    {
        EnsureAuthorized(sid);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idle?.Cancel();
            _subscriptions++;
            TryStartLocked();
            return new Lease(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal SensorCollectorSnapshot Snapshot(string sid)
    {
        EnsureAuthorized(sid);
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_suspended) return Unavailable(SensorCollectorQuality.Suspended, "suspended");
            if (_worker is null) return Unavailable(SensorCollectorQuality.Idle, "not started");
            return _worker.Snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task SetSuspendedAsync(bool suspended, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_suspended == suspended) return;
            _suspended = suspended;
            if (suspended)
            {
                _worker?.RequestStop();
                return;
            }

            TryStartLocked();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void TryStartLocked()
    {
        if (_suspended || _subscriptions == 0 || _restartRequired) return;
        // Completion is processed by ObserveCompletionAsync, which applies the recovery/latch decision first.
        if (_worker is not null) return;
        if (Stopwatch.GetTimestamp() < Volatile.Read(ref _retryAt)) return;
        var candidate = new SensorCollectorWorker(_createProvider(), _sampleInterval, _operationBudget);
        _worker = candidate;
        _ = ObserveCompletionAsync(candidate);
    }

    private async Task ObserveCompletionAsync(SensorCollectorWorker worker)
    {
        await worker.Completion.ConfigureAwait(false);
        worker.DisposeCompleted();
        var snapshot = worker.Snapshot;
        if (worker.CleanupFailed || (worker.TimedOut && !worker.CleanupReturnedWithin(_recoveryBudget)))
        {
            // Cleanup was uncertain, or a late native call consumed the recovery window. Preserve it until SCM restarts us.
            await _gate.WaitAsync().ConfigureAwait(false);
            try { if (!_disposed && ReferenceEquals(_worker, worker)) _restartRequired = true; }
            finally { _gate.Release(); }
            return;
        }
        if (snapshot.Quality == SensorCollectorQuality.Failed)
            Volatile.Write(ref _retryAt, Stopwatch.GetTimestamp() + (long)(_retryBackoff.TotalSeconds * Stopwatch.Frequency));
        try
        {
            if (snapshot.Quality == SensorCollectorQuality.Failed)
                await Task.Delay(_retryBackoff).ConfigureAwait(false);
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_disposed && ReferenceEquals(_worker, worker))
                {
                    _worker = null;
                    TryStartLocked();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async ValueTask ReleaseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || --_subscriptions != 0) return;
            _idle?.Dispose();
            _idle = new CancellationTokenSource();
            _ = StopAfterIdleAsync(_idle.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopAfterIdleAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(_idleGrace, token).ConfigureAwait(false);
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!_disposed && _subscriptions == 0 && !token.IsCancellationRequested) _worker?.RequestStop();
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private static SensorCollectorSnapshot Unavailable(SensorCollectorQuality quality, string reason) =>
        new(Guid.Empty, 0, Stopwatch.GetTimestamp(), Stopwatch.Frequency, quality, null, reason);

    private void EnsureAuthorized(string sid)
    {
        if (!_allowedSids.Contains(sid)) throw new UnauthorizedAccessException();
    }

    public async ValueTask DisposeAsync()
    {
        SensorCollectorWorker? worker;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _idle?.Cancel();
            _idle?.Dispose();
            worker = _worker;
            _worker = null;
            worker?.RequestStop();
        }
        finally
        {
            _gate.Release();
        }

        if (worker is not null)
        {
            await worker.Completion.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            worker.DisposeCompleted();
        }
    }

    private sealed class Lease(SensorCollectorManager manager) : IAsyncDisposable
    {
        private SensorCollectorManager? _manager = manager;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _manager, null)?.ReleaseAsync() ?? ValueTask.CompletedTask;
    }
}
