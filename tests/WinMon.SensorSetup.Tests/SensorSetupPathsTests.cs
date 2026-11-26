using NUnit.Framework;
using WinMon.NetworkSetup;

namespace WinMon.SensorSetup.Tests;

[TestFixture]
public sealed class SensorSetupPathsTests
{
    [Test]
    public void SensorSetup_UsesItsOwnServiceAndInstallationNames()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SetupPaths.ServiceName, Is.EqualTo("WinMonSensorMonitor"));
            Assert.That(SetupPaths.ServiceExecutableName, Is.EqualTo("WinMon.SensorService.exe"));
            Assert.That(SetupPaths.SetupExecutableName, Is.EqualTo("WinMon.SensorSetup.exe"));
            Assert.That(SetupPaths.InstallDirectory, Does.EndWith(Path.Combine("WinMon", "SensorMonitor")));
            Assert.That(SetupService.StagingPath("test"), Does.Contain(".SensorMonitor-test-"));
        });
    }

    [Test]
    public void SensorSetup_OnlyAcceptsItsOwnHandshakePipePrefix()
    {
        var token = new string('a', 64);
        var pipeSuffix = new string('b', 32);
        Assert.DoesNotThrow(() => SetupArguments.Parse(["--pipe", "WinMon.SensorSetup." + pipeSuffix, "--token", token]));
        Assert.Throws<ArgumentException>(() => SetupArguments.Parse(["--pipe", "WinMon.NetworkSetup." + pipeSuffix, "--token", token]));
    }

    [Test]
    public void MissingPawnIo_StopsInstallationBeforeAnySetupHostMutation()
    {
        var prepared = false;
        var host = new SetupHost
        {
            EnsureSensorPrerequisite = () => throw new InvalidOperationException("PawnIO is required."),
            PrepareParent = () => prepared = true
        };

        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(() => SetupService.BeginAsync("install", "S-1-5-21-1-2-3-1001", null!, CancellationToken.None, host))!.Message,
            Is.EqualTo("PawnIO is required."));
        Assert.That(prepared, Is.False);
    }

    [Test]
    public void SensorSetup_RequiresPawnIoOnlyForInstallation()
    {
        Assert.That(SetupService.RequiresSensorPrerequisite("install"), Is.True);
        Assert.That(SetupService.RequiresSensorPrerequisite("remove"), Is.False);
    }
}
