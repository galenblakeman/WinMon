using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class RestartHistorySaveTests
{
    private const string Token = "test";
    private const long Frequency = 10_000_000, SavedAt = 5_000_000_000;
    private static readonly byte[] Existing = [11, 22, 33];
    private string _directory = "", _path = "";

    [SetUp]
    public void CreateOwnedDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.RestartHistorySaveTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "state.bin");
        File.WriteAllBytes(_path, Existing);
    }

    [TearDown]
    public void DeleteOwnedDirectory() => Directory.Delete(_directory, true);

    private static MonitorSample[] Samples(int count = 1) => Enumerable.Range(1, count).Select(i =>
        new MonitorSample(DateTimeOffset.UnixEpoch.AddDays(10).AddSeconds(i), 10,
            [new ProcessUsage(1, 10, "worker", 5) { ImagePath = @"C:\worker.exe" }], 0, false, i * Frequency)
        { IntervalSeconds = 1, IntervalCpuTicks = 1000 }).ToArray();

    private static RestartClock Clock() => new(() => SavedAt, () => 500_000, Frequency);
    private static UsageLedgerState EmptyUsage() => new(DateTimeOffset.UnixEpoch.AddDays(10), 0, 0, [], []);
    private static InvestigationHistory Investigation(IReadOnlyList<MonitorSample> samples) =>
        new(samples.Select(sample => new InvestigationHistory.Frame(SampleKey.Of(sample), null, false, null, sample.Processes, null, null)));

    private void AssertPreserved()
    {
        Assert.That(File.ReadAllBytes(_path), Is.EqualTo(Existing));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [TestCase("independent")]
    [TestCase("domain")]
    [TestCase("cpu")]
    [TestCase("started")]
    [TestCase("policy")]
    [TestCase("lifetime")]
    public void Save_UnsupportedMetadataShortCircuitsBeforeClockOrTemporaryFile(string kind)
    {
        var samples = Samples();
        var process = samples[0].Processes[0];
        var usage = EmptyUsage();
        switch (kind)
        {
            case "independent": samples[0] = samples[0] with { IndependentProcessReadings = true, Processes = null! }; break;
            case "domain": samples[0] = samples[0] with { Processes = [process with { LifetimeDomain = ProcessLifetimeIdentity.UnixMicroseconds }] }; break;
            case "cpu": samples[0] = samples[0] with { Processes = [process with { CpuAvailable = false }] }; break;
            case "started": samples[0] = samples[0] with { Processes = [process with { StartedAtUtc = DateTimeOffset.UnixEpoch }] }; break;
            case "policy": usage = usage with { IdentityPolicyId = ProcessIdentityPolicy.ExactPaths.Id, Lifetimes = null! }; break;
            case "lifetime": usage = usage with { Lifetimes = [new UsageLifetimeState(1, 10, 0, 0) { LifetimeDomain = ProcessLifetimeIdentity.UnixMicroseconds }] }; break;
        }
        if (kind is not ("policy" or "lifetime"))
        {
            samples = [samples[0], null!];
            usage = usage with { Lifetimes = null! };
        }
        var opened = false;
        var clock = Clock() with { Timestamp = () => throw new InvalidOperationException("Clock must not run.") };
        Assert.That(RestartHistory.Save(_path, Token, "test", samples, usage, clock,
            _ => { opened = true; throw new InvalidOperationException("Temporary file must not open."); }), Is.False);
        Assert.That(opened, Is.False);
        AssertPreserved();
    }

    [TestCase("samples", typeof(ArgumentNullException))]
    [TestCase("sample", typeof(NullReferenceException))]
    [TestCase("processes", typeof(ArgumentNullException))]
    [TestCase("process", typeof(NullReferenceException))]
    [TestCase("lifetimes", typeof(ArgumentNullException))]
    [TestCase("clock", typeof(InvalidOperationException))]
    [TestCase("null-clock", typeof(NullReferenceException))]
    public void Save_PreflightFailuresKeepTheirExistingExceptionAndDoNotOpenTemporaryFiles(string kind, Type expected)
    {
        IReadOnlyList<MonitorSample> samples = Samples();
        var usage = EmptyUsage();
        var clock = Clock();
        switch (kind)
        {
            case "samples": samples = null!; break;
            case "sample": samples = [null!]; break;
            case "processes": samples = [samples[0] with { Processes = null! }]; break;
            case "process": samples = [samples[0] with { Processes = [null!] }]; break;
            case "lifetimes": usage = usage with { Lifetimes = null! }; break;
            case "clock": clock = clock with { Timestamp = () => throw new InvalidOperationException("clock") }; break;
            case "null-clock": clock = null!; break;
        }
        var opened = false;
        Assert.That(() => RestartHistory.Save(_path, Token, "test", samples, usage, clock,
            _ => { opened = true; throw new IOException("Must not open."); }), Throws.TypeOf(expected));
        Assert.That(opened, Is.False);
        AssertPreserved();
    }

    [Test]
    public void Save_InvalidPathCanReachTheFactoryAndCleanupStillThrows()
    {
        var opened = false;
        Assert.That(() => RestartHistory.Save("invalid\0path", Token, "test", Samples(), null, Clock(),
            _ => { opened = true; throw new IOException("factory"); }), Throws.TypeOf<ArgumentException>());
        Assert.That(opened, Is.True);
        AssertPreserved();
    }

    [TestCase("token")]
    [TestCase("version")]
    [TestCase("header-clock")]
    public void Save_HeaderFailuresDisposeTheStreamAndPreserveTheExistingFile(string kind)
    {
        var calls = 0;
        var clock = Clock();
        if (kind == "header-clock") clock = clock with { Timestamp = () => ++calls == 1 ? SavedAt : throw new IOException("header clock") };
        TrackingStream? tracked = null;
        Assert.That(RestartHistory.Save(_path, kind == "token" ? null! : Token, kind == "version" ? null! : "test",
            Samples(), null, clock, temporary => tracked = new TrackingStream(File.Create(temporary))), Is.False);
        Assert.That(tracked!.Disposed, Is.True);
        AssertPreserved();
    }

    [TestCase(-1, 61)]
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    public void Save_ChunkCutoffIsExclusiveAndStillProducesAReadableFile(long ticksBeyond, int expected)
    {
        var calls = 0;
        var clock = Clock() with { Timestamp = () => ++calls <= 2 ? SavedAt : SavedAt + Frequency * 6 / 5 + ticksBeyond };
        Assert.That(RestartHistory.Save(_path, Token, "test", Samples(61), null, clock), Is.True);
        using var stream = new MemoryStream(File.ReadAllBytes(_path));
        Assert.That(RestartHistory.Read(stream, Token, Clock())!.Samples, Has.Count.EqualTo(expected));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [TestCase(-1, 65, 65)]
    [TestCase(0, 60, 0)]
    [TestCase(1, 60, 0)]
    public void Save_InvestigationHasItsOwnExclusiveChunkCutoff(long ticksBeyond, int expectedSamples, int expectedMetadata)
    {
        var samples = Samples(65);
        var calls = 0;
        var clock = Clock() with { Timestamp = () => ++calls <= 3 ? SavedAt : SavedAt + Frequency * 6 / 5 + ticksBeyond };
        Assert.That(RestartHistory.Save(_path, Token, "test", samples, null, clock, investigation: Investigation(samples)), Is.True);
        using var stream = new MemoryStream(File.ReadAllBytes(_path));
        var result = RestartHistory.Read(stream, Token, Clock())!;
        Assert.That(result.Samples, Has.Count.EqualTo(expectedSamples));
        Assert.That(result.Samples.Count(sample => result.Investigation!.Find(sample) is not null), Is.EqualTo(expectedMetadata));
    }

    [TestCase(-1, true)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void Save_FinalDeadlineIsInclusiveAndLateSavesPreserveTheOriginal(long ticksBeyond, bool expected)
    {
        var calls = 0;
        var clock = Clock() with { Timestamp = () => ++calls <= 2 ? SavedAt : SavedAt + Frequency * 3 / 2 + ticksBeyond };
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            Assert.That(RestartHistory.Save(_path, Token, "test", [], null, clock), Is.EqualTo(expected));
            Assert.That(output.ToString().Contains("WinMon restart history missed its deadline and was discarded.", StringComparison.Ordinal), Is.EqualTo(!expected));
        }
        finally { Trace.Listeners.Remove(listener); }
        if (!expected) AssertPreserved();
        else
        {
            using var stream = new MemoryStream(File.ReadAllBytes(_path));
            Assert.That(RestartHistory.Read(stream, Token, Clock())!.Samples, Is.Empty);
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        }
    }

    [TestCase(false, -1)]
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(true, -1)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    public void Save_EncodedSizeLimitIsInclusiveAndSuccessfulBytesStayIdentical(bool metadata, int extraByte)
    {
        var samples = Samples();
        var investigation = metadata ? Investigation(samples) : null;
        var probe = Path.Combine(_directory, "probe.bin");
        Assert.That(RestartHistory.Save(probe, Token, "test", samples, null, Clock(), investigation: investigation), Is.True);
        var bytes = File.ReadAllBytes(probe);
        var saved = RestartHistory.Save(_path, Token, "test", samples, null, Clock(), maxBytes: bytes.Length + extraByte, investigation: investigation);
        Assert.That(saved, Is.EqualTo(extraByte >= 0));
        if (saved) Assert.That(File.ReadAllBytes(_path), Is.EqualTo(bytes));
        else AssertPreserved();
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [TestCase("sample")]
    [TestCase("investigation")]
    [TestCase("trailer")]
    public void Save_EachSizeCheckpointPreservesTheOriginalAndRemovesTemporaryFiles(string checkpoint)
    {
        var samples = Samples();
        var investigation = Investigation(samples);
        var probe = Path.Combine(_directory, "probe.bin");
        Assert.That(RestartHistory.Save(probe, Token, "test", samples, null, Clock(), investigation: investigation), Is.True);
        var bytes = File.ReadAllBytes(probe);
        long limit = bytes.Length - 1;
        using (var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
        {
            reader.BaseStream.Position = 8; _ = reader.ReadString(); _ = reader.ReadString(); reader.BaseStream.Position += 24;
            while (reader.BaseStream.Position < bytes.Length - 12)
            {
                var kind = reader.ReadByte(); var length = reader.ReadInt32(); _ = reader.ReadUInt32();
                reader.BaseStream.Position += length;
                if (checkpoint == "sample" && kind == 2 || checkpoint == "investigation" && kind == 3)
                { limit = reader.BaseStream.Position - 1; break; }
            }
        }
        Assert.That(RestartHistory.Save(_path, Token, "test", samples, null, Clock(), maxBytes: limit, investigation: investigation), Is.False);
        AssertPreserved();
    }

    [TestCase("write")]
    [TestCase("position")]
    [TestCase("flush")]
    [TestCase("dispose")]
    public void Save_StreamFailuresAreCaughtDisposedAndCannotReplaceTheOriginal(string fault)
    {
        TrackingStream? tracked = null;
        Assert.That(RestartHistory.Save(_path, Token, "test", Samples(), null, Clock(), temporary =>
        {
            Assert.That(Path.GetDirectoryName(temporary), Is.EqualTo(_directory));
            Assert.That(Path.GetFileName(temporary), Does.StartWith(".state.bin.").And.EndWith(".tmp"));
            return tracked = new TrackingStream(File.Create(temporary), fault);
        }), Is.False);
        Assert.That(tracked!.Disposed, Is.True);
        AssertPreserved();
    }

    [Test]
    public void Save_FactoryAndMoveFailuresPreserveExistingDestinations()
    {
        Assert.That(RestartHistory.Save(_path, Token, "test", Samples(), null, Clock(), _ => throw new IOException("factory")), Is.False);
        AssertPreserved();
        var destination = Path.Combine(_directory, "destination");
        Directory.CreateDirectory(destination);
        var child = Path.Combine(destination, "existing.bin");
        File.WriteAllBytes(child, Existing);
        Assert.That(RestartHistory.Save(destination, Token, "test", Samples(), null, Clock()), Is.False);
        Assert.That(File.ReadAllBytes(child), Is.EqualTo(Existing));
        AssertPreserved();
    }

    [Test]
    public void Save_NonFileStreamFlushesAndDisposesBeforeTheAtomicMove()
    {
        TrackingStream? tracked = null;
        Assert.That(RestartHistory.Save(_path, Token, "test", Samples(), null, Clock(), temporary =>
            tracked = new TrackingStream(new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))), Is.True);
        Assert.That(tracked!.Flushed, Is.True);
        Assert.That(tracked.Disposed, Is.True);
        using var stream = new MemoryStream(File.ReadAllBytes(_path));
        Assert.That(RestartHistory.Read(stream, Token, Clock())!.Samples.Single().Processes.Single().Name, Is.EqualTo("worker"));
    }

    [Test]
    public void Save_FileStreamRequestsDurableFlushBeforeDisposalAndReplacement()
    {
        TrackingFileStream? tracked = null;
        Assert.That(RestartHistory.Save(_path, Token, "test", Samples(), null, Clock(), temporary =>
            tracked = new TrackingFileStream(temporary)), Is.True);
        Assert.That(tracked!.DurableFlushesBeforeDisposal, Is.EqualTo(1));
        Assert.That(tracked.Disposed, Is.True);
        using var stream = new MemoryStream(File.ReadAllBytes(_path));
        Assert.That(RestartHistory.Read(stream, Token, Clock())!.Samples, Has.Count.EqualTo(1));
    }

    private sealed class TrackingFileStream(string path) : FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
    {
        public int DurableFlushesBeforeDisposal { get; private set; }
        public bool Disposed { get; private set; }
        public override void Flush(bool flushToDisk)
        {
            if (flushToDisk && !Disposed) DurableFlushesBeforeDisposal++;
            base.Flush(flushToDisk);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingStream(Stream inner, string fault = "") : Stream
    {
        public bool Disposed { get; private set; }
        public bool Flushed { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => fault == "position" ? throw new IOException("position") : inner.Position; set => inner.Position = value; }
        public override void Flush() { Flushed = true; if (fault == "flush") throw new IOException("flush"); inner.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { if (fault == "write") throw new IOException("write"); inner.Write(buffer); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                inner.Dispose();
                if (fault == "dispose") throw new IOException("dispose");
            }
            base.Dispose(disposing);
        }
    }
}
