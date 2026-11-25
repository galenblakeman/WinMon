using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ResourceTrendIngestionTests
{
    private static MonitorSample Sample(double second, double seconds = 1, double cpu = 10, long monotonic = 0) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), cpu, [], 0, false, monotonic) { IntervalSeconds = seconds };

    [TestCase("gap")]
    [TestCase("zero")]
    [TestCase("negative")]
    [TestCase("nan")]
    [TestCase("infinity")]
    [TestCase("too-long")]
    [TestCase("underflow")]
    public void Add_RejectedIntervalsPreserveStateAndPendingRestart(string kind)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1));
        archive.BreakContinuity();
        var before = JsonSerializer.Serialize(archive.Snapshot());
        var rejected = kind switch
        {
            "gap" => Sample(2) with { IsGap = true },
            "zero" => Sample(2, 0),
            "negative" => Sample(2, -1),
            "nan" => Sample(2, double.NaN),
            "infinity" => Sample(2, double.PositiveInfinity),
            "too-long" => Sample(2, Math.BitIncrement(5)),
            _ => Sample(2) with { Timestamp = DateTimeOffset.MinValue }
        };
        archive.Add(rejected);
        Assert.That(JsonSerializer.Serialize(archive.Snapshot()), Is.EqualTo(before));
        archive.Add(Sample(2, cpu: 20));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.CollectionRestart));
        Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        Assert.That(value.Average, Is.EqualTo(15));
    }

    [Test]
    public void Add_NullPreservesTheExistingArgumentException()
    {
        Assert.That(() => new ResourceTrendArchive().Add(null!),
            Throws.ArgumentNullException.With.Property("ParamName").EqualTo("sample"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Add_EarliestPrunableDatePreservesTheExistingPartialStateAndException(bool beforeBoundary)
    {
        var archive = new ResourceTrendArchive();
        var boundary = DateTimeOffset.MinValue.AddDays(7);
        var sample = Sample(1) with { Timestamp = boundary.AddTicks(beforeBoundary ? -1 : 0) };
        if (beforeBoundary) Assert.That(() => archive.Add(sample), Throws.TypeOf<ArgumentOutOfRangeException>());
        else Assert.That(() => archive.Add(sample), Throws.Nothing);
        var retained = archive.Snapshot(boundary).Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(retained.Average, Is.EqualTo(10));
        Assert.That(retained.ObservedSeconds, Is.EqualTo(1));
        archive.BreakContinuity();
        archive.Add(Sample(1, cpu: 20));
        Assert.That(archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(20));
    }

    [Test]
    public void Add_NullResourcesPreservesPartialCpuAccumulationAndRecovery()
    {
        var archive = new ResourceTrendArchive();
        Assert.That(() => archive.Add(Sample(1) with { Resources = null! }), Throws.TypeOf<NullReferenceException>());
        Assert.That(archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].Average, Is.EqualTo(10));
        archive.Add(Sample(2, cpu: 20));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        Assert.That(value.Average, Is.EqualTo(15));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Add_ElapsedIntervalsAllowExactlyOneClockTickOfSlack(int extraTicks)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, monotonic: Stopwatch.Frequency));
        archive.Add(Sample(2, 1 + extraTicks / (double)Stopwatch.Frequency, 20, 2 * Stopwatch.Frequency));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(extraTicks <= 1 ? 2 + extraTicks / (double)Stopwatch.Frequency : 1).Within(1e-7));
        Assert.That(value.Average, Is.EqualTo(extraTicks <= 1 ? 15 : 10).Within(1e-5));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void Add_WallClockDiscontinuityRequiresMoreThanFiveSeconds(int ticksBeyondBoundary)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(10, monotonic: Stopwatch.Frequency));
        archive.Add(Sample(16, cpu: 20, monotonic: 2 * Stopwatch.Frequency) with
            { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(16).AddTicks(ticksBeyondBoundary) });
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        Assert.That(value.Break, Is.EqualTo(ticksBeyondBoundary > 0 ? ResourceTrendBreak.ClockChanged : ResourceTrendBreak.None));
    }

    [Test]
    public void Add_BackwardClockQuarantineIncludesTheOldEndpointAndClearsOnlyAfterIt()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(180, monotonic: Stopwatch.Frequency));
        archive.Add(Sample(1, monotonic: 2 * Stopwatch.Frequency));
        archive.Add(Sample(180, monotonic: 181 * Stopwatch.Frequency));
        archive.Add(Sample(181, monotonic: 182 * Stopwatch.Frequency));
        var buckets = archive.Snapshot(DateTimeOffset.UnixEpoch.AddSeconds(240)).Buckets;
        Assert.That(buckets.Single(b => b.StartUtc == DateTimeOffset.UnixEpoch.AddMinutes(2)).Values[ResourceTrendMetric.Cpu].Break,
            Is.EqualTo(ResourceTrendBreak.ClockChanged));
        Assert.That(buckets.Single(b => b.StartUtc == DateTimeOffset.UnixEpoch.AddMinutes(3)).Values[ResourceTrendMetric.Cpu].Break,
            Is.EqualTo(ResourceTrendBreak.None));
    }

    [Test]
    public void Add_FiveSecondIntervalsAndTheLastUtcMinuteRemainUsable()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(5, 5));
        Assert.That(archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu].ObservedSeconds, Is.EqualTo(5));
        archive.BreakContinuity();
        archive.Add(Sample(5, 2) with { Timestamp = DateTimeOffset.MaxValue });
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(2));
        Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.CollectionRestart));
    }

    [Test]
    public void Add_SubTickIntervalAdvancesTheCheckpointAndConsumesTheRestartMarker()
    {
        var archive = new ResourceTrendArchive();
        archive.BreakContinuity();
        archive.Add(Sample(1, double.Epsilon));
        var empty = archive.Snapshot();
        Assert.That(empty.EndUtc, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(1)));
        Assert.That(empty.Buckets, Is.Empty);
        archive.Add(Sample(2));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.That(value.ObservedSeconds, Is.EqualTo(1));
        Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.None));
    }

    [Test]
    public void Add_FullMinuteCapsDurationButStillTracksDeviceChanges()
    {
        var archive = new ResourceTrendArchive();
        for (var index = 1; index <= 65; index++)
            archive.Add(Reading(59, ResourceTrendMetric.Receive, index <= 60 ? 10 : 90, index <= 60 ? "original" : "other") with
                { MonotonicTimestamp = index * Stopwatch.Frequency, TotalCpuPercent = index <= 60 ? 10 : 90 });
        archive.Add(Reading(61, ResourceTrendMetric.Receive, 20, "original") with { MonotonicTimestamp = 66 * Stopwatch.Frequency });
        var buckets = archive.Snapshot().Buckets;
        var cpu = buckets[0].Values[ResourceTrendMetric.Cpu];
        var full = buckets[0].Values[ResourceTrendMetric.Receive];
        Assert.That(cpu.ObservedSeconds, Is.EqualTo(60));
        Assert.That(cpu.Average, Is.EqualTo(10));
        Assert.That(full.ObservedSeconds, Is.EqualTo(60));
        Assert.That(full.WeightedSum, Is.EqualTo(600));
        Assert.That(full.DeviceId, Is.EqualTo("original"));
        Assert.That(full.Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
        Assert.That(buckets[1].Values[ResourceTrendMetric.Receive].Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
    }

    private static IEnumerable<TestCaseData> NumericLimits()
    {
        foreach (var metric in Enum.GetValues<ResourceTrendMetric>().Where(m => m is not (ResourceTrendMetric.MemoryBytes or ResourceTrendMetric.MemoryPercent)))
        {
            var limit = metric is ResourceTrendMetric.Cpu or ResourceTrendMetric.PrimaryDiskActive or ResourceTrendMetric.SecondaryDiskActive or ResourceTrendMetric.Gpu ? 100d : 1e20;
            yield return new TestCaseData(metric, 0d, true);
            yield return new TestCaseData(metric, limit, true);
            yield return new TestCaseData(metric, Math.BitIncrement(limit), false);
            yield return new TestCaseData(metric, -1d, false);
            yield return new TestCaseData(metric, double.NaN, false);
            yield return new TestCaseData(metric, double.PositiveInfinity, false);
            yield return new TestCaseData(metric, null, false);
        }
    }

    private static MonitorSample Reading(int second, ResourceTrendMetric metric, double? reading, string id = "device", string label = "label")
    {
        var resources = new ResourceSample { DiskName = id, SecondaryDiskName = id, NetworkId = id, NetworkName = label, GpuId = id, GpuName = label };
        resources = metric switch
        {
            ResourceTrendMetric.PrimaryDiskActive => resources with { DiskActivePercent = reading },
            ResourceTrendMetric.PrimaryDiskRead => resources with { DiskReadBytesPerSecond = reading },
            ResourceTrendMetric.PrimaryDiskWrite => resources with { DiskWriteBytesPerSecond = reading },
            ResourceTrendMetric.SecondaryDiskActive => resources with { SecondaryDiskActivePercent = reading },
            ResourceTrendMetric.SecondaryDiskRead => resources with { SecondaryDiskReadBytesPerSecond = reading },
            ResourceTrendMetric.SecondaryDiskWrite => resources with { SecondaryDiskWriteBytesPerSecond = reading },
            ResourceTrendMetric.Receive => resources with { ReceiveBytesPerSecond = reading },
            ResourceTrendMetric.Send => resources with { SendBytesPerSecond = reading },
            ResourceTrendMetric.Gpu => resources with { GpuPercent = reading },
            _ => resources
        };
        return Sample(second) with { TotalCpuPercent = metric == ResourceTrendMetric.Cpu ? reading : 10, Resources = resources };
    }

    [TestCaseSource(nameof(NumericLimits))]
    public void Add_LiveReadingsKeepInclusiveMetricLimitsAndRejectInvalidValues(ResourceTrendMetric metric, double? reading, bool accepted)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Reading(1, metric, reading));
        var values = archive.Snapshot().Buckets.Single().Values;
        Assert.That(values.ContainsKey(metric), Is.EqualTo(accepted));
        if (accepted)
        {
            Assert.That(values[metric].Average, Is.EqualTo(reading));
            Assert.That(values[metric].ObservedSeconds, Is.EqualTo(1));
            Assert.That(values[metric].Break, Is.EqualTo(ResourceTrendBreak.None));
        }
    }

    [TestCase(0UL)]
    [TestCase(100UL)]
    [TestCase(101UL)]
    [TestCase(ulong.MaxValue)]
    public void Add_MemoryBytesRemainIndependentOfInvalidMemoryPercent(ulong used)
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1) with { Resources = new ResourceSample { TotalMemoryBytes = 100, UsedMemoryBytes = used } });
        var values = archive.Snapshot().Buckets.Single().Values;
        Assert.That(values[ResourceTrendMetric.MemoryBytes].Average, Is.EqualTo((double)used));
        Assert.That(values.ContainsKey(ResourceTrendMetric.MemoryPercent), Is.EqualTo(used <= 100));
        if (used <= 100) Assert.That(values[ResourceTrendMetric.MemoryPercent].Average, Is.EqualTo((double)used));
    }

    [TestCase("above-percent")]
    [TestCase("above-rate")]
    [TestCase("negative")]
    [TestCase("nan")]
    [TestCase("missing")]
    [TestCase("unnamed")]
    public void Add_DeviceTrackingPreservesItsDifferentEligibilityFromNumericAccumulation(string kind)
    {
        var metric = kind == "above-percent" ? ResourceTrendMetric.Gpu : ResourceTrendMetric.Receive;
        var invalid = kind switch { "above-percent" => Math.BitIncrement(100d), "above-rate" => Math.BitIncrement(1e20),
            "negative" => -1d, "nan" => double.NaN, "missing" => (double?)null, _ => 20d };
        var archive = new ResourceTrendArchive();
        archive.Add(Reading(1, metric, 10, "original"));
        archive.Add(Reading(61, metric, invalid, kind == "unnamed" ? " " : "other"));
        archive.Add(Reading(121, metric, 20, "original"));
        var buckets = archive.Snapshot().Buckets;
        Assert.That(buckets[1].Values.ContainsKey(metric), Is.False);
        var restoredDevice = buckets[2].Values[metric];
        Assert.That(restoredDevice.Break, Is.EqualTo(kind is "above-percent" or "above-rate" ? ResourceTrendBreak.ResourceChanged : ResourceTrendBreak.None));
        Assert.That(restoredDevice.DeviceId, Is.EqualTo("original"));
        Assert.That(restoredDevice.ObservedSeconds, Is.EqualTo(1));
    }

    [Test]
    public void Add_LabelChangesAndTruncatedIdentityCollisionsKeepTheirBreakMarkers()
    {
        var labels = new ResourceTrendArchive();
        labels.Add(Reading(1, ResourceTrendMetric.Receive, 10, "same", "first"));
        labels.Add(Reading(2, ResourceTrendMetric.Receive, 20, "same", "second"));
        var relabeled = labels.Snapshot().Buckets.Single().Values[ResourceTrendMetric.Receive];
        Assert.That(relabeled.Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
        Assert.That(relabeled.DeviceLabel, Is.EqualTo("second"));
        var identities = new ResourceTrendArchive();
        var prefix = new string('d', 256);
        identities.Add(Reading(1, ResourceTrendMetric.Receive, 10, prefix + "a"));
        identities.Add(Reading(61, ResourceTrendMetric.Receive, 20, prefix + "b"));
        var changed = identities.Snapshot().Buckets.Last().Values[ResourceTrendMetric.Receive];
        Assert.That(changed.Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
        Assert.That(changed.DeviceId, Is.EqualTo(prefix));
    }
}
