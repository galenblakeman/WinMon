using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class PerformanceAggregateTests
{
    [Test]
    public void UsesActualIrregularIntervalsForWeightedAverageAndCoverage()
    {
        var aggregate = new PerformanceAggregate(10, 10);
        aggregate.Add(Sample(10, 1), null, 1, true);
        aggregate.Add(Sample(40, 3), null, 4, true);

        var cpu = aggregate.Summary()["cpuPercent"];

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Minimum, Is.EqualTo(10));
            Assert.That(cpu.Average, Is.EqualTo(32.5));
            Assert.That(cpu.Maximum, Is.EqualTo(40));
            Assert.That(cpu.ValidSampleCount, Is.EqualTo(2));
            Assert.That(cpu.ValidSeconds, Is.EqualTo(4));
            Assert.That(cpu.Coverage, Is.EqualTo(0.4));
        });
    }

    [Test]
    public void ExcludesNullAndGapReadingsPerMetricInsteadOfTreatingThemAsZero()
    {
        var aggregate = new PerformanceAggregate(4, 4);
        aggregate.Add(Sample(50, 2, memory: 100), null, 2, true);
        aggregate.Add(Sample(null, 2, memory: 200), null, 4, true);
        aggregate.Add(Sample(99, 1, memory: 300, gap: true), null, 5, true);

        var metrics = aggregate.Summary();
        var cpu = metrics["cpuPercent"];
        var memory = metrics["memoryUsedBytes"];

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Average, Is.EqualTo(50));
            Assert.That(cpu.ValidSampleCount, Is.EqualTo(1));
            Assert.That(cpu.ValidSeconds, Is.EqualTo(2));
            Assert.That(cpu.Coverage, Is.EqualTo(0.5));
            Assert.That(memory.Average, Is.EqualTo(150));
            Assert.That(memory.ValidSampleCount, Is.EqualTo(2));
            Assert.That(memory.ValidSeconds, Is.EqualTo(4));
            Assert.That(memory.Coverage, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClipsTheFinalIntervalAtTheRequestedWindowBoundary()
    {
        var aggregate = new PerformanceAggregate(5, 5);
        aggregate.Add(Sample(10, 4), null, 4, true);
        aggregate.Add(Sample(50, 4), null, 8, true);

        var cpu = aggregate.Summary()["cpuPercent"];

        Assert.Multiple(() =>
        {
            Assert.That(cpu.Average, Is.EqualTo(18));
            Assert.That(cpu.ValidSeconds, Is.EqualTo(5));
            Assert.That(cpu.Coverage, Is.EqualTo(1));
            Assert.That(cpu.Minimum, Is.EqualTo(10));
            Assert.That(cpu.Maximum, Is.EqualTo(50));
        });
    }

    [Test]
    public void AggregatesOneSecondSamplesIntoATenSecondOutputBucket()
    {
        var aggregate = new PerformanceAggregate(10, 10);
        for (var second = 1; second <= 10; second++)
            aggregate.Add(Sample(second, 1), null, second, true);

        var bucket = aggregate.Trend(10).Single();
        var cpu = bucket.Metrics["cpuPercent"];

        Assert.Multiple(() =>
        {
            Assert.That(bucket.StartSeconds, Is.Zero);
            Assert.That(bucket.DurationSeconds, Is.EqualTo(10));
            Assert.That(cpu.Minimum, Is.EqualTo(1));
            Assert.That(cpu.Average, Is.EqualTo(5.5));
            Assert.That(cpu.Maximum, Is.EqualTo(10));
            Assert.That(cpu.ValidSampleCount, Is.EqualTo(10));
            Assert.That(cpu.ValidSeconds, Is.EqualTo(10));
        });
    }

    [Test]
    public void DoesNotTreatUnavailableProcessCpuPlaceholderAsARealReading()
    {
        var aggregate = new PerformanceAggregate(2, 2);
        var unavailable = new ProcessUsage(42, 100, "example", 99) { CpuAvailable = false };
        var observed = new ProcessUsage(42, 100, "example", 25) { CpuAvailable = true };
        aggregate.Add(Sample(1, 1), unavailable, 1, true);
        aggregate.Add(Sample(1, 1), observed, 2, true);

        var processCpu = aggregate.Summary()["processCpuPercent"];

        Assert.Multiple(() =>
        {
            Assert.That(processCpu.Minimum, Is.EqualTo(25));
            Assert.That(processCpu.Average, Is.EqualTo(25));
            Assert.That(processCpu.Maximum, Is.EqualTo(25));
            Assert.That(processCpu.ValidSampleCount, Is.EqualTo(1));
            Assert.That(processCpu.ValidSeconds, Is.EqualTo(1));
            Assert.That(processCpu.Coverage, Is.EqualTo(0.5));
        });
    }

    [Test]
    public void SplitsSamplesAcrossBucketsAndReportsAPartialFinalBucket()
    {
        var aggregate = new PerformanceAggregate(12, 3);
        aggregate.Add(Sample(10, 4), null, 4, true);
        aggregate.Add(Sample(40, 4), null, 8, true);

        var buckets = aggregate.Trend(8);
        Assert.Multiple(() =>
        {
            Assert.That(buckets.Select(bucket => bucket.StartSeconds), Is.EqualTo(new[] { 0d, 3d, 6d }));
            Assert.That(buckets.Select(bucket => bucket.DurationSeconds), Is.EqualTo(new[] { 3d, 3d, 2d }));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].Average),
                Is.EqualTo(new[] { 10d, 30d, 40d }));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].ValidSeconds),
                Is.EqualTo(new[] { 3d, 3d, 2d }));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].ValidSampleCount),
                Is.EqualTo(new[] { 1, 2, 1 }));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].Coverage), Is.All.EqualTo(1));
            Assert.That(aggregate.Summary()["cpuPercent"].Average, Is.EqualTo(25));
            Assert.That(aggregate.Summary()["cpuPercent"].ValidSeconds, Is.EqualTo(8));
            Assert.That(aggregate.Summary()["cpuPercent"].Coverage, Is.EqualTo(2d / 3));
        });
    }

    [Test]
    public void DuplicateOlderAndOverlappingSamplesCannotCountTimeTwice()
    {
        var aggregate = new PerformanceAggregate(6, 3);
        aggregate.Add(Sample(10, 4), null, 4, true);
        aggregate.Add(Sample(999, 4), null, 4, true);
        aggregate.Add(Sample(999, 3), null, 3, true);
        aggregate.Add(Sample(40, 4), null, 6, true);

        var cpu = aggregate.Summary()["cpuPercent"];
        var buckets = aggregate.Trend(6);
        Assert.Multiple(() =>
        {
            Assert.That(cpu.Average, Is.EqualTo(20));
            Assert.That(cpu.Minimum, Is.EqualTo(10));
            Assert.That(cpu.Maximum, Is.EqualTo(40));
            Assert.That(cpu.ValidSeconds, Is.EqualTo(6));
            Assert.That(cpu.ValidSampleCount, Is.EqualTo(2));
            Assert.That(cpu.Coverage, Is.EqualTo(1));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].Average),
                Is.EqualTo(new[] { 10d, 30d }));
            Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].ValidSeconds), Is.All.EqualTo(3));
        });
    }

    [TestCase(50, 1, false)]
    [TestCase(double.NaN, 1, true)]
    [TestCase(double.PositiveInfinity, 1, true)]
    [TestCase(double.NegativeInfinity, 1, true)]
    [TestCase(50, 0, true)]
    [TestCase(50, -1, true)]
    [TestCase(50, 5.001, true)]
    [TestCase(50, double.NaN, true)]
    [TestCase(50, double.PositiveInfinity, true)]
    public void InvalidReadingsContributeNoTimeAndLaterValidReadingsRecover(double cpu, double interval, bool fresh)
    {
        var aggregate = new PerformanceAggregate(2, 1);
        aggregate.Add(Sample(cpu, interval), null, 1, fresh);
        var rejected = aggregate.Summary()["cpuPercent"];
        aggregate.Add(Sample(25, 1), null, 2, true);
        var recovered = aggregate.Summary()["cpuPercent"];
        var buckets = aggregate.Trend(2);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Available, Is.False);
            Assert.That(rejected.Minimum, Is.Null);
            Assert.That(rejected.Average, Is.Null);
            Assert.That(rejected.Maximum, Is.Null);
            Assert.That(rejected.ValidSampleCount, Is.Zero);
            Assert.That(rejected.ValidSeconds, Is.Zero);
            Assert.That(rejected.Coverage, Is.Zero);
            Assert.That(recovered.Minimum, Is.EqualTo(25));
            Assert.That(recovered.Average, Is.EqualTo(25));
            Assert.That(recovered.Maximum, Is.EqualTo(25));
            Assert.That(recovered.ValidSampleCount, Is.EqualTo(1));
            Assert.That(recovered.ValidSeconds, Is.EqualTo(1));
            Assert.That(recovered.Coverage, Is.EqualTo(0.5));
            Assert.That(buckets[0].Metrics["cpuPercent"].Available, Is.False);
            Assert.That(buckets[1].Metrics["cpuPercent"].Coverage, Is.EqualTo(1));
        });
    }

    private static MonitorSample Sample(double? cpu, double interval, ulong? memory = null, bool gap = false) =>
        new(DateTimeOffset.UtcNow, cpu, [], 0, gap) { IntervalSeconds = interval, Resources = new ResourceSample
        {
            UsedMemoryBytes = memory,
            TotalMemoryBytes = memory is null ? null : 1_000
        } };
}
