using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class RestartHistoryTests
{
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const long Frequency = 10_000_000;
    private const long SavedAt = 5_000_000_000;
    private const long SavedTickCount = 500_000;
    private static readonly DateTimeOffset Anchor = DateTimeOffset.Parse("2026-10-24T12:00:00+01:00");
    private string _directory = "";

    [SetUp]
    public void CreateDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.RestartHistoryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void DeleteDirectory()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void RoundTrip_KeepsEveryFieldOfEverySample()
    {
        var samples = Samples(130);
        var path = SaveWith(samples);

        var restored = RestartHistory.Read(path, Token, Later(10));

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(Json(restored!.Samples), Is.EqualTo(Json(samples)));
            Assert.That(restored.Samples.SelectMany(s => s.Processes).Select(p => p.HasHadAppWindow),
                Is.EqualTo(samples.SelectMany(s => s.Processes).Select(p => p.HasHadAppWindow)));
            Assert.That(restored.Samples.Select(s => s.Timestamp.Offset),
                Is.EqualTo(samples.Select(s => s.Timestamp.Offset)), "Displayed times keep their offset.");
            Assert.That(restored.Usage, Is.Null);
            Assert.That(File.Exists(path), Is.False, "The file is deleted once read.");
        });
    }

    [Test]
    public void NormalRestart_RestoresRecentSamplesAfterClockReset()
    {
        var path = Path.Combine(_directory, RestartHistory.NormalFileName);
        var samples = Samples(5);
        Assert.That(RestartHistory.SaveNormal(path, "3.3.0", samples, Later(0)), Is.True);
        var rebooted = new RestartClock(() => Frequency * 10, () => 10_000, Frequency);

        var restored = RestartHistory.ReadNormal(path, rebooted, Anchor.AddSeconds(9), 600);

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Value.Samples.Select(s => s.Timestamp),
                Is.EqualTo(samples.Select(s => s.Timestamp)));
            Assert.That(restored.Value.Samples.Select(s => s.MonotonicTimestamp), Is.Ordered.Ascending);
            Assert.That(restored.Value.Samples[0].MonotonicTimestamp, Is.GreaterThan(0));
            Assert.That(restored.Value.Samples[^1].MonotonicTimestamp,
                Is.EqualTo(rebooted.Timestamp() + restored.Value.LiveOffset - Frequency * 5));
            Assert.That(File.Exists(path), Is.False);
        });
    }

    [Test]
    public void NormalRestart_TrimsExpiredSamplesAndRejectsClockErrors()
    {
        var path = Path.Combine(_directory, RestartHistory.NormalFileName);
        var clock = Later(0);
        Assert.That(RestartHistory.SaveNormal(path, "3.3.0", Samples(5), clock), Is.True);
        var retained = RestartHistory.ReadNormal(path, clock, Anchor.AddSeconds(7), 5);
        Assert.That(retained!.Value.Samples.Select(s => s.Timestamp),
            Is.EqualTo(new[] { Anchor.AddSeconds(2), Anchor.AddSeconds(3), Anchor.AddSeconds(4) }));

        Assert.That(RestartHistory.SaveNormal(path, "3.3.0", Samples(5), clock), Is.True);
        Assert.That(RestartHistory.ReadNormal(path, clock, Anchor.AddHours(1), 600), Is.Null);
        Assert.That(RestartHistory.SaveNormal(path, "3.3.0", Samples(5), clock), Is.True);
        Assert.That(RestartHistory.ReadNormal(path, clock, Anchor.AddSeconds(-10), 600), Is.Null);
        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public void RoundTrip_GuardsEverySettableProperty()
    {
        // Rich() sets every property by reflection; a property the format forgets fails the round trip above.
        var sample = Samples(1)[0];
        Assert.Multiple(() =>
        {
            foreach (var property in Settable(typeof(ResourceSample)))
                Assert.That(property.GetValue(sample.Resources), Is.Not.EqualTo(Default(property.PropertyType)),
                    property.Name);
            foreach (var property in Settable(typeof(ProcessUsage)))
                Assert.That(property.GetValue(sample.Processes[0]), Is.Not.EqualTo(Default(property.PropertyType)),
                    property.Name);
        });
    }

    [Test]
    public void RoundTrip_CarriesTheUsageLedger()
    {
        var ledger = new UsageLedger(Anchor);
        var running = Valid(1, [Process(7, 70, "app", 20, @"C:\app.exe"), Process(8, 80, "tool", 10)]);
        ledger.Add(running);
        ledger.Add(Valid(2, [Process(7, 70, "app", 20, @"C:\app.exe")]));
        var path = SaveWith(Samples(3), ledger.Export());

        var restored = RestartHistory.Read(path, Token, Later(10))!;
        var copy = new UsageLedger(DateTimeOffset.MinValue);
        copy.Restore(restored.Usage!);
        var restoredApps = copy.Apps;
        var savedApps = ledger.Apps;
        copy.Add(Valid(3, [Process(7, 70, "app", 20, @"C:\app.exe")]));
        ledger.Add(Valid(3, [Process(7, 70, "app", 20, @"C:\app.exe")]));

        Assert.Multiple(() =>
        {
            Assert.That(copy.Since, Is.EqualTo(Anchor));
            Assert.That(restoredApps, Is.EquivalentTo(savedApps.Select(a => a with { Running = 0 })),
                "Running waits for the first sample after the restart.");
            Assert.That(copy.Apps, Is.EquivalentTo(ledger.Apps), "A process running through the restart is not counted again.");
            Assert.That(copy.MachineBusyTicks, Is.EqualTo(ledger.MachineBusyTicks));
            Assert.That(copy.RememberedLifetimes, Is.EqualTo(ledger.RememberedLifetimes));
        });
    }

    [TestCase("ffffffffffffffffffffffffffffffff", 0, 0, TestName = "Read_RejectsAnotherUpdatesToken")]
    [TestCase(Token, 301, 301, TestName = "Read_RejectsAnOldFile")]
    [TestCase(Token, -1, -1, TestName = "Read_RejectsAFileFromTheFuture")]
    [TestCase(Token, 10, 13, TestName = "Read_RejectsClocksThatDisagree")]
    public void Read_RejectsTheWholeFile(string token, double timerSeconds, double tickSeconds)
    {
        var path = SaveWith(Samples(5));

        var clock = new RestartClock(() => SavedAt + (long)(timerSeconds * Frequency),
            () => SavedTickCount + (long)(tickSeconds * 1000), Frequency);

        Assert.Multiple(() =>
        {
            Assert.That(RestartHistory.Read(path, token, clock), Is.Null);
            Assert.That(File.Exists(path), Is.False, "A rejected file is deleted too.");
        });
    }

    [Test]
    public void Read_RejectsADifferentTimerFrequency()
    {
        var path = SaveWith(Samples(5));

        Assert.That(RestartHistory.Read(path, Token, Later(10) with { Frequency = Frequency * 2 }), Is.Null);
    }

    [Test]
    public void Read_RejectsAnUnknownFormatVersion()
    {
        var path = SaveWith(Samples(5));
        var bytes = File.ReadAllBytes(path);
        bytes[4] = RestartHistory.FormatVersion + 1;
        File.WriteAllBytes(path, bytes);

        Assert.That(RestartHistory.Read(path, Token, Later(10)), Is.Null);
    }

    [Test]
    public void Read_RejectsAFileWithoutItsTrailer()
    {
        var path = SaveWith(Samples(5));
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..^12]);

        Assert.That(RestartHistory.Read(path, Token, Later(10)), Is.Null);
    }

    [Test]
    public void Read_RejectsAChangedHeader()
    {
        var path = SaveWith(Samples(5));
        var bytes = File.ReadAllBytes(path);
        bytes[9] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        Assert.That(RestartHistory.Read(path, Token, Later(10)), Is.Null);
    }

    [Test]
    public void Read_ADamagedChunkKeepsOnlyTheNewerHistory()
    {
        var samples = Samples(RestartHistory.ChunkSamples * 5);
        var path = SaveWith(samples);
        var bytes = File.ReadAllBytes(path);
        var chunks = Chunks(bytes);
        // Chunk 0 is the ledger; chunks 1 and 2 are the newest samples, and chunk 3 is damaged.
        bytes[chunks[3].Payload + 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var restored = RestartHistory.Read(path, Token, Later(10))!;

        Assert.That(Json(restored.Samples), Is.EqualTo(Json(samples.TakeLast(RestartHistory.ChunkSamples * 2).ToArray())));
    }

    [Test]
    public void Read_ADamagedLedgerStillRestoresTheSamples()
    {
        var ledger = new UsageLedger(Anchor);
        ledger.Add(Valid(1, [Process(7, 70, "app", 20)]));
        var samples = Samples(70);
        var path = SaveWith(samples, ledger.Export());
        var bytes = File.ReadAllBytes(path);
        bytes[Chunks(bytes)[0].Payload + 1] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var restored = RestartHistory.Read(path, Token, Later(10))!;

        Assert.Multiple(() =>
        {
            Assert.That(restored.Usage, Is.Null);
            Assert.That(Json(restored.Samples), Is.EqualTo(Json(samples)));
        });
    }

    [Test]
    public void Read_IgnoresWallClockChangesBetweenSaveAndRestore()
    {
        // Sample times an hour ahead of anything else: only the monotonic clocks are checked.
        var samples = Samples(3).Select(s => s with { Timestamp = s.Timestamp.AddHours(1) }).ToArray();
        var path = SaveWith(samples);

        Assert.That(RestartHistory.Read(path, Token, Later(10))!.Samples, Has.Count.EqualTo(3));
    }

    [Test]
    public void Save_StopsStartingChunksAtItsCutOffAndKeepsTheNewest()
    {
        var samples = Samples(RestartHistory.ChunkSamples * 10);
        var path = Path.Combine(_directory, RestartHistory.FileName);

        // Each clock read advances 0.2 s: the start, the header, then one read before each chunk.
        var saved = RestartHistory.Save(path, Token, "test", samples, null, Stepping(0.2));
        var restored = RestartHistory.Read(path, Token, Later(10, SavedAt + Frequency / 5))!;

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.True);
            Assert.That(Json(restored.Samples),
                Is.EqualTo(Json(samples.TakeLast(RestartHistory.ChunkSamples * 4).ToArray())));
        });
    }

    [Test]
    public void Save_PastTheDeadlineLeavesNoFile()
    {
        var path = Path.Combine(_directory, RestartHistory.FileName);

        var saved = RestartHistory.Save(path, Token, "test", Samples(600), null, Stepping(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.False);
            Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Empty, "The temporary file is deleted.");
        });
    }

    [Test]
    public void Save_AFailedWriteLeavesNoFileAndDoesNotThrow()
    {
        var path = Path.Combine(_directory, RestartHistory.FileName);

        var saved = RestartHistory.Save(path, Token, "test", Samples(10), null, Fixed(),
            temporary => new FailingStream(File.Create(temporary)));

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.False);
            Assert.That(Directory.EnumerateFileSystemEntries(_directory), Is.Empty);
        });
    }

    [Test]
    public void Read_AMissingFileIsNotAnError()
    {
        Assert.That(RestartHistory.Read(Path.Combine(_directory, "missing.bin"), Token, Later(10)), Is.Null);
    }

    [Test]
    public void Restore_TrimsToTheNewVersionsRetention()
    {
        var path = SaveWith(Samples(90));
        var history = new MonitorHistory(30);

        foreach (var sample in RestartHistory.Read(path, Token, Later(10))!.Samples) history.Add(sample);

        Assert.That(history.Samples.Select(s => s.MonotonicTimestamp),
            Is.EqualTo(Samples(90).TakeLast(30).Select(s => s.MonotonicTimestamp)));
    }

    [Test]
    public void Fixture_FormatVersion1StillReads()
    {
        var path = Path.Combine(_directory, RestartHistory.FileName);
        File.Copy(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", FixtureName), path);

        var restored = RestartHistory.Read(path, Token, Later(10));

        Assert.Multiple(() =>
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(Json(restored!.Samples), Is.EqualTo(Json(Samples(75))));
            Assert.That(restored.Usage!.Apps.Select(a => a.Key), Is.EquivalentTo(new[] { @"C:\app.exe", "tool" }));
        });
    }

    // Regenerates the checked-in fixture. Run it only when deliberately introducing a new format version.
    [Test, Explicit("Writes the format fixture into the source tree.")]
    public void Fixture_Write()
    {
        var ledger = new UsageLedger(Anchor);
        ledger.Add(Valid(1, [Process(7, 70, "app", 20, @"C:\app.exe"), Process(8, 80, "tool", 10)]));
        var path = Path.Combine(SourceDirectory(), "Fixtures", FixtureName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.That(RestartHistory.Save(path, Token, "fixture", Samples(75), ledger.Export(), Fixed()), Is.True);
    }

    private const string FixtureName = "restart-history-v1.bin";

    private string SaveWith(IReadOnlyList<MonitorSample> samples, UsageLedgerState? usage = null)
    {
        var path = Path.Combine(_directory, RestartHistory.FileName);
        Assert.That(RestartHistory.Save(path, Token, "test", samples, usage, Fixed()), Is.True);
        return path;
    }

    private static RestartClock Fixed()
    {
        return new RestartClock(() => SavedAt, () => SavedTickCount, Frequency);
    }

    private static RestartClock Later(double seconds, long savedAt = SavedAt)
    {
        return new RestartClock(() => savedAt + (long)(seconds * Frequency),
            () => SavedTickCount + (long)(seconds * 1000), Frequency);
    }

    private static RestartClock Stepping(double seconds)
    {
        var now = SavedAt;
        return new RestartClock(() =>
        {
            var current = now;
            now += (long)(seconds * Frequency);
            return current;
        }, () => SavedTickCount, Frequency);
    }

    private static List<(int Kind, int Payload)> Chunks(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        reader.ReadBytes(8);
        reader.ReadString();
        reader.ReadString();
        reader.ReadBytes(24);
        var chunks = new List<(int, int)>();
        while (reader.BaseStream.Position < bytes.Length - 12)
        {
            var kind = reader.ReadByte();
            var length = reader.ReadInt32();
            reader.ReadUInt32();
            chunks.Add((kind, (int)reader.BaseStream.Position));
            reader.BaseStream.Position += length;
        }

        return chunks;
    }

    private static string Json(IEnumerable<MonitorSample> samples)
    {
        return JsonSerializer.Serialize(samples.ToArray());
    }

    // Every sample sets every property to a value that differs between samples; every 7th is a gap with nulls.
    private static MonitorSample[] Samples(int count)
    {
        return [.. Enumerable.Range(0, count).Select(i => i % 7 == 3 ? Gap(i) : Rich(i))];
    }

    private static MonitorSample Gap(int i)
    {
        return new MonitorSample(Anchor.AddSeconds(i), null, [], i, true, Frequency * (100 + i));
    }

    private static MonitorSample Rich(int i)
    {
        var resources = new ResourceSample();
        var n = 0;
        foreach (var property in Settable(typeof(ResourceSample))) property.SetValue(resources, Value(property, i, ++n));
        ProcessUsage[] processes =
        [
            .. Enumerable.Range(0, 3).Select(p =>
            {
                var process = new ProcessUsage(p + 1, p + 10, "", 0);
                var m = 0;
                foreach (var property in Settable(typeof(ProcessUsage)))
                    property.SetValue(process, Value(property, i * 10 + p, ++m));
                return process;
            })
        ];
        return new MonitorSample(Anchor.AddSeconds(i), 12.5 + i, processes, i, false, Frequency * (100 + i))
        {
            Resources = resources,
            CompactedProcesses = 40 + i,
            IntervalSeconds = 1 + i / 1000d,
            IntervalCpuTicks = 120_000_000 + i
        };
    }

    private static object Value(PropertyInfo property, int i, int n)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type == typeof(string) ? $"{property.Name}-{i % 4}"
            : type == typeof(double) ? i + n / 8d
            : type == typeof(ulong) ? (ulong)(i * 1000 + n)
            : type == typeof(long) ? (long)(i * 1000 + n)
            : type == typeof(uint) ? (uint)(i * 1000 + n)
            : type == typeof(int) ? i * 1000 + n
            : type == typeof(bool) ? property.PropertyType == typeof(bool) || (i + n) % 2 == 0
            : type == typeof(IReadOnlyList<int>) ? new[] { i, n, i + n }
            : throw new NotSupportedException(property.Name);
    }

    private static IEnumerable<PropertyInfo> Settable(Type type)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(property => property.SetMethod is not null);
    }

    private static object? Default(Type type)
    {
        return type == typeof(string) ? "" : type.IsValueType && Nullable.GetUnderlyingType(type) is null
            ? Activator.CreateInstance(type)
            : null;
    }

    private static ProcessUsage Process(int id, long creation, string name, double cpu, string path = "")
    {
        return new ProcessUsage(id, creation, name, cpu) { ImagePath = path };
    }

    private static MonitorSample Valid(int second, IReadOnlyList<ProcessUsage> processes)
    {
        return new MonitorSample(Anchor.AddSeconds(second), 50, processes, 0, false, Stopwatch.Frequency * second)
        {
            IntervalSeconds = 1,
            IntervalCpuTicks = 1_000
        };
    }

    private static string SourceDirectory([CallerFilePath] string file = "")
    {
        return Path.GetDirectoryName(file)!;
    }

    private sealed class FailingStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Disk failed.");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Disk failed.");

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
