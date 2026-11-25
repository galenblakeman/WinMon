using System.Diagnostics;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ResourceSamplingTests
{
    [Test]
    public void MemoryPressure_FirstRateIsUnavailableAndStandbyRequiresEveryList()
    {
        var ticks = 0L;
        using var counter = new MemoryPressureCounter(new FakeMemoryPressurePdh(
            new(12.5, 10, 20, null, 30, 40),
            new(18.5, 10, 20, 30, 31, 41)), () => ticks += Stopwatch.Frequency);

        var first = counter.Read();
        var second = counter.Read();

        Assert.Multiple(() =>
        {
            Assert.That(first.PagingReadsPerSecond, Is.Null);
            Assert.That(first.StandbyBytes, Is.Null, "A partial standby list is unavailable, never a partial sum.");
            Assert.That(first.ModifiedBytes, Is.EqualTo(30));
            Assert.That(first.FreeBytes, Is.EqualTo(40));
            Assert.That(second.PagingReadsPerSecond, Is.EqualTo(18.5));
            Assert.That(second.StandbyBytes, Is.EqualTo(60));
        });
    }

    [Test]
    public void MemoryPressure_RejectsStandbyOverflow()
    {
        using var counter = new MemoryPressureCounter(new FakeMemoryPressurePdh(
            new MemoryPressureRaw(1, ulong.MaxValue, 1, 1, 4, 5)), Stopwatch.GetTimestamp);

        Assert.That(counter.Read().StandbyBytes, Is.Null);
    }

    [Test]
    public void MemoryPressure_ResetMakesNextRateUnavailableAndCompressionUsesKnownSampleNamesOnly()
    {
        var ticks = 0L;
        using var counter = new MemoryPressureCounter(new FakeMemoryPressurePdh(
            new(4, 1, 2, 3, 4, 5), new(6, 1, 2, 3, 4, 5), new(8, 1, 2, 3, 4, 5)),
            () => ticks += Stopwatch.Frequency);
        _ = counter.Read();
        var beforeReset = counter.Read();
        counter.Reset();
        var afterReset = counter.Read();
        var resources = WindowsResourceSampler.ApplyMemoryPressure(new ResourceSample(), beforeReset,
            [new ProcessUsage(1, 1, "MemCompression", 0, 123), new ProcessUsage(2, 2, "compression helper", 0, 456)]);
        var displayName = WindowsResourceSampler.ApplyMemoryPressure(new ResourceSample(), beforeReset,
            [new ProcessUsage(3, 3, "Memory Compression", 0, 789)]);
        var absent = WindowsResourceSampler.ApplyMemoryPressure(new ResourceSample(), beforeReset,
            [new ProcessUsage(2, 2, "compression helper", 0, 456)]);

        Assert.Multiple(() =>
        {
            Assert.That(beforeReset.PagingReadsPerSecond, Is.EqualTo(6));
            Assert.That(afterReset.PagingReadsPerSecond, Is.Null);
            Assert.That(resources.CompressionStoreBytes, Is.EqualTo(123));
            Assert.That(displayName.CompressionStoreBytes, Is.EqualTo(789));
            Assert.That(absent.CompressionStoreBytes, Is.Null);
        });
    }

    [Test]
    public void MemoryPressure_FailedCollectionAndInvalidRateDoNotCarryABaseline()
    {
        var ticks = 0L;
        using var counter = new MemoryPressureCounter(new FakeMemoryPressurePdh(
            new(4, 1, 2, 3, 4, 5),
            new(6, 1, 2, 3, 4, 5),
            default,
            new(8, 1, 2, 3, 4, 5),
            new(10, 1, 2, 3, 4, 5)), () => ticks += Stopwatch.Frequency);

        var warmup = counter.Read();
        var valid = counter.Read();
        var failed = counter.Read();
        var recovered = counter.Read();
        var resumed = counter.Read();

        Assert.Multiple(() =>
        {
            Assert.That(warmup.PagingReadsPerSecond, Is.Null);
            Assert.That(valid.PagingReadsPerSecond, Is.EqualTo(6));
            Assert.That(failed.PagingReadsPerSecond, Is.Null);
            Assert.That(recovered.PagingReadsPerSecond, Is.Null, "The query recovery needs a fresh baseline.");
            Assert.That(resumed.PagingReadsPerSecond, Is.EqualTo(10));
        });
    }

    [TestCase(double.NaN)]
    [TestCase(-1d)]
    [TestCase(double.PositiveInfinity)]
    public void MemoryPressure_RejectsInvalidRatesEvenWhenWarm(double invalidRate)
    {
        var ticks = 0L;
        using var counter = new MemoryPressureCounter(new FakeMemoryPressurePdh(
            new(4, 1, 2, 3, 4, 5), new(6, 1, 2, 3, 4, 5), new(invalidRate, 1, 2, 3, 4, 5)),
            () => ticks += Stopwatch.Frequency);

        _ = counter.Read();
        _ = counter.Read();

        Assert.That(counter.Read().PagingReadsPerSecond, Is.Null);
    }
    [Test]
    public void Memory_UsesPhysicalUsedOverTotal_AndDistinguishesMissingFromZero()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ResourceSample { TotalMemoryBytes = 1000, UsedMemoryBytes = 620 }.MemoryPercent,
                Is.EqualTo(62));
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
    public void Rates_UseElapsedSeconds_AndRejectGaps(double seconds, double? expected)
    {
        Assert.That(ResourceRates.PerSecond(100, 200, seconds), Is.EqualTo(expected));
    }

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
        var resource = new ResourceSample
            { LinkBitsPerSecond = 1_000_000_000, ReceiveBytesPerSecond = 75_000_000, SendBytesPerSecond = 50_000_000 };
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
    public void Network_TotalsResetOnAdapterChangeOrCounterRegression()
    {
        var start = new WindowsResourceSampler.NetworkObservation("a", 1000, 1, 100, 200);
        var current = start with { Received = 300, Sent = 500 };
        var totals = WindowsResourceSampler.NetworkTotals(start, current);
        var adapter = WindowsResourceSampler.NetworkTotals(totals.Start, current with { Id = "b" });
        var regression = WindowsResourceSampler.NetworkTotals(start, current with { Received = 50 });
        var speed = WindowsResourceSampler.NetworkTotals(totals.Start, current with { Speed = 2000 });
        Assert.Multiple(() =>
        {
            Assert.That(totals.Received, Is.EqualTo(200));
            Assert.That(totals.Sent, Is.EqualTo(300));
            Assert.That(speed.Received, Is.EqualTo(200), "A Wi-Fi link-rate change is the same adapter.");
            Assert.That(speed.Start, Is.SameAs(start));
            Assert.That(adapter.Received, Is.Zero);
            Assert.That(adapter.Sent, Is.Zero);
            Assert.That(regression.Received, Is.Zero);
            Assert.That(regression.Sent, Is.Zero);
        });
    }

    [Test]
    public void Network_ManualInterfaceNeverFallsBackAndAutomaticSelectionStaysOnItsConnectedAdapter()
    {
        WindowsResourceSampler.NetworkInterfaceChoice[] candidates =
        [
            new("ethernet", true, 1_000_000_000),
            new("wi-fi", false, 2_000_000_000)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(WindowsResourceSampler.SelectNetworkId(candidates, "wi-fi", null), Is.EqualTo("wi-fi"));
            Assert.That(WindowsResourceSampler.SelectNetworkId(candidates, null, null), Is.EqualTo("ethernet"));
            Assert.That(WindowsResourceSampler.SelectNetworkId(candidates, "ethernet", "wi-fi"), Is.EqualTo("wi-fi"));
            Assert.That(WindowsResourceSampler.SelectNetworkId(candidates, null, "WI-FI"), Is.EqualTo("wi-fi"));
            Assert.That(WindowsResourceSampler.SelectNetworkId(candidates, "ethernet", "missing"), Is.Null);
        });
    }

    [Test]
    public void Network_AutomaticSelectionImmediatelyFallsBackWhenPreviousAdapterDisconnects()
    {
        WindowsResourceSampler.NetworkInterfaceChoice[] remaining =
        [
            new("ethernet", true, 1_000_000_000),
            new("mobile", false, 100_000_000)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(WindowsResourceSampler.SelectNetworkId(remaining, "wi-fi", null), Is.EqualTo("ethernet"));
            Assert.That(WindowsResourceSampler.SelectNetworkId(remaining, "wi-fi", "wi-fi"), Is.Null,
                "A disconnected manual choice must remain unavailable.");
            Assert.That(WindowsResourceSampler.SelectNetworkId([], "wi-fi", null), Is.Null);
        });
    }

    [Test]
    public void History_RetainsResourceValuesAndProcessMeasurementsForSelectedTimestamp()
    {
        var resource = new ResourceSample { TotalMemoryBytes = 1000, UsedMemoryBytes = 500, DiskActivePercent = 30 };
        var first = new MonitorSample(DateTimeOffset.UnixEpoch, 10, [new ProcessUsage(1, 1, "a", 5, 200, 300)], 0)
            { Resources = resource };
        var history = new MonitorHistory();
        history.Add(first);
        history.Add(first with
        {
            Timestamp = first.Timestamp.AddSeconds(1), Resources = resource with { UsedMemoryBytes = 800 }
        });
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
    public void Preferences_SeriesMaskAllowsAllHiddenAndRejectsUnknownBits(int value, int expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { VisibleMetrics = value }).VisibleMetrics,
            Is.EqualTo(expected));
    }

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

    private sealed class FakeMemoryPressurePdh(params MemoryPressureRaw[] readings) : IMemoryPressurePdh
    {
        private int _index;
        public MemoryPressureRaw Collect() => readings[Math.Min(_index++, readings.Length - 1)];
        public void Reset() { }
        public void Dispose() { }
    }
}
