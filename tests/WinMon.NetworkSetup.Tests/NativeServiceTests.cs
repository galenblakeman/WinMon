using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class NativeServiceTests
{
    [Test]
    public void ConfigurationMatches_AcceptsOnlyExactFixedIdentity()
    {
        Assert.That(NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", $"\"{SetupPaths.ServiceExecutable}\""), Is.True);
    }

    [TestCase(0x20u, 3u, "LocalSystem", "")]
    [TestCase(0x10u, 2u, "LocalSystem", "")]
    [TestCase(0x10u, 3u, "NetworkService", "")]
    public void ConfigurationMatches_RejectsDifferentServiceIdentity(uint serviceType, uint startType, string account, string ignored)
    {
        Assert.That(NativeService.ConfigurationMatches(serviceType, startType, account, $"\"{SetupPaths.ServiceExecutable}\""), Is.False);
    }

    [Test]
    public void ConfigurationMatches_RejectsArgumentsAndDifferentPath()
    {
        Assert.That(NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", $"\"{SetupPaths.ServiceExecutable}\" --debug"), Is.False);
        Assert.That(NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", "\"C:\\Users\\Public\\service.exe\""), Is.False);
    }
}
