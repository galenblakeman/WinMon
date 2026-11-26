using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class SensorServiceSetupDescriptorTests
{
    [Test]
    public void SensorEndpoint_UsesTheFixedServicePipeAndProgramFilesPayloadNames()
    {
        var endpoint = ProtectedServiceEndpoints.Sensor;
        Assert.Multiple(() =>
        {
            Assert.That(endpoint.ServiceName, Is.EqualTo("WinMonSensorMonitor"));
            Assert.That(endpoint.PipeName, Is.EqualTo("WinMonSensorMonitor.v1"));
            Assert.That(endpoint.InstallDirectoryName, Is.EqualTo("SensorMonitor"));
            Assert.That(endpoint.ServiceExecutableName, Is.EqualTo("WinMon.SensorService.exe"));
            Assert.That(endpoint.SetupExecutableName, Is.EqualTo("WinMon.SensorSetup.exe"));
            Assert.That(endpoint.SetupPipePrefix, Is.EqualTo("WinMon.SensorSetup."));
            Assert.That(endpoint.InstallationOwner, Is.EqualTo("WinMonSensorMonitor.v1"));
        });
    }

    [Test]
    public void SensorPackage_UsesFixedNamesAndTheExistingReleaseTrustRoot()
    {
        var package = ProtectedServiceEndpoints.SensorPackage;
        Assert.Multiple(() =>
        {
            Assert.That(package.HelperFileName, Is.EqualTo("WinMon.SensorSetup.exe"));
            Assert.That(package.ArchiveFileName, Is.EqualTo("sensor-service.zip"));
            Assert.That(package.ManifestFileName, Is.EqualTo("sensor-service-manifest.json"));
            Assert.That(package.SignatureFileName, Is.EqualTo("sensor-service-manifest.sig"));
            Assert.That(package.PublicKeyResourceName,
                Is.EqualTo(ProtectedServiceEndpoints.NetworkPackage.PublicKeyResourceName));
        });
    }

    [TestCase("PawnIO", "2.1.0.0", true)]
    [TestCase("PawnIO", "not-a-version", false)]
    [TestCase("PawnIO", "", false)]
    [TestCase("Other", "2.1.0.0", false)]
    public void PawnIoRegistration_RequiresExactProductNameAndParseableVersion(string name, string version,
        bool valid)
    {
        Assert.That(PawnIoPrerequisite.IsValidRegistration(name, version), Is.EqualTo(valid));
    }
}
