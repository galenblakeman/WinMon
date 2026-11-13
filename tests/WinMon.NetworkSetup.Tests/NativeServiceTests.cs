using System.Security.AccessControl;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class NativeServiceTests
{
    [Test]
    public void Descriptor_GrantsOnlyReadStartAndQueryToProvisionedUser()
    {
        const string user = "S-1-5-21-1-2-3-1001";
        var descriptor = new RawSecurityDescriptor(NativeService.Descriptor([user]), 0);
        Assert.That(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), Is.True);
        var rules = descriptor.DiscretionaryAcl!.Cast<CommonAce>().ToArray();
        Assert.That(rules, Has.Length.EqualTo(3));
        Assert.That(rules.Single(rule => rule.SecurityIdentifier.Value == user).AccessMask, Is.EqualTo(0x15));
        Assert.That(rules.Where(rule => rule.SecurityIdentifier.Value != user).Select(rule => rule.AccessMask),
            Is.All.EqualTo(0xF01FF));
    }

    [TestCase("O:BAD:P(A;;0xF01FF;;;SY)(A;;0x15;;;S-1-5-21-1-2-3-1001)", true)]
    [TestCase("O:SYD:P(A;;0xF01FF;;;BA)(A;;0x15;;;S-1-5-21-1-2-3-1001)", true)]
    [TestCase("O:BAD:P(A;;0xF01FF;;;BU)", false)]
    [TestCase("O:BUD:P(A;;0xF01FF;;;SY)", false)]
    [TestCase("D:P(A;;0xF01FF;;;SY)", false)]
    [TestCase("O:BA", false)]
    public void ValidateSecurity_RequiresTrustedOwnerAndLimitedUntrustedRights(string sddl, bool allowed)
    {
        var descriptor = new RawSecurityDescriptor(sddl);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        if (allowed) Assert.DoesNotThrow(() => NativeService.ValidateSecurity(bytes));
        else Assert.Throws<UnauthorizedAccessException>(() => NativeService.ValidateSecurity(bytes));
    }

    [Test]
    public void Descriptor_RejectsMissingOrExcessiveProvisioning()
    {
        Assert.Throws<InvalidDataException>(() => NativeService.Descriptor([]));
        Assert.Throws<InvalidDataException>(() => NativeService.Descriptor(
            Enumerable.Repeat("S-1-5-21-1-2-3-1001", AuthorizationPolicy.MaximumUsers + 1).ToArray()));
    }

    [Test]
    public void ConfigurationMatches_AcceptsOnlyExactFixedIdentity()
    {
        Assert.That(NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", $"\"{SetupPaths.ServiceExecutable}\""),
            Is.True);
    }

    [TestCase(0x20u, 3u, "LocalSystem", "")]
    [TestCase(0x10u, 2u, "LocalSystem", "")]
    [TestCase(0x10u, 3u, "NetworkService", "")]
    public void ConfigurationMatches_RejectsDifferentServiceIdentity(uint serviceType, uint startType, string account,
        string ignored)
    {
        Assert.That(
            NativeService.ConfigurationMatches(serviceType, startType, account, $"\"{SetupPaths.ServiceExecutable}\""),
            Is.False);
    }

    [Test]
    public void ConfigurationMatches_RejectsArgumentsAndDifferentPath()
    {
        Assert.That(
            NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", $"\"{SetupPaths.ServiceExecutable}\" --debug"),
            Is.False);
        Assert.That(NativeService.ConfigurationMatches(0x10, 3, "LocalSystem", "\"C:\\Users\\Public\\service.exe\""),
            Is.False);
    }
}
