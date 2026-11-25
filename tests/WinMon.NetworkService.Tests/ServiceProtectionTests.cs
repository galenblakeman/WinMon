using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class ServiceProtectionTests
{
    private const string System = "S-1-5-18";
    private const string Administrators = "S-1-5-32-544";
    private const string Installer = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const string Account = ServicePolicyOriginalTests.Account;

    private static IEnumerable<TestCaseData> TrustedPrincipals()
    {
        foreach (var owner in new[] { System, Administrators, Installer })
            foreach (var writer in new[] { System, Administrators, Installer })
                yield return new TestCaseData(owner, writer);
    }

    [TestCaseSource(nameof(TrustedPrincipals))]
    public void TrustedOwnersAndWritersRetainFullControl(string owner, string writer)
    {
        var security = Descriptor(owner);
        security.AddAccessRule(Rule(writer, FileSystemRights.FullControl));
        Assert.DoesNotThrow(() => ServicePolicy.ValidateProtection(security));
    }

    [TestCase(Account)]
    [TestCase("S-1-1-0")]
    [TestCase(Installer + "-1")]
    public void UntrustedOwnerIsRejectedBeforeExaminingItsWriteRules(string owner)
    {
        var security = Descriptor(owner);
        security.AddAccessRule(Rule(Account, FileSystemRights.FullControl));
        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ServicePolicy.ValidateProtection(security))!.Message,
            Is.EqualTo("Service installation has an untrusted owner."));
    }

    [TestCase(FileSystemRights.WriteData)]
    [TestCase(FileSystemRights.AppendData)]
    [TestCase(FileSystemRights.WriteExtendedAttributes)]
    [TestCase(FileSystemRights.WriteAttributes)]
    [TestCase(FileSystemRights.Delete)]
    [TestCase(FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [TestCase(FileSystemRights.ChangePermissions)]
    [TestCase(FileSystemRights.TakeOwnership)]
    [TestCase(FileSystemRights.FullControl)]
    public void EveryWriteOrOwnershipPermissionRequiresATrustedWriter(FileSystemRights right)
    {
        var security = Descriptor(System);
        security.AddAccessRule(Rule(Account, right));
        AssertWriteRejection(security);
    }

    [TestCase(FileSystemRights.ReadData)]
    [TestCase(FileSystemRights.ReadExtendedAttributes)]
    [TestCase(FileSystemRights.ReadAttributes)]
    [TestCase(FileSystemRights.ReadPermissions)]
    [TestCase(FileSystemRights.Synchronize)]
    [TestCase(FileSystemRights.ExecuteFile)]
    [TestCase(FileSystemRights.ReadAndExecute)]
    public void UntrustedReadAndExecutePermissionsRemainAllowed(FileSystemRights right)
    {
        var security = Descriptor(System);
        security.AddAccessRule(Rule(Account, right));
        Assert.DoesNotThrow(() => ServicePolicy.ValidateProtection(security));
    }

    [TestCase(FileSystemRights.Write)]
    [TestCase(FileSystemRights.FullControl)]
    public void DenyRulesDoNotGrantWriteAccess(FileSystemRights right)
    {
        var security = Descriptor(System);
        security.AddAccessRule(new(new SecurityIdentifier(Account), right, AccessControlType.Deny));
        Assert.DoesNotThrow(() => ServicePolicy.ValidateProtection(security));
    }

    [TestCase(PropagationFlags.None, false)]
    [TestCase(PropagationFlags.NoPropagateInherit, false)]
    [TestCase(PropagationFlags.InheritOnly, true)]
    [TestCase(PropagationFlags.InheritOnly | PropagationFlags.NoPropagateInherit, true)]
    public void OnlyInheritOnlyRulesAreIgnoredForTheCurrentEntry(PropagationFlags propagation, bool accepted)
    {
        var security = Descriptor(System);
        security.AddAccessRule(new(new SecurityIdentifier(Account), FileSystemRights.Write,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, propagation, AccessControlType.Allow));
        if (accepted) Assert.DoesNotThrow(() => ServicePolicy.ValidateProtection(security));
        else AssertWriteRejection(security);
    }

    [TestCase("")]
    [TestCase("ID")]
    public void ExplicitAndInheritedWriteGrantsAreBothInspected(string inheritance)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"O:SYG:SYD:P(A;{inheritance};FW;;;{Account})");
        AssertWriteRejection(security);
    }

    [Test]
    public void AnAllowWriteGrantIsRejectedEvenWhenADenyRuleWouldOverrideIt()
    {
        var security = Descriptor(System);
        security.AddAccessRule(new(new SecurityIdentifier(Account), FileSystemRights.Write, AccessControlType.Deny));
        security.AddAccessRule(Rule(Account, FileSystemRights.Write));
        AssertWriteRejection(security);
    }

    [Test]
    public void AnExplicitlyEmptyDaclIsAcceptedForATrustedOwner()
    {
        var security = Descriptor(System);
        Assert.That(security.GetAccessRules(true, true, typeof(SecurityIdentifier)), Is.Empty);
        Assert.DoesNotThrow(() => ServicePolicy.ValidateProtection(security));
    }

    [Test]
    public void NullDaclRepresentationIsTreatedAsUnrestrictedWriteAccess()
    {
        var sid = new SecurityIdentifier(System);
        var descriptor = new RawSecurityDescriptor(ControlFlags.DiscretionaryAclPresent, sid, sid, null, null);
        var bytes = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(bytes, 0);
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorBinaryForm(bytes);
        AssertWriteRejection(security);
    }

    internal static DirectorySecurity Descriptor(string owner)
    {
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"O:{owner}G:SYD:P");
        return security;
    }
    private static FileSystemAccessRule Rule(string sid, FileSystemRights rights) =>
        new(new SecurityIdentifier(sid), rights, AccessControlType.Allow);
    private static void AssertWriteRejection(FileSystemSecurity security) =>
        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ServicePolicy.ValidateProtection(security))!.Message,
            Is.EqualTo("Service installation is writable by an untrusted account."));
}
