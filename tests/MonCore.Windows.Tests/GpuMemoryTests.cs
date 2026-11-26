using Win32Exception = System.ComponentModel.Win32Exception;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

public sealed class GpuMemoryTests
{
    private const string Adapter = "0x00000000_0x00001234/0";
    private const string Instance = "luid_0x00000000_0x00001234_phys_0";

    [Test]
    public void SharedReadFailure_PreservesDedicatedMemoryAndRecoversOnTheSameQuery()
    {
        var counters = new FakeCounters { FailShared = true };
        var opened = 0;
        using var memory = CreateMemory(() => { opened++; return counters; });
        var input = new ResourceSample { GpuId = Adapter };
        var failed = memory.Attach(input);
        counters.FailShared = false;
        var recovered = memory.Attach(input);

        Assert.Multiple(() =>
        {
            Assert.That(failed.GpuMemoryUsedBytes, Is.EqualTo(200));
            Assert.That(failed.GpuMemoryTotalBytes, Is.EqualTo(1000));
            Assert.That(failed.GpuSharedMemoryUsedBytes, Is.Null);
            Assert.That(recovered.GpuSharedMemoryUsedBytes, Is.EqualTo(300));
            Assert.That(counters.SharedReads, Is.EqualTo(2));
            Assert.That(counters.Collections, Is.EqualTo(2));
            Assert.That(opened, Is.EqualTo(1), "A transient optional read must not replace the query.");
            Assert.That(counters.Disposals, Is.Zero);
        });
        memory.Dispose();
        memory.Dispose();
        Assert.That(counters.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void MissingSharedCounter_PreservesDedicatedMemoryWithoutReportingZero()
    {
        var counters = new FakeCounters { MissingShared = true };
        using var memory = CreateMemory(() => counters);
        var resources = memory.Attach(new ResourceSample { GpuId = Adapter });
        Assert.Multiple(() =>
        {
            Assert.That(resources.GpuMemoryUsedBytes, Is.EqualTo(200));
            Assert.That(resources.GpuSharedMemoryUsedBytes, Is.Null);
        });
    }

    [Test]
    public void DedicatedReadFailure_DisposesTheQueryAndReopensOnTheNextSample()
    {
        var failedCounters = new FakeCounters { FailDedicated = true };
        var recoveredCounters = new FakeCounters();
        var opened = 0;
        using var memory = CreateMemory(() => ++opened == 1 ? failedCounters : recoveredCounters);
        var input = new ResourceSample { GpuId = Adapter };
        var failed = memory.Attach(input);
        var recovered = memory.Attach(input);
        Assert.Multiple(() =>
        {
            Assert.That(failed.GpuMemoryUsedBytes, Is.Null);
            Assert.That(failed.GpuSharedMemoryUsedBytes, Is.Null);
            Assert.That(failedCounters.Disposals, Is.EqualTo(1));
            Assert.That(opened, Is.EqualTo(2));
            Assert.That(recovered.GpuMemoryUsedBytes, Is.EqualTo(200));
            Assert.That(recovered.GpuSharedMemoryUsedBytes, Is.EqualTo(300));
        });
    }

    private static WindowsGpuMemory CreateMemory(Func<WindowsGpuMemory.ICounters> open) => new(open, names =>
    {
        names[Adapter] = "Test GPU";
        return new Dictionary<string, ulong> { [Adapter] = 1000 };
    });

    private sealed class FakeCounters : WindowsGpuMemory.ICounters
    {
        public bool FailShared { get; set; }
        public bool MissingShared { get; init; }
        public bool FailDedicated { get; init; }
        public int SharedReads { get; private set; }
        public int Collections { get; private set; }
        public int Disposals { get; private set; }
        public void Collect() => Collections++;
        public WindowsGpuSampler.EngineCounter[] ReadDedicated() => FailDedicated
            ? throw new Win32Exception(13) : [new(Instance, 200)];
        public WindowsGpuSampler.EngineCounter[] ReadShared()
        {
            SharedReads++;
            if (FailShared) throw new Win32Exception(13);
            return MissingShared ? [] : [new(Instance, 300)];
        }
        public void Dispose() => Disposals++;
    }

    [Test]
    public void Usage_MatchesTheSelectedAdapterAndPhysicalGpu()
    {
        WindowsGpuSampler.EngineCounter[] counters =
        [
            new("luid_0x00000000_0x00005678_phys_0", 700), new(Instance, 200),
            new("luid_0x00000000_0x00001234_phys_1", 900)
        ];
        Assert.That(WindowsGpuMemory.SelectUsage(counters, Adapter), Is.EqualTo(200));
    }

    [Test]
    public void Usage_PreservesValidZeroAndRejectsMissingInvalidOrDuplicateReadings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsGpuMemory.SelectUsage([new(Instance, 0)], Adapter), Is.Zero);
            Assert.That(WindowsGpuMemory.SelectUsage([], Adapter), Is.Null);
            Assert.That(WindowsGpuMemory.SelectUsage([new(Instance, 1, 2)], Adapter), Is.Null);
            Assert.That(WindowsGpuMemory.SelectUsage([new(Instance, double.NaN)], Adapter), Is.Null);
            Assert.That(WindowsGpuMemory.SelectUsage([new(Instance, -1)], Adapter), Is.Null);
            Assert.That(WindowsGpuMemory.SelectUsage([new(Instance, 1), new(Instance, 2)], Adapter), Is.Null);
        });
    }

    [Test]
    [Category("Integration")]
    public async Task NativeMemory_ReadsCapacityAndUsageForTheUtilizationAdapter()
    {
        foreach (var (id, bytes) in WindowsGpuMemory.ReadCapacities())
            TestContext.Out.WriteLine($"DXGI {id}: {bytes} bytes dedicated memory");
        using var sampler = new WindowsGpuSampler();
        var sample = new MonitorSample(DateTimeOffset.Now, 0, [], 0);
        sampler.Sample(sample, false);
        await Task.Delay(1100);
        var resources = sampler.Sample(sample, false).Resources;
        TestContext.Out.WriteLine(
            $"GPU {resources.GpuId}: {resources.GpuMemoryUsedBytes?.ToString() ?? "unavailable"} / {resources.GpuMemoryTotalBytes?.ToString() ?? "unavailable"} bytes");
        Assert.That(resources.GpuMemoryUsedBytes.HasValue, Is.EqualTo(resources.GpuMemoryTotalBytes.HasValue));
        if (resources.GpuMemoryTotalBytes is { } total)
        {
            Assert.That(total, Is.GreaterThan(0));
            Assert.That(resources.GpuName, Is.Not.Empty);
        }
    }
}
