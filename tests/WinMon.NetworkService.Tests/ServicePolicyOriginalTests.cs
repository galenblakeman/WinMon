using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class ServicePolicyOriginalTests
{
    internal const string Account = "S-1-5-21-1-2-3-1001";

    [Test]
    [Category("Integration")]
    public void LoaderRejectsTheTestHostBeforeReadingAnyAuthorizationPolicy()
    {
        Assert.That(Path.GetDirectoryName(Environment.ProcessPath), Is.Not.EqualTo(ServicePolicy.InstallDirectory).IgnoreCase);
        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ServicePolicy.Load())!.Message,
            Is.EqualTo("Network service must run from its protected installation directory."));
        Assert.That(new DirectoryInfo(ServicePolicy.InstallDirectory).FullName, Is.EqualTo(ServicePolicy.InstallDirectory));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void NativeProtectionRejectsAnUntrustedTemporaryInstallationEntry(bool directory)
    {
        using var tree = new PolicyDirectory();
        FileSystemInfo item;
        if (directory)
        {
            item = tree.Root;
            var security = tree.Root.GetAccessControl();
            security.AddAccessRule(WorldWrite());
            tree.Root.SetAccessControl(security);
        }
        else
        {
            var file = new FileInfo(Path.Combine(tree.Root.FullName, "empty-test-file"));
            File.WriteAllText(file.FullName, "");
            var security = file.GetAccessControl();
            security.AddAccessRule(WorldWrite());
            file.SetAccessControl(security);
            item = file;
        }
        var check = typeof(ServicePolicy).GetMethod("CheckProtected", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Action<FileSystemInfo>>();
        var error = Assert.Throws<UnauthorizedAccessException>(() => check(item));
        Assert.That(error!.Message, Is.AnyOf("Service installation has an untrusted owner.",
            "Service installation is writable by an untrusted account."));
        return;
        static FileSystemAccessRule WorldWrite() => new(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Write, AccessControlType.Allow);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(64, true)]
    [TestCase(65, false)]
    public void RawUserCountIsBoundedBeforeDuplicateRemoval(int count, bool accepted)
    {
        var users = Enumerable.Repeat(Account, count).ToArray();
        if (accepted) Assert.That(ServicePolicy.ValidateUsers(users), Is.EqualTo(new[] { Account }));
        else Assert.That(Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers(users))!.Message,
            Is.EqualTo("Invalid number of provisioned users."));
    }

    [Test]
    public void SixtyFourDifferentAccountsAreRetainedWithoutChangingTheirIdentifiers()
    {
        var users = Enumerable.Range(1001, 64).Select(id => "S-1-5-21-1-2-3-" + id).ToArray();
        Assert.That(ServicePolicy.ValidateUsers(users), Is.EquivalentTo(users));
    }

    [TestCase("S-1-1-0")]
    [TestCase("S-1-5-7")]
    [TestCase("S-1-5-11")]
    public void BroadPrincipalsAreRejectedWithTheExplicitAccountError(string sid)
    {
        Assert.That(Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers([sid]))!.Message,
            Is.EqualTo("Authorization requires explicit account SIDs."));
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("overlong")]
    public void MissingOrOverlongUserIdentifiersKeepTheirValidationError(string kind)
    {
        var sid = kind == "null" ? null : kind == "empty" ? "" : new string('x', 185);
        Assert.That(Assert.Throws<InvalidDataException>(() => ServicePolicy.ValidateUsers([sid!]))!.Message,
            Is.EqualTo("Invalid provisioned SID."));
    }
}

internal sealed class PolicyDirectory : IDisposable
{
    internal readonly DirectoryInfo Root = Directory.CreateTempSubdirectory("WinMon-service-policy-");
    internal string Policy => Path.Combine(Root.FullName, "allowed-users.json");
    public void Dispose()
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!string.Equals(Root.Parent?.FullName, parent, StringComparison.OrdinalIgnoreCase) ||
            !Root.Name.StartsWith("WinMon-service-policy-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup outside the owned policy fixture.");
        Root.Delete(true);
    }
}
