using System.Diagnostics;
using System.Runtime.InteropServices;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class GpuThermalLifetimeTests
{
    [Test]
    public void GraphicsClock_UsesTheRequestedPhysicalAdapterAndDropsFailedReadings()
    {
        var api = new FakeApi();
        using var collector = new WindowsGpuThermals(api, () => 1);
        collector.Read();
        const string adapter = "0x00000000_0x0000002a/1";

        Assert.That(collector.ReadGraphicsClock(adapter, 3), Is.EqualTo(1860d));
        Assert.That(api.ClockTarget, Is.EqualTo((7u, 3, 1)));
        Assert.That(collector.ReadGraphicsClock("missing", 0), Is.Null);

        api.FailPerformance = true;
        Assert.That(collector.ReadGraphicsClock(adapter, 3), Is.Null);
        api.FailPerformance = false;
        api.ClockHertz = 0;
        Assert.That(collector.ReadGraphicsClock(adapter, 3), Is.Null);
    }

    [Test]
    public void Cache_ReusesHandlesAndCapsUntilThirtySecondsThenClosesThePreviousGeneration()
    {
        long now = 0;
        var api = new FakeApi();
        using var collector = new WindowsGpuThermals(api, () => now);
        var first = collector.Read();
        now = 30 * Stopwatch.Frequency - 1;
        var second = collector.Read();
        Assert.Multiple(() =>
        {
            Assert.That(api.EnumerationCalls, Is.EqualTo(2), "Sizing and filling happen only at initial discovery.");
            Assert.That(api.CapsCalls, Is.EqualTo(2), "Each linked physical GPU has its own capabilities.");
            Assert.That(api.PerformanceCalls, Is.EqualTo(4));
            Assert.That(api.Closed, Is.Empty);
            Assert.That(first.Select(reading => reading.AdapterId), Is.EqualTo(new[] { "0x00000000_0x0000002a/0", "0x00000000_0x0000002a/1" }));
            Assert.That(first.Select(reading => reading.Name), Is.All.EqualTo("Test GPU"));
            Assert.That(second.Select(reading => reading.TemperatureCelsius), Is.EqualTo(new double?[] { 61, 62 }));
        });
        now++;
        collector.Read();
        Assert.Multiple(() =>
        {
            Assert.That(api.EnumerationCalls, Is.EqualTo(4));
            Assert.That(api.Closed, Is.EqualTo(new uint[] { 7 }), "One native handle is shared by the two physical GPUs.");
        });
        collector.Reset();
        collector.Dispose();
        Assert.That(api.Closed, Is.EqualTo(new uint[] { 7, 7 }), "Reset closes the current handle once; Dispose is idempotent.");
    }

    [Test]
    public void FailedPerformanceQuery_DoesNotReuseThePreviousTemperature()
    {
        var api = new FakeApi();
        using var collector = new WindowsGpuThermals(api, () => 1);
        Assert.That(collector.Read(), Has.Count.EqualTo(2));
        api.FailPerformance = true;
        Assert.Multiple(() =>
        {
            Assert.That(collector.Read(), Is.Empty);
            Assert.That(collector.HasTemperature, Is.True, "Session capability records an earlier valid reading.");
            Assert.That(api.Closed, Is.Empty, "A transient read failure keeps the owned adapter until refresh or disposal.");
        });
    }

    [TestCase(0u)]
    [TestCase(33u)]
    public void InvalidPhysicalAdapterCount_ClosesTheOpenedHandleWithoutQueryingPerformance(uint count)
    {
        var api = new FakeApi { PhysicalCount = count };
        using var collector = new WindowsGpuThermals(api, () => 1);
        Assert.Multiple(() =>
        {
            Assert.That(collector.Read(), Is.Empty);
            Assert.That(api.Closed, Is.EqualTo(new uint[] { 7 }));
            Assert.That(api.PerformanceCalls, Is.Zero);
        });
    }

    [TestCase(0u)]
    [TestCase(129u)]
    public void InvalidEnumerationSize_NeverAllocatesOrReadsAnAdapter(uint count)
    {
        var api = new FakeApi { EnumeratedCount = count };
        using var collector = new WindowsGpuThermals(api, () => 1);
        Assert.Multiple(() =>
        {
            Assert.That(collector.Read(), Is.Empty);
            Assert.That(api.EnumerationCalls, Is.EqualTo(1));
            Assert.That(api.CapsCalls, Is.Zero);
            Assert.That(api.PerformanceCalls, Is.Zero);
        });
    }

    [Test]
    public void EnumerationFailure_DoesNotExposeOldReadingsAndRetriesAfterTheRefreshInterval()
    {
        long now = 1;
        var api = new FakeApi();
        using var collector = new WindowsGpuThermals(api, () => now);
        collector.Read();
        api.FailEnumeration = true;
        now += 30 * Stopwatch.Frequency;
        Assert.That(collector.Read(), Is.Empty);
        Assert.That(api.Closed, Is.EqualTo(new uint[] { 7 }));
        var calls = api.EnumerationCalls;
        collector.Read();
        Assert.That(api.EnumerationCalls, Is.EqualTo(calls));
        api.FailEnumeration = false;
        now += 30 * Stopwatch.Frequency;
        Assert.That(collector.Read(), Has.Count.EqualTo(2));
    }

    private sealed class FakeApi : WindowsGpuThermals.IApi
    {
        public uint PhysicalCount { get; init; } = 2;
        public uint EnumeratedCount { get; init; } = 1;
        public bool FailPerformance { get; set; }
        public bool FailEnumeration { get; set; }
        public int EnumerationCalls { get; private set; }
        public int CapsCalls { get; private set; }
        public int PerformanceCalls { get; private set; }
        public long ClockHertz { get; set; } = 1_860_000_000;
        public (uint Handle, int Node, int Physical) ClockTarget { get; private set; }
        public List<uint> Closed { get; } = [];

        public int Enumerate(ref WindowsGpuThermals.Enumeration adapters)
        {
            EnumerationCalls++;
            if (FailEnumeration) return unchecked((int)0xC000000D);
            adapters.Count = EnumeratedCount;
            if (adapters.Items != IntPtr.Zero)
                Marshal.StructureToPtr(new WindowsGpuThermals.Info { Handle = 7, LuidLow = 42 }, adapters.Items, false);
            return 0;
        }

        public int Query(ref WindowsGpuThermals.AdapterQuery query)
        {
            var physical = unchecked((uint)Marshal.ReadInt32(query.Data));
            switch (query.Type)
            {
                case 30:
                    Marshal.WriteInt32(query.Data, checked((int)PhysicalCount));
                    return 0;
                case 8:
                    Assert.That(query.Size, Is.EqualTo(4 * 260 * sizeof(char)), "The full native registry structure is required.");
                    var name = "Test GPU\0".ToCharArray();
                    Marshal.Copy(name, 0, query.Data, name.Length);
                    return 0;
                case 63:
                    CapsCalls++;
                    Marshal.StructureToPtr(new WindowsGpuThermals.Caps { Physical = physical, MaxFan = 3600, Warning = 830, Maximum = 880 }, query.Data, false);
                    return 0;
                case 62:
                    PerformanceCalls++;
                    if (FailPerformance) return unchecked((int)0xC000000D);
                    Marshal.StructureToPtr(new WindowsGpuThermals.Data { Physical = physical, Temperature = 610 + physical * 10, Power = 203 }, query.Data, false);
                    return 0;
                case 61:
                    Assert.That(query.Size, Is.EqualTo(56));
                    ClockTarget = (query.Handle, Marshal.ReadInt32(query.Data), Marshal.ReadInt32(query.Data, 4));
                    if (FailPerformance) return unchecked((int)0xC000000D);
                    Marshal.WriteInt64(query.Data, 8, ClockHertz);
                    return 0;
                default:
                    throw new AssertionException("Unexpected native query type.");
            }
        }

        public void Close(uint handle) => Closed.Add(handle);
    }
}
