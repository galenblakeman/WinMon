// These tasks must start even during cancellation; the worker or bounded shutdown owns cancellation.
// ReSharper disable MethodSupportsCancellation

using System.Collections.Concurrent;
using System.Diagnostics;

// ReSharper disable GrammarMistakeInComment

namespace WinMon.Monitoring;

/// <summary>Why the latest capture is incomplete.</summary>
public enum SamplingError
{
    CpuUnavailable,
    ReadingsUnavailable,

    /// <summary>Windows cannot report this machine's CPU, such as with more than one processor group.</summary>
    CpuUnsupported,

    /// <summary>Process enumeration failed; system resources remain useful.</summary>
    // Keep the public sampling status contract stable.
    // ReSharper disable once UnusedMember.Global
    ProcessesUnavailable
}

/// <summary>The resource targets and retention rules one capture uses.</summary>
/// <param name="Retain">Processes kept in compacted history even when idle, such as pins and the selection.</param>
/// <param name="RetainAll">Keep every process, for the All choice of the process count.</param>
/// <param name="NetworkInterfaceId">The manually selected network adapter, or <see langword="null"/> for automatic selection.</param>
public sealed record SamplingOptions(
    string? PrimaryDrive = null,
    string? SecondaryDrive = null,
    Func<ProcessUsage, bool>? Retain = null,
    bool RetainAll = false,
    string? NetworkInterfaceId = null)
{
    /// <summary>Latest nonblocking service observation; captured with these sampling choices.</summary>
    public ProcessNetworkSnapshot? ProcessNetwork { get; init; }

    public Guid ProcessNetworkSessionId { get; init; }
}

/// <summary>One complete capture: the full sample, its service frame, and a partial failure if there was one.</summary>
public sealed record CapturedFrame(MonitorSample Full, ServiceFrame? Services = null, SamplingError? Error = null)
{
    /// <summary>Endpoint observation taken in the same sampling pass, regardless of Connections tab visibility.</summary>
    public NetworkConnectionsFrame? Connections { get; init; }
}

