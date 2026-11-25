using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class IncidentReadingCharacterizationTests
{
    private static ProcessUsage Owner(int id = 1, long token = 100) => new(id, token, "p" + id, 5, 10, 20)
        { PrivateBytes = 700, GpuPercent = 30, ImagePath = "path", ThreadCount = 3, HandleCount = 4 };
    private static MonitorSample Sample(int second = 10, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0, false, (second + 1) * Stopwatch.Frequency);
    private static ProcessNetworkReading Rate(ProcessUsage owner, double? receive = 10, double? send = 20) =>
        new(owner.ProcessId, owner.CreationTicks, 100, 200, receive, send);
    private static ConnectionCapture Traffic(MonitorSample sample, params ProcessNetworkReading[] rows) =>
        new(sample, null, [], new(sample.MonotonicTimestamp, Stopwatch.Frequency, ProcessNetworkQuality.Provisional, rows));

    [TestCase(CaptureMetric.Cpu, 5d)]
    [TestCase(CaptureMetric.Memory, 10d)]
    [TestCase(CaptureMetric.PrimaryDisk, 20d)]
    [TestCase(CaptureMetric.SecondaryDisk, 20d)]
    [TestCase(CaptureMetric.Gpu, 30d)]
    [TestCase(CaptureMetric.Process, 5d)]
    [TestCase(CaptureMetric.ApplicationMemory, 5d)]
    [TestCase((CaptureMetric)999, 5d)]
    public void ProcessReadings_ScalarMetricsKeepTheirRawMappingAndIgnoreTraffic(CaptureMetric metric, double expected)
    {
        var owner = Owner(); var sample = Sample(10, owner);
        var invalidTraffic = new ConnectionCapture(null!, null, null!, null);
        var row = IncidentTimeline.ProcessReadings(sample, metric, invalidTraffic).Single();
        Assert.That(row.Process, Is.SameAs(owner));
        Assert.That(row.Value, Is.EqualTo(expected));
    }

    [Test]
    public void ProcessReadings_ScalarProjectionKeepsDuplicatesAndDoesNotApplyTheNetworkGapGate()
    {
        var owner = Owner(); var sample = Sample(10, owner, owner) with { IsGap = true };
        var rows = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Cpu);
        Assert.That(rows.Select(r => r.Value), Is.EqualTo(new[] { 5d, 5d }));
        Assert.That(rows.All(r => ReferenceEquals(r.Process, owner)), Is.True);
        Assert.That(IncidentTimeline.ProcessReadings(Sample(10, owner with { CpuPercent = double.NaN }), CaptureMetric.Cpu)
            .Single().Value, Is.NaN);
    }

    [TestCase("same", true)]
    [TestCase("offset", true)]
    [TestCase("wall", false)]
    [TestCase("qpc", false)]
    [TestCase("negative-qpc", false)]
    public void ProcessReadings_NetworkRequiresBothCaptureKeyFieldsButNotNetworkClockMetadata(string condition, bool aligned)
    {
        var sampleOwner = Owner(1); var exact = Owner(2); var cached = Owner(3);
        var sample = Sample(10, sampleOwner);
        var capture = Traffic(sample, Rate(sampleOwner, 10, 0), Rate(exact, 20, 0), Rate(cached, 30, 0)) with
            { Owners = [exact] };
        capture = capture with { Sample = condition switch
        {
            "offset" => sample with { Timestamp = sample.Timestamp.ToOffset(TimeSpan.FromHours(2)) },
            "wall" => sample with { Timestamp = sample.Timestamp.AddTicks(1) },
            "qpc" => sample with { MonotonicTimestamp = sample.MonotonicTimestamp + 1 },
            "negative-qpc" => sample with { MonotonicTimestamp = -1 }, _ => sample
        }, Network = capture.Network! with { TimestampQpc = -100, Frequency = 0, Owners = [cached],
            FlowQuality = ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Discontinuity } };
        var rows = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture);
        Assert.That(rows.Select(r => r.Process.ProcessId), Is.EqualTo(aligned ? new[] { 3, 2, 1 } : [1]));
        Assert.That(rows.Select(r => r.Value), Is.EqualTo(aligned ? new double?[] { 30, 20, 10 } : [null]));
    }

    [TestCase(ProcessNetworkQuality.None, true)]
    [TestCase(ProcessNetworkQuality.Provisional, true)]
    [TestCase(ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.WarmingUp, false)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, false)]
    [TestCase(ProcessNetworkQuality.Unavailable, false)]
    [TestCase(ProcessNetworkQuality.Discontinuity, false)]
    [TestCase((ProcessNetworkQuality)64, false)]
    public void ProcessReadings_ProcessQualityGatesRatesWithoutHidingOwners(ProcessNetworkQuality quality, bool valid)
    {
        var owner = Owner(); var sample = Sample(10, owner);
        var capture = Traffic(sample, Rate(owner));
        capture = capture with { Network = capture.Network! with { Quality = quality } };
        var row = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture).Single();
        Assert.That(row.Process, Is.SameAs(owner));
        Assert.That(row.Value, Is.EqualTo(valid ? 30d : (double?)null));
    }

    [TestCase(null, 0d, null)]
    [TestCase(0d, null, null)]
    [TestCase(-1d, 0d, null)]
    [TestCase(0d, -1d, null)]
    [TestCase(double.NaN, 0d, null)]
    [TestCase(0d, double.NaN, null)]
    [TestCase(double.PositiveInfinity, 0d, null)]
    [TestCase(0d, double.PositiveInfinity, null)]
    [TestCase(double.NegativeInfinity, 0d, null)]
    [TestCase(double.MaxValue, double.MaxValue, null)]
    [TestCase(0d, 0d, 0d)]
    [TestCase(double.MaxValue, 0d, double.MaxValue)]
    [TestCase(3d, 4d, 7d)]
    public void ProcessReadings_RequiresBothNonnegativeDirectionsAndAFiniteSum(double? receive, double? send, double? expected)
    {
        var owner = Owner(); var sample = Sample(10, owner);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, Traffic(sample, Rate(owner, receive, send)))
            .Single().Value, Is.EqualTo(expected));
    }

    [TestCase("null", true)]
    [TestCase("empty", false)]
    [TestCase("exact", true)]
    [TestCase("other-domain", false)]
    public void ProcessReadings_CurrentLifetimeSetPreservesUnknownVersusExplicitMembership(string membership, bool valid)
    {
        var owner = Owner(); var sample = Sample(10, owner); var capture = Traffic(sample, Rate(owner));
        capture = capture with { Network = capture.Network! with { CurrentLifetimes = membership switch
        {
            "null" => null, "empty" => new HashSet<ProcessLifetimeIdentity>(),
            "exact" => new HashSet<ProcessLifetimeIdentity> { owner.Identity },
            _ => new HashSet<ProcessLifetimeIdentity> { new(owner.ProcessId, owner.CreationTicks, ProcessLifetimeDomain.UnixMicroseconds) }
        } } };
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture).Single().Value,
            Is.EqualTo(valid ? 30d : (double?)null));
    }

    [TestCase(0L, ProcessLifetimeDomain.WindowsFileTime)]
    [TestCase(-1L, ProcessLifetimeDomain.WindowsFileTime)]
    [TestCase(100L, ProcessLifetimeDomain.UnixMicroseconds)]
    public void ProcessReadings_NetworkRequiresAPositiveWindowsLifetime(long token, ProcessLifetimeDomain domain)
    {
        var owner = Owner(token: token) with { LifetimeDomain = domain }; var sample = Sample(10, owner);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, Traffic(sample, Rate(owner))).Single().Value, Is.Null);
    }

    [Test]
    public void ProcessReadings_PreservesMetadataPriorityAndScrubsOnlyTheExistingCachedFields()
    {
        var first = Owner(1); var sample = Sample(10, first, first with { Name = "duplicate" });
        var exact = Owner(2); var cached = Owner(3);
        var capture = Traffic(sample, Rate(first, 10, 0), Rate(exact, 20, 0), Rate(cached, 30, 0)) with
        {
            Owners = [first with { Name = "captured-duplicate" }, exact],
            Network = Traffic(sample).Network! with
            {
                Processes = [Rate(first, 10, 0), Rate(exact, 20, 0), Rate(cached, 30, 0)],
                Owners = [first with { Name = "cached-duplicate" }, exact with { Name = "cached-exact" }, cached, cached with { Name = "last-cached" }]
            }
        };
        var rows = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture);
        Assert.That(rows.Select(r => r.Process.ProcessId), Is.EqualTo(new[] { 3, 2, 1 }));
        Assert.That(rows[2].Process, Is.SameAs(first));
        Assert.That(rows[1].Process, Is.SameAs(exact));
        Assert.That(rows[0].Process, Is.EqualTo(cached with { CpuAvailable = false, WorkingSetBytes = null,
            IoBytesPerSecond = null, GpuPercent = null }));
        Assert.That(rows[0].Process, Is.Not.SameAs(cached));
        Assert.That(cached.CpuAvailable, Is.True);
        Assert.That(IncidentTimeline.Contributors(sample, CaptureMetric.Network, capture).Select(p => p.Identity),
            Is.EqualTo(rows.Select(r => r.Process.Identity)));
    }

    [TestCase("readings", null)]
    [TestCase("captured-owners", 30d)]
    [TestCase("cached-owners", 30d)]
    public void ProcessReadings_NullOptionalNetworkCollectionsKeepTheirEmptySemantics(string collection, double? expected)
    {
        var owner = Owner(); var sample = Sample(10, owner); var capture = Traffic(sample, Rate(owner));
        capture = collection switch
        {
            "readings" => capture with { Network = capture.Network! with { Processes = null! } },
            "captured-owners" => capture with { Owners = null! },
            _ => capture with { Network = capture.Network! with { Owners = null! } }
        };
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture).Single().Value, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProcessReadings_DuplicateReadingsThrowBeforeInvalidCoverageOrOwnerCollectionsAreExamined(bool gap)
    {
        var owner = Owner(); var sample = Sample(10, owner) with { IsGap = gap, Processes = null! };
        var capture = Traffic(sample, Rate(owner), Rate(owner, 50, 60));
        capture = capture with { Network = capture.Network! with { Quality = gap ? ProcessNetworkQuality.None : ProcessNetworkQuality.Unavailable } };
        Assert.That(() => IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("sample")]
    [TestCase("capture-sample")]
    [TestCase("processes")]
    [TestCase("process")]
    [TestCase("reading")]
    [TestCase("captured-owner")]
    [TestCase("cached-owner")]
    public void ProcessReadings_MalformedRequiredDataKeepsItsExistingException(string malformed)
    {
        var owner = Owner(); var sample = Sample(10, owner); var capture = Traffic(sample, Rate(owner));
        if (malformed == "sample") sample = null!;
        if (malformed == "capture-sample") capture = capture with { Sample = null! };
        if (malformed == "processes") sample = sample with { Processes = null! };
        if (malformed == "process") sample = sample with { Processes = [null!] };
        if (malformed == "reading") capture = capture with { Network = capture.Network! with { Processes = [null!] } };
        if (malformed == "captured-owner") capture = capture with { Owners = [null!] };
        if (malformed == "cached-owner") capture = capture with { Network = capture.Network! with { Owners = [null!] } };
        Assert.That(() => IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture), malformed == "processes"
            ? Throws.TypeOf<ArgumentNullException>() : Throws.TypeOf<NullReferenceException>());
    }

    [Test]
    public void Compare_NetworkMeansExcludeMissingRatesAndKeepLatestObservedMetadata()
    {
        var owner = Owner(); var latest = owner with { Name = "latest" };
        var before = Sample(0, owner); var beforeMissing = Sample(1, owner);
        var during = Sample(15, owner); var duringMissing = Sample(16, latest); var after = Sample(20);
        var frames = new[] { Traffic(before, Rate(owner)), Traffic(beforeMissing, Rate(owner, null, 0)),
            Traffic(during, Rate(owner, 50, 0)), Traffic(duringMissing, Rate(latest, null, 0)) }
            .ToDictionary(f => SampleKey.Of(f.Sample));
        var result = IncidentTimeline.Compare([before, beforeMissing, during, duringMissing, after], during, CaptureMetric.Network, frames);
        var row = result.Processes.Single();
        Assert.That(row.Before, Is.EqualTo(new IncidentTimeline.Reading(30, 1)));
        Assert.That(row.During, Is.EqualTo(new IncidentTimeline.Reading(50, 1)));
        Assert.That(row.After, Is.EqualTo(new IncidentTimeline.Reading(null, 0)));
        Assert.That(row.Change, Is.EqualTo(20));
        Assert.That(row.Process, Is.SameAs(latest));
    }
}
