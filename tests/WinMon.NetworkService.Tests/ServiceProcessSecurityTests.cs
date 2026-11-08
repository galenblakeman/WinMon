using NUnit.Framework;
using System.Security.AccessControl;
using System.Security.Principal;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceProcessSecurityTests
{
    [Test]
    public void BuildSddl_GrantsOnlyQueryLimitedInformationToValidatedUsers()
    {
        var user = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        var descriptor = new RawSecurityDescriptor(ServiceProcessSecurity.BuildSddl([user.Value]));
        var rules = descriptor.DiscretionaryAcl!.Cast<CommonAce>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), Is.True);
            Assert.That(rules, Has.Length.EqualTo(3));
            Assert.That(rules.All(rule => rule.AceQualifier == AceQualifier.AccessAllowed), Is.True);
            Assert.That(rules.Single(rule => rule.SecurityIdentifier == user).AccessMask, Is.EqualTo(0x1000));
            Assert.That(rules.Where(rule => rule.SecurityIdentifier != user).All(rule =>
                (rule.SecurityIdentifier.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                 rule.SecurityIdentifier.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)) &&
                rule.AccessMask == 0x10000000), Is.True);
        });
    }

    [Test]
    public void BuildSddl_RejectsUntrustedOrExcessiveSidInput()
    {
        Assert.Throws<InvalidDataException>(() => ServiceProcessSecurity.BuildSddl(["S-1-1-0"]));
        Assert.Throws<InvalidDataException>(() => ServiceProcessSecurity.BuildSddl(
            Enumerable.Repeat("S-1-5-21-1-2-3-1001", 65).ToArray()));
    }
}
