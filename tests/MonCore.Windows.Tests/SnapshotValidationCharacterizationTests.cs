using System.Collections;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class SnapshotValidationCharacterizationTests
{
    private static ProcessNetworkReading ProcessRow() => new(42, 100, 5, 7, 0, null);
    private static ProcessNetworkFlowReading FlowRow() => new(42, 100, TransportProtocol.Tcp,
        "127.0.0.1:4000", "127.0.0.2:5000", 5, 7, 0, null);
    private static ProcessNetworkSnapshot Valid() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000001"), 1, 100, 10, 20, 70,
        ProcessNetworkQuality.Provisional, 0, 0, [ProcessRow()])
    {
        SubscriptionId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
        FlowTrafficSupported = true, FlowQuality = ProcessNetworkQuality.Provisional, Flows = [FlowRow()]
    };

    private static void Reject(ProcessNetworkSnapshot snapshot) => Assert.That(
        Assert.Throws<InvalidDataException>(() => NetworkServiceClient.ValidateSnapshot(snapshot))!.Message,
        Is.EqualTo("Network service snapshot is invalid."));

    private static IEnumerable<TestCaseData> InvalidCases()
    {
        (string Id, Func<ProcessNetworkSnapshot, ProcessNetworkSnapshot> Change)[] cases =
        [
            ("subscription", s => s with { SubscriptionId = Guid.Empty }),
            ("frequency-zero", s => s with { Frequency = 0 }),
            ("frequency-negative", s => s with { Frequency = -1 }),
            ("sequence-negative", s => s with { Sequence = -1 }),
            ("lost-negative", s => s with { EventsLost = -1 }),
            ("late-negative", s => s with { LateEvents = -1 }),
            ("dropped-negative", s => s with { DroppedEvents = -1 }),
            ("process-list-null", s => s with { Processes = null! }),
            ("flow-list-null", s => s with { Flows = null! }),
            ("unsupported-flow-rows", s => s with { FlowTrafficSupported = false }),
            ("process-null", s => s with { Processes = [null!] }),
            ("process-id-zero", s => s with { Processes = [ProcessRow() with { ProcessId = 0 }] }),
            ("process-id-negative", s => s with { Processes = [ProcessRow() with { ProcessId = -1 }] }),
            ("process-birth-zero", s => s with { Processes = [ProcessRow() with { CreationTicks = 0 }] }),
            ("process-birth-negative", s => s with { Processes = [ProcessRow() with { CreationTicks = -1 }] }),
            ("process-received-negative", s => s with { Processes = [ProcessRow() with { ReceivedBytes = -1 }] }),
            ("process-sent-negative", s => s with { Processes = [ProcessRow() with { SentBytes = -1 }] }),
            ("flow-null", s => s with { Flows = [null!] }),
            ("flow-id-zero", s => s with { Flows = [FlowRow() with { ProcessId = 0 }] }),
            ("flow-id-negative", s => s with { Flows = [FlowRow() with { ProcessId = -1 }] }),
            ("flow-birth-zero", s => s with { Flows = [FlowRow() with { CreationTicks = 0 }] }),
            ("flow-birth-negative", s => s with { Flows = [FlowRow() with { CreationTicks = -1 }] }),
            ("flow-received-negative", s => s with { Flows = [FlowRow() with { ReceivedBytes = -1 }] }),
            ("flow-sent-negative", s => s with { Flows = [FlowRow() with { SentBytes = -1 }] }),
            ("flow-protocol", s => s with { Flows = [FlowRow() with { Protocol = (TransportProtocol)999 }] }),
            ("flow-protocol-negative", s => s with { Flows = [FlowRow() with { Protocol = (TransportProtocol)(-1) }] }),
            ("failure-negative", s => s with { Quality = ProcessNetworkQuality.Unavailable, Failure = (ProcessNetworkFailure)(-1) }),
            ("unavailable-reason-without-incomplete", s => s with
            { Quality = ProcessNetworkQuality.Unavailable, IncompleteReasons = ProcessNetworkIncompleteReason.NativeLoss }),
            ("local-endpoint-control", s => s with { Flows = [FlowRow() with { LocalEndpoint = "bad\nendpoint" }] }),
            ("local-endpoint-null", s => s with { Flows = [FlowRow() with { LocalEndpoint = null! }] }),
            ("remote-endpoint-null", s => s with { Flows = [FlowRow() with { RemoteEndpoint = null! }] }),
            ("remote-endpoint-invalid", s => s with { Flows = [FlowRow() with { RemoteEndpoint = "invalid" }] }),
            ("endpoint-family", s => s with { Flows = [FlowRow() with { RemoteEndpoint = "[::1]:5000" }] }),
            ("unavailable-does-not-bypass-rows", s => s with
            {
                Quality = ProcessNetworkQuality.Unavailable, Generation = Guid.Empty, Sequence = 0,
                Flows = [FlowRow() with { ReceivedBytes = -1 }]
            })
        ];
        foreach (var item in cases)
            yield return new TestCaseData(new Func<ProcessNetworkSnapshot>(() => item.Change(Valid())))
                .SetName("RejectsMalformedSnapshot_" + item.Id);
    }

    [TestCaseSource(nameof(InvalidCases))]
    public void RejectsMalformedSnapshotWithTheOriginalWireError(Func<ProcessNetworkSnapshot> create) => Reject(create());

    [Test]
    public void NullSnapshotRetainsItsOriginalNullReferenceException() =>
        Assert.Throws<NullReferenceException>(() => NetworkServiceClient.ValidateSnapshot(null!));

    private static IEnumerable<TestCaseData> Rates()
    {
        (string Id, double? Value, bool Accepted)[] rates =
        [ ("missing", null, true), ("zero", 0, true), ("maximum-finite", double.MaxValue, true),
          ("negative", -double.Epsilon, false), ("nan", double.NaN, false),
          ("positive-infinity", double.PositiveInfinity, false), ("negative-infinity", double.NegativeInfinity, false) ];
        foreach (var target in new[] { "process-receive", "process-send", "flow-receive", "flow-send" })
            foreach (var rate in rates)
                yield return new TestCaseData(target, rate.Value, rate.Accepted).SetName("RateContract_" + target + "_" + rate.Id);
    }

    [TestCaseSource(nameof(Rates))]
    public void RatesKeepTheirFiniteNonnegativeOrMissingContract(string target, double? rate, bool accepted)
    {
        var snapshot = target switch
        {
            "process-receive" => Valid() with { Processes = [ProcessRow() with { ReceiveBytesPerSecond = rate }] },
            "process-send" => Valid() with { Processes = [ProcessRow() with { SendBytesPerSecond = rate }] },
            "flow-receive" => Valid() with { Flows = [FlowRow() with { ReceiveBytesPerSecond = rate }] },
            _ => Valid() with { Flows = [FlowRow() with { SendBytesPerSecond = rate }] }
        };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    private static IEnumerable<TestCaseData> Reasons()
    {
        var reasons = Enum.GetValues<ProcessNetworkIncompleteReason>()
            .Concat([(ProcessNetworkIncompleteReason)255, (ProcessNetworkIncompleteReason)256]);
        var index = 0;
        foreach (var reason in reasons)
        {
            foreach (var incomplete in new[] { false, true })
                yield return new TestCaseData(reason, incomplete, reason == 0 || (reason != (ProcessNetworkIncompleteReason)256 && incomplete))
                    .SetName($"ReasonContract_{index}_{(incomplete ? "incomplete" : "ordinary")}");
            index++;
        }
    }

    [TestCaseSource(nameof(Reasons))]
    public void KnownIncompleteReasonsRequireTheIncompleteFlag(ProcessNetworkIncompleteReason reason, bool incomplete, bool accepted)
    {
        var snapshot = Valid() with
        {
            IncompleteReasons = reason,
            Quality = ProcessNetworkQuality.Provisional | (incomplete ? ProcessNetworkQuality.Incomplete : 0)
        };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    private static IEnumerable<TestCaseData> Failures()
    {
        foreach (var failure in Enum.GetValues<ProcessNetworkFailure>().Append((ProcessNetworkFailure)999))
            foreach (var unavailable in new[] { false, true })
                yield return new TestCaseData(failure, unavailable, failure != (ProcessNetworkFailure)999 && (failure == 0 || unavailable))
                    .SetName($"FailureContract_{(int)failure}_{(unavailable ? "unavailable" : "available")}");
    }

    [TestCaseSource(nameof(Failures))]
    public void DefinedCollectorFailuresRequireUnavailableQuality(ProcessNetworkFailure failure, bool unavailable, bool accepted)
    {
        var snapshot = Valid() with { Failure = failure, Quality = unavailable ? ProcessNetworkQuality.Unavailable : ProcessNetworkQuality.Provisional };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [TestCase(0, true)] [TestCase(1, true)] [TestCase(2, true)] [TestCase(4, true)] [TestCase(8, true)]
    [TestCase(16, true)] [TestCase(32, true)] [TestCase(63, true)] [TestCase(64, false)] [TestCase(-1, false)]
    public void FlowQualityKeepsItsKnownBitMask(int bits, bool accepted)
    {
        var snapshot = Valid() with { FlowQuality = (ProcessNetworkQuality)bits };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [TestCase(0L, 0L, 0L, true)] [TestCase(100L, 0L, 0L, true)] [TestCase(100L, 100L, 100L, true)]
    [TestCase(100L, 101L, 101L, false)] [TestCase(100L, 20L, 19L, false)]
    [TestCase(-1L, 0L, 0L, false)] [TestCase(100L, -1L, 0L, false)] [TestCase(100L, 0L, 101L, false)]
    public void WindowsRetainTheirOrderedInclusiveBoundaries(long observed, long start, long end, bool accepted)
    {
        var snapshot = Valid() with { TimestampQpc = observed, WindowStartQpc = start, WindowEndQpc = end };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    private static IEnumerable<TestCaseData> Generations()
    {
        foreach (var unavailable in new[] { false, true })
            foreach (var generation in new[] { false, true })
                foreach (var sequence in new long[] { -1, 0, 1 })
                    yield return new TestCaseData(unavailable, generation, sequence,
                        sequence >= 0 && (unavailable || (generation && sequence > 0)))
                        .SetName($"GenerationContract_{unavailable}_{generation}_{sequence}");
    }

    [TestCaseSource(nameof(Generations))]
    public void GenerationAndSequenceRequirementsFollowAvailability(bool unavailable, bool generation, long sequence, bool accepted)
    {
        var snapshot = Valid() with
        {
            Quality = unavailable ? ProcessNetworkQuality.Unavailable : ProcessNetworkQuality.Provisional,
            Generation = generation ? Valid().Generation : Guid.Empty, Sequence = sequence
        };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [TestCase(0, true)] [TestCase(8191, true)] [TestCase(8192, true)] [TestCase(8193, false)]
    public void ProcessRowsKeepTheirInclusiveCapacity(int count, bool accepted)
    {
        var snapshot = Valid() with { Processes = Enumerable.Repeat(ProcessRow(), count).ToArray() };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [TestCase(0, true)] [TestCase(65535, true)] [TestCase(65536, true)] [TestCase(65537, false)]
    public void FlowRowsKeepTheirInclusiveCapacity(int count, bool accepted)
    {
        var flow = FlowRow();
        var snapshot = Valid() with { Flows = Enumerable.Range(1, count).Select(id => flow with { ProcessId = id }).ToArray() };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [TestCase("same", false)] [TestCase("counters-only", false)] [TestCase("process", true)]
    [TestCase("birth", true)] [TestCase("protocol", true)] [TestCase("local", true)] [TestCase("remote", true)]
    public void FlowUniquenessUsesTheExistingFivePartIdentity(string change, bool accepted)
    {
        var first = FlowRow();
        var second = change switch
        {
            "counters-only" => first with { ReceivedBytes = 19, SentBytes = 23 },
            "process" => first with { ProcessId = 43 },
            "birth" => first with { CreationTicks = 101 },
            "protocol" => first with { Protocol = TransportProtocol.Udp },
            "local" => first with { LocalEndpoint = "127.0.0.1:4001" },
            "remote" => first with { RemoteEndpoint = "127.0.0.2:5001" },
            _ => first with { }
        };
        var snapshot = Valid() with { Flows = [first, second] };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [Test]
    public void DuplicateProcessRowsKeepTheirExistingAcceptance()
    {
        var first = ProcessRow();
        var snapshot = Valid() with { Processes = [first, first with { ReceivedBytes = 99 }] };
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        Assert.That(snapshot.Processes.Select(row => row.ReceivedBytes), Is.EqualTo(new long[] { 5, 99 }));
    }

    [Test]
    public void UnknownAggregateQualityBitsRemainAcceptedWithOtherwiseOrdinaryData() =>
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(Valid() with { Quality = (ProcessNetworkQuality)(1 << 30) }));

    [TestCase("127.0.0.1:0", "127.0.0.2:65535", true)]
    [TestCase("127.0.0.1:65536", "127.0.0.2:80", false)]
    [TestCase("127.0.0.1:80", "127.0.0.2:-1", false)]
    public void EndpointPortsRetainTheirParserBoundaries(string local, string remote, bool accepted)
    {
        var snapshot = Valid() with { Flows = [FlowRow() with { LocalEndpoint = local, RemoteEndpoint = remote }] };
        if (accepted) Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
        else Reject(snapshot);
    }

    [Test]
    public void EquivalentIpv6SpellingsRemainDistinctRawFlowKeys()
    {
        var flow = FlowRow() with { LocalEndpoint = "[::1]:4000", RemoteEndpoint = "[::1]:5000" };
        var snapshot = Valid() with { Flows = [flow, flow with { LocalEndpoint = "[0:0:0:0:0:0:0:1]:4000" }] };
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
    }

    [Test]
    public void UpperScalarValuesAndUncoupledLossCountersRemainAccepted()
    {
        var snapshot = Valid() with
        {
            Sequence = long.MaxValue, Frequency = long.MaxValue, TimestampQpc = long.MaxValue,
            WindowStartQpc = 0, WindowEndQpc = long.MaxValue, EventsLost = long.MaxValue,
            LateEvents = long.MaxValue, DroppedEvents = long.MaxValue,
            Processes = [ProcessRow() with { ProcessId = int.MaxValue, CreationTicks = long.MaxValue, ReceivedBytes = long.MaxValue, SentBytes = long.MaxValue }]
        };
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(snapshot));
    }

    [TestCase(false)] [TestCase(true)]
    public void EmptyFlowCollectionsDoNotRequireFlowSupport(bool supported) =>
        Assert.DoesNotThrow(() => NetworkServiceClient.ValidateSnapshot(Valid() with { FlowTrafficSupported = supported, Flows = [] }));

    [Test]
    public void ValidationPreservesCollectionReferencesOrderAndAllPayloadValues()
    {
        var processes = new List<ProcessNetworkReading> { ProcessRow() with { ProcessId = 43 }, ProcessRow() };
        var flows = new List<ProcessNetworkFlowReading> { FlowRow() with { ProcessId = 43 }, FlowRow() };
        var snapshot = Valid() with { Processes = processes, Flows = flows };
        var before = JsonSerializer.Serialize(snapshot);

        NetworkServiceClient.ValidateSnapshot(snapshot);

        Assert.That(snapshot.Processes, Is.SameAs(processes));
        Assert.That(snapshot.Flows, Is.SameAs(flows));
        Assert.That(JsonSerializer.Serialize(snapshot), Is.EqualTo(before));
    }

    [Test]
    public void HeaderFailurePrecedesCollectionCountAccess()
    {
        var failure = new InvalidOperationException("Collection must not be inspected.");
        Reject(Valid() with
        {
            SubscriptionId = Guid.Empty, Processes = new PoisonList<ProcessNetworkReading>(true, failure),
            Flows = new PoisonList<ProcessNetworkFlowReading>(true, failure)
        });
    }

    [Test]
    public void ProcessRowFailurePrecedesFlowEnumeration()
    {
        Reject(Valid() with
        {
            Processes = [ProcessRow() with { ProcessId = 0 }],
            Flows = new PoisonList<ProcessNetworkFlowReading>(false, new InvalidOperationException("Flow enumeration must not run."))
        });
    }

    [Test]
    public void CallerEnumerationFailureRetainsItsIdentityAndPrecedence()
    {
        var failure = new InvalidOperationException("Controlled process collection failure.");
        var snapshot = Valid() with
        {
            Processes = new PoisonList<ProcessNetworkReading>(false, failure),
            Flows = new PoisonList<ProcessNetworkFlowReading>(false, new IOException("Flow enumeration must not run."))
        };
        Assert.That(Assert.Throws<InvalidOperationException>(() => NetworkServiceClient.ValidateSnapshot(snapshot)), Is.SameAs(failure));
    }

    private sealed class PoisonList<T>(bool failCount, Exception failure) : IReadOnlyList<T>
    {
        public int Count => failCount ? throw failure : 1;
        public T this[int index] => throw failure;
        public IEnumerator<T> GetEnumerator() => throw failure;
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
