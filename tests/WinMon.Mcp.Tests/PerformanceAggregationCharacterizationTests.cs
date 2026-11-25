using System.Text.Json;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class PerformanceAggregationCharacterizationTests
{
    private static readonly (string Name, double Value, string Unit)[] Metrics =
    [
        ("cpuPercent", 11, "percentOfMachineCapacity"), ("memoryUsedBytes", 23, "bytes"),
        ("memoryPercent", 11.5, "percent"), ("gpuPercent", 29, "percentOfBusiestEngine"),
        ("diskActivePercent", 31, "percent"), ("diskReadBytesPerSecond", 37, "bytesPerSecond"),
        ("diskWriteBytesPerSecond", 41, "bytesPerSecond"), ("receiveBytesPerSecond", 43, "bytesPerSecond"),
        ("sendBytesPerSecond", 47, "bytesPerSecond"), ("processCpuPercent", 53, "percentOfMachineCapacity"),
        ("processWorkingSetBytes", 59, "bytes"), ("processPrivateBytes", 67, "bytes"),
        ("processIoBytesPerSecond", 61, "bytesPerSecond"), ("processGpuPercent", 71, "percentOfBusiestEngine"),
        ("processThreads", 73, "count"), ("processHandles", 79, "count")
    ];

    private static MonitorSample Sample(double interval = 1) => new(
        new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero), 11,
        [new ProcessUsage(99, 1, "other", 97)], 1)
    {
        IntervalSeconds = interval,
        Resources = new ResourceSample { UsedMemoryBytes = 23, TotalMemoryBytes = 200, GpuPercent = 29,
            DiskActivePercent = 31, DiskReadBytesPerSecond = 37, DiskWriteBytesPerSecond = 41,
            ReceiveBytesPerSecond = 43, SendBytesPerSecond = 47 }
    };
    private static ProcessUsage Process() => new(42, 100, "fixture", 53, 59, 61)
        { PrivateBytes = 67, GpuPercent = 71, ThreadCount = 73, HandleCount = 79 };

    private static IEnumerable<TestCaseData> MetricCases() => Metrics.Select(metric =>
        new TestCaseData(metric.Name, metric.Value, metric.Unit).SetName("AggregationMetric_" + metric.Name));

    [TestCaseSource(nameof(MetricCases))]
    public void EveryMetricKeepsItsNameValueUnitAndIndependentBucketCoverage(string name, double value, string unit)
    {
        var aggregate = new PerformanceAggregate(2, 1);
        aggregate.Add(Sample(), Process(), 1, true);
        var summary = aggregate.Summary();
        Assert.That(summary.Keys, Is.EqualTo(Metrics.Select(metric => metric.Name)));
        var metric = summary[name];
        Assert.Multiple(() =>
        {
            Assert.That(metric.Minimum, Is.EqualTo(value)); Assert.That(metric.Average, Is.EqualTo(value));
            Assert.That(metric.Maximum, Is.EqualTo(value)); Assert.That(metric.Unit, Is.EqualTo(unit));
            Assert.That(metric.ValidSampleCount, Is.EqualTo(1)); Assert.That(metric.ValidSeconds, Is.EqualTo(1));
            Assert.That(metric.Coverage, Is.EqualTo(0.5)); Assert.That(metric.Available, Is.True);
        });
        var buckets = aggregate.Trend(2);
        Assert.That(buckets[0].Metrics[name].Average, Is.EqualTo(value));
        Assert.That(buckets[0].Metrics[name].Coverage, Is.EqualTo(1));
        Assert.That(buckets[1].Metrics[name].Available, Is.False);
        Assert.That(buckets[1].Metrics[name].Unit, Is.EqualTo(unit));
    }

    private static IEnumerable<TestCaseData> DoubleCases()
    {
        string[] names = ["cpuPercent", "gpuPercent", "diskActivePercent", "diskReadBytesPerSecond",
            "diskWriteBytesPerSecond", "receiveBytesPerSecond", "sendBytesPerSecond", "processCpuPercent",
            "processIoBytesPerSecond", "processGpuPercent"];
        double?[] values = [null, 0, -1, double.MaxValue, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (var name in names)
        for (var i = 0; i < values.Length; i++)
            yield return new TestCaseData(name, values[i], i is 1 or 2 or 3).SetName($"AggregationDouble_{name}_{i}");
    }
    [TestCaseSource(nameof(DoubleCases))]
    public void FloatingMetricsRetainMissingFiniteNegativeAndNonfiniteBehavior(string name, double? value, bool available)
    {
        var sample = Sample(); var process = Process();
        switch (name)
        {
            case "cpuPercent": sample = sample with { TotalCpuPercent = value }; break;
            case "gpuPercent": sample = sample with { Resources = sample.Resources with { GpuPercent = value } }; break;
            case "diskActivePercent": sample = sample with { Resources = sample.Resources with { DiskActivePercent = value } }; break;
            case "diskReadBytesPerSecond": sample = sample with { Resources = sample.Resources with { DiskReadBytesPerSecond = value } }; break;
            case "diskWriteBytesPerSecond": sample = sample with { Resources = sample.Resources with { DiskWriteBytesPerSecond = value } }; break;
            case "receiveBytesPerSecond": sample = sample with { Resources = sample.Resources with { ReceiveBytesPerSecond = value } }; break;
            case "sendBytesPerSecond": sample = sample with { Resources = sample.Resources with { SendBytesPerSecond = value } }; break;
            case "processCpuPercent": process = process with { CpuPercent = value ?? 0, CpuAvailable = value.HasValue }; break;
            case "processIoBytesPerSecond": process = process with { IoBytesPerSecond = value }; break;
            case "processGpuPercent": process = process with { GpuPercent = value }; break;
            default: throw new ArgumentOutOfRangeException(nameof(name));
        }
        var aggregate = new PerformanceAggregate(1, 1);
        aggregate.Add(sample, process, 1, true);
        var metric = aggregate.Summary()[name];
        Assert.That(metric.Available, Is.EqualTo(available));
        Assert.That(metric.Minimum, Is.EqualTo(available ? value : null));
        Assert.That(metric.Average, Is.EqualTo(available ? value : null));
        Assert.That(metric.Maximum, Is.EqualTo(available ? value : null));
        Assert.That(metric.ValidSampleCount, Is.EqualTo(available ? 1 : 0));
        Assert.That(metric.ValidSeconds, Is.EqualTo(available ? 1 : 0));
        Assert.That(metric.Coverage, Is.EqualTo(available ? 1 : 0));
    }

    [Test]
    public void MissingExplicitProcessDoesNotReadProcessRowsFromTheSample()
    {
        var aggregate = new PerformanceAggregate(1, 1);
        aggregate.Add(Sample(), null, 1, true);
        var metrics = aggregate.Summary();
        foreach (var name in Metrics.Select(metric => metric.Name).Where(name => name.StartsWith("process", StringComparison.Ordinal)))
        {
            Assert.That(metrics[name].Available, Is.False);
            Assert.That(metrics[name].Average, Is.Null);
        }
        Assert.That(metrics["cpuPercent"].Average, Is.EqualTo(11));
    }

    [Test]
    public void UnsignedAndCountExtremesKeepTheirExistingDoubleProjection()
    {
        var aggregate = new PerformanceAggregate(1, 1);
        aggregate.Add(Sample() with { Resources = Sample().Resources with { UsedMemoryBytes = ulong.MaxValue, TotalMemoryBytes = 1 } },
            Process() with { WorkingSetBytes = ulong.MaxValue, PrivateBytes = ulong.MaxValue, ThreadCount = long.MaxValue, HandleCount = long.MinValue }, 1, true);
        var metrics = aggregate.Summary();
        foreach (var name in new[] { "memoryUsedBytes", "processWorkingSetBytes", "processPrivateBytes" })
            Assert.That(metrics[name].Average, Is.EqualTo(18446744073709551616d));
        Assert.That(metrics["memoryPercent"].Average, Is.EqualTo(100d * ulong.MaxValue));
        Assert.That(metrics["processThreads"].Average, Is.EqualTo(9223372036854775808d));
        Assert.That(metrics["processHandles"].Average, Is.EqualTo(-9223372036854775808d));
    }

    [TestCase("stale")] [TestCase("gap")] [TestCase("interval")]
    public void InvalidFramesStillConsumeTheirObservedWindowBeforeRecovery(string kind)
    {
        var aggregate = new PerformanceAggregate(6, 2);
        var rejected = Sample(kind == "interval" ? 6 : 3) with { IsGap = kind == "gap" };
        aggregate.Add(rejected, Process(), 3, kind != "stale");
        Assert.That(aggregate.Summary().Values.Select(metric => metric.Available), Is.All.False);
        aggregate.Add(Sample(5), Process(), 5, true);
        var metric = aggregate.Summary()["cpuPercent"];
        Assert.That(metric.ValidSampleCount, Is.EqualTo(1));
        Assert.That(metric.ValidSeconds, Is.EqualTo(2));
        Assert.That(metric.Coverage, Is.EqualTo(1d / 3));
        var buckets = aggregate.Trend(5);
        Assert.That(buckets.Select(bucket => bucket.Metrics["cpuPercent"].ValidSeconds), Is.EqualTo(new[] { 0d, 1d, 1d }));
        Assert.That(buckets.Select(bucket => bucket.DurationSeconds), Is.EqualTo(new[] { 2d, 2d, 1d }));
    }

    [TestCase(-1, 0)] [TestCase(0, 0)] [TestCase(0.5, 0.5)] [TestCase(5, 5)]
    [TestCase(6, 5)] [TestCase(7, 4)] [TestCase(10, 1)] [TestCase(11, 0)]
    public void WindowClippingKeepsItsInclusiveFiniteBoundaries(double end, double seconds)
    {
        var aggregate = new PerformanceAggregate(6, 2);
        aggregate.Add(Sample(5), Process(), end, true);
        var metric = aggregate.Summary()["cpuPercent"];
        Assert.That(metric.ValidSeconds, Is.EqualTo(seconds));
        Assert.That(metric.ValidSampleCount, Is.EqualTo(seconds > 0 ? 1 : 0));
        Assert.That(metric.Coverage, Is.EqualTo(seconds / 6));
        Assert.That(aggregate.Trend(6).Sum(bucket => bucket.Metrics["cpuPercent"].ValidSeconds), Is.EqualTo(seconds));
    }

    [TestCase(false, 5)] [TestCase(true, 2)]
    public void NullInputFailuresKeepTheirOriginalWindowState(bool resources, double expectedSeconds)
    {
        var aggregate = new PerformanceAggregate(6, 2);
        Assert.Throws<NullReferenceException>(() => aggregate.Add(resources ? Sample() with { Resources = null! } : null!, null, 3, true));
        Assert.That(aggregate.Summary(), Is.Empty);
        aggregate.Add(Sample(5), Process(), 5, true);
        Assert.That(aggregate.Summary()["cpuPercent"].ValidSeconds, Is.EqualTo(expectedSeconds));
    }

    [Test]
    public void FiniteReadingsRetainTheirExistingWeightedOverflow()
    {
        var aggregate = new PerformanceAggregate(2, 2);
        aggregate.Add(Sample(2) with { TotalCpuPercent = double.MaxValue }, null, 2, true);
        var metric = aggregate.Summary()["cpuPercent"];
        Assert.That(metric.Minimum, Is.EqualTo(double.MaxValue));
        Assert.That(metric.Maximum, Is.EqualTo(double.MaxValue));
        Assert.That(metric.Average, Is.EqualTo(double.PositiveInfinity));
        Assert.That(metric.ValidSeconds, Is.EqualTo(2));
    }

    [Test]
    public void SummaryAndTrendRemainIndependentSnapshotsAndInputsAreUnchanged()
    {
        var aggregate = new PerformanceAggregate(2, 1);
        var sample = Sample(); var process = Process();
        var beforeSample = JsonSerializer.Serialize(sample); var beforeProcess = JsonSerializer.Serialize(process);
        aggregate.Add(sample, process, 1, true);
        var summary = aggregate.Summary(); var trend = aggregate.Trend(2);
        aggregate.Add(sample with { TotalCpuPercent = 31 }, process, 2, true);
        Assert.That(summary["cpuPercent"].Average, Is.EqualTo(11));
        Assert.That(summary["cpuPercent"].ValidSampleCount, Is.EqualTo(1));
        Assert.That(trend[1].Metrics["cpuPercent"].Available, Is.False);
        summary.Remove("cpuPercent"); trend[0].Metrics.Clear();
        Assert.That(aggregate.Summary()["cpuPercent"].Average, Is.EqualTo(21));
        Assert.That(aggregate.Trend(2)[0].Metrics["cpuPercent"].Average, Is.EqualTo(11));
        Assert.That(JsonSerializer.Serialize(sample), Is.EqualTo(beforeSample));
        Assert.That(JsonSerializer.Serialize(process), Is.EqualTo(beforeProcess));
    }
}
