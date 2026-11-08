using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class SetupArgumentsTests
{
    private const string Pipe = "WinMon.NetworkSetup.0123456789abcdef0123456789abcdef";
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Test]
    public void Parse_AcceptsOnlyFixedHandshakeArguments()
    {
        var parsed = SetupArguments.Parse(["--pipe", Pipe, "--token", Token]);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.PipeName, Is.EqualTo(Pipe));
            Assert.That(parsed.Uninstall, Is.False);
            Assert.That(parsed.MatchesToken(Token), Is.True);
            Assert.That(parsed.MatchesToken(Token[..63] + "0"), Is.False);
        });
    }

    [TestCase("--pipe", "WinMon.NetworkSetup.not-a-guid", "--token", Token)]
    [TestCase("--pipe", Pipe, "--token", "not-a-token")]
    [TestCase("--pipe", Pipe, "--sid", Token)]
    [TestCase("--uninstall", "--pipe", Pipe, "--token")]
    public void Parse_RejectsArgumentsThatCanExpandPrivilege(string first, string second, string third, string fourth)
    {
        Assert.That(() => SetupArguments.Parse([first, second, third, fourth]), Throws.ArgumentException);
    }

    [Test]
    public void Parse_AcceptsOnlyStandaloneUninstall()
    {
        Assert.That(SetupArguments.Parse(["--uninstall"]).Uninstall, Is.True);
    }

    [Test]
    public void Parse_AcceptsOnlyCanonicalStandaloneRevokeUser()
    {
        var parsed = SetupArguments.Parse(["--revoke-user", "S-1-5-21-1-2-3-4"]);
        Assert.That(parsed.RevokeUser, Is.EqualTo("S-1-5-21-1-2-3-4"));
        Assert.That(() => SetupArguments.Parse(["--revoke-user", "S-1-1-0"]),
            Throws.InstanceOf<InvalidDataException>());
        Assert.That(() => SetupArguments.Parse(["--revoke-user", "S-1-5-21-1-2-3-4", "--uninstall"]),
            Throws.InstanceOf<ArgumentException>());
    }
}