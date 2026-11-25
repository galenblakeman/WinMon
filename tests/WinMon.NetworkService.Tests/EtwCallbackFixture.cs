using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using MonCore.Monitoring;

namespace WinMon.NetworkService.Tests;

// TraceEvent 3.2.8 exposes no event-header factory; this owns only synthetic memory, never an ETW session.
internal sealed class EtwHeader : IDisposable
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo RecordField = typeof(TraceEvent).GetField("eventRecord", Fields)!;
    private nint _record;

    internal EtwHeader(byte version = 1, int pid = 42, long timestamp = 3000, bool clock = true)
    {
        // TraceEvent resolves raw PID -1 through thread metadata that these synthetic headers do not contain.
        if (pid == -1) throw new ArgumentOutOfRangeException(nameof(pid));
        var type = RecordField.FieldType.GetElementType()!;
        var record = Activator.CreateInstance(type)!;
        var headerField = type.GetField("EventHeader")!;
        var header = Activator.CreateInstance(headerField.FieldType)!;
        headerField.FieldType.GetField("Version")!.SetValue(header, version);
        headerField.FieldType.GetField("ProcessId")!.SetValue(header, pid);
        headerField.FieldType.GetField("TimeStamp")!.SetValue(header, timestamp);
        headerField.SetValue(record, header);
        Data = (TraceEvent)RuntimeHelpers.GetUninitializedObject(typeof(TcpIpTraceData));
        if (clock)
        {
            var source = (ETWTraceEventSource)RuntimeHelpers.GetUninitializedObject(typeof(ETWTraceEventSource));
            // No constructor ran and no native resources exist; do not run the native source finalizer.
            GC.SuppressFinalize(source);
            typeof(TraceEventSource).GetField("_QPCFreq", Fields)!.SetValue(source, 1000L);
            typeof(TraceEventSource).GetField("sessionStartTimeQPC", Fields)!.SetValue(source, 1000L);
            typeof(TraceEvent).GetField("traceEventSource", Fields)!.SetValue(Data, source);
        }
        _record = Marshal.AllocHGlobal(Marshal.SizeOf(type));
        try
        {
            Marshal.StructureToPtr(record, _record, false);
            RecordField.SetValue(Data, _record);
        }
        catch
        {
            Marshal.FreeHGlobal(_record);
            throw;
        }
    }

    internal TraceEvent Data { get; }
    public void Dispose()
    {
        if (_record == 0) return;
        RecordField.SetValue(Data, nint.Zero);
        Marshal.FreeHGlobal(_record);
        _record = 0;
    }
}

