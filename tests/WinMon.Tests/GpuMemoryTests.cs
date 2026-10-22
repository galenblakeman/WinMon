using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed class GpuMemoryTests
{
    private const string Adapter = "0x00000000_0x00001234/0";
    private const string Instance = "luid_0x00000000_0x00001234_phys_0";

    [Test]
    public void Usage_MatchesTheSelectedAdapterAndPhysicalGpu()
    {
        WindowsGpuSampler.EngineCounter[] counters =
        [new("luid_0x00000000_0x00005678_phys_0", 700), new(Instance, 200),
            new("luid_0x00000000_0x00001234_phys_1", 900)];
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
    public async Task NativeMemory_ReadsCapacityAndUsageForTheUtilizationAdapter()
    {
        foreach (var (id, bytes) in WindowsGpuMemory.ReadCapacities())
            TestContext.Out.WriteLine($"DXGI {id}: {bytes} bytes dedicated memory");
        using var sampler = new WindowsGpuSampler();
        var sample = new CpuSample(DateTimeOffset.Now, 0, [], 0);
        sampler.Sample(sample, false);
        await Task.Delay(1100);
        var resources = sampler.Sample(sample, false).Resources;
        TestContext.Out.WriteLine($"GPU {resources.GpuId}: {resources.GpuMemoryUsedBytes?.ToString() ?? "unavailable"} / {resources.GpuMemoryTotalBytes?.ToString() ?? "unavailable"} bytes");
        Assert.That(resources.GpuMemoryUsedBytes.HasValue, Is.EqualTo(resources.GpuMemoryTotalBytes.HasValue));
        if (resources.GpuMemoryTotalBytes is { } total) Assert.That(total, Is.GreaterThan(0));
    }
}