/// <summary>Reads one capture each tick on the sampling thread.</summary>
public interface IFrameSource : IDisposable
{
    /// <summary>Supported features of this implementation; each captured reading can still be unavailable.</summary>
    CollectorCapabilities Capabilities => CollectorCapabilities.None;

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
public sealed class MonitorSession(
    Func<IFrameSource> createSource,
    Func<SamplingOptions> options,
    Action<Action> post,
    TimeSpan? interval = null,
    ProcessIdentityPolicy? identityPolicy = null,
    bool retainUsageHistory = false)
{
    private readonly ConcurrentQueue<Pending> _pending = new();
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(1);
    private int _drainScheduled;
    private bool _closed;
    private long _historyClockOffset;
    private readonly ProcessNetworkSession _network = new();
    private readonly Dictionary<ProcessLifetimeIdentity, (ProcessUsage Process, long SeenAt)> _networkOwners = [];
    // Per-tick scratch for the drain thread, plus last tick's connection owners to reuse when unchanged.
    private readonly HashSet<ProcessLifetimeIdentity> _retainedNetworkLifetimes = [];
    private readonly HashSet<ProcessLifetimeIdentity> _connectionOwnerLifetimes = [];
    private Dictionary<ProcessLifetimeIdentity, ProcessUsage> _connectionOwners = [];
    private Dictionary<ProcessLifetimeIdentity, ProcessUsage> _nextConnectionOwners = [];
    private Guid _networkSessionId;

    /// <summary>Live client-session traffic; never persisted or rebased with resource history.</summary>
    public ProcessNetworkFrame? ProcessNetwork { get; private set; }

    /// <summary>Compacted samples, oldest first.</summary>
    public MonitorHistory History { get; } = new();

    /// <summary>Complete service frames for the last two minutes.</summary>
    public ServiceHistory Services { get; } = new();

    /// <summary>Live-only connection observations aligned with resource history for Capture mode.</summary>
    public ConnectionHistory Connections { get; } = new();

    /// <summary>Per-app totals since WinMon started, from every live full sample.</summary>
    public UsageLedger Usage { get; } = new(DateTimeOffset.Now, identityPolicy);

    public UsageHistory UsageHistory { get; } = new(identityPolicy: identityPolicy);

    /// <summary>The newest compacted sample, the same instance history ends with.</summary>
    public MonitorSample? Latest { get; private set; }

    /// <summary>The newest sample before compaction, so live ranking and group totals are exact.</summary>
    public MonitorSample? LatestFull { get; private set; }

    /// <summary>Whether this session has drained a sample it captured itself.</summary>
    /// <remarks>
    /// Samples restored after an update restart set <see cref="Latest"/> but not this, so only a live capture can
    /// prove a new version healthy.
    /// </remarks>
    public bool HasLiveSample { get; private set; }

    /// <summary>Whether a live capture has shown this build sampling as well as this machine allows.</summary>
    /// <remarks>
    /// An update's new version must prove this or be rolled back. A machine whose CPU Windows cannot report proves
    /// it every time, or it could never update; a CPU read that fails for any other reason does not.
    /// </remarks>
    public bool IsHealthy => HasLiveSample && Error is null or SamplingError.CpuUnsupported;

    /// <summary>Why the most recent capture is incomplete, or null when it succeeded.</summary>
    public SamplingError? Error { get; private set; }

    /// <summary>Raised after queued captures, including failed ones, have been recorded.</summary>
    public event Action? Recorded;

    /// <summary>
    /// Raised for each live full sample as a drain records it, in order, before <see cref="Recorded"/>; a drain of
    /// several frames raises it once per frame. Restored and synthetic samples recorded through
    /// <see cref="Record"/> do not raise it.
    /// </summary>
    public event Action<MonitorSample>? LiveSampleRecorded;

    /// <summary>Capture until cancelled. Completes without throwing when cancelled.</summary>
    public Task Run(CancellationToken cancellationToken)
    {
        // No token for Task.Run: cancelled before it starts, it would return a cancelled task, not complete.
        return Task.Run(() => CollectAsync(cancellationToken));
    }

    /// <summary>Ignore captures that are still queued or arrive later, while the owner shuts down.</summary>
    public void Close()
    {
        _closed = true;
    }

    /// <summary>Align future live samples with history rebased onto this boot's clock.</summary>
    public void SetHistoryClockOffset(long offset)
    {
        _historyClockOffset = offset;
    }

    /// <summary>Record a frame as a drain would, without raising <see cref="Recorded"/>.</summary>
    /// <remarks>
    /// Only a frame with its full sample feeds <see cref="Usage"/>: live frames carry one, while samples restored
    /// after an update restart are compacted only and must not be counted twice.
    /// </remarks>
    public void Record(MonitorSample compacted, MonitorSample? full = null, ServiceFrame? services = null,
        NetworkConnectionsFrame? connections = null, ProcessNetworkFrame? network = null)
    {
        History.Add(compacted);
        Latest = compacted;
        LatestFull = full;
        if (services is not null) Services.Add(services);
        if (retainUsageHistory) UsageHistory.Resize(History.Capacity);
        if (full is not null)
        {
            Usage.Add(full);
            if (retainUsageHistory) UsageHistory.Add(full);
        }
        if (full is not null || connections is not null || network is not null)
        {
            Connections.Resize(History.Capacity);
            Connections.Add(compacted, connections, ConnectionOwners(connections, full, network), network);
        }
    }

    private async Task CollectAsync(CancellationToken cancellationToken)
    {
        IFrameSource? source = null;
        var current = new SamplingOptions();
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    current = options();
                    // Created inside the loop, so a source that cannot start is reported and retried each second
                    // like a failed capture instead of ending sampling unseen.
                    source ??= createSource();
                    var frame = source.Capture(current);
                    Deliver(new Pending(
                        ProcessRetention.Compact(frame.Full, retain: current.Retain, retainAll: current.RetainAll),
                        frame.Error, frame.Full, frame.Services, current.ProcessNetwork,
                        current.ProcessNetworkSessionId,
                        frame.Connections));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("WinMon CPU sampling failed: {0}", ex);
                    if (!TryReset(source))
                    {
                        // A source that cannot drop its baselines is replaced on the next tick.
                        TryDispose(source);
                        source = null;
                    }

                    Deliver(new Pending(null, SamplingError.ReadingsUnavailable,
                        Network: current.ProcessNetwork, NetworkSessionId: current.ProcessNetworkSessionId));
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            TryDispose(source);
        }
    }