internal sealed class EtwCallbackFixture : IAsyncDisposable
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type EventType = typeof(EtwNetworkCollector).GetNestedType("CollectorEvent", BindingFlags.NonPublic)!;
    private static readonly Type ReaderType = typeof(ChannelReader<>).MakeGenericType(EventType);
    private readonly object _reader;

    internal EtwCallbackFixture() : this(new EtwNetworkCollector(["S-1-5-21-1-2-3-1001"])) { }

    internal EtwCallbackFixture(EtwNetworkCollector collector)
    {
        Collector = collector;
        Offset = 17;
        Packet = typeof(EtwNetworkCollector).GetMethod("Enqueue", Members, [typeof(TraceEvent), typeof(int), typeof(bool),
            typeof(TransportProtocol), typeof(IPAddress), typeof(int), typeof(IPAddress), typeof(int)])!
            .CreateDelegate<Action<TraceEvent, int, bool, TransportProtocol, IPAddress, int, IPAddress, int>>(Collector);
        Aggregate = typeof(EtwNetworkCollector).GetMethod("Enqueue", Members, [typeof(TraceEvent), typeof(int), typeof(bool)])!
            .CreateDelegate<Action<TraceEvent, int, bool>>(Collector);
        var channel = Field("_events").GetValue(Collector)!;
        _reader = typeof(Channel<>).MakeGenericType(EventType).GetProperty("Reader")!.GetValue(channel)!;
    }

    internal EtwNetworkCollector Collector { get; }
    internal Action<TraceEvent, int, bool, TransportProtocol, IPAddress, int, IPAddress, int> Packet { get; }
    internal Action<TraceEvent, int, bool> Aggregate { get; }
    internal int Count => (int)ReaderType.GetProperty("Count")!.GetValue(_reader)!;
    internal long Dropped => Read<long>("_droppedEvents");
    internal int RateGap => Read<int>("_rateGapPending");
    internal int Ambiguous => Read<int>("_flowAmbiguous");
    internal long Offset { set => Field("_qpcOffset").SetValue(Collector, value); }
    internal static long TwoSeconds => 17 + 2 * Stopwatch.Frequency;

    internal void Lifecycle(bool start, int pid, long timestamp)
    {
        var kind = Enum.Parse(typeof(EtwNetworkCollector).GetNestedType("CollectorEventKind", BindingFlags.NonPublic)!, start ? "Start" : "Stop");
        var item = Activator.CreateInstance(EventType, kind, pid, timestamp, 0, false, null, null, 0, null, 0)!;
        typeof(EtwNetworkCollector).GetMethod("Enqueue", Members, [EventType])!.Invoke(Collector, [item]);
    }

    internal void Network(int pid, long timestamp, int bytes, bool send, TransportProtocol? protocol = null,
        IPAddress? local = null, int localPort = 0, IPAddress? remote = null, int remotePort = 0)
    {
        var kind = Enum.Parse(typeof(EtwNetworkCollector).GetNestedType("CollectorEventKind", BindingFlags.NonPublic)!, "Network");
        var item = Activator.CreateInstance(EventType, kind, pid, timestamp, bytes, send, protocol, local, localPort, remote, remotePort)!;
        typeof(EtwNetworkCollector).GetMethod("Enqueue", Members, [EventType])!.Invoke(Collector, [item]);
    }

    internal bool Batch() => typeof(EtwNetworkCollector).GetMethod("ProcessBatch", Members)!.CreateDelegate<Func<bool>>(Collector)();
    internal Task Consume() => typeof(EtwNetworkCollector).GetMethod("ConsumeAsync", Members)!.CreateDelegate<Func<Task>>(Collector)();
    internal void Cancel() => Read<CancellationTokenSource>("_stop").Cancel();
    internal bool Complete(Exception? error = null)
    {
        var channel = Field("_events").GetValue(Collector)!;
        var writer = typeof(Channel<>).MakeGenericType(EventType).GetProperty("Writer")!.GetValue(channel)!;
        return (bool)typeof(ChannelWriter<>).MakeGenericType(EventType).GetMethod("TryComplete")!.Invoke(writer, [error])!;
    }

    internal QueuedPacket? Take()
    {
        object?[] args = [null];
        if (!(bool)ReaderType.GetMethod("TryRead")!.Invoke(_reader, args)!) return null;
        var item = args[0]!;
        T Get<T>(string name) => (T)EventType.GetProperty(name)!.GetValue(item)!;
        return new(Get<object>("Kind").ToString()!, Get<int>("ProcessId"), Get<long>("EventQpc"), Get<int>("Bytes"),
            Get<bool>("Send"), Get<TransportProtocol?>("Protocol"), Get<IPAddress?>("LocalAddress"), Get<int>("LocalPort"),
            Get<IPAddress?>("RemoteAddress"), Get<int>("RemotePort"));
    }

    private T Read<T>(string name) => (T)Field(name).GetValue(Collector)!;
    private static FieldInfo Field(string name) => typeof(EtwNetworkCollector).GetField(name, Members)!;
    public ValueTask DisposeAsync() => Collector.DisposeAsync();
}

internal sealed record QueuedPacket(string Kind, int Pid, long Qpc, int Bytes, bool Send, TransportProtocol? Protocol,
    IPAddress? Local, int LocalPort, IPAddress? Remote, int RemotePort);
