using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture, NonParallelizable]
public sealed class RestartHistoryReadTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const long Frequency = 10_000_000, SavedAt = 5_000_000_000, SavedTicks = 500_000;
    private byte[] _samplePayload = [];
    private byte[] _ledgerPayload = [];
    private static readonly MonitorSample Marker = new(DateTimeOffset.UnixEpoch, 10, [], 0, MonotonicTimestamp: 1);
    private sealed record Chunk(byte Kind, byte[] Payload, bool BadCrc = false, int? Length = null);

    [OneTimeSetUp]
    public void ReadRetainedVersionThreeSamplePayload()
    {
        _samplePayload = FixturePayload("restart-history-v3.bin", 2);
        _ledgerPayload = FixturePayload("restart-history-v1.bin", 1);
    }

    private static byte[] FixturePayload(string file, byte wanted)
    {
        using var reader = new BinaryReader(File.OpenRead(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", file)));
        reader.BaseStream.Position = 8;
        _ = reader.ReadString();
        _ = reader.ReadString();
        reader.BaseStream.Position += 24;
        while (reader.BaseStream.Position < reader.BaseStream.Length - 12)
        {
            var kind = reader.ReadByte();
            var length = reader.ReadInt32();
            _ = reader.ReadUInt32();
            var payload = reader.ReadBytes(length);
            if (kind == wanted) return payload;
        }
        throw new InvalidDataException("The retained fixture has no requested chunk.");
    }

    private static RestartClock Clock(long elapsedTicks = 0, long? elapsedMilliseconds = null) =>
        new(() => SavedAt + elapsedTicks, () => SavedTicks + (elapsedMilliseconds ?? elapsedTicks / 10_000), Frequency);

    private static (byte[] Bytes, int HeaderLength) FileData(params Chunk[] chunks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("WMRH"u8);
        writer.Write(3);
        writer.Write("test");
        writer.Write(Token);
        writer.Write(Frequency);
        writer.Write(SavedAt);
        writer.Write(SavedTicks);
        var header = stream.ToArray();
        foreach (var chunk in chunks)
        {
            writer.Write(chunk.Kind);
            writer.Write(chunk.Length ?? chunk.Payload.Length);
            writer.Write(RestartHistory.Crc32.Compute(chunk.Payload) ^ (chunk.BadCrc ? 1u : 0));
            writer.Write(chunk.Payload);
        }
        writer.Write("WMRT"u8);
        writer.Write(chunks.Length);
        writer.Write(RestartHistory.Crc32.Compute(header));
        return (stream.ToArray(), header.Length);
    }

    private static byte[] Compressed(Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(deflate, Encoding.UTF8)) body(writer);
        return stream.ToArray();
    }

    [TestCase("version", "an unknown format version")]
    [TestCase("token", "another update's token")]
    [TestCase("frequency", "a different timer frequency")]
    [TestCase("age", "monotonic clocks that do not match a recent restart")]
    [TestCase("trailer", "an unfinished save")]
    [TestCase("trailer-magic", "an unfinished save")]
    public void Read_RejectionsKeepTheirWarningAndLeaveTheCallerStreamOpen(string failure, string reason)
    {
        var data = FileData(new Chunk(2, _samplePayload));
        if (failure == "version") BinaryPrimitives.WriteInt32LittleEndian(data.Bytes.AsSpan(4), RestartHistory.FormatVersion + 1);
        if (failure == "trailer") data.Bytes[^1] ^= 1;
        if (failure == "trailer-magic") data.Bytes[^12] ^= 1;
        var clock = failure == "age" ? Clock(301 * Frequency) : Clock();
        if (failure == "frequency") clock = clock with { Frequency = Frequency * 2 };
        using var stream = new MemoryStream(data.Bytes);
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            Assert.That(RestartHistory.Read(stream, failure == "token" ? "other" : Token, clock), Is.Null);
            Assert.That(output.ToString(), Does.Contain("WinMon discarded restart history with " + reason + "."));
        }
        finally { Trace.Listeners.Remove(listener); }
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(failure switch
        {
            "version" => 8,
            "token" => data.HeaderLength - 24,
            "trailer" or "trailer-magic" => data.Bytes.Length,
            _ => data.HeaderLength
        }));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(4, true)]
    [TestCase(5, true)]
    [TestCase(6, false)]
    public void Read_HeaderVersionLimitsAreInclusive(int version, bool accepted)
    {
        var data = FileData();
        BinaryPrimitives.WriteInt32LittleEndian(data.Bytes.AsSpan(4), version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.Bytes.AsSpan(data.Bytes.Length - 4), RestartHistory.Crc32.Compute(data.Bytes.AsSpan(0, data.HeaderLength)));
        using var stream = new MemoryStream(data.Bytes);
        var result = RestartHistory.Read(stream, Token, Clock());
        Assert.That(result is not null, Is.EqualTo(accepted));
        if (accepted) Assert.That(result!.Samples, Is.Empty);
    }

    [TestCase(0, true)]
    [TestCase(300 * Frequency, true)]
    [TestCase(300 * Frequency + 1, false)]
    [TestCase(-1, false)]
    public void Read_RestartAgeKeepsExactTickBoundaries(long elapsed, bool accepted)
    {
        using var stream = new MemoryStream(FileData(new Chunk(2, _samplePayload)).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock(elapsed));
        Assert.That(result is not null, Is.EqualTo(accepted));
        if (accepted) Assert.That(result!.Samples, Has.Count.EqualTo(1));
    }

    [TestCase(2000, true)]
    [TestCase(2001, false)]
    [TestCase(-2000, true)]
    [TestCase(-2001, false)]
    public void Read_ClockAgreementKeepsTheInclusiveTwoSecondLimit(long difference, bool accepted)
    {
        using var stream = new MemoryStream(FileData(new Chunk(2, _samplePayload)).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock(10 * Frequency, 10_000 + difference));
        Assert.That(result is not null, Is.EqualTo(accepted));
        if (accepted) Assert.That(result!.Samples, Has.Count.EqualTo(1));
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(300_000, true)]
    [TestCase(300_001, false)]
    public void Read_TickClockAgeHasItsOwnInclusiveBounds(long milliseconds, bool accepted)
    {
        using var stream = new MemoryStream(FileData(new Chunk(2, _samplePayload)).Bytes);
        var timer = Math.Clamp(milliseconds, 0, 300_000) * Frequency / 1000;
        var result = RestartHistory.Read(stream, Token, Clock(timer, milliseconds));
        Assert.That(result is not null, Is.EqualTo(accepted));
        if (accepted) Assert.That(result!.Samples, Has.Count.EqualTo(1));
    }

    [Test]
    public void Read_NormalModeSkipsAgeChecksButStillChecksFrequencyAndConsultsBothClocks()
    {
        var ticksRead = 0;
        var clock = Clock(-Frequency) with { TickCount = () => { ticksRead++; return SavedTicks + 900_000; } };
        using var stream = new MemoryStream(FileData(new Chunk(2, _samplePayload)).Bytes);
        Assert.That(RestartHistory.Read(stream, Token, clock, false)!.Samples, Has.Count.EqualTo(1));
        Assert.That(ticksRead, Is.EqualTo(1));
        stream.Position = 0;
        Assert.That(RestartHistory.Read(stream, Token, clock with { Frequency = Frequency * 2 }, false), Is.Null);
    }

    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(int.MaxValue, 1)]
    public void Read_DeclaredChunkCountBoundsHowMuchAvailableHistoryIsRead(int count, int expected)
    {
        var data = FileData(new Chunk(2, _samplePayload));
        BinaryPrimitives.WriteInt32LittleEndian(data.Bytes.AsSpan(data.Bytes.Length - 8), count);
        using var stream = new MemoryStream(data.Bytes);
        var result = RestartHistory.Read(stream, Token, Clock());
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Samples, Has.Count.EqualTo(expected));
        Assert.That(stream.Position, Is.EqualTo(count <= 0 ? data.HeaderLength : data.Bytes.Length - 12));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase(-1, 1)]
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    public void Read_DeadlineIsExclusiveAndLeavesUnreadFramesInPlace(long ticksBeyondDeadline, int expected)
    {
        var data = FileData(new Chunk(2, _samplePayload));
        var calls = 0;
        var clock = Clock() with { Timestamp = () => ++calls <= 2 ? SavedAt : SavedAt + Frequency * 3 / 2 + ticksBeyondDeadline };
        using var stream = new MemoryStream(data.Bytes);
        Assert.That(RestartHistory.Read(stream, Token, clock)!.Samples, Has.Count.EqualTo(expected));
        Assert.That(stream.Position, Is.EqualTo(expected == 0 ? data.HeaderLength : data.Bytes.Length - 12));
    }

    [Test]
    public void Read_DeadlineBetweenFramesKeepsOnlyTheCompletedNewerChunk()
    {
        var data = FileData(new Chunk(2, _samplePayload), new Chunk(2, _samplePayload));
        var calls = 0;
        var clock = Clock() with { Timestamp = () => ++calls <= 3 ? SavedAt : SavedAt + Frequency * 3 / 2 };
        using var stream = new MemoryStream(data.Bytes);
        Assert.That(RestartHistory.Read(stream, Token, clock)!.Samples, Has.Count.EqualTo(1));
        Assert.That(stream.Position, Is.EqualTo(data.HeaderLength + 9 + _samplePayload.Length));
    }

    [TestCase("crc")]
    [TestCase("unknown-kind")]
    [TestCase("parse")]
    [TestCase("negative-length")]
    [TestCase("excessive-length")]
    [TestCase("short-header")]
    public void Read_BadSampleFramesStopOlderHistoryAndRetainTheValidPrefix(string failure)
    {
        var sample = new Chunk(2, _samplePayload);
        var bad = failure switch
        {
            "crc" => sample with { BadCrc = true },
            "unknown-kind" => sample with { Kind = 99 },
            "negative-length" => sample with { Length = -1 },
            "excessive-length" => sample with { Length = int.MaxValue },
            _ => new Chunk(2, Compressed(writer => writer.Write(-1)))
        };
        var data = FileData(sample, bad, sample);
        if (failure == "short-header")
        {
            var first = FileData(sample);
            data = ([.. first.Bytes[..^12], .. new byte[8], .. first.Bytes[^12..]], first.HeaderLength);
            BinaryPrimitives.WriteInt32LittleEndian(data.Bytes.AsSpan(data.Bytes.Length - 8), 3);
        }
        using var stream = new MemoryStream(data.Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(1));
        Assert.That(result.Samples[0].Resources.CachedBytes, Is.EqualTo(100));
        Assert.That(result.Samples[0].LogicalProcessorPercents, Is.EqualTo(new double?[] { 12.5, null }));
        Assert.That(stream.CanRead, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Read_BadLedgerIsSkippedWithoutLosingTheFollowingSample(bool badCrc)
    {
        var ledger = new Chunk(1, Compressed(writer => writer.Write(true)), badCrc);
        using var stream = new MemoryStream(FileData(ledger, new Chunk(2, _samplePayload)).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Usage, Is.Null);
        Assert.That(result.Samples, Has.Count.EqualTo(1));
    }

    [TestCase("bad-crc-after", true)]
    [TestCase("bad-parse-after", false)]
    [TestCase("valid-after", true)]
    public void Read_LedgerReplacementDistinguishesBadCrcFromBadPayload(string order, bool hasUsage)
    {
        var valid = new Chunk(1, _ledgerPayload);
        var bad = new Chunk(1, Compressed(writer => writer.Write(true)), order == "bad-crc-after");
        Chunk[] chunks = order == "valid-after" ? [bad, valid] : [valid, bad];
        using var stream = new MemoryStream(FileData([.. chunks, new Chunk(2, _samplePayload)]).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(1));
        Assert.That(result.Usage is not null, Is.EqualTo(hasUsage));
        if (hasUsage) Assert.That(result.Usage!.Apps.Select(app => app.Key), Is.EquivalentTo(new[] { @"C:\app.exe", "tool" }));
    }

    [Test]
    public void Read_BadInvestigationDoesNotClearAnEarlierValidLedger()
    {
        using var stream = new MemoryStream(FileData(new Chunk(1, _ledgerPayload),
            new Chunk(3, Investigation("null")), new Chunk(2, _samplePayload)).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(1));
        Assert.That(result.Usage!.Apps.Select(app => app.Key), Is.EquivalentTo(new[] { @"C:\app.exe", "tool" }));
        Assert.That(result.Investigation!.Find(Marker), Is.Null);
    }

    private static InvestigationHistory.Frame Observation(string name) =>
        new(SampleKey.Of(Marker), null, false, null, [new ProcessUsage(9, 90, name, 10)], null, null);

    private static byte[] Investigation(string json, int? declaredLength = null) => Compressed(writer =>
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        writer.Write(declaredLength ?? bytes.Length);
        writer.Write(bytes);
    });

    [TestCase("crc")]
    [TestCase("negative-length")]
    [TestCase("excessive-length")]
    [TestCase("json")]
    [TestCase("null")]
    [TestCase("too-many")]
    [TestCase("invalid-frame")]
    public void Read_BadInvestigationIsSkippedWithoutLosingSamples(string failure)
    {
        var frame = Observation("marker");
        var json = failure switch
        {
            "json" => "{",
            "null" => "null",
            "too-many" => JsonSerializer.Serialize(new InvestigationHistory.Frame?[] { frame }.Concat(Enumerable.Repeat<InvestigationHistory.Frame?>(null, 60)), InvestigationHistory.Json),
            "invalid-frame" => JsonSerializer.Serialize(new[] { frame with { Owners = [null!] } }, InvestigationHistory.Json),
            _ => JsonSerializer.Serialize(new[] { frame }, InvestigationHistory.Json)
        };
        var length = failure switch { "negative-length" => -1, "excessive-length" => 64 * 1024 * 1024 + 1, _ => (int?)null };
        using var stream = new MemoryStream(FileData(new Chunk(3, Investigation(json, length), failure == "crc"), new Chunk(2, _samplePayload)).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(1));
        Assert.That(result.Investigation, Is.Not.Null);
        Assert.That(result.Investigation!.Find(Marker), Is.Null);
    }

    [Test]
    public void Read_InvestigationIgnoresNullEntriesAndKeepsTheFirstDuplicate()
    {
        var json = JsonSerializer.Serialize(new InvestigationHistory.Frame?[] { null, Observation("first"), Observation("second") }, InvestigationHistory.Json);
        using var stream = new MemoryStream(FileData(new Chunk(3, Investigation(json))).Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Is.Empty);
        Assert.That(result.Investigation!.Find(Marker)!.Owners!.Single().Name, Is.EqualTo("first"));
    }

    [Test]
    public void Read_InvestigationAcceptsTheInclusiveSixtyEntryLimit()
    {
        var frames = Enumerable.Repeat<InvestigationHistory.Frame?>(null, 59).Append(Observation("last"));
        var json = JsonSerializer.Serialize(frames, InvestigationHistory.Json);
        using var stream = new MemoryStream(FileData(new Chunk(3, Investigation(json))).Bytes);
        Assert.That(RestartHistory.Read(stream, Token, Clock())!.Investigation!.Find(Marker)!.Owners!.Single().Name, Is.EqualTo("last"));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void Read_InvestigationKeepsTheExistingShortJsonReadBehavior(int extraDeclaredByte)
    {
        var json = JsonSerializer.Serialize(new[] { Observation("short") }, InvestigationHistory.Json);
        var payload = Investigation(json, Encoding.UTF8.GetByteCount(json) + extraDeclaredByte);
        using var stream = new MemoryStream(FileData(new Chunk(3, payload)).Bytes);
        Assert.That(RestartHistory.Read(stream, Token, Clock())!.Investigation!.Find(Marker)!.Owners!.Single().Name, Is.EqualTo("short"));
    }

    [Test]
    public void Read_TruncatedHeaderThrowsWithoutDisposingTheCallerStream()
    {
        using var stream = new MemoryStream([.. "WMRH"u8, 3, 0, 0, 0, 127, .. new byte[7]]);
        Assert.That(() => RestartHistory.Read(stream, Token, Clock()), Throws.TypeOf<EndOfStreamException>());
        Assert.That(stream.CanRead, Is.True);
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
    }

    [TestCase(0)]
    [TestCase(15)]
    [TestCase(16)]
    public void Read_ShortOrBadMagicSourcesReturnNullWithoutClosingTheStream(int length)
    {
        using var stream = new MemoryStream(new byte[length]);
        Assert.That(RestartHistory.Read(stream, Token, Clock()), Is.Null);
        Assert.That(stream.Position, Is.EqualTo(length < 16 ? 0 : 4));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Read_HeaderWithoutTrailerReturnsNullAfterRewindingTheHeader()
    {
        var data = FileData();
        using var stream = new MemoryStream(data.Bytes[..data.HeaderLength]);
        Assert.That(RestartHistory.Read(stream, Token, Clock()), Is.Null);
        Assert.That(stream.Position, Is.EqualTo(data.HeaderLength));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Read_NullStreamKeepsClockFailurePrecedence()
    {
        Assert.That(() => RestartHistory.Read((Stream)null!, Token, Clock()), Throws.TypeOf<NullReferenceException>());
        Assert.That(() => RestartHistory.Read((Stream)null!, Token, Clock() with
            { Timestamp = () => throw new InvalidOperationException("clock") }),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("clock"));
    }

    [Test]
    public void Read_NullClockDoesNotTouchOrCloseTheCallerStream()
    {
        using var stream = new MemoryStream(FileData().Bytes);
        Assert.That(() => RestartHistory.Read(stream, Token, null!), Throws.TypeOf<NullReferenceException>());
        Assert.That(stream.Position, Is.Zero);
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Read_ShortReadsRestoreTheCompleteChunkWithoutTakingOwnership()
    {
        var data = FileData(new Chunk(2, _samplePayload));
        using var stream = new ShortReadStream(data.Bytes);
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(1));
        Assert.That(result.Samples[0].Resources.CachedBytes, Is.EqualTo(100));
        Assert.That(stream.Position, Is.EqualTo(data.Bytes.Length - 12));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void Read_NonseekableStreamDoesNotQueryLengthOrTakeOwnership()
    {
        using var stream = new NonseekableStream();
        var calls = 0;
        Assert.That(RestartHistory.Read(stream, Token, Clock() with { Timestamp = () => { calls++; return SavedAt; } }), Is.Null);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(stream.CanRead, Is.True);
    }

    private sealed class NonseekableStream : MemoryStream
    {
        public override bool CanSeek => false;
        public override long Length => throw new InvalidOperationException("Length must not be queried.");
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(1, buffer.Length)]);
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(1, count));
    }
}
