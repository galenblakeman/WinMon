using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class SensorServiceProtocolTests
{
    private static SensorSnapshot Valid(long now) => new(Guid.NewGuid(), 1, now, Stopwatch.Frequency,
        SensorServiceQuality.Complete,
        new("/cpu/0", "CPU", "/cpu/0/temperature/0", "CPU Package", 52.5, "LibreHardwareMonitor", now));

    [Test]
    public async Task FanOnlySnapshot_RoundTripsZeroRpmAndDropsStaleOrInvalidReadings()
    {
        var now = 10 * Stopwatch.Frequency;
        var snapshot = Valid(now) with
        {
            Quality = SensorServiceQuality.Partial,
            CpuTemperature = null,
            Fans = [new HardwareFanReading("CPU Fan", 0, true), new HardwareFanReading("Chassis Fan", 1240, false)]
        };
        using var stream = new MemoryStream();
        await SensorServiceProtocol.WriteAsync(stream, snapshot, CancellationToken.None);
        stream.Position = 0;
        var received = await SensorServiceProtocol.ReadAsync<SensorSnapshot>(stream, CancellationToken.None);
        SensorSnapshotValidation.Validate(received);
        var invalid = received with { Fans = [new HardwareFanReading("Fan", -1, false)] };
        Assert.Multiple(() =>
        {
            Assert.That(SensorSnapshotValidation.GetFreshFans(received, now + 5 * Stopwatch.Frequency),
                Is.EqualTo(snapshot.Fans));
            Assert.That(SensorSnapshotValidation.GetFreshFans(received, now + 5 * Stopwatch.Frequency + 1), Is.Empty);
            Assert.That(SensorSnapshotValidation.GetFreshFans(received with { Fans = null }, now), Is.Empty);
            Assert.That(SensorSnapshotValidation.GetFreshFans(invalid, now), Is.Empty);
            Assert.That(() => SensorSnapshotValidation.Validate(invalid), Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void PackageWatts_UsesItsOwnFreshnessAndWorksWithoutTemperature()
    {
        var now = 10 * Stopwatch.Frequency;
        var boundary = now - 5 * Stopwatch.Frequency;
        var snapshot = Valid(now) with
        {
            Quality = SensorServiceQuality.Partial,
            CpuTemperature = null,
            CpuPackagePower = new(0.5, boundary)
        };
        SensorSnapshotValidation.Validate(snapshot);
        Assert.Multiple(() =>
        {
            Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot, now), Is.EqualTo(0.5));
            Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot, now + 1), Is.Null);
            Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot with { CpuPackagePower = null }, now),
                Is.Null);
            Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot with { UpdatedAt = now + 1 }, now),
                Is.Null);
            Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot with { Quality = SensorServiceQuality.Failed }, now),
                Is.Null);
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(SensorSnapshotValidation.MaximumCpuPackageWatts + 1)]
    public void InvalidPackageWatts_AreRejectedBeforeDisplay(double watts)
    {
        var now = Stopwatch.GetTimestamp();
        var snapshot = Valid(now) with { CpuPackagePower = new(watts, now) };
        Assert.That(() => SensorSnapshotValidation.Validate(snapshot), Throws.TypeOf<InvalidDataException>());
        Assert.That(SensorSnapshotValidation.GetFreshCpuPackageWatts(snapshot, now), Is.Null);
    }

    [Test]
    public void HardwareReadings_ExpireBySensorKindAndRequireAFreshSnapshot()
    {
        var now = 60 * Stopwatch.Frequency;
        var lastRead = now - 5 * Stopwatch.Frequency;
        var board = new HardwareSensorReading(HardwareSensorKind.BoardTemperature, null, "VRM", 43, lastRead);
        var gpu = new HardwareSensorReading(HardwareSensorKind.GpuFan, "pci/gpu-a", "Fan 1", 0, lastRead);
        var disk = new HardwareSensorReading(HardwareSensorKind.NvmeEnduranceUsed, "C:", null, 106, now - 35 * Stopwatch.Frequency);
        var snapshot = Valid(now) with { Quality = SensorServiceQuality.Partial, Sensors = [board, gpu, disk] };
        var refreshed = snapshot with { Sensors = [board, gpu, disk with { LastReadAt = now }] };
        Assert.Multiple(() =>
        {
            Assert.That(() => SensorSnapshotValidation.Validate(snapshot), Throws.Nothing);
            Assert.That(SensorSnapshotValidation.GetFreshHardwareSensors(snapshot, now), Is.EqualTo(snapshot.Sensors));
            Assert.That(SensorSnapshotValidation.GetFreshHardwareSensors(snapshot, now + 1), Is.Empty);
            Assert.That(SensorSnapshotValidation.GetFreshHardwareSensors(refreshed, now + 5 * Stopwatch.Frequency + 1), Is.Empty);
            Assert.That(() => SensorSnapshotValidation.Validate(snapshot with { Sensors = [disk with { Target = "bad" }] }),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(() => SensorSnapshotValidation.Validate(snapshot with { Sensors = [gpu with { Target = "C:" }] }),
                Throws.TypeOf<InvalidDataException>());
        });
    }

    [Test]
    public void CurrentSnapshot_PreservesSensorIdentityMeaningAndGeneration()
    {
        var now = Stopwatch.GetTimestamp();
        var snapshot = Valid(now);
        var reading = SensorSnapshotValidation.TryGetReading(snapshot, now);
        Assert.Multiple(() =>
        {
            Assert.That(reading, Is.Not.Null);
            Assert.That(reading!.Generation, Is.EqualTo(snapshot.Generation));
            Assert.That(reading.HardwareId, Is.EqualTo("/cpu/0"));
            Assert.That(reading.SensorId, Is.EqualTo("/cpu/0/temperature/0"));
            Assert.That(reading.SensorName, Is.EqualTo("CPU Package"));
            Assert.That(reading.Celsius, Is.EqualTo(52.5));
        });
    }

    [TestCase(SensorServiceQuality.Idle)]
    [TestCase(SensorServiceQuality.Starting)]
    [TestCase(SensorServiceQuality.Partial)]
    [TestCase(SensorServiceQuality.Failed)]
    [TestCase(SensorServiceQuality.Suspended)]
    public void NonCompleteSnapshot_NeverCreatesACurrentReading(SensorServiceQuality quality)
    {
        var now = Stopwatch.GetTimestamp();
        Assert.That(SensorSnapshotValidation.TryGetReading(Valid(now) with { Quality = quality }, now), Is.Null);
    }

    [Test]
    public void Freshness_RequiresBothPassAndSensorTimestampWithinFiveSeconds()
    {
        var now = Stopwatch.GetTimestamp();
        var snapshot = Valid(now);
        var fiveSeconds = 5 * Stopwatch.Frequency;
        var boundary = snapshot with { UpdatedAt = now - fiveSeconds,
            CpuTemperature = snapshot.CpuTemperature! with { LastReadAt = now - fiveSeconds } };
        Assert.Multiple(() =>
        {
            Assert.That(SensorSnapshotValidation.TryGetReading(boundary, now), Is.Not.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(boundary, now + 1), Is.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(snapshot with
                { CpuTemperature = snapshot.CpuTemperature! with { LastReadAt = now - fiveSeconds - 1 } }, now), Is.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(snapshot with { UpdatedAt = now + 1 }, now), Is.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(snapshot with
                { CpuTemperature = snapshot.CpuTemperature! with { LastReadAt = now + 1 } }, now), Is.Null);
        });
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(0)]
    [TestCase(151)]
    public void InvalidTemperature_IsRejectedBeforePortableProjection(double value)
    {
        var now = Stopwatch.GetTimestamp();
        var snapshot = Valid(now);
        snapshot = snapshot with { CpuTemperature = snapshot.CpuTemperature! with { Celsius = value } };
        Assert.That(() => SensorSnapshotValidation.Validate(snapshot), Throws.TypeOf<InvalidDataException>());
        Assert.That(SensorSnapshotValidation.TryGetReading(snapshot, now), Is.Null);
    }

    [Test]
    public void InvalidHeaderAndIdentity_AreRejectedRatherThanTrusted()
    {
        var now = Stopwatch.GetTimestamp();
        var valid = Valid(now);
        SensorSnapshot[] invalid =
        [
            valid with { Frequency = 0 }, valid with { Frequency = Stopwatch.Frequency + 1 },
            valid with { Sequence = -1 }, valid with { Generation = Guid.Empty },
            valid with { Quality = (SensorServiceQuality)99 }, valid with { CpuTemperature = null },
            valid with { CpuTemperature = valid.CpuTemperature! with { HardwareId = "" } },
            valid with { CpuTemperature = valid.CpuTemperature! with { SensorName = "bad\nname" } },
            valid with { CpuTemperature = valid.CpuTemperature! with { Source = new string('a', 257) } }
        ];
        foreach (var snapshot in invalid)
            Assert.That(() => SensorSnapshotValidation.Validate(snapshot), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void FailedAcquisitionOrMissingValue_NeverRestampsAnOldValue()
    {
        var now = Stopwatch.GetTimestamp();
        var valid = Valid(now);
        Assert.Multiple(() =>
        {
            Assert.That(SensorSnapshotValidation.TryGetReading(valid with
                { CpuTemperature = valid.CpuTemperature! with { Celsius = null } }, now), Is.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(valid with
                { CpuTemperature = valid.CpuTemperature! with { LastReadAt = 0 } }, now), Is.Null);
            Assert.That(SensorSnapshotValidation.TryGetReading(valid with
                { CpuTemperature = valid.CpuTemperature! with { AvailabilityReason = "read-failed" } }, now), Is.Null);
        });
    }

    [Test]
    public async Task SharedFraming_PreservesNetworkCompatibilityAndSensorPayload()
    {
        using var stream = new MemoryStream();
        var expected = new SensorServiceRequest(1, "subscribe");
        await NetworkServiceProtocol.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        Assert.That(await SensorServiceProtocol.ReadAsync<SensorServiceRequest>(stream, CancellationToken.None),
            Is.EqualTo(expected));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(SensorServiceProtocol.MaximumRequestBytes + 1)]
    public void Framing_RejectsInvalidRequestLengthBeforeAllocatingPayload(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using var stream = new MemoryStream(bytes);
        Assert.That(async () => await SensorServiceProtocol.ReadAsync<SensorServiceRequest>(stream,
                CancellationToken.None, SensorServiceProtocol.MaximumRequestBytes), Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("null", typeof(InvalidDataException))]
    [TestCase("{", typeof(InvalidDataException))]
    public void Framing_RejectsNullOrMalformedJson(string json, Type error)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        using var stream = new MemoryStream();
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, payload.Length);
        stream.Write(bytes); stream.Write(payload); stream.Position = 0;
        Assert.That(async () => await SensorServiceProtocol.ReadAsync<SensorServiceRequest>(stream,
            CancellationToken.None), Throws.TypeOf(error));
    }

    [Test]
    public void ResponseVersionMismatch_IsDistinguishedFromServiceRejection()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => SensorServiceClient.ValidateResponse(new(2, "ok")),
                Throws.TypeOf<SensorServiceProtocolMismatchException>());
            Assert.That(() => SensorServiceClient.ValidateResponse(new(1, "denied")),
                Throws.TypeOf<InvalidOperationException>().And.Not.TypeOf<SensorServiceProtocolMismatchException>());
        });
    }
}
