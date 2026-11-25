using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class NetworkSetupReplyCharacterizationTests
{
    private SetupTrace _trace = null!;
    private static readonly string[] FailurePhases =
        ["prepare", "archive", "helper", "policy", "protect", "inventory", "verify", "stop", "swap", "service", "registration", "unknown"];

    [SetUp]
    public void SetUp() { _trace = new SetupTrace(); Trace.Listeners.Add(_trace); }

    [TearDown]
    public void TearDown() { Trace.Listeners.Remove(_trace); _trace.Dispose(); }

    private static IEnumerable<string> KnownPhases => FailurePhases;

    [TestCaseSource(nameof(KnownPhases))]
    public async Task KnownFailurePhasePreservesTypedErrorAndStructuredLog(string phase)
    {
        await using var pipe = await Reply(new(1, "failed", phase, 5));
        var bytes = pipe.ToArray();
        var error = await Failure(pipe, "ready");
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<NetworkSetupFailedException>());
            Assert.That(((NetworkSetupFailedException)error).Phase, Is.EqualTo(phase));
            Assert.That(((NetworkSetupFailedException)error).ErrorCode, Is.EqualTo(5));
            Assert.That(error.Message, Is.EqualTo($"Network setup failed during {phase} (5)."));
            Assert.That(_trace.Events, Has.Count.EqualTo(1));
            Assert.That(_trace.Events[0].Level, Is.EqualTo(TraceEventType.Error));
            Assert.That(_trace.Events[0].Format, Is.EqualTo("Network.SetupFailed phase=" + phase + " code={0}"));
            Assert.That(_trace.Events[0].Arguments, Is.EqualTo(new object[] { 5 }));
            Assert.That(pipe.CanRead, Is.True);
            Assert.That(pipe.ToArray(), Is.EqualTo(bytes));
        });
    }

    [TestCase(int.MinValue)]
    [TestCase(-1)]
    [TestCase(1)]
    [TestCase(1056)]
    [TestCase(int.MaxValue)]
    public async Task EveryNonzeroErrorCodeRetainsItsExactValue(int code)
    {
        await using var pipe = await Reply(new(1, "failed", "service", code));
        var error = await Failure(pipe, "complete");
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<NetworkSetupFailedException>());
            Assert.That(((NetworkSetupFailedException)error).ErrorCode, Is.EqualTo(code));
            Assert.That(error.Message, Is.EqualTo($"Network setup failed during service ({code})."));
            Assert.That(_trace.Events.Single().Arguments, Is.EqualTo(new object[] { code }));
        });
    }

    [TestCaseSource(nameof(KnownPhases))]
    public async Task KnownFailureMetadataWithZeroCodeIsRejectedWithoutLogging(string phase)
    {
        await using var pipe = await Reply(new(1, "failed", phase, 0));
        await AssertRejected(pipe, "ready");
    }

    private static IEnumerable<TestCaseData> NearMatches()
    {
        foreach (var phase in FailurePhases)
            foreach (var mutation in new[] { "upper", "leading-space", "trailing-space", "prefix", "suffix" })
                yield return new TestCaseData(phase, mutation);
    }

    [TestCaseSource(nameof(NearMatches))]
    public async Task PhaseAllowlistUsesExactOrdinalMatching(string phase, string mutation)
    {
        var value = mutation switch
        {
            "upper" => phase.ToUpperInvariant(),
            "leading-space" => " " + phase,
            "trailing-space" => phase + " ",
            "prefix" => "x" + phase,
            _ => phase + "x"
        };
        await using var pipe = await Reply(new(1, "failed", value, 5));
        await AssertRejected(pipe, "ready");
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("space")]
    [TestCase("tab")]
    [TestCase("partial")]
    [TestCase("newline")]
    [TestCase("nul")]
    [TestCase("escape")]
    [TestCase("path")]
    [TestCase("unicode")]
    [TestCase("long")]
    public async Task UntrustedFailureTextNeverEntersTheErrorTemplate(string shape)
    {
        string? value = shape switch
        {
            "null" => null, "empty" => "", "space" => " ", "tab" => "\t", "partial" => "servic",
            "newline" => "service\r\nNetwork.SetupComplete", "nul" => "service\0", "escape" => "service\u001b[31m",
            "path" => "../service", "unicode" => "sеrvice", _ => new string('x', 2048)
        };
        await using var pipe = await Reply(new(1, "failed", value, 5));
        await AssertRejected(pipe, "ready");
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public async Task VersionRejectionPrecedesOtherwiseValidFailureMetadata(int version)
    {
        await using var pipe = await Reply(new(version, "failed", "service", 5));
        await AssertRejected(pipe, "ready");
    }

    [TestCase("wrong-phase")]
    [TestCase("phase-case")]
    [TestCase("null-phase")]
    [TestCase("success-with-failure")]
    [TestCase("success-with-code")]
    [TestCase("failed-without-metadata")]
    [TestCase("unknown-failure")]
    [TestCase("zero-failure")]
    public async Task UnexpectedReplyCombinationsRetainTheGenericProtocolError(string shape)
    {
        var reply = shape switch
        {
            "wrong-phase" => new NetworkSetupReply(1, "complete"),
            "phase-case" => new(1, "READY"),
            "null-phase" => new(1, null!),
            "success-with-failure" => new(1, "ready", "service"),
            "success-with-code" => new(1, "ready", null, 5),
            "failed-without-metadata" => new(1, "failed"),
            "unknown-failure" => new(1, "failed", "other", 5),
            _ => new(1, "failed", "service", 0)
        };
        await using var pipe = await Reply(reply);
        await AssertRejected(pipe, "ready");
    }

    [TestCase("ready")]
    [TestCase("complete")]
    [TestCase("custom")]
    [TestCase("failed")]
    [TestCase("")]
    [TestCase(null)]
    public async Task MatchingExpectedPhaseWithoutFailureMetadataRetainsExistingSuccess(string? phase)
    {
        await using var pipe = await Reply(new(1, phase!));
        await NetworkSetupBroker.ExpectAsync(pipe, phase!, default);
        Assert.Multiple(() => { Assert.That(_trace.Events, Is.Empty); Assert.That(pipe.CanRead, Is.True); });
    }

    [Test]
    public async Task EachCallConsumesOneFrameAndLeavesTheStreamOwnedByItsCaller()
    {
        await using var pipe = new MemoryStream();
        await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupReply(1, "ready"), NetworkSetupWireJsonContext.Default.NetworkSetupReply, default);
        var firstEnd = pipe.Position;
        await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupReply(1, "complete"), NetworkSetupWireJsonContext.Default.NetworkSetupReply, default);
        var bytes = pipe.ToArray(); pipe.Position = 0;
        await NetworkSetupBroker.ExpectAsync(pipe, "ready", default);
        Assert.That(pipe.Position, Is.EqualTo(firstEnd));
        await NetworkSetupBroker.ExpectAsync(pipe, "complete", default);
        Assert.Multiple(() => { Assert.That(pipe.Position, Is.EqualTo(pipe.Length)); Assert.That(pipe.CanRead, Is.True); Assert.That(pipe.ToArray(), Is.EqualTo(bytes)); Assert.That(_trace.Events, Is.Empty); });
    }

    [Test]
    public async Task FragmentedReadsStillProduceOneValidatedReply()
    {
        await using var source = await Reply(new(1, "ready"));
        await using var pipe = new FragmentedStream(source.ToArray());
        await NetworkSetupBroker.ExpectAsync(pipe, "ready", default);
        Assert.Multiple(() => { Assert.That(pipe.Position, Is.EqualTo(pipe.Length)); Assert.That(pipe.Reads, Is.GreaterThan(4)); Assert.That(_trace.Events, Is.Empty); });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationPropagatesItsTokenWithoutClosingTheStream(bool duringBody)
    {
        using var cancel = new CancellationTokenSource();
        await using var source = await Reply(new(1, "ready"));
        await using var pipe = new CancelingStream(source.ToArray(), cancel, duringBody);
        if (!duringBody) cancel.Cancel();
        var error = Assert.CatchAsync<OperationCanceledException>(async () => await NetworkSetupBroker.ExpectAsync(pipe, "ready", cancel.Token));
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancel.Token));
            Assert.That(pipe.Position, Is.EqualTo(duringBody ? 4 : 0));
            Assert.That(pipe.CanRead, Is.True);
            Assert.That(_trace.Events, Is.Empty);
        });
    }

    [TestCase("empty")]
    [TestCase("short-header")]
    [TestCase("short-body")]
    public async Task TruncatedFramesRetainTheWireEndOfStreamError(string shape)
    {
        var bytes = shape == "empty" ? Array.Empty<byte>() : shape == "short-header" ? new byte[3] : Frame(Encoding.UTF8.GetBytes("{}"), 10);
        await using var pipe = new MemoryStream(bytes);
        var error = await Failure(pipe, "ready");
        Assert.Multiple(() => { Assert.That(error, Is.TypeOf<EndOfStreamException>()); Assert.That(error.Message, Is.EqualTo("Network setup pipe closed.")); Assert.That(pipe.CanRead, Is.True); Assert.That(_trace.Events, Is.Empty); });
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(NetworkSetupWire.MaximumMessageBytes + 1)]
    public async Task InvalidFrameLengthsRetainTheWireLengthError(int length)
    {
        await using var pipe = new MemoryStream(Frame([], length));
        var error = await Failure(pipe, "ready");
        Assert.Multiple(() => { Assert.That(error, Is.TypeOf<InvalidDataException>()); Assert.That(error.Message, Is.EqualTo("Network setup message length is invalid.")); Assert.That(pipe.Position, Is.EqualTo(4)); Assert.That(_trace.Events, Is.Empty); });
    }

    [TestCase("malformed")]
    [TestCase("null")]
    [TestCase("array")]
    public async Task InvalidPayloadsRetainTheWireDecodeError(string shape)
    {
        var bytes = Encoding.UTF8.GetBytes(shape switch { "malformed" => "{", "null" => "null", _ => "[]" });
        await using var pipe = new MemoryStream(Frame(bytes));
        var error = await Failure(pipe, "ready");
        Assert.Multiple(() => { Assert.That(error, Is.TypeOf<InvalidDataException>()); Assert.That(error.Message, Is.EqualTo("Network setup message is invalid.")); Assert.That(error.InnerException is JsonException, Is.EqualTo(shape != "null")); Assert.That(_trace.Events, Is.Empty); });
    }

    [Test]
    public async Task MaximumLengthValidPayloadIsAccepted()
    {
        var bytes = new byte[NetworkSetupWire.MaximumMessageBytes]; Array.Fill(bytes, (byte)' ');
        var json = JsonSerializer.SerializeToUtf8Bytes(new NetworkSetupReply(1, "ready"), NetworkSetupWireJsonContext.Default.NetworkSetupReply);
        json.CopyTo(bytes, 0);
        await using var pipe = new MemoryStream(Frame(bytes));
        await NetworkSetupBroker.ExpectAsync(pipe, "ready", default);
        Assert.Multiple(() => { Assert.That(pipe.Position, Is.EqualTo(bytes.Length + 4)); Assert.That(_trace.Events, Is.Empty); });
    }

    private async Task AssertRejected(Stream pipe, string expected)
    {
        var error = await Failure(pipe, expected);
        Assert.Multiple(() => { Assert.That(error, Is.TypeOf<InvalidDataException>()); Assert.That(error.Message, Is.EqualTo("Unexpected network setup response.")); Assert.That(_trace.Events, Is.Empty); Assert.That(pipe.CanRead, Is.True); });
    }
    private static async Task<Exception> Failure(Stream pipe, string expected)
    {
        try { await NetworkSetupBroker.ExpectAsync(pipe, expected, default); }
        catch (Exception error) { return error; }
        Assert.Fail("The response must be rejected."); throw new InvalidOperationException();
    }
    private static async Task<MemoryStream> Reply(NetworkSetupReply reply)
    {
        var pipe = new MemoryStream();
        await NetworkSetupWire.WriteAsync(pipe, reply, NetworkSetupWireJsonContext.Default.NetworkSetupReply, default);
        pipe.Position = 0; return pipe;
    }
    private static byte[] Frame(byte[] payload, int? count = null)
    {
        var bytes = new byte[payload.Length + 4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, count ?? payload.Length); payload.CopyTo(bytes, 4); return bytes;
    }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int Reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { Reads++; return base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken); }
    }
    private sealed class CancelingStream(byte[] bytes, CancellationTokenSource cancel, bool duringBody) : MemoryStream(bytes)
    {
        private int _reads;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { if (++_reads == 2 && duringBody) cancel.Cancel(); return base.ReadAsync(buffer, cancellationToken); }
    }
    private sealed class SetupTrace : TraceListener
    {
        public List<(TraceEventType Level, string Format, object?[] Arguments)> Events { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { }
        public override void TraceEvent(TraceEventCache? cache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        { if (format?.StartsWith("Network.SetupFailed phase=", StringComparison.Ordinal) == true) Events.Add((eventType, format, args?.ToArray() ?? [])); }
    }
}
