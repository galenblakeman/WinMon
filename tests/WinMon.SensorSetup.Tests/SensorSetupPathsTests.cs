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

    [TestCase(5)]
    [TestCase(3010)]
    public void PawnIoInstallerPreservesFailureAndRestartExitCodes(int exitCode)
    {
        var failure = Assert.ThrowsAsync<SetupFailure>(() => PawnIoInstaller.EnsureInstalledAsync(
            () => false, _ => Task.FromResult(exitCode), CancellationToken.None));
        Assert.That(failure!.Phase, Is.EqualTo("pawnio"));
        Assert.That(failure.ErrorCode, Is.EqualTo(exitCode));
    }

    [Test]
    public async Task RegisteredPawnIoSkipsInstaller()
    {
        await PawnIoInstaller.EnsureInstalledAsync(() => true,
            _ => throw new AssertionException("Installer called for a registered PawnIO package."), CancellationToken.None);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuccessfulPawnIoExitRequiresRegistration(bool registers)
    {
        var installed = false;
        Task ProvisionAsync() => PawnIoInstaller.EnsureInstalledAsync(() => installed, _ =>
        {
            installed = registers;
            return Task.FromResult(0);
        }, CancellationToken.None);

        if (registers) await ProvisionAsync();
        else Assert.That(Assert.ThrowsAsync<SetupFailure>(ProvisionAsync)!.Phase, Is.EqualTo("pawnio"));
    }

    [Test]
    public void SensorSetup_RequiresPawnIoOnlyForInstallation()
    {
        Assert.That(SetupService.RequiresSensorPrerequisite("install"), Is.True);
        Assert.That(SetupService.RequiresSensorPrerequisite("remove"), Is.False);
    }
}