    private static bool TryReset(IFrameSource? source)
    {
        try
        {
            source?.Reset();
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon could not reset sampling: {0}", ex);
            return false;
        }
    }

    private static void TryDispose(IFrameSource? source)
    {
        try
        {
            source?.Dispose();
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon could not release sampling resources: {0}", ex);
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
                try
                {
                    if (_networkSessionId != pending.NetworkSessionId)
                    {
                        _network.Reset();
                        _networkOwners.Clear();
                        _networkSessionId = pending.NetworkSessionId;
                    }

                    if (_networkSessionId == Guid.Empty) ProcessNetwork = null;
                    else if (pending.Network is { } network)
                        ProcessNetwork = _network.Add(network, Stopwatch.GetTimestamp());
                    else
                        ProcessNetwork = _network.Unavailable(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                    if (ProcessNetwork is { } traffic)
                        ProcessNetwork = AttachNetworkOwners(traffic, pending.Full);
                }
                catch (Exception networkError)
                {
                    Trace.TraceError("WinMon network accounting unavailable: {0}", networkError);
                    ProcessNetwork = _network.Unavailable(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
                }

                if (pending.Sample is not null)
                {
                    var sample = Shift(pending.Sample);
                    var full = pending.Full is null ? null : Shift(pending.Full);
                    var services = pending.Services;
                    if (services is not null && _historyClockOffset != 0)
                        services = services with { Sample = Shift(services.Sample) };
                    Record(sample, full, services, pending.Connections, ProcessNetwork);
                    HasLiveSample = true;
                    if (full is not null) LiveSampleRecorded?.Invoke(full);
                }

                changed = true;
            }
            catch (Exception ex)
            {
                Trace.TraceError("WinMon could not record a sample: {0}", ex);
            }
        }

        if (changed) Recorded?.Invoke();
    }

    private MonitorSample Shift(MonitorSample sample)
    {
        return _historyClockOffset == 0 || sample.MonotonicTimestamp <= 0
            ? sample
            : sample with { MonotonicTimestamp = checked(sample.MonotonicTimestamp + _historyClockOffset) };
    }

    private ProcessNetworkFrame AttachNetworkOwners(ProcessNetworkFrame frame, MonitorSample? full)
    {
        var now = Stopwatch.GetTimestamp();
        var retained = _retainedNetworkLifetimes;
        retained.Clear();
        var readings = frame.Processes;
        for (var i = 0; i < readings.Count; i++)
            retained.Add(new ProcessLifetimeIdentity(readings[i].ProcessId, readings[i].CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime));
        var flows = frame.Flows;
        for (var i = 0; i < flows.Count; i++)
            retained.Add(new ProcessLifetimeIdentity(flows[i].ProcessId, flows[i].CreationTicks,
                ProcessLifetimeIdentity.WindowsFileTime));
        // Keep brief metadata for late ETW arrivals; lifetimes with observed totals retain their known path.
        // Dictionary allows Remove during enumeration since .NET Core 3.0.
        foreach (var pair in _networkOwners)
            if (!retained.Contains(pair.Key) && Stopwatch.GetElapsedTime(pair.Value.SeenAt, now).TotalSeconds > 30)
                _networkOwners.Remove(pair.Key);
        var capacityExceeded = false;
        var processes = full?.Processes ?? [];
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            if (process.LifetimeDomain != ProcessLifetimeIdentity.WindowsFileTime) continue;
            var known = _networkOwners.TryGetValue(process.Identity, out var previous);
            if (!known && _networkOwners.Count >= 8192)
            {
                capacityExceeded = true;
                continue;
            }

            var metadata = process.ImagePath.Length == 0 && known && previous.Process.ImagePath.Length > 0
                ? previous.Process
                : process;
            _networkOwners[process.Identity] = (metadata, now);
        }

        var owners = new ProcessUsage[retained.Count];
        var count = 0;
        foreach (var identity in retained)
            if (_networkOwners.TryGetValue(identity, out var owner))
                owners[count++] = owner.Process;
        if (count < owners.Length) Array.Resize(ref owners, count);
        return frame with
        {
            Owners = owners,
            Quality = frame.Quality | (capacityExceeded
                ? ProcessNetworkQuality.CapacityExceeded | ProcessNetworkQuality.Incomplete
                : 0)
        };
    }

