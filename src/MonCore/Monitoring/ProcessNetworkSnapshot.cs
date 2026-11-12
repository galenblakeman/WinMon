namespace WinMon.Monitoring;

/// <summary>Quality of observed traffic, independent of the current adapter's counters.</summary>
[Flags]
public enum ProcessNetworkQuality
{
    None = 0,
    WarmingUp = 1,
    Provisional = 2,
    Incomplete = 4,
    CapacityExceeded = 8,
    Unavailable = 16,
    Discontinuity = 32
}

/// <summary>Bounded collector failure diagnostics; contains no process or traffic metadata.</summary>
public enum ProcessNetworkFailure
{
    None = 0,
    Startup = 1,
    TraceConsumer = 2,
    TraceStopped = 3,
    EventConsumer = 4,
    ClockDiscontinuity = 5,
    Shutdown = 6
}

/// <summary>Generation-wide coverage gaps, without process identities or traffic metadata.</summary>
[Flags]
public enum ProcessNetworkIncompleteReason
{
    None = 0,
    OwnershipUnavailable = 1,
    InvalidEvent = 2,
    QueueOverflow = 4,
    NativeLoss = 8,
    AmbiguousLifetime = 16,
    CapacityExceeded = 32,
    EventTimestamp = 64,
    ClockDiscontinuity = 128
}

/// <summary>Cumulative observed bytes and event-time rates for one Windows process lifetime.</summary>
public sealed record ProcessNetworkReading(
    int ProcessId,
    long CreationTicks,
    long ReceivedBytes,
    long SentBytes,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond);

/// <summary>Observed traffic for one process lifetime and endpoint pair. Addresses include the port.</summary>
public sealed record ProcessNetworkFlowReading(
    int ProcessId,
    long CreationTicks,
    TransportProtocol Protocol,
    string LocalEndpoint,
    string RemoteEndpoint,
    long ReceivedBytes,
    long SentBytes,
    double? ReceiveBytesPerSecond,
    double? SendBytesPerSecond);

/// <summary>A delayed, provisional event-time window. QPC timestamps use <see cref="Frequency"/> ticks per second.</summary>
public sealed record ProcessNetworkSnapshot(
    Guid Generation,
    long Sequence,
    long TimestampQpc,
    long Frequency,
    long WindowStartQpc,
    long WindowEndQpc,
    ProcessNetworkQuality Quality,
    long EventsLost,
    long LateEvents,
    IReadOnlyList<ProcessNetworkReading> Processes)
{
    /// <summary>Events rejected by the service's bounded queue, separate from native ETW loss.</summary>
    public long DroppedEvents { get; init; }

    /// <summary>Identifies a wire subscription; sequences are local to it, not shared across other clients.</summary>
    public Guid SubscriptionId { get; init; }

    /// <summary>First collector failure, retained until a fresh collector generation starts.</summary>
    public ProcessNetworkFailure Failure { get; init; }

    /// <summary>Sticky coverage reasons; None also supports older protocol-1 services.</summary>
    public ProcessNetworkIncompleteReason IncompleteReasons { get; init; }

    /// <summary>Optional endpoint-pair traffic. An empty list is either no observed flow or an older service.</summary>
    public IReadOnlyList<ProcessNetworkFlowReading> Flows { get; init; } = [];

    /// <summary>True only when the connected service supports endpoint-pair traffic.</summary>
    public bool FlowTrafficSupported { get; init; }

    /// <summary>Endpoint-pair coverage separate from process traffic coverage.</summary>
    public ProcessNetworkQuality FlowQuality { get; init; }
}
