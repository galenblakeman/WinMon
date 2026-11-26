using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ThermalHistoryTests
{
    private static readonly Guid Generation = Guid.Parse("cf46c1ba-7949-4bec-8675-698ac6dd3ef0");
    private static ResourceSample Resources(double temperature = 85) => new()
    {
        CpuTemperature = new(Generation, "cpu/0", "CPU", "package", "CPU Package", temperature, "LibreHardwareMonitorLib 0.9.6"),
        CpuPerformanceLimitPercent = 62, CpuPerformanceLimitFlags = 2,
        GpuId = "gpu/0", GpuName = "GPU",
        GpuThermals = [new("gpu/0", "GPU", temperature, 0, 30, 85, 100)],
        DiskName = "C:", DiskTemperatureCelsius = temperature,
        SecondaryDiskName = "D:", SecondaryDiskTemperatureCelsius = temperature,
        ThermalZones = [new("zone", temperature, false)]
    };
    private static MonitorSample Sample(int second, ResourceSample? resources = null) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(second), null, [], 0, false, second * Stopwatch.Frequency)
    { Resources = resources ?? Resources(), IntervalSeconds = 1 };

    [TestCase(CaptureMetric.CpuTemperature)]
    [TestCase(CaptureMetric.GpuTemperature)]
    [TestCase(CaptureMetric.PrimaryDiskTemperature)]
    [TestCase(CaptureMetric.SecondaryDiskTemperature)]
    public void Capture_TemperaturesFireAtInclusiveThresholdWithoutCpuUtilization(CaptureMetric metric)
    {
        var rules = new CaptureRules([new(metric, true, 85, 2)], TimeSpan.Zero, 1);
        Assert.That(rules.Observe(Sample(1, Resources(84.99))), Is.Empty);
        Assert.That(rules.Observe(Sample(2)), Is.Empty);
        var fired = rules.Observe(Sample(3, Resources(90))).Single();
        Assert.That(fired.Rule.Metric, Is.EqualTo(metric));
        Assert.That(fired.PeakValue, Is.EqualTo(90));
        Assert.That(fired.Run, Is.EqualTo(new[] { 85d, 90d }));
    }

    [TestCase(CaptureMetric.CpuPerformanceLimited)]
    [TestCase(CaptureMetric.GpuThermalLimited)]
    public void Capture_LimitsFireForDurationAndRearmAfterRecovery(CaptureMetric metric)
    {
        var rules = new CaptureRules([new(metric, true, 1, 2)], TimeSpan.Zero, 1);
        Assert.That(rules.Observe(Sample(1)), Is.Empty);
        Assert.That(rules.Observe(Sample(2)), Has.Count.EqualTo(1));
        Assert.That(rules.Observe(Sample(3)), Is.Empty);
        var recovered = Resources(60) with { CpuPerformanceLimitPercent = 100 };
        Assert.That(rules.Observe(Sample(4, recovered)), Is.Empty);
        Assert.That(rules.Observe(Sample(5, recovered)), Is.Empty);
        Assert.That(rules.Observe(Sample(6)), Is.Empty);
        Assert.That(rules.Observe(Sample(7)), Has.Count.EqualTo(1));
    }

    [TestCase(CaptureMetric.CpuTemperature)]
    [TestCase(CaptureMetric.GpuTemperature)]
    [TestCase(CaptureMetric.PrimaryDiskTemperature)]
    [TestCase(CaptureMetric.SecondaryDiskTemperature)]
    [TestCase(CaptureMetric.GpuThermalLimited)]
    public void Capture_SensorIdentityChangesResetDuration(CaptureMetric metric)
    {
        var rules = new CaptureRules([new(metric, true, metric == CaptureMetric.GpuThermalLimited ? 1 : 85, 2)], TimeSpan.Zero, 1);
        Assert.That(rules.Observe(Sample(1)), Is.Empty);
        var r = Resources();
        var changed = r with
        {
            CpuTemperature = r.CpuTemperature! with { Generation = Guid.NewGuid() },
            GpuId = "gpu/1", GpuThermals = [r.GpuThermals[0] with { AdapterId = "gpu/1" }],
            DiskName = "E:", SecondaryDiskName = "F:"
        };
        Assert.That(rules.Observe(Sample(2, changed)), Is.Empty);
        Assert.That(rules.Observe(Sample(3, changed)), Has.Count.EqualTo(1));
    }

    [TestCase("missing")]
    [TestCase("gap")]
    [TestCase("elapsed-gap")]
    public void Capture_MissingOrInterruptedSensorsResetDuration(string interruption)
    {
        var rules = new CaptureRules([new(CaptureMetric.CpuTemperature, true, 85, 2)], TimeSpan.Zero, 1);
        Assert.That(rules.Observe(Sample(1)), Is.Empty);
        if (interruption != "elapsed-gap")
            Assert.That(rules.Observe(Sample(2, new()) with { IsGap = interruption == "gap" }), Is.Empty);
        Assert.That(rules.Observe(Sample(5)), Is.Empty);
        Assert.That(rules.Observe(Sample(6)), Has.Count.EqualTo(1));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(150, true)]
    [TestCase(151, false)]
    [TestCase(double.NaN, false)]
    [TestCase(double.PositiveInfinity, false)]
    public void Projection_RejectsInvalidTemperatures(double value, bool valid)
    {
        Assert.That(ThermalMetrics.Value(CaptureMetric.CpuTemperature, Resources(value)), valid ? Is.EqualTo(value) : Is.Null);
        Assert.That(ThermalHistory.From(Resources(value)).Valid(), Is.EqualTo(valid));
    }

    [Test]
    public void HistoricalGpuAllowsUnavailableOrLongDriverDisplayNames()
    {
        var r = Resources();
        Assert.That(ThermalHistory.From(r with { GpuThermals = [r.GpuThermals[0] with { Name = "" }] }).Valid(), Is.True);
        Assert.That(ThermalHistory.From(r with { GpuThermals = [r.GpuThermals[0] with { Name = new string('x', 260) }] }).Valid(), Is.True);
        Assert.That(ThermalHistory.From(r with { GpuThermals = [r.GpuThermals[0] with { Name = new string('x', 1025) }] }).Valid(), Is.False);
    }

    [Test]
    public void Projection_PairsPhysicalGpuAndDoesNotInventThresholdOrCpuLimit()
    {
        Assert.That(ThermalMetrics.Value(CaptureMetric.GpuTemperature, Resources() with { GpuId = "gpu/1" }), Is.Null);
        var r = Resources();
        Assert.That(ThermalMetrics.Value(CaptureMetric.GpuThermalLimited,
            r with { GpuThermals = [r.GpuThermals[0] with { WarningCelsius = null }] }), Is.Null);
        Assert.That(ThermalMetrics.Value(CaptureMetric.CpuPerformanceLimited, r with { CpuPerformanceLimitPercent = null }), Is.Null);
        Assert.That(ThermalMetrics.Value(CaptureMetric.CpuPerformanceLimited, r with { CpuPerformanceLimitPercent = 99.99 }), Is.EqualTo(1));
    }

    [Test]
    public void Trends_RetainEveryTemperatureAndLimitPeakThroughSaveAndRestore()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1, Resources(60) with { CpuPerformanceLimitPercent = 100 }));
        archive.Add(Sample(2, Resources(90)));
        archive.Add(Sample(3, Resources(65) with { CpuPerformanceLimitPercent = 100 }));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gz");
        try
        {
            ResourceTrendArchive.SaveAsync(path, archive.Snapshot()).GetAwaiter().GetResult();
            var restored = ResourceTrendArchive.LoadAsync(path).GetAwaiter().GetResult();
            var values = restored.Snapshot().Buckets.Single().Values;
            foreach (var metric in new[] { ResourceTrendMetric.CpuTemperature, ResourceTrendMetric.GpuTemperature,
                ResourceTrendMetric.PrimaryDiskTemperature, ResourceTrendMetric.SecondaryDiskTemperature })
            {
                Assert.That(values[metric].Average, Is.EqualTo(90), metric.ToString());
                Assert.That(values[metric].Minimum, Is.EqualTo(60));
                Assert.That(values[metric].Maximum, Is.EqualTo(90));
                Assert.That(values[metric].ObservedSeconds, Is.EqualTo(3));
            }
            Assert.That(values[ResourceTrendMetric.CpuPerformanceLimited].Average, Is.EqualTo(1));
            Assert.That(values[ResourceTrendMetric.GpuThermalLimited].Average, Is.EqualTo(1));
            Assert.That(restored.Snapshot().SchemaVersion, Is.EqualTo(2));
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void Trends_ChangedCpuGenerationMakesMixedMinuteUnavailable()
    {
        var archive = new ResourceTrendArchive();
        var first = Resources(60);
        archive.Add(Sample(1, first));
        archive.Add(Sample(2, first with { CpuTemperature = first.CpuTemperature! with { Generation = Guid.NewGuid(), Celsius = 90 } }));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.CpuTemperature];
        Assert.That(value.Break, Is.EqualTo(ResourceTrendBreak.ResourceChanged));
        Assert.That(value.Average, Is.Null);
    }

    [Test]
    public void Trends_MissingSensorsDoNotCreateZeroOrCoverage()
    {
        var archive = new ResourceTrendArchive();
        archive.Add(Sample(1));
        archive.Add(Sample(2, new()));
        var value = archive.Snapshot().Buckets.Single().Values[ResourceTrendMetric.CpuTemperature];
        Assert.That(value.ObservedSeconds, Is.EqualTo(1));
        Assert.That(value.Average, Is.EqualTo(85));
        Assert.That(ResourceTrendArchive.Restore(new(1, DateTimeOffset.UnixEpoch, [])).Snapshot().Buckets, Is.Empty);
    }

    [Test]
    public void Incident_RoundTripsAllThermalFieldsWithoutProcessAttribution()
    {
        var sample = Sample(1);
        var recording = new IncidentRecording(3, Stopwatch.Frequency, [new(sample, null, [], null, null)], CaptureMetric.CpuTemperature, 0);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wmincident");
        try
        {
            recording.Save(path);
            var loaded = IncidentRecording.Load(path);
            Assert.That(JsonSerializer.Serialize(ThermalHistory.From(loaded.Observations[0].Sample.Resources)),
                Is.EqualTo(JsonSerializer.Serialize(ThermalHistory.From(sample.Resources))));
            Assert.That(loaded.Metric, Is.EqualTo(CaptureMetric.CpuTemperature));
            Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.CpuTemperature), Is.Empty);
            Assert.That(IncidentTimeline.Compare([sample], sample, CaptureMetric.CpuTemperature).Processes, Is.Empty);
        }
        finally { File.Delete(path); }
    }

    [TestCase("temperature")]
    [TestCase("cpu-limit")]
    [TestCase("null-zones")]
    [TestCase("too-many-zones")]
    [TestCase("duplicate-gpu")]
    [TestCase("sensor-name")]
    public void Incident_RejectsMalformedHistoricalSensors(string invalid)
    {
        var r = Resources();
        r = invalid switch
        {
            "temperature" => r with { DiskTemperatureCelsius = 151 },
            "cpu-limit" => r with { CpuPerformanceLimitPercent = 101 },
            "null-zones" => r with { ThermalZones = null! },
            "too-many-zones" => r with { ThermalZones = Enumerable.Range(0, 65).Select(i => new ThermalZoneReading(i.ToString(), 50, false)).ToArray() },
            "duplicate-gpu" => r with { GpuThermals = [r.GpuThermals[0], r.GpuThermals[0]] },
            _ => r with { CpuTemperature = r.CpuTemperature! with { SensorName = new string('x', 257) } }
        };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wmincident");
        var recording = new IncidentRecording(3, Stopwatch.Frequency, [new(Sample(1, r), null, [], null, null)], CaptureMetric.CpuTemperature, 0);
        using (var file = File.Create(path))
        using (var compressed = new GZipStream(file, CompressionMode.Compress)) JsonSerializer.Serialize(compressed, recording);
        try { Assert.Throws<InvalidDataException>(() => IncidentRecording.Load(path)); }
        finally { File.Delete(path); }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void Incident_LegacyFilesWithoutSensorPropertiesRemainReadable(int version)
    {
        var recording = new IncidentRecording(version, Stopwatch.Frequency,
            [new(Sample(1, new()), null, [], null, null)], CaptureMetric.Cpu, 0);
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(recording))!;
        var resources = json["Observations"]![0]!["Sample"]!["Resources"]!.AsObject();
        foreach (var key in new[] { "CpuTemperature", "CpuPerformanceLimitPercent", "CpuPerformanceLimitFlags", "GpuThermals", "ThermalZones", "DiskTemperatureCelsius", "SecondaryDiskTemperatureCelsius" })
            resources.Remove(key);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wmincident");
        using (var file = File.Create(path))
        using (var compressed = new GZipStream(file, CompressionMode.Compress)) JsonSerializer.Serialize(compressed, json);
        try
        {
            var loaded = IncidentRecording.Load(path).Observations.Single().Sample.Resources;
            Assert.That(loaded.CpuTemperature, Is.Null);
            Assert.That(loaded.CpuPerformanceLimitPercent, Is.Null);
            Assert.That(loaded.GpuThermals, Is.Empty);
            Assert.That(loaded.ThermalZones, Is.Empty);
        }
        finally { File.Delete(path); }
    }
}
