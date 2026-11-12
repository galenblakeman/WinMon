using Microsoft.Extensions.Hosting;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed record PublishedFrame(long Sequence, CapturedFrame Frame, long ObservedAt,
    ProcessNetworkFrame? Network = null);

/// <summary>One MonitorSession owns all native calls; requests read immutable publications.</summary>
internal sealed class SamplingOwner : IHostedService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Queue<PublishedFrame> _frames = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly MonitorSession _session;
    private readonly TimeProvider _clock;
    private TaskCompletionSource _changed = NewSignal();
    private CapturedFrame? _captured;
    private long _sequence;
    private Task? _run;
    private Task? _dispose;
    private readonly Func<NetworkTrafficClient> _createTraffic;
    private NetworkTrafficClient? _traffic;
    private Task? _trafficRun;

    public SamplingOwner() : this(() => new WindowsFrameSource(), TimeProvider.System) { }

    internal SamplingOwner(Func<IFrameSource> createSource, TimeProvider clock, TimeSpan? interval = null,
        Func<NetworkTrafficClient>? createTraffic = null)
    {
        _clock = clock;
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
        _run ??= _session.Run(_shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        if (_run is not null) await _run.WaitAsync(cancellationToken);
        if (_trafficRun is not null) await _trafficRun.WaitAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new ValueTask(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await StopAsync(CancellationToken.None);
        _session.Close();
        if (_traffic is not null) await _traffic.DisposeAsync();
        _shutdown.Dispose();
    }

    internal void EnableTraffic()
    {
        lock (_gate)
        {
            if (_traffic is not null || _shutdown.IsCancellationRequested) return;
            var traffic = _createTraffic();
            _trafficRun = traffic.Run(_shutdown.Token);
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5), _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                long sequence;
                lock (_gate)
                {
                    if (_frames.TryPeek(out _) && _sequence >= 2) return _frames.Last();
                    sequence = _sequence;
                }
                await WaitForChangeAsync(sequence, linked.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
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
            _frames.Enqueue(new PublishedFrame(++_sequence, frame, observedAt, _session.ProcessNetwork));
            while (_frames.Count > 120) _frames.Dequeue();
            changed = _changed;
            _changed = NewSignal();
        }
        changed.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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
