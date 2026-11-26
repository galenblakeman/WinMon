using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class CpuTemperatureProbeTests
{
    [TestCase(null)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(0f)]
    [TestCase(-5f)]
    [TestCase(151f)]
    public void LibreTemperature_RejectsUnavailableAndImplausibleValues(float? value)
    {
        Assert.That(CpuTemperatureRules.Normalize(value), Is.Null);
    }

    [TestCase(1f)]
    [TestCase(52.5f)]
    [TestCase(150f)]
    public void LibreTemperature_PreservesAbsoluteCelsius(float value)
    {
        Assert.That(CpuTemperatureRules.Normalize(value), Is.EqualTo((double)value));
    }

    [TestCase("CPU Package")]
    [TestCase("Core (Tdie)")]
    [TestCase("Core (Tctl/Tdie)")]
    [TestCase("Core (Tctl)")]
    public void PackageCandidate_PreservesSensorIdentityAndMeaning(string meaning)
    {
        var expected = new CpuTemperatureReading("cpu/0", "CPU", "cpu/0/temperature/3", meaning, 52.5);
        Assert.That(CpuTemperatureRules.SelectPackageCandidate([expected]), Is.EqualTo(expected));
    }

    [Test]
    public void PackageCandidate_PrefersPackageInsteadOfTheHottestCore()
    {
        var package = new CpuTemperatureReading("cpu/0", "CPU", "package", "CPU Package", 45);
        var core = new CpuTemperatureReading("cpu/0", "CPU", "core", "Core #1", 90);
        Assert.That(CpuTemperatureRules.SelectPackageCandidate([core, package]), Is.EqualTo(package));
        Assert.That(CpuTemperatureRules.SelectPackageCandidate([core]), Is.Null);
    }

    [Test]
    public void PackageCandidate_RejectsAmbiguousPackagesAndDuplicateMeanings()
    {
        var first = new CpuTemperatureReading("cpu/0", "CPU", "package", "CPU Package", 45);
        Assert.Multiple(() =>
        {
            Assert.That(CpuTemperatureRules.SelectPackageCandidate([first, first with { HardwareId = "cpu/1" }]), Is.Null);
            Assert.That(CpuTemperatureRules.SelectPackageCandidate([first, first with { SensorId = "other" }]), Is.Null);
            Assert.That(CpuTemperatureRules.SelectPackageCandidate([first with { Celsius = null }]), Is.Null);
        });
    }
}
