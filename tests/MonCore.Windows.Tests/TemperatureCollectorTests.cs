using System.Diagnostics;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class TemperatureCollectorTests
{
    [TestCase(0, null)]
    [TestCase(9, null)]
    [TestCase(10, 1)]
    [TestCase(1500, 150)]
    [TestCase(1501, null)]
    public void GpuThermalValidation_TreatsZeroAndOutOfRangeTemperaturesAsMissing(int tenths, double? expected)
    {
        Assert.That(WindowsGpuThermals.Temperature((uint)tenths), Is.EqualTo(expected));
    }

    [Test]
    public void GpuThermalValidation_OnlyReportsStoppedFanWhenDriverSuppliesAMaximum()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsGpuThermals.Fan(0, null), Is.Null);
            Assert.That(WindowsGpuThermals.Fan(0, 3600), Is.Zero);
            Assert.That(WindowsGpuThermals.Percent(0), Is.Null);
            Assert.That(WindowsGpuThermals.Percent(1001), Is.EqualTo(100.1));
            Assert.That(WindowsGpuThermals.Percent(203), Is.EqualTo(20.3));
        });
    }

    [Test]
    public void ThermalZones_ConvertKelvinAndRejectValuesOutsideThePortableRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsThermalZones.ToCelsius(3_001, true), Is.EqualTo(26.95).Within(0.001));
            Assert.That(WindowsThermalZones.ToCelsius(300, false), Is.EqualTo(26.85).Within(0.001));
            Assert.That(WindowsThermalZones.ToCelsius(0, true), Is.Null);
            Assert.That(WindowsThermalZones.ToCelsius(4_240, true), Is.Null);
        });
    }

    [Test]
    public void ThermalZones_MarksOnlyHighPrecisionZoneAfterTenMinutesOfVariationSinceValueChange()
    {
        long ticks = 0;
        var zones = new WindowsThermalZones(() => ticks);
        var raw = new[] { new ThermalZoneRaw("zone", 3000, true) };
        Assert.That(zones.Read(raw, 10, false)[0].Unchanging, Is.False);
        ticks += 5 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(raw, 40, false)[0].Unchanging, Is.False);
        ticks += 5 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(raw, 40, false)[0].Unchanging, Is.True);
        Assert.That(zones.Read([new ThermalZoneRaw("zone", 3010, true)], 40, false)[0].Unchanging, Is.False);
    }

    [Test]
    public void ThermalZones_GapMissingReadingAndWholeKelvinResetUnchangingContinuity()
    {
        long ticks = 0;
        var zones = new WindowsThermalZones(() => ticks);
        var high = new[] { new ThermalZoneRaw("zone", 3000, true) };
        zones.Read(high, 10, false); ticks += 10 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(high, 40, false)[0].Unchanging, Is.True);
        zones.Read([], 40, false); ticks += 10 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(high, 40, false)[0].Unchanging, Is.False);
        Assert.That(zones.Read([new ThermalZoneRaw("coarse", 300, false)], 40, false)[0].Unchanging, Is.False);
        zones.Read(high, 40, true); ticks += 10 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(high, 40, false)[0].Unchanging, Is.False);
    }

    [Test]
    public void ThermalZones_RequiresMoreThanTwentyValidCpuPointsOfVariation()
    {
        long ticks = 0;
        var zones = new WindowsThermalZones(() => ticks);
        var raw = new[] { new ThermalZoneRaw("zone", 3000, true) };
        zones.Read(raw, double.NaN, false);
        ticks += 10 * 60 * Stopwatch.Frequency;
        Assert.That(zones.Read(raw, 120, false)[0].Unchanging, Is.False);
        Assert.That(zones.Read(raw, 10, false)[0].Unchanging, Is.False);
        Assert.That(zones.Read(raw, 30, false)[0].Unchanging, Is.False);
        Assert.That(zones.Read(raw, 31, false)[0].Unchanging, Is.True);
    }

    [Test]
    public void MemoryPressure_ThermalZoneSelectionPrefersHighPrecisionPerZone()
    {
        var selected = MemoryPressureCounter.SelectThermalZones(
            [new ThermalZoneRaw("a", 3000, true), new ThermalZoneRaw("b", 3010, true)],
            [new ThermalZoneRaw("b", 302, false), new ThermalZoneRaw("c", 303, false)]);
        Assert.That(selected, Is.EqualTo(new[]
        {
            new ThermalZoneRaw("a", 3000, true), new ThermalZoneRaw("b", 3010, true),
            new ThermalZoneRaw("c", 303, false)
        }));
    }

    [Test]
    public void MemoryPressure_PerformanceLimitWarmsUpAndRejectsInvalidValuesAndNonIntegralFlags()
    {
        long ticks = 0;
        using var counter = new MemoryPressureCounter(new FakePdh(
            new MemoryPressureRaw(1, 1, 1, 1, 1, 1, 100, 100, 2),
            new MemoryPressureRaw(1, 1, 1, 1, 1, 1, 100, 62, 2),
            new MemoryPressureRaw(1, 1, 1, 1, 1, 1, 100, 101, 2),
            new MemoryPressureRaw(1, 1, 1, 1, 1, 1, 100, double.NaN, 2.5),
            new MemoryPressureRaw(1, 1, 1, 1, 1, 1, 100, 80, uint.MaxValue)),
            () => ticks += Stopwatch.Frequency);

        var cold = counter.Read(); var limited = counter.Read(); var aboveRange = counter.Read();
        var invalid = counter.Read(); var flags = counter.Read();
        Assert.Multiple(() =>
        {
            Assert.That(cold.ProcessorPerformanceLimitPercent, Is.Null);
            Assert.That(limited.ProcessorPerformanceLimitPercent, Is.EqualTo(62));
            Assert.That(limited.ProcessorPerformanceLimitFlags, Is.EqualTo(2));
            Assert.That(aboveRange.ProcessorPerformanceLimitPercent, Is.Null);
            Assert.That(invalid.ProcessorPerformanceLimitPercent, Is.Null);
            Assert.That(invalid.ProcessorPerformanceLimitFlags, Is.Null);
            Assert.That(flags.ProcessorPerformanceLimitFlags, Is.EqualTo(uint.MaxValue));
        });
    }

    [Test]
    public void ThermalZones_PrecisionChangesRestartTheUnchangingObservationWindow()
    {
        long ticks = 0;
        using var zones = new WindowsThermalZones(() => ticks);
        var high = new[] { new ThermalZoneRaw("zone", 3000, true) };
        zones.Read(high, 10, false);
        ticks += 600 * Stopwatch.Frequency;
        Assert.That(zones.Read(high, 40, false)[0].Unchanging, Is.True);
        Assert.That(zones.Read([new("zone", 300, false)], 10, false)[0].Unchanging, Is.False);
        Assert.That(zones.Read(high, 40, false)[0].Unchanging, Is.False,
            "A coarse interval cannot establish ten minutes of unchanged high-precision readings.");
    }

    [Test]
    public void MemoryPressure_InvalidHighPrecisionFallsBackToTheValidCoarseReading()
    {
        var validCoarse = new ThermalZoneRaw("zone", 300, false);
        Assert.That(MemoryPressureCounter.SelectThermalZones([new("zone", 0, true)], [validCoarse]),
            Is.EqualTo(new[] { validCoarse }));
    }

    [Test]
    public void MemoryPressure_MissingOptionalTemperatureAndLimitCountersPreserveMemoryAndClockReadings()
    {
        long ticks = 0;
        var availableMemory = new MemoryPressureRaw(4, 10, 20, 30, 40, 50, 128);
        using var counter = new MemoryPressureCounter(new FakePdh(availableMemory),
            () => ticks += Stopwatch.Frequency);
        counter.Read();
        var reading = counter.Read();
        Assert.Multiple(() =>
        {
            Assert.That(reading.PagingReadsPerSecond, Is.EqualTo(4));
            Assert.That(reading.StandbyBytes, Is.EqualTo(60));
            Assert.That(reading.ModifiedBytes, Is.EqualTo(40));
            Assert.That(reading.FreeBytes, Is.EqualTo(50));
            Assert.That(reading.ProcessorPerformancePercent, Is.EqualTo(128));
            Assert.That(reading.ProcessorPerformanceLimitPercent, Is.Null);
            Assert.That(reading.ProcessorPerformanceLimitFlags, Is.Null);
            Assert.That(reading.ThermalZones, Is.Empty);
        });
    }

    private sealed class FakePdh(params MemoryPressureRaw[] readings) : IMemoryPressurePdh
    {
        private int _index;
        public MemoryPressureRaw Collect() => readings[Math.Min(_index++, readings.Length - 1)];
        public void Reset() { }
        public void Dispose() { }
    }
}