    private IReadOnlyList<ProcessUsage> ConnectionOwners(NetworkConnectionsFrame? connections, MonitorSample? full,
        ProcessNetworkFrame? network)
    {
        var identities = _connectionOwnerLifetimes;
        identities.Clear();
        var endpoints = connections?.Connections ?? [];
        for (var i = 0; i < endpoints.Count; i++)
            if (endpoints[i].ProcessCreationTicks is { } ticks)
                identities.Add(new ProcessLifetimeIdentity(endpoints[i].ProcessId, ticks,
                    ProcessLifetimeIdentity.WindowsFileTime));
        var networkOwners = network?.Owners ?? [];
        for (var i = 0; i < networkOwners.Count; i++) identities.Add(networkOwners[i].Identity);
        _nextConnectionOwners.Clear();
        if (identities.Count == 0)
        {
            (_connectionOwners, _nextConnectionOwners) = (_nextConnectionOwners, _connectionOwners);
            return [];
        }

        var owners = new ProcessUsage[identities.Count];
        var count = 0;
        // Removing each lifetime as it is emitted keeps its first observation, sampled processes first.
        var processes = full?.Processes ?? [];
        for (var i = 0; i < processes.Count; i++)
            if (identities.Remove(processes[i].Identity))
                Add(processes[i]);
        for (var i = 0; i < networkOwners.Count; i++)
            if (identities.Remove(networkOwners[i].Identity))
                Add(networkOwners[i]);
        (_connectionOwners, _nextConnectionOwners) = (_nextConnectionOwners, _connectionOwners);
        if (count < owners.Length) Array.Resize(ref owners, count);
        return owners;

        void Add(ProcessUsage process)
        {
            // Reuse last tick's copy so retained captures share one owner per unchanged lifetime.
            var owner = _connectionOwners.TryGetValue(process.Identity, out var previous) &&
                        SameConnectionOwner(previous, process)
                ? previous
                : ConnectionOwner(process);
            _nextConnectionOwners[owner.Identity] = owner;
            owners[count++] = owner;
        }
    }

    private static ProcessUsage ConnectionOwner(ProcessUsage process)
    {
        return new ProcessUsage(process.ProcessId, process.CreationTicks,
            process.Name, 0)
        {
            LifetimeDomain = process.LifetimeDomain, ImagePath = process.ImagePath,
            AppName = process.AppName, ProductName = process.ProductName, Publisher = process.Publisher,
            HasAppWindow = process.HasAppWindow, HasHadAppWindow = process.HasHadAppWindow
        };
    }

    /// <summary>Compares the fields <see cref="ConnectionOwner"/> copies; keep the two in step.</summary>
    private static bool SameConnectionOwner(ProcessUsage owner, ProcessUsage process)
    {
        return owner.Identity == process.Identity && owner.Name == process.Name &&
               owner.ImagePath == process.ImagePath && owner.AppName == process.AppName &&
               owner.ProductName == process.ProductName && owner.Publisher == process.Publisher &&
               owner.HasAppWindow == process.HasAppWindow && owner.HasHadAppWindow == process.HasHadAppWindow;
    }

    /// <summary>A compacted capture, or a failed pass when <see cref="Sample"/> is null, awaiting the drain.</summary>
    private sealed record Pending(
        MonitorSample? Sample,
        SamplingError? Error,
        MonitorSample? Full = null,
        ServiceFrame? Services = null,
        ProcessNetworkSnapshot? Network = null,
        Guid NetworkSessionId = default,
        NetworkConnectionsFrame? Connections = null);
}
