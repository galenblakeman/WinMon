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
        return new(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
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
        Assert.Throws<InvalidDataException>(() => (source with { Version = 999 }).Save(FilePath));
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

    [Test]
    public void SaveLoad_PreservesEventsAndCoverageAndUpgradesOldRecordings()
    {
        var source = Recording() with { Version = 1, WindowsEvents = Events() };
        source.Save(FilePath);
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.Version, Is.EqualTo(2));
        Assert.That(loaded.WindowsEvents.Events, Is.EqualTo(source.WindowsEvents.Events));
        Assert.That(loaded.WindowsEvents.Coverage, Is.EqualTo(source.WindowsEvents.Coverage));
        Assert.That(loaded.WindowsEvents.LimitReached, Is.True);
    }

    [Test]
    public void Load_VersionOneWithoutEventsReportsUnknownCoverage()
    {
        var json = JsonSerializer.SerializeToNode(WithoutTraffic() with { Version = 1 })!;
        json.AsObject().Remove("WindowsEvents");
        WriteJson(json.ToJsonString());
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.WindowsEvents.Events, Is.Empty);
        Assert.That(loaded.WindowsEvents.Coverage.Select(item => item.Status),
            Is.All.EqualTo(IncidentEventCoverageStatus.Unknown));
        loaded.Save(FilePath);
        Assert.That(IncidentRecording.Load(FilePath).Version, Is.EqualTo(2));
    }

    [TestCase("too-many")]
    [TestCase("null-event")]
    [TestCase("provider")]
    [TestCase("message-length")]
    [TestCase("message-lines")]
    [TestCase("level")]
    [TestCase("partial-identity")]
    [TestCase("invalid-identity")]
    [TestCase("coverage")]
    [TestCase("coverage-status")]
    [TestCase("missing-coverage")]
    [TestCase("duplicate-log")]
    [TestCase("null-result")]
    public void Load_RejectsMalformedEvents(string kind)
    {
        var events = Events();
        var item = events.Events[0];
        events = kind switch
        {
            "too-many" => events with { Events = Enumerable.Repeat(item, 501).ToArray() },
            "null-event" => events with { Events = [null!] },
            "provider" => events with { Events = [item with { Provider = new string('x', 257) }] },
            "message-length" => events with { Events = [item with { Message = new string('x', 301) }] },
            "message-lines" => events with { Events = [item with { Message = "first\nsecond" }] },
            "level" => events with { Events = [item with { Level = (IncidentEventLevel)999 }] },
            "partial-identity" => events with { Events = [item with { CreationTicks = null }] },
            "invalid-identity" => events with { Events = [item with { ProcessId = -1 }] },
            "coverage" => events with { Coverage = [new("Security", IncidentEventCoverageStatus.Complete), events.Coverage[1]] },
            "coverage-status" => events with { Coverage = [new("Application", (IncidentEventCoverageStatus)999), events.Coverage[1]] },
            "missing-coverage" => events with { Coverage = null! },
            "duplicate-log" => events with { Coverage = [events.Coverage[0], events.Coverage[0]] },
            _ => null!
        };
        WriteJson(JsonSerializer.Serialize(WithoutTraffic() with { WindowsEvents = events }));
        Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(FilePath));
    }

    [Test]
    public void SaveLoad_AcceptsTheFullEventBudget()
    {
        var events = Events();
        (Recording() with { WindowsEvents = events with
            { Events = Enumerable.Repeat(events.Events[0], 500).ToArray() } }).Save(FilePath);
        Assert.That(IncidentRecording.Load(FilePath).WindowsEvents.Events, Has.Count.EqualTo(500));
    }

    [TestCase("negative-clock")]
    [TestCase("mixed-clocks")]
    [TestCase("negative-unavailable")]
    [TestCase("negative-compacted")]
    [TestCase("negative-interval")]
    [TestCase("nan-interval")]
    [TestCase("nan-paging")]
    [TestCase("missing-resources")]
    [TestCase("missing-owners")]
    [TestCase("partial-lifecycle")]
    [TestCase("processor-limit")]
    [TestCase("duplicate-traffic")]
    [TestCase("span-limit")]
    [TestCase("invalid-process")]
    [TestCase("invalid-owners")]
    [TestCase("invalid-started")]
    [TestCase("invalid-stopped")]
    public void Save_InvalidObservationPreservesFileAndValidationError(string kind)
    {
        var source = Recording();
        source.Save(FilePath);
        var original = File.ReadAllBytes(FilePath);
        var first = source.Observations[0];
        var sample = first.Sample;
        var changed = kind switch
        {
            "negative-clock" => first with { Sample = sample with { MonotonicTimestamp = -1 } },
            "mixed-clocks" => first with { Sample = sample with { MonotonicTimestamp = 0 } },
            "negative-unavailable" => first with { Sample = sample with { UnavailableProcesses = -1 } },
            "negative-compacted" => first with { Sample = sample with { CompactedProcesses = -1 } },
            "negative-interval" => first with { Sample = sample with { IntervalSeconds = -1 } },
            "nan-interval" => first with { Sample = sample with { IntervalSeconds = double.NaN } },
            "nan-paging" => first with { Sample = sample with { Resources = sample.Resources with { PagingReadsPerSecond = double.NaN } } },
            "missing-resources" => first with { Sample = sample with { Resources = null! } },
            "missing-owners" => first with { Owners = null! },
            "partial-lifecycle" => first with { Started = [], Stopped = null },
            "processor-limit" => first with { Sample = sample with { LogicalProcessorPercents = new double?[4097] } },
            "duplicate-traffic" => first with { Network = first.Network! with
                { Processes = [first.Network!.Processes[0], first.Network.Processes[0]] } },
            "span-limit" => first with { Sample = sample with
                { MonotonicTimestamp = sample.MonotonicTimestamp - Stopwatch.Frequency } },
            "invalid-process" => first with { Sample = sample with { Processes = [sample.Processes[0] with { Name = null! }] } },
            "invalid-owners" => first with { Owners = [sample.Processes[0] with { Name = null! }] },
            "invalid-started" => first with { Started = [sample.Processes[0] with { Name = null! }], Stopped = [] },
            "invalid-stopped" => first with { Started = [], Stopped = [sample.Processes[0] with { Name = null! }] },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var second = source.Observations[1];
        if (kind == "span-limit")
            second = second with { Sample = second.Sample with
                { MonotonicTimestamp = sample.MonotonicTimestamp + 86400L * Stopwatch.Frequency } };
        var invalid = source with { Observations = [changed, second] };
        var message = kind switch
        {
            "mixed-clocks" => "Incident observations must be ordered.",
            "duplicate-traffic" => "Invalid incident traffic or observation limit.",
            "span-limit" => "Incident span exceeds one day.",
            _ => "Invalid incident observation."
        };

        Assert.That(() => invalid.Save(FilePath), Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(message));
        Assert.That(File.ReadAllBytes(FilePath), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SaveLoad_AcceptsOneDaySpanWithWallOrMonotonicClock(bool monotonic)
    {
        var source = WithoutTraffic();
        source = source with { Observations = source.Observations.Select((frame, index) => frame with
        {
            Sample = frame.Sample with
            {
                Timestamp = DateTimeOffset.UnixEpoch.AddDays(index),
                MonotonicTimestamp = monotonic ? (1 + index * 86400L) * Stopwatch.Frequency : 0
            }
        }).ToArray() };
        source.Save(FilePath);
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.Observations[1].Sample.Timestamp - loaded.Observations[0].Sample.Timestamp,
            Is.EqualTo(TimeSpan.FromDays(1)));
        Assert.That(loaded.Observations[0].Sample.MonotonicTimestamp, Is.EqualTo(monotonic ? Stopwatch.Frequency : 0));
    }

    [Test]
    public void SaveLoad_EmptyRecordingKeepsNoSelection()
    {
        var source = Recording() with { Observations = [], SelectedIndex = -1 };
        source.Save(FilePath);
        var loaded = IncidentRecording.Load(FilePath);
        Assert.That(loaded.Observations, Is.Empty);
        Assert.That(loaded.SelectedIndex, Is.EqualTo(-1));
    }

    private static IncidentEventResult Events() => new(
        [new(DateTimeOffset.UnixEpoch, "Application Error", 1000, IncidentEventLevel.Error, "Crash", 1, 123)],
        [new("Application", IncidentEventCoverageStatus.Complete), new("System", IncidentEventCoverageStatus.TimedOut)], true);

    private static IncidentRecording WithoutTraffic()
    {
        var source = Recording();
        return source with { Observations = source.Observations.Select(frame => frame with { Network = null }).ToArray() };
    }

    private void WriteJson(string json)
    {
        using var file = File.Create(FilePath);
        using var compressed = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new StreamWriter(compressed);
        writer.Write(json);
    }
}
