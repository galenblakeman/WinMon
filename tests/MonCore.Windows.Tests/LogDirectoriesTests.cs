using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace MonCore.Windows.Tests;

[TestFixture]
[Category("Integration")]
public sealed class LogDirectoriesTests
{
    [Test]
    public void PerUserDirectory_RejectsAnotherUserWhoCanWrite()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var path = Path.Combine(Path.GetTempPath(), "WinMon-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var security = new DirectorySecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(true, false);
            Add(security, user, FileSystemRights.FullControl);
            Add(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
            Add(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl);
            FileSystemAclExtensions.CreateDirectory(security, path);

            LogDirectories.ValidateDirectory(new DirectoryInfo(path), user);
            Assert.That(() => LogDirectories.ValidateDirectory(new DirectoryInfo(path), null),
                Throws.TypeOf<UnauthorizedAccessException>());

            Add(security, new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write);
            new DirectoryInfo(path).SetAccessControl(security);
            Assert.That(() => LogDirectories.ValidateDirectory(new DirectoryInfo(path), user),
                Throws.TypeOf<UnauthorizedAccessException>());
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private static void Add(DirectorySecurity security, SecurityIdentifier sid, FileSystemRights rights)
    {
        security.AddAccessRule(new FileSystemAccessRule(sid, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
            AccessControlType.Allow));
    }
}
