using System.Reflection;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class SetupEntryPointTests
{
    [Test]
    public async Task InvalidArguments_ReturnFailureWithoutThrowing()
    {
        Assert.That(await Main([]), Is.EqualTo(1));
    }

    [Test]
    [Category("Integration")]
    public async Task UninstalledProcess_CannotRunPrivilegedOperations()
    {
        Assert.That(await Main(["--uninstall"]), Is.EqualTo(1));
        Assert.That(await Main(["--revoke-user", "S-1-5-21-1-2-3-1001"]), Is.EqualTo(1));
        Assert.That(await Main(["--pipe", "WinMon.NetworkSetup." + new string('a', 32),
            "--token", new string('a', 64)]), Is.EqualTo(1));
    }

    private static readonly Func<string[], Task<int>> Main = typeof(Program)
        .GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<string[], Task<int>>>();
}
