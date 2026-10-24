using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ResourceSamplingTests
{
    [Test]
    public void Memory_UsesPhysicalUsedOverTotal_AndDistinguishesMissingFromZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ResourceSample { TotalMemoryBytes = 1000, UsedMemoryBytes = 620 }.MemoryPercent, Is.EqualTo(62));
            Assert.That(new ResourceSample { TotalMemoryBytes = 1000, UsedMemoryBytes = 0 }.MemoryPercent, Is.Zero);
            Assert.That(new ResourceSample().MemoryPercent, Is.Null);
            Assert.That(new ResourceSample { TotalMemoryBytes = 0, UsedMemoryBytes = 0 }.MemoryPercent, Is.Null);
        });
    }

    [TestCase(2, 50)]
    [TestCase(5, 20)]
    [TestCase(0, null)]
    [TestCase(-1, null)]
    [TestCase(6, null)]
    public void Rates_UseElapsedSeconds_AndRejectGaps(double seconds, double? expected) =>
        Assert.That(ResourceRates.PerSecond(100, 200, seconds), Is.EqualTo(expected));

    [Test]
    public void Rates_RejectMissingOrRegressingCounters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResourceRates.PerSecond(null, 200, 1), Is.Null);
            Assert.That(ResourceRates.PerSecond(200, 100, 1), Is.Null);
            Assert.That(ResourceRates.PerSecond(100, 100, 1), Is.Zero);
        });
    }

    [Test]
    public void Network_NormalizesBusierDirection_WithoutAddingFullDuplexDirections()
    {
        var resource = new ResourceSample { LinkBitsPerSecond = 1_000_000_000, ReceiveBytesPerSecond = 75_000_000, SendBytesPerSecond = 50_000_000 };
        Assert.Multiple(() =>
        {
            Assert.That(resource.NetworkPercent, Is.EqualTo(60));
            Assert.That((resource with { LinkBitsPerSecond = null }).NetworkPercent, Is.Null);
            Assert.That((resource with { SendBytesPerSecond = null }).NetworkPercent, Is.Null);
            Assert.That((resource with { ReceiveBytesPerSecond = 0, SendBytesPerSecond = 0 }).NetworkPercent, Is.Zero);
        });
    }

    [Test]
    public void Network_AdapterOrLinkSpeedChangeAndCounterReset_RequireNewBaseline()
    {
        var prior = new WindowsResourceSampler.NetworkObservation("a", 1000, Stopwatch.Frequency, 100, 200);
        var current = prior with { Timestamp = Stopwatch.Frequency * 2, Received = 300, Sent = 500 };
        Assert.Multiple(() =>
        {
            Assert.That(WindowsResourceSampler.NetworkRate(prior, current, true), Is.EqualTo(200));
            Assert.That(WindowsResourceSampler.NetworkRate(prior, current, false), Is.EqualTo(300));
            Assert.That(WindowsResourceSampler.NetworkRate(prior, current with { Id = "b" }, true), Is.Null);
            Assert.That(WindowsResourceSampler.NetworkRate(prior, current with { Speed = 2000 }, true), Is.Null);
            Assert.That(WindowsResourceSampler.NetworkRate(prior, current with { Received = 1 }, true), Is.Null);
            Assert.That(WindowsResourceSampler.NetworkRate(null, current, true), Is.Null);
        });
    }

    [Test]
    public void History_RetainsResourceValuesAndProcessMeasurementsForSelectedTimestamp()
    {
        var resource = new ResourceSample { TotalMemoryBytes = 1000, UsedMemoryBytes = 500, DiskActivePercent = 30 };
        var first = new MonitorSample(DateTimeOffset.UnixEpoch, 10, [new ProcessUsage(1, 1, "a", 5, 200, 300)], 0) { Resources = resource };
        var history = new MonitorHistory();
        history.Add(first);
        history.Add(first with { Timestamp = first.Timestamp.AddSeconds(1), Resources = resource with { UsedMemoryBytes = 800 } });
        Assert.Multiple(() =>
        {
            Assert.That(history.Samples[0].Resources.MemoryPercent, Is.EqualTo(50));
            Assert.That(history.Samples[0].Processes[0].IoBytesPerSecond, Is.EqualTo(300));
            Assert.That(CpuGraph.MetricValue(history.Samples[0], 2), Is.EqualTo(30));
            Assert.That(CpuGraph.MetricValue(history.Samples[0], 3), Is.Null);
            Assert.That(CpuGraph.DescribeSample(history.Samples[0]), Does.Contain("Memory 50.0%"));
        });
    }

    [TestCase(0, 0)]
    [TestCase(5, 5)]
    [TestCase(31, 15)]
    public void Preferences_SeriesMaskAllowsAllHiddenAndRejectsUnknownBits(int value, int expected) =>
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { VisibleMetrics = value }).VisibleMetrics, Is.EqualTo(expected));

    [Test]
    public void ProcessIo_UsesMonotonicElapsedAndLifetimeIdentity()
    {
        var provider = new SequenceProvider(
            new ProcessCpuTimes(1, 100, "a", 10, 1000, 100, 200),
            new ProcessCpuTimes(1, 100, "a", 30, 2000, 300, 600),
            new ProcessCpuTimes(1, 200, "new", 40, 3000, 900, 1000));
        using var sampler = new WindowsCpuSampler(provider);
        sampler.Sample();
        var usage = sampler.Sample().Processes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(usage.WorkingSetBytes, Is.EqualTo(2000));
            Assert.That(usage.IoBytesPerSecond, Is.EqualTo(300)); // 600 bytes / two seconds
            Assert.That(sampler.Sample().Processes, Is.Empty); // Reused PID
        });
    }

    private sealed class SequenceProvider(params ProcessCpuTimes[] processes) : ICpuSnapshotProvider
    {
        private int index;
        public CpuSnapshot Capture()
        {
            var process = processes[index++];
            return new CpuSnapshot(DateTimeOffset.UnixEpoch.AddSeconds(index * 2), Stopwatch.Frequency * index * 2,
                new SystemCpuTimes(index * 100, index * 1000, 0), [process], 0);
        }
    }
}
