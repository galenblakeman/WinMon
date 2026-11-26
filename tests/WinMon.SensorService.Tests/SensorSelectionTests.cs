using NUnit.Framework;

namespace WinMon.SensorService.Tests;

[TestFixture]
public sealed class SensorSelectionTests
{
    [Test]
    public void FixedSelection_DoesNotSwitchToAnotherCpuMeaningWhenChosenSensorBecomesUnavailable()
    {
        var package = new SensorReading("cpu/0", "CPU", "package", "CPU Package", null, "test", 0, "not-refreshed");
        var tdie = new SensorReading("cpu/0", "CPU", "tdie", "Core (Tdie)", 78, "test", 10);
        var selected = SensorSelection.SelectFixedCandidate([package, tdie], "cpu/0", "package", "CPU Package");
        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.SameAs(package));
            Assert.That(selected!.Celsius, Is.Null);
        });
    }

    [Test]
    public void Selection_RejectsMultipleCpuRootsEvenWhenOnlyOneReportsCelsius()
    {
        var available = new SensorReading("cpu/0", "CPU0", "package", "CPU Package", 51, "test", 10);
        var unavailable = new SensorReading("cpu/1", "CPU1", "package", "CPU Package", null, "test", 0,
            "not-refreshed");
        Assert.That(SensorSelection.SelectPackageCandidate([available, unavailable]), Is.Null);
    }

    [TestCase(null, null, null)]
    [TestCase(50f, null, 50f)]
    [TestCase(50f, 50f, null)]
    [TestCase(50f, 49f, 50f)]
    [TestCase(0f, 0f, 0f)]
    [TestCase(151f, 151f, 151f)]
    public void FreshCelsius_RejectsValuesWithoutThisPassEvidence(float? value, float? minimum, float? maximum)
    {
        Assert.That(SensorSelection.FreshCelsius(value, minimum, maximum), Is.Null);
    }

    [Test]
    public void FreshCelsius_AcceptsUnchangedSuccessfulAssignment()
    {
        Assert.That(SensorSelection.FreshCelsius(52.5f, 52.5f, 52.5f), Is.EqualTo(52.5d));
    }

    [Test]
    public void PackageCandidate_PreservesPackageOverHotterCore()
    {
        var package = Reading("CPU Package", 45);
        var core = Reading("Core #1", 92);
        Assert.That(SensorSelection.SelectPackageCandidate([core, package]), Is.EqualTo(package));
    }

    [Test]
    public void PackageCandidate_RejectsAmbiguousOrMultiplePackageHardware()
    {
        var package = Reading("CPU Package", 45);
        Assert.Multiple(() =>
        {
            Assert.That(SensorSelection.SelectPackageCandidate([package, package with { SensorId = "sensor/1" }]), Is.Null);
            Assert.That(SensorSelection.SelectPackageCandidate([package, package with { HardwareId = "cpu/1" }]), Is.Null);
            Assert.That(SensorSelection.SelectPackageCandidate([package with { Celsius = null }]), Is.Null);
        });
    }

    private static SensorReading Reading(string name, double? value) =>
        new("cpu/0", "CPU", "sensor/0", name, value, "LibreHardwareMonitorLib 0.9.6", 12);
}
