using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using NUnit.Framework;
using WinMon.NetworkProbe;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkCaptureAccountingCharacterizationTests
{
    private const int Worker = 42;
    private const long Creation = 123;
    private static readonly DateTime Started = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [TestCase(0)]
    [TestCase(43)]
    [TestCase(int.MaxValue)]
    public void ForeignProcessEventsCountOnlyTowardTheGlobalTotal(int pid)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(pid: pid, version: 0, clock: false, payload: []);
        capture.Add(packet.Data, 0, true, false, true);
        Check(capture.ReadAccounting(), events: 1, kinds: 0);
    }

    [Test]
    public void UnsupportedWorkerVersionReturnsBeforePayloadSizeAndTimestampChecks()
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(version: 0, clock: false, payload: []);
        capture.Add(packet.Data, 0, true, false, true);
        Check(capture.ReadAccounting(), events: 1, kinds: 0, unsupported: 1);
    }

    [TestCase(1)]
    [TestCase(255)]
    public void SupportedVersionBoundaryAndLaterVersionsAttributeBytes(int version)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(version: (byte)version);
        capture.Add(packet.Data, 11, true, false, true);
        Check(capture.ReadAccounting(), 1, 1, rows: [new(Worker, Creation, true, false, true, 11)]);
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void EachProtocolFamilyAndDirectionAccumulatesOnlyItsOwnBytes(bool tcp, bool ipv6, bool send)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet();
        capture.Add(packet.Data, 10, tcp, ipv6, send);
        capture.Add(packet.Data, 15, tcp, ipv6, send);
        Check(capture.ReadAccounting(), 2, 1, rows: [new(Worker, Creation, tcp, ipv6, send, 25)]);
    }

    [Test]
    public void PayloadIdentityIsCheckedOnceForEachOfTheEightIndependentKinds()
    {
        using var capture = new PassiveCapture();
        using var mismatch = new Packet(payload: PidBytes(Worker + 1));
        using var shortRepeat = new Packet(payload: []);
        var expected = new List<NetworkLedgerRow>();
        foreach (var tcp in new[] { false, true })
        foreach (var ipv6 in new[] { false, true })
        foreach (var send in new[] { false, true })
        {
            capture.Add(mismatch.Data, 5, tcp, ipv6, send);
            capture.Add(shortRepeat.Data, 7, tcp, ipv6, send);
            expected.Add(new(Worker, Creation, tcp, ipv6, send, 12));
        }
        Check(capture.ReadAccounting(), 16, 8, mismatches: 8, rows: expected);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ShortPayloadsRecordAMismatchWithoutDroppingAttributedBytes(int length)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(payload: PidBytes(Worker)[..length]);
        capture.Add(packet.Data, 9, false, true, false);
        Check(capture.ReadAccounting(), 1, 1, mismatches: 1, rows: [new(Worker, Creation, false, true, false, 9)]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroSizedEventsCheckPayloadIdentityButNeverReadTheTimestamp(bool mismatch)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(payload: PidBytes(mismatch ? Worker + 1 : Worker), clock: false);
        capture.Add(packet.Data, 0, true, true, false);
        Check(capture.ReadAccounting(), 1, 1, mismatches: mismatch ? 1 : 0, zero: 1);
    }

    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void NegativeSizesBecomeUnattributedRatherThanZeroSizedEvents(int size)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet();
        capture.Add(packet.Data, size, true, false, true);
        Check(capture.ReadAccounting(), 1, 1, unattributed: 1);
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void AttributionStartsStrictlyAfterTheWorkerLifetimeBoundary(int milliseconds)
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet(timestamp: 1000 + milliseconds);
        capture.Add(packet.Data, 5, true, false, true);
        Check(capture.ReadAccounting(), 1, 1, unattributed: milliseconds <= 0 ? 1 : 0,
            rows: milliseconds <= 0 ? [] : [new(Worker, Creation, true, false, true, 5)]);
    }

    [Test]
    public void NullEventPreservesTheTotalIncrementBeforeItsNullReferenceFailure()
    {
        using var capture = new PassiveCapture();
        Assert.Throws<NullReferenceException>(() => capture.Add(null!, 1, true, false, true));
        Check(capture.ReadAccounting(), 1, 0);
    }

    [Test]
    public async Task ConcurrentCallbacksKeepTotalsBytesAndOneTimePayloadChecksConsistent()
    {
        using var capture = new PassiveCapture();
        using var packet = new Packet();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var index = 0; index < 250; index++) capture.Add(packet.Data, 1, false, false, true);
        })));
        Check(capture.ReadAccounting(), 1000, 1, rows: [new(Worker, Creation, false, false, true, 1000)]);
    }

    [Test]
    public void PassiveCaptureCanBeDisposedTwiceWithoutStartingAConsumer()
    {
        using var capture = new PassiveCapture();
        Check(capture.ReadAccounting(), 0, 0);
        Assert.DoesNotThrow(capture.Capture.Dispose);
        Assert.DoesNotThrow(capture.Capture.Dispose);
        capture.AssertInactive();
    }

    [Test, Category("Integration")]
    public void SnapshotBeforeActivationReportsTheMissingNativeSessionWithoutStartingIt()
    {
        using var capture = new PassiveCapture();
        var error = Assert.Throws<COMException>(() => capture.Capture.Snapshot());
        Assert.That(error!.HResult, Is.EqualTo(unchecked((int)0x80071069)));
        capture.AssertInactive();
    }

    private static byte[] PidBytes(int pid)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, pid);
        return bytes;
    }

    private static void Check(AccountingState actual, long events, int kinds, long mismatches = 0,
        long zero = 0, long unsupported = 0, long unattributed = 0, IReadOnlyList<NetworkLedgerRow>? rows = null)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.NetworkEvents, Is.EqualTo(events));
            Assert.That(actual.PayloadKindsChecked, Is.EqualTo(kinds));
            Assert.That(actual.PayloadPidMismatches, Is.EqualTo(mismatches));
            Assert.That(actual.ZeroSizeWorkerEvents, Is.EqualTo(zero));
            Assert.That(actual.UnsupportedWorkerEvents, Is.EqualTo(unsupported));
            Assert.That(actual.UnattributedWorkerEvents, Is.EqualTo(unattributed));
            Assert.That(actual.WorkerStarts, Is.Zero);
            Assert.That(actual.WorkerStops, Is.Zero);
            Assert.That(actual.Rows, Is.EquivalentTo(rows ?? []));
        });
    }

    private sealed record AccountingState(IReadOnlyList<NetworkLedgerRow> Rows, long NetworkEvents,
        long UnattributedWorkerEvents, long PayloadPidMismatches, int PayloadKindsChecked,
        long WorkerStarts, long WorkerStops, long ZeroSizeWorkerEvents, long UnsupportedWorkerEvents);

    private sealed class PassiveCapture : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly TraceEventSession _session;
        internal NetworkCapture Capture { get; } = new(Worker, Creation, Started);
        internal Action<TraceEvent, int, bool, bool, bool> Add { get; }

        internal PassiveCapture()
        {
            // Pinned TraceEvent 3.2.8 defers activation until EnableProvider; never call Start or Source here.
            _session = (TraceEventSession)typeof(NetworkCapture).GetField("_session", Fields)!.GetValue(Capture)!;
            AssertInactive();
            Add = typeof(NetworkCapture).GetMethod("Add", Fields)!
                .CreateDelegate<Action<TraceEvent, int, bool, bool, bool>>(Capture);
        }

        internal AccountingState ReadAccounting()
        {
            // Snapshot also queries native EventsLost and requires an active ETW session. Observe the
            // original Add side effects directly, as the existing collector callback fixtures do.
            AccountingState state;
            lock (Read<object>("_gate"))
            {
                state = new(Read<NetworkLedger>("_ledger").Snapshot(Started.AddSeconds(2)),
                    Read<long>("_networkEvents"), Read<long>("_unattributedWorkerEvents"),
                    Read<long>("_payloadPidMismatches"), Read<HashSet<(bool, bool, bool)>>("_checkedPayloads").Count,
                    Read<long>("_processStarts"), Read<long>("_processStops"),
                    Read<long>("_zeroSizeWorkerEvents"), Read<long>("_unsupportedWorkerEvents"));
            }
            AssertInactive();
            return state;
        }

        private T Read<T>(string name) => (T)typeof(NetworkCapture).GetField(name, Fields)!.GetValue(Capture)!;

        internal void AssertInactive()
        {
            Assert.That(_session.IsActive, Is.False);
            Assert.That(typeof(NetworkCapture).GetField("_consumer", Fields)!.GetValue(Capture), Is.Null);
        }

        public void Dispose()
        {
            try { AssertInactive(); }
            finally { Capture.Dispose(); }
        }
    }

    // Same owned synthetic-record convention as NetworkService.Tests.EtwHeader, with payload and a fixed UTC clock.
    // TraceEvent 3.2.8 has no public event-header factory. No ETW source constructor or native session is used.
    private sealed class Packet : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo RecordField = typeof(TraceEvent).GetField("eventRecord", Fields)!;
        private nint _record, _payload;
        internal TraceEvent Data { get; }

        internal Packet(int pid = Worker, byte version = 1, long timestamp = 2000, bool clock = true, byte[]? payload = null)
        {
            if (pid == -1) throw new ArgumentOutOfRangeException(nameof(pid));
            payload ??= PidBytes(Worker);
            var type = RecordField.FieldType.GetElementType()!;
            var record = Activator.CreateInstance(type)!;
            var headerField = type.GetField("EventHeader")!;
            var header = Activator.CreateInstance(headerField.FieldType)!;
            headerField.FieldType.GetField("ProcessId")!.SetValue(header, pid);
            headerField.FieldType.GetField("Version")!.SetValue(header, version);
            headerField.FieldType.GetField("TimeStamp")!.SetValue(header, timestamp);
            headerField.SetValue(record, header);
            Data = (TraceEvent)RuntimeHelpers.GetUninitializedObject(typeof(TcpIpTraceData));
            if (clock)
            {
                var source = (ETWTraceEventSource)RuntimeHelpers.GetUninitializedObject(typeof(ETWTraceEventSource));
                GC.SuppressFinalize(source);
                typeof(TraceEventSource).GetField("_QPCFreq", Fields)!.SetValue(source, 1000L);
                typeof(TraceEventSource).GetField("_syncTimeQPC", Fields)!.SetValue(source, 1000L);
                typeof(TraceEventSource).GetField("_syncTimeUTC", Fields)!.SetValue(source, Started);
                typeof(TraceEventSource).GetField("sessionStartTimeQPC", Fields)!.SetValue(source, 1000L);
                typeof(TraceEvent).GetField("traceEventSource", Fields)!.SetValue(Data, source);
            }
            _payload = Marshal.AllocHGlobal(Math.Max(1, payload.Length));
            try
            {
                Marshal.Copy(payload, 0, _payload, payload.Length);
                type.GetField("UserDataLength")!.SetValue(record, checked((ushort)payload.Length));
                type.GetField("UserData")!.SetValue(record, _payload);
                _record = Marshal.AllocHGlobal(Marshal.SizeOf(type));
                Marshal.StructureToPtr(record, _record, false);
                RecordField.SetValue(Data, _record);
                typeof(TraceEvent).GetField("userData", Fields)!.SetValue(Data, _payload);
                Assert.That(Data.ProcessID, Is.EqualTo(pid));
                Assert.That(Data.Version, Is.EqualTo(version));
                Assert.That(Data.EventDataLength, Is.EqualTo(payload.Length));
                if (clock) Assert.That(Data.TimeStamp.ToUniversalTime(), Is.EqualTo(Started.AddMilliseconds(timestamp - 1000)));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            RecordField.SetValue(Data, nint.Zero);
            typeof(TraceEvent).GetField("userData", Fields)!.SetValue(Data, nint.Zero);
            if (_record != 0) { Marshal.FreeHGlobal(_record); _record = 0; }
            if (_payload != 0) { Marshal.FreeHGlobal(_payload); _payload = 0; }
        }
    }
}
