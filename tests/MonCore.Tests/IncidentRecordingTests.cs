using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class IncidentRecordingTests
{
    private string _directory = "";
    private string FilePath => Path.Combine(_directory, "incident.wmincident");

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("WinMonIncidentTests-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, true);

    private static IncidentRecording Recording()
    {
        var owner = new ProcessUsage(1, 123, "download", 2, 1234, 5678) { ImagePath = @"C:\apps\download.exe" };
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 50, [owner], 0,
            MonotonicTimestamp: 20 * Stopwatch.Frequency) { Resources = new()
            {
                ReceiveBytesPerSecond = 10, PagingReadsPerSecond = 12.5, StandbyBytes = 20,
                ModifiedBytes = 30, FreeBytes = 40, CompressionStoreBytes = 50
            } };
        var frame = new ProcessNetworkFrame(sample.MonotonicTimestamp, Stopwatch.Frequency, ProcessNetworkQuality.Provisional,
            [new(1, 123, 100, 200, 10, 20)]) { Owners = [owner], CurrentLifetimes = new HashSet<ProcessLifetimeIdentity> { owner.Identity } };
        return new(1, Stopwatch.Frequency,
            [new(sample, frame, [owner], null, null),
                new(sample with { Timestamp = sample.Timestamp.AddSeconds(1), MonotonicTimestamp = sample.MonotonicTimestamp + Stopwatch.Frequency,
                    IsGap = true }, null, [], [owner], [])], CaptureMetric.Network, 1);
    }

    [Test]
    public void SaveLoad_PreservesTimelineTrafficLifecycleSelectionAndPaths()
    {
        var source = Recording();
        source.Save(FilePath);
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.Metric, Is.EqualTo(CaptureMetric.Network));
        Assert.That(loaded.SelectedIndex, Is.EqualTo(1));
        Assert.That(loaded.Observations, Has.Length.EqualTo(2));
        var first = loaded.Observations[0];
        Assert.That(first.Sample.Processes[0].ImagePath, Is.EqualTo(source.Observations[0].Sample.Processes[0].ImagePath));
        Assert.That(first.Sample.Resources.ReceiveBytesPerSecond, Is.EqualTo(10));
        Assert.That(first.Sample.Resources.PagingReadsPerSecond, Is.EqualTo(12.5));
        Assert.That(first.Sample.Resources.StandbyBytes, Is.EqualTo(20));
        Assert.That(first.Sample.Resources.ModifiedBytes, Is.EqualTo(30));
        Assert.That(first.Sample.Resources.FreeBytes, Is.EqualTo(40));
        Assert.That(first.Sample.Resources.CompressionStoreBytes, Is.EqualTo(50));
        Assert.That(first.Network!.Processes.Single(), Is.EqualTo(source.Observations[0].Network!.Processes.Single()));
        Assert.That(first.Network.CurrentLifetimes, Does.Contain(first.Sample.Processes[0].Identity));
        Assert.That(first.Started, Is.Null);
        Assert.That(loaded.Observations[1].Sample.IsGap, Is.True);
        Assert.That(loaded.Observations[1].Started!.Single().Identity, Is.EqualTo(first.Sample.Processes[0].Identity));
        Assert.That(loaded.Observations[1].Stopped, Is.Empty);
        Assert.That(loaded.Observations[1].Sample.MonotonicTimestamp - first.Sample.MonotonicTimestamp, Is.EqualTo(Stopwatch.Frequency));
        var capture = new ConnectionCapture(first.Sample, null, first.Owners, first.Network);
        Assert.That(IncidentTimeline.ProcessReadings(first.Sample, CaptureMetric.Network, capture).Single().Value, Is.EqualTo(30));
    }

    [Test]
    public void Load_RebasesForeignClockFrequencyWithoutChangingWallTime()
    {
        var source = Recording();
        source = source with { Frequency = 1000, Observations = source.Observations.Select((frame, index) => frame with
            { Sample = frame.Sample with { MonotonicTimestamp = 100_000 + index * 1000 } }).ToArray() };
        source.Save(FilePath);
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.Frequency, Is.EqualTo(Stopwatch.Frequency));
        Assert.That(loaded.Observations[0].Sample.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch));
        Assert.That(loaded.Observations[0].Sample.MonotonicTimestamp, Is.EqualTo(Stopwatch.Frequency));
        Assert.That(loaded.Observations[1].Sample.MonotonicTimestamp, Is.EqualTo(2 * Stopwatch.Frequency));
    }

    [TestCase("unsupported-version")]
    [TestCase("invalid-frequency")]
    [TestCase("missing-observations")]
    [TestCase("null-frame")]
    [TestCase("missing-processes")]
    [TestCase("unordered")]
    [TestCase("invalid-selection")]
    [TestCase("invalid-metric")]
    [TestCase("missing-resource-name")]
    [TestCase("oversized-process-name")]
    [TestCase("negative-paging-read")]
    public void Load_RejectsInvalidRecording(string kind)
    {
        var source = Recording();
        source = source with { Observations = source.Observations.Select(frame => frame with { Network = null }).ToArray() };
        source = kind switch
        {
            "unsupported-version" => source with { Version = 999 },
            "invalid-frequency" => source with { Frequency = 0 },
            "missing-observations" => source with { Observations = null! },
            "null-frame" => source with { Observations = [null!], SelectedIndex = 0 },
            "missing-processes" => source with { Observations = [source.Observations[0] with
                { Sample = source.Observations[0].Sample with { Processes = null! } }], SelectedIndex = 0 },
            "unordered" => source with { Observations = source.Observations.Reverse().ToArray() },
            "invalid-selection" => source with { SelectedIndex = 2 },
            "missing-resource-name" => source with { SelectedIndex = 0, Observations = [source.Observations[0] with
                { Sample = source.Observations[0].Sample with { Resources = new() { DiskName = null! } } }] },
            "oversized-process-name" => source with { SelectedIndex = 0, Observations = [source.Observations[0] with
                { Sample = source.Observations[0].Sample with { Processes = [new(1, 1, new string('x', 1025), 0)] } }] },
            "negative-paging-read" => source with { SelectedIndex = 0, Observations = [source.Observations[0] with
                { Sample = source.Observations[0].Sample with { Resources = new() { PagingReadsPerSecond = -1 } } }] },
            _ => source with { Metric = CaptureMetric.ApplicationCpu }
        };
        // Keep the malformed fixture independent of the writer's validation.
        WriteJson(JsonSerializer.Serialize(source));
        Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(FilePath));
    }

    [Test]
    public void Load_RejectsOversizedTruncatedAndUnrelatedFiles()
    {
        using (var file = File.Create(FilePath)) file.SetLength(IncidentRecording.MaximumBytes + 1);
        Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(FilePath));
        WriteJson("{\"Version\":1,");
        Assert.Throws<JsonException>(() => IncidentRecording.Load(FilePath));
        WriteJson("{}");
        Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(FilePath));
    }

    [Test]
    public void Save_InvalidRecordingPreservesExistingFileAndLeavesNoTemporary()
    {
        var source = Recording();
        source.Save(FilePath);
        var original = File.ReadAllBytes(FilePath);
        Assert.Throws<InvalidDataException>(() => (source with { Version = 2 }).Save(FilePath));
        Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [Test]
    public void Load_RejectsCompressedExpansionBeyondDecodedLimit()
    {
        using (var file = File.Create(FilePath))
        using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
        {
            var spaces = Enumerable.Repeat((byte)' ', 65536).ToArray();
            for (var written = 0L; written <= IncidentRecording.MaximumDecodedBytes; written += spaces.Length)
                compressed.Write(spaces);
        }
        Assert.That(new FileInfo(FilePath).Length, Is.LessThan(IncidentRecording.MaximumBytes));
        Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(FilePath));
    }

    [Test]
    public void Save_RejectsOversizedOutputWithoutReplacingExistingFile()
    {
        var source = Recording();
        source.Save(FilePath);
        var original = File.ReadAllBytes(FilePath);
        var frame = source.Observations[0];
        var large = frame.Sample.Processes[0] with { ImagePath = new string('x', 65536) };
        var oversized = source with { SelectedIndex = 0, Observations = [frame with
            { Sample = frame.Sample with { Processes = Enumerable.Repeat(large, 4097).ToArray() } }] };
        Assert.Throws<InvalidDataException>(() => oversized.Save(FilePath));
        Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    private void WriteJson(string json)
    {
        using var file = File.Create(FilePath);
        using var compressed = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new StreamWriter(compressed);
        writer.Write(json);
    }
}
