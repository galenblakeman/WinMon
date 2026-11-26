using NUnit.Framework;

namespace WinMon.SensorService.Tests;

[TestFixture]
public sealed class CpuTemperatureValidationTests
{
    private static SensorReading Reading(string hardwareId, string hardwareName, string sensorId, string name,
        double? celsius) => new(hardwareId, hardwareName, sensorId, name, celsius, "LibreHardwareMonitorLib 0.9.6", 1);

    [TestCase(null)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(0f)]
    [TestCase(-5f)]
    [TestCase(151f)]
    public void LibreTemperature_RejectsUnavailableAndImplausibleValues(float? value)
    {
        Assert.That(SensorSelection.Normalize(value), Is.Null);
    }

    [TestCase(1f)]
    [TestCase(52.5f)]
    [TestCase(150f)]
    public void LibreTemperature_PreservesAbsoluteCelsius(float value)
    {
        Assert.That(SensorSelection.Normalize(value), Is.EqualTo((double)value));
    }

    [TestCase("CPU Package")]
    [TestCase("Core (Tdie)")]
    [TestCase("Core (Tctl/Tdie)")]
    [TestCase("Core (Tctl)")]
    public void PackageCandidate_PreservesSensorIdentityAndMeaning(string meaning)
    {
        var expected = Reading("cpu/0", "CPU", "cpu/0/temperature/3", meaning, 52.5);
        Assert.That(SensorSelection.SelectPackageCandidate([expected]), Is.EqualTo(expected));
    }

    [Test]
    public void PackageCandidate_PrefersPackageInsteadOfTheHottestCore()
    {
        var package = Reading("cpu/0", "CPU", "package", "CPU Package", 45);
        var core = Reading("cpu/0", "CPU", "core", "Core #1", 90);
        Assert.That(SensorSelection.SelectPackageCandidate([core, package]), Is.EqualTo(package));
        Assert.That(SensorSelection.SelectPackageCandidate([core]), Is.Null);
    }

    [Test]
    public void PackageCandidate_RejectsAmbiguousPackagesAndDuplicateMeanings()
    {
        var first = Reading("cpu/0", "CPU", "package", "CPU Package", 45);
        Assert.Multiple(() =>
        {
            Assert.That(SensorSelection.SelectPackageCandidate([first, first with { HardwareId = "cpu/1" }]), Is.Null);
            Assert.That(SensorSelection.SelectPackageCandidate([first, first with { SensorId = "other" }]), Is.Null);
            Assert.That(SensorSelection.SelectPackageCandidate([first with { Celsius = null }]), Is.Null);
        });
    }
}
