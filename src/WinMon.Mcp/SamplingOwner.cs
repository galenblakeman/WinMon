using Microsoft.Extensions.Hosting;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed record PublishedFrame(long Sequence, CapturedFrame Frame, long ObservedAt,
    ProcessNetworkFrame? Network = null);

internal sealed record PublishedUsage(PublishedFrame Frame, IReadOnlyList<AppUsage> Apps,
    DateTimeOffset Since, long MachineBusyTicks);

/// <summary>One MonitorSession owns all native calls; requests read immutable publications.</summary>
internal sealed class SamplingOwner : IHostedService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PublishedFrame> _frames = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly TimeSpan _idleTimeout;
    private readonly MonitorSession _session;
    private readonly TimeProvider _clock;
    private TaskCompletionSource _changed = NewSignal();
    private CapturedFrame? _captured;
    private long _sequence;
    private PublishedUsage? _usage;
    private Task? _run;
    private CancellationTokenSource? _sampling;
    private CancellationTokenSource? _idle;
    private Task _idleTask = Task.CompletedTask;
    private int _requests;
    private long _warmupSequence;
    private Task? _dispose;
    private Task? _stop;
    private readonly Func<NetworkTrafficClient> _createTraffic;
    private NetworkTrafficClient? _traffic;
    private Task? _trafficRun;

    public SamplingOwner() : this(() => new WindowsFrameSource(), TimeProvider.System) { }

    internal SamplingOwner(Func<IFrameSource> createSource, TimeProvider clock, TimeSpan? interval = null,
        Func<NetworkTrafficClient>? createTraffic = null, TimeSpan? idleTimeout = null)
    {
        _clock = clock;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(30);
        _createTraffic = createTraffic ?? (() => new NetworkTrafficClient());
        _session = new MonitorSession(() => new PublishingSource(createSource(), frame => _captured = frame),
            () =>
            {
                var traffic = Volatile.Read(ref _traffic)?.Sample;
                return new SamplingOptions { ProcessNetwork = traffic?.Snapshot,
                    ProcessNetworkSessionId = traffic?.SessionId ?? Guid.Empty };
            }, action => action(), interval);
        _session.History.Resize(120);
        _session.Recorded += Publish;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        await _lifecycle.WaitAsync(linked.Token);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _shutdown.Token.ThrowIfCancellationRequested();
            await CancelIdleAsync();
            if (_run is null)
            {
                _sampling = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                lock (_gate) _warmupSequence = _sequence + 2;
                _run = _session.Run(_sampling.Token);
            }
            _requests++;
            return new Request(this);
        }
        finally { _lifecycle.Release(); }
    }

    private async ValueTask ReleaseAsync()
    {
        if (_shutdown.IsCancellationRequested) return;
        try { await _lifecycle.WaitAsync(_shutdown.Token); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return; }
        try
        {
            if (--_requests != 0 || _shutdown.IsCancellationRequested) return;
            _idle = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _idleTask = StopWhenIdleAsync(_idle.Token);
        }
        finally { _lifecycle.Release(); }
    }

    private async Task StopWhenIdleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_idleTimeout, _clock, cancellationToken);
            await _lifecycle.WaitAsync(cancellationToken);
            try { await StopSamplingAsync(); }
            finally { _lifecycle.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task CancelIdleAsync()
    {
        if (_idle is null) return;
        await _idle.CancelAsync();
        await _idleTask;
        _idle.Dispose();
        _idle = null;
    }

    private async Task StopSamplingAsync()
    {
        if (_sampling is null) return;
        await _sampling.CancelAsync();
        if (_run is not null) await _run;
        if (_trafficRun is not null) await _trafficRun;
        if (_traffic is not null) await _traffic.DisposeAsync();
        _traffic = null;
        _trafficRun = null;
        _run = null;
        _sampling.Dispose();
        _sampling = null;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task stop;
        lock (_gate) stop = _stop ??= StopCoreAsync();
        await stop.WaitAsync(cancellationToken);
    }

    private async Task StopCoreAsync()
    {
        await _shutdown.CancelAsync();
        lock (_gate) _changed.TrySetCanceled(_shutdown.Token);
        await _lifecycle.WaitAsync();
        try
        {
            await CancelIdleAsync();
            await StopSamplingAsync();
        }
        finally { _lifecycle.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await StopAsync(CancellationToken.None);
        _session.Close();
        _shutdown.Dispose();
    }

    internal void EnableTraffic()
    {
        lock (_gate)
        {
            if (_traffic is not null || _shutdown.IsCancellationRequested) return;
            var traffic = _createTraffic();
            _trafficRun = traffic.Run(_sampling!.Token);
            Volatile.Write(ref _traffic, traffic);
        }
    }

    internal string TrafficState => _traffic?.Sample.State.ToString() ?? NetworkTrafficState.NotStarted.ToString();
    internal Guid TrafficSessionId => _traffic?.Sample.SessionId ?? Guid.Empty;

    internal long Now => _clock.GetTimestamp();
    internal TimeProvider Clock => _clock;
    internal DateTimeOffset UtcNow => _clock.GetUtcNow();
    internal double Seconds(long from, long to) => _clock.GetElapsedTime(from, to).TotalSeconds;
    internal bool IsFresh(PublishedFrame frame) => Seconds(frame.ObservedAt, Now) is >= 0 and <= 2;

    internal PublishedFrame[] ReadAfter(long sequence)
    {
        lock (_gate) return _frames.Where(frame => frame.Sequence > sequence).ToArray();
    }

    internal PublishedUsage? ReadUsage()
    {
        lock (_gate) return _usage;
    }

    internal async Task WaitForChangeAsync(long sequence, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task changed;
        lock (_gate)
        {
            if (_sequence > sequence) return;
            changed = _changed.Task;
        }
        await changed.WaitAsync(cancellationToken);
    }

    internal async Task<PublishedFrame?> CurrentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var request = await AcquireAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token, _shutdown.Token);
        try
        {
            while (true)
            {
                long sequence;
                lock (_gate)
                {
                    if (_frames.TryPeek(out _) && _sequence >= _warmupSequence)
                        return _frames.Last();
                    sequence = _sequence;
                }
                await WaitForChangeAsync(sequence, linked.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_shutdown.IsCancellationRequested) { return null; }
    }

    private void Publish()
    {
        var frame = _captured;
        _captured = null;
        frame ??= new CapturedFrame(new MonitorSample(UtcNow, null, [], 0, true), Error: _session.Error);
        TaskCompletionSource changed;
        lock (_gate)
        {
            var observedAt = ReferenceEquals(_clock, TimeProvider.System) && frame.Full.MonotonicTimestamp > 0
                ? frame.Full.MonotonicTimestamp : Now;
            var published = new PublishedFrame(++_sequence, frame, observedAt, _session.ProcessNetwork);
            _frames.Enqueue(published);
            _usage = new PublishedUsage(published, _session.Usage.Apps, _session.Usage.Since, _session.Usage.MachineBusyTicks);
            while (_frames.Count > 120) _frames.Dequeue();
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Request(SamplingOwner owner) : IAsyncDisposable
    {
        private SamplingOwner? _owner = owner;
        public ValueTask DisposeAsync() => Interlocked.Exchange(ref _owner, null)?.ReleaseAsync() ?? ValueTask.CompletedTask;
    }

    private sealed class PublishingSource(IFrameSource source, Action<CapturedFrame> captured) : IFrameSource
    {
        public CapturedFrame Capture(SamplingOptions options)
        {
            var frame = source.Capture(options);
            captured(frame);
            return frame;
        }
        public void Reset() => source.Reset();
        public void Dispose() => source.Dispose();
    }
}
