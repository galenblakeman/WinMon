// Close handles and cancel callbacks synchronously before releasing ownership or continuing shutdown.
// ReSharper disable MethodHasAsyncOverload

using System.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Shares one collector across authenticated, connection-bound subscriptions.</summary>
internal sealed class NetworkCollectorManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _allowedSids;
    private readonly Func<INetworkCollector> _create;
    private readonly TimeSpan _idleGrace;
    private INetworkCollector? _collector;
    private CancellationTokenSource? _idle;
    private int _subscribers;
    private bool _disposed;
    private bool _suspended;

    internal NetworkCollectorManager(IReadOnlyCollection<string> allowedSids, Func<INetworkCollector> create,
        TimeSpan? idleGrace = null)
    {
        _allowedSids = new HashSet<string>(allowedSids, StringComparer.Ordinal);
        _create = create;
        _idleGrace = idleGrace ?? TimeSpan.FromSeconds(2);
        if (_idleGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleGrace));
    }

    internal bool HasSubscriptions
    {
        get
        {
            _gate.Wait();
            try
            {
                return _subscribers > 0;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    internal async Task<IAsyncDisposable> SubscribeAsync(string sid, CancellationToken token)
    {
        if (!_allowedSids.Contains(sid)) throw new UnauthorizedAccessException();
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idle?.Cancel();
            if (_collector?.Failed == true)
            {
                var failed = _collector;
                _collector = null;
                await failed.DisposeAsync().ConfigureAwait(false);
            }

            if (_collector is null && !_suspended) await StartAsync(token).ConfigureAwait(false);
            _subscribers++;
            return new Subscription(this);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal ProcessNetworkSnapshot Snapshot(string sid)
    {
        if (!_allowedSids.Contains(sid)) throw new UnauthorizedAccessException();
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_suspended && _collector is not null && !_collector.Failed) return _collector.Snapshot(sid);
            return new ProcessNetworkSnapshot(Guid.Empty, 0, Stopwatch.GetTimestamp(), Stopwatch.Frequency, 0, 0,
                    ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Discontinuity, 0, 0, [])
                { Failure = _collector?.Failure ?? ProcessNetworkFailure.None };
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
            if (_collector is not null)
            {
                var previous = _collector;
                _collector = null;
                await previous.DisposeAsync().ConfigureAwait(false);
            }

            if (!suspended && _subscribers > 0) await StartAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StartAsync(CancellationToken token)
    {
        var candidate = _create();
        try
        {
            await candidate.StartAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException || !token.IsCancellationRequested)
                Trace.TraceError("Network.CollectorStartFailed: {0}", exception);
            await candidate.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _collector = candidate;
        Trace.TraceInformation("Network.CollectorStarted");
    }

    private async ValueTask ReleaseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || --_subscribers != 0) return;
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
                if (_disposed || _subscribers != 0 || token.IsCancellationRequested) return;
                if (_collector is not null)
                {
                    var previous = _collector;
                    _collector = null;
                    await previous.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // A failed stop must be visible without logging traffic or process metadata.
            Trace.TraceError("Network collector idle shutdown failed: {0}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            _idle?.Cancel();
            _idle?.Dispose();
            if (_collector is not null)
            {
                var previous = _collector;
                _collector = null;
                await previous.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private sealed class Subscription(NetworkCollectorManager manager) : IAsyncDisposable
    {
        private NetworkCollectorManager? _manager = manager;

        public ValueTask DisposeAsync()
        {
            return Interlocked.Exchange(ref _manager, null)?.ReleaseAsync() ?? ValueTask.CompletedTask;
        }
    }
}