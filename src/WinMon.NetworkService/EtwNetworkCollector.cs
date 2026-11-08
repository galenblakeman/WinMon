using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using WinMon.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Owns a private kernel ETW session and keeps its callbacks free of native process I/O.</summary>
internal sealed class EtwNetworkCollector : INetworkCollector
{
    // A 16 MiB ETW flush can arrive as a burst; bound its compact event queue to about 6 MiB.
    private const int MaximumEvents = 262_144;
    private const int MaximumOwners = 8_192;
    // Events processed per lock hold, so a burst cannot delay a snapshot request for long.
    private const int BatchSize = 4_096;
    private readonly object _gate = new();
    private readonly HashSet<string> _allowedSids;
    private readonly Channel<CollectorEvent> _events = Channel.CreateBounded<CollectorEvent>(new BoundedChannelOptions(MaximumEvents)
    {
        // With Wait, TryWrite reports the rejected item rather than silently dropping it.
        FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false
    });
    private readonly Dictionary<int, OwnerCache> _owners = [];
    private readonly Dictionary<int, RejectedOwner> _rejectedUntil = [];
    private readonly CancellationTokenSource _stop = new();
    private TraceEventSession? _session;
    private NetworkTrafficLedger? _ledger;
    private Task? _consumer;
    private Task? _worker;
    private ETWTraceEventSource? _source;
    private DateTime _sessionStartedUtc;
    private long _qpcOffset;
    private int _started;
    private int _disposed;
    private int _failure;
    private long _droppedEvents;
    private long _observedDroppedEvents;
    private int _rateGapPending;
    private int _coverageReasons;

    internal EtwNetworkCollector(IReadOnlyCollection<string> allowedSids)
    {
        ArgumentNullException.ThrowIfNull(allowedSids);
        _allowedSids = new HashSet<string>(allowedSids.Where(sid => !string.IsNullOrWhiteSpace(sid)), StringComparer.Ordinal);
        if (_allowedSids.Count == 0) throw new ArgumentException("At least one provisioned SID is required.", nameof(allowedSids));
    }

    internal string? SessionName { get; private set; }
    public ProcessNetworkFailure Failure => (ProcessNetworkFailure)Volatile.Read(ref _failure);
    public bool Failed => Failure != ProcessNetworkFailure.None;
    internal ProcessNetworkIncompleteReason CoverageReasons => (ProcessNetworkIncompleteReason)Volatile.Read(ref _coverageReasons);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (Volatile.Read(ref _started) != 0) return Task.CompletedTask;
            try
            {
                SessionName = $"WinMonNetworkMonitor-{Environment.ProcessId}-{Guid.NewGuid():N}";
                _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create | TraceEventSessionOptions.NoRestartOnCreate)
                { StopOnDispose = true, BufferSizeMB = 16 };
                _session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP | KernelTraceEventParser.Keywords.Process);
                _source = _session.Source;

                // TraceEvent's QPC conversion APIs are not public in 3.1.23. Freeze one UTC/QPC
                // calibration; all following values share Stopwatch's monotonic clock domain.
                _sessionStartedUtc = _source.SessionStartTime.ToUniversalTime();
                var calibrationUtc = DateTime.UtcNow;
                var calibrationQpc = Stopwatch.GetTimestamp();
                _qpcOffset = checked(calibrationQpc - ToTicks(calibrationUtc - _sessionStartedUtc));
                _ledger = new NetworkTrafficLedger(Stopwatch.Frequency, _qpcOffset, MaximumOwners);
                RegisterCallbacks(_source.Kernel);
                _worker = Task.Run(ConsumeAsync);
                _consumer = Task.Run(() =>
                {
                    try { _source.Process(); }
                    catch { SignalFailure(ProcessNetworkFailure.TraceConsumer); throw; }
                    finally
                    {
                        if (!_stop.IsCancellationRequested) SignalFailure(ProcessNetworkFailure.TraceStopped);
                        _events.Writer.TryComplete();
                    }
                });
                Seed();
                Volatile.Write(ref _started, 1);
            }
            catch
            {
                SignalFailure(ProcessNetworkFailure.Startup);
                _session?.Dispose();
                throw;
            }
        }
        return Task.CompletedTask;
    }

    public ProcessNetworkSnapshot Snapshot(string ownerSid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerSid);
        lock (_gate)
        {
            if (Failed || _ledger is null || _source is null) return Unavailable();
            var now = Stopwatch.GetTimestamp();
            DrainPendingQuality(now);
            _ledger.ReportLoss(_session?.EventsLost ?? 0, now);
            var snapshot = _ledger.Snapshot(ownerSid, now) with { DroppedEvents = Interlocked.Read(ref _droppedEvents) };
            if (CoverageReasons != ProcessNetworkIncompleteReason.None)
                snapshot = snapshot with { Quality = snapshot.Quality | ProcessNetworkQuality.Incomplete,
                    IncompleteReasons = snapshot.IncompleteReasons | CoverageReasons };
            return Failed ? snapshot with { Quality = snapshot.Quality | ProcessNetworkQuality.Unavailable, Failure = Failure } : snapshot;
        }
    }

    private void RegisterCallbacks(KernelTraceEventParser kernel)
    {
        kernel.TcpIpSend += data => Enqueue(data, data.size, true);
        kernel.TcpIpRecv += data => Enqueue(data, data.size, false);
        kernel.TcpIpSendIPV6 += data => Enqueue(data, data.size, true);
        kernel.TcpIpRecvIPV6 += data => Enqueue(data, data.size, false);
        kernel.UdpIpSend += data => Enqueue(data, data.size, true);
        kernel.UdpIpRecv += data => Enqueue(data, data.size, false);
        kernel.UdpIpSendIPV6 += data => Enqueue(data, data.size, true);
        kernel.UdpIpRecvIPV6 += data => Enqueue(data, data.size, false);
        kernel.ProcessStart += data => Enqueue(new(CollectorEventKind.Start, data.ProcessID, EventQpc(data), 0, false));
        kernel.ProcessStop += data => Enqueue(new(CollectorEventKind.Stop, data.ProcessID, EventQpc(data), 0, false));
    }

    private void Enqueue(TraceEvent data, int bytes, bool send)
    {
        if (data.Version < 1 || data.ProcessID <= 0 || bytes < 0)
        { SignalIncomplete(ProcessNetworkIncompleteReason.InvalidEvent, rateGap: true); return; }
        if (bytes == 0) return;
        Enqueue(new(CollectorEventKind.Network, data.ProcessID, EventQpc(data), bytes, send));
    }

    private void Enqueue(CollectorEvent item)
    {
        if (_events.Writer.TryWrite(item)) return;
        Interlocked.Increment(ref _droppedEvents);
        SignalIncomplete(ProcessNetworkIncompleteReason.QueueOverflow, rateGap: true);
    }

    private async Task ConsumeAsync()
    {
        try
        {
            var nextReconcile = Stopwatch.GetTimestamp();
            Task<bool>? ready = null;
            Task? timeout = null;
            while (!_stop.IsCancellationRequested)
            {
                ready ??= _events.Reader.WaitToReadAsync(_stop.Token).AsTask();
                // Reuse the pending timer: a fresh one per wake piles up timers under heavy traffic.
                timeout ??= Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
                var completed = await Task.WhenAny(ready, timeout).ConfigureAwait(false);
                if (completed == timeout) timeout = null;
                else
                {
                    if (!await ready.ConfigureAwait(false)) break;
                    ready = null;
                    while (ProcessBatch()) { }
                }
                var now = Stopwatch.GetTimestamp();
                if (now >= nextReconcile) { Reconcile(); nextReconcile = checked(Stopwatch.GetTimestamp() + Stopwatch.Frequency); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch { SignalFailure(ProcessNetworkFailure.EventConsumer); }
    }

    /// <summary>Processes up to one batch under a single lock hold; returns whether the queue may hold more.</summary>
    private bool ProcessBatch()
    {
        lock (_gate)
        {
            if (_ledger is not { } ledger) return false;
            for (var count = 0; count < BatchSize; count++)
            {
                if (!_events.Reader.TryRead(out var item)) return false;
                Process(item, ledger);
            }
            return true;
        }
    }

    private void Process(CollectorEvent item, NetworkTrafficLedger ledger)
    {
        DrainPendingQuality(Stopwatch.GetTimestamp());
        switch (item.Kind)
        {
            case CollectorEventKind.Start:
                ResolveAndRegister(item.ProcessId, item.EventQpc, Stopwatch.GetTimestamp(), observedTraffic: false);
                break;
            case CollectorEventKind.Stop:
                if (_owners.TryGetValue(item.ProcessId, out var current) && item.EventQpc > current.StartedQpc)
                {
                    ledger.End(item.ProcessId, item.EventQpc);
                    _owners.Remove(item.ProcessId);
                }
                break;
            case CollectorEventKind.Network:
                var verifiedQpc = Stopwatch.GetTimestamp(); // taken before native ownership I/O
                if (EnsureKnown(item.ProcessId, item.EventQpc, verifiedQpc))
                    ledger.Add(item.ProcessId, item.EventQpc, verifiedQpc, item.Send, item.Bytes);
                break;
        }
    }

    private bool EnsureKnown(int processId, long eventQpc, long verifiedQpc)
    {
        // A closed lifetime is safe for a delayed packet even when the PID has been reused.
        if (_ledger!.HasExitedLifetime(processId, eventQpc)) return true;
        if (_owners.TryGetValue(processId, out var cached) && eventQpc > cached.StartedQpc && eventQpc <= cached.ValidThroughQpc)
        {
            return true;
        }
        if (_rejectedUntil.TryGetValue(processId, out var rejected) && verifiedQpc <= rejected.UntilQpc)
        {
            if (rejected.Unknown) SignalIncomplete(ProcessNetworkIncompleteReason.OwnershipUnavailable);
            return false;
        }
        return ResolveAndRegister(processId, eventQpc, verifiedQpc, observedTraffic: true);
    }

    internal bool ResolveAndRegister(int processId, long eventQpc, long verifiedQpc, bool observedTraffic)
    {
        if (_ledger is null || !ProcessOwnerProbe.TryRead(processId, out var owner))
        {
            Reject(processId, verifiedQpc, unknown: true);
            var retained = _ledger?.HasExitedLifetime(processId, eventQpc) == true;
            // A failed startup/process probe is not evidence that any traffic was missed.
            if (observedTraffic && !retained) SignalIncomplete(ProcessNetworkIncompleteReason.OwnershipUnavailable);
            return retained;
        }
        var createdQpc = ToSessionQpc(DateTime.FromFileTimeUtc(owner.CreationTicks));
        if (!_allowedSids.Contains(owner.Sid) || eventQpc <= createdQpc)
        {
            Reject(processId, verifiedQpc);
            return _ledger.HasExitedLifetime(processId, eventQpc);
        }
        if (_owners.TryGetValue(processId, out var previous))
        {
            if (previous.CreationTicks == owner.CreationTicks)
            {
                _owners[processId] = previous with { ValidThroughQpc = verifiedQpc };
                return true;
            }
            _ledger.End(processId, createdQpc);
            _owners.Remove(processId);
        }
        if (_owners.Count >= MaximumOwners)
        {
            // Never evict an active ledger lifetime: a later packet would then fail duplicate
            // registration and permanently lose that process until service restart.
            SignalIncomplete(ProcessNetworkIncompleteReason.CapacityExceeded);
            return _ledger.HasExitedLifetime(processId, eventQpc);
        }
        if (!_ledger.Register(processId, owner.CreationTicks, owner.Sid, createdQpc)) return _ledger.HasExitedLifetime(processId, eventQpc);
        _owners[processId] = new(owner.CreationTicks, createdQpc, verifiedQpc);
        _rejectedUntil.Remove(processId);
        return eventQpc > createdQpc;
    }

    private void Reject(int processId, long verifiedQpc, bool unknown = false)
    {
        if (_rejectedUntil.Count >= MaximumOwners && !_rejectedUntil.ContainsKey(processId))
            _rejectedUntil.Remove(_rejectedUntil.MinBy(pair => pair.Value.UntilQpc).Key);
        _rejectedUntil[processId] = new(checked(verifiedQpc + Stopwatch.Frequency), unknown);
    }

    private void Reconcile()
    {
        lock (_gate)
        {
            if (_ledger is null) return;
            // Bracket UTC after acquiring the lock so contention or preemption cannot look like clock drift.
            var nowQpc = Stopwatch.GetTimestamp();
            var utcNow = DateTime.UtcNow;
            var sampledThroughQpc = Stopwatch.GetTimestamp();
            if (!IsClockCalibrationValid(_sessionStartedUtc, _qpcOffset, Stopwatch.Frequency, nowQpc, utcNow, sampledThroughQpc))
            {
                SignalFailure(ProcessNetworkFailure.ClockDiscontinuity);
                return;
            }
            DrainPendingQuality(nowQpc);
            _ledger.ReportLoss(_session?.EventsLost ?? 0, nowQpc);
            foreach (var processId in _owners.Keys.ToArray())
            {
                if (!ProcessOwnerProbe.TryReadCreation(processId, out var creationTicks))
                {
                    _ledger.End(processId, nowQpc);
                    _owners.Remove(processId);
                    continue;
                }
                var createdQpc = ToSessionQpc(DateTime.FromFileTimeUtc(creationTicks));
                if (_owners[processId].CreationTicks == creationTicks)
                {
                    _owners[processId] = _owners[processId] with { ValidThroughQpc = nowQpc };
                    continue;
                }
                _ledger.End(processId, createdQpc);
                _owners.Remove(processId);
                ResolveAndRegister(processId, nowQpc, nowQpc, observedTraffic: false);
            }
        }
    }

    private void Seed()
    {
        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            try { Enqueue(new(CollectorEventKind.Start, process.Id, Stopwatch.GetTimestamp(), 0, false)); }
            finally { process.Dispose(); }
        }
    }

    private long EventQpc(TraceEvent data) => checked(_qpcOffset + ToTicks(TimeSpan.FromMilliseconds(data.TimeStampRelativeMSec)));
    private long ToSessionQpc(DateTime utc) => checked(_qpcOffset + ToTicks(utc.ToUniversalTime() - _sessionStartedUtc));
    private static long ToTicks(TimeSpan elapsed) => elapsed <= TimeSpan.Zero ? 0 : checked((long)Math.Round(elapsed.TotalSeconds * Stopwatch.Frequency));
    // A protected or already-exited unrelated PID makes the totals provisional, but does not
    // invalidate a rate window for a process whose complete packet stream was captured.
    private void SignalIncomplete(ProcessNetworkIncompleteReason reason, bool rateGap = false)
    {
        Interlocked.Or(ref _coverageReasons, (int)reason);
        if (rateGap) Interlocked.Exchange(ref _rateGapPending, 1);
    }

    private void DrainPendingQuality(long nowQpc)
    {
        var dropped = Interlocked.Read(ref _droppedEvents);
        if (Interlocked.Exchange(ref _rateGapPending, 0) != 0 || dropped != _observedDroppedEvents)
        {
            _ledger!.MarkIncomplete(nowQpc, CoverageReasons);
            _observedDroppedEvents = dropped;
        }
    }
    internal static bool IsClockCalibrationValid(DateTime sessionStartedUtc, long qpcOffset, long frequency, long nowQpc,
        DateTime utcNow, long? sampledThroughQpc = null)
    {
        var after = sampledThroughQpc ?? nowQpc;
        if (frequency <= 0 || nowQpc < qpcOffset || after < nowQpc) return false;
        var actualSeconds = (utcNow.ToUniversalTime() - sessionStartedUtc.ToUniversalTime()).TotalSeconds;
        var earliestSeconds = ((double)nowQpc - qpcOffset) / frequency;
        var latestSeconds = ((double)after - qpcOffset) / frequency;
        return actualSeconds >= earliestSeconds - 1 && actualSeconds <= latestSeconds + 1;
    }

    internal void SignalFailure(ProcessNetworkFailure failure)
    {
        if (failure == ProcessNetworkFailure.None || !Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        if (Interlocked.CompareExchange(ref _failure, (int)failure, 0) == 0)
            Trace.TraceError("Network.CollectorFailed reason={0}", failure);
        SignalIncomplete(ProcessNetworkIncompleteReason.None, rateGap: true);
    }
    private ProcessNetworkSnapshot Unavailable() => new(Guid.Empty, 0, 0, Stopwatch.Frequency, 0, 0, ProcessNetworkQuality.Unavailable, 0, 0, []) { Failure = Failure };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stop.Cancel();
        _session?.Dispose();
        _events.Writer.TryComplete();
        try { await AwaitBounded(_consumer).ConfigureAwait(false); await AwaitBounded(_worker).ConfigureAwait(false); }
        catch { SignalFailure(ProcessNetworkFailure.Shutdown); }
        finally { _session?.Dispose(); _stop.Dispose(); }
    }

    private static Task AwaitBounded(Task? task) => task is null ? Task.CompletedTask : task.WaitAsync(TimeSpan.FromSeconds(10));
    private enum CollectorEventKind { Start, Stop, Network }
    private readonly record struct CollectorEvent(CollectorEventKind Kind, int ProcessId, long EventQpc, int Bytes, bool Send);
    private readonly record struct OwnerCache(long CreationTicks, long StartedQpc, long ValidThroughQpc);
    private readonly record struct RejectedOwner(long UntilQpc, bool Unknown);
}
