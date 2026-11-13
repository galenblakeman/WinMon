using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration"), Category("Elevated")]
[Explicit("Requires elevation for read-only service inspection and protected temporary log folders.")]
public sealed class NativeReadOnlyTests
{
    [Test]
    public void InstalledServiceAndRegistration_AreInspectedWithoutMutation()
    {
        NativeService.EnsureOwnedOrAbsent();
        var state = NativeService.CaptureState();
        if (state.Exists)
        {
            Assert.That(state.SecurityDescriptor, Is.Not.Null.And.Not.Empty);
            Assert.DoesNotThrow(() => NativeService.ValidateSecurity(state.SecurityDescriptor!));
        }
        else Assert.That(state.SecurityDescriptor, Is.Null);
        var registration = SetupRegistration.Capture();
        if (registration is not null)
        {
            Assert.That(registration["InstallLocation"], Is.EqualTo(SetupPaths.InstallDirectory));
            Assert.That(registration["UninstallString"], Is.EqualTo($"\"{SetupPaths.InstalledSetupExecutable}\" --uninstall"));
        }
    }

    [Test]
    public void LogSecurityFactories_CreateAndValidateIsolatedProtectedFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "WinMon-log-security-" + Guid.NewGuid().ToString("N"));
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User!;
        var protectedSecurity = Method<Func<bool, DirectorySecurity>>("ProtectedSecurity");
        var userSecurity = Method<Func<SecurityIdentifier, DirectorySecurity>>("UserSecurity");
        var serviceSecurity = Method<Func<SecurityIdentifier, DirectorySecurity>>("ServiceSecurity");
        var ensure = Method<Action<string, DirectorySecurity, SecurityIdentifier?>>("Ensure");
        try
        {
            ensure(root, protectedSecurity(true), null);
            ensure(root, protectedSecurity(true), null);
            LogDirectories.ValidateDirectory(new DirectoryInfo(root), null);
            var application = Path.Combine(root, "App");
            ensure(application, userSecurity(user), user);
            Assert.DoesNotThrow(() => LogDirectories.ValidateDirectory(new DirectoryInfo(application), user));
            Assert.Throws<UnauthorizedAccessException>(() => LogDirectories.ValidateDirectory(new DirectoryInfo(application), null));
            var service = Path.Combine(root, "Service");
            ensure(service, serviceSecurity(user), null);
            Assert.DoesNotThrow(() => LogDirectories.ValidateDirectory(new DirectoryInfo(service), null));
            var privatePath = Path.Combine(root, "Elevated");
            ensure(privatePath, protectedSecurity(false), null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            Assert.That(new DirectoryInfo(privatePath).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>().Any(rule => rule.IdentityReference == users), Is.False);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Test]
    public void ExistingLogLocations_AreValidatedWithoutProvisioning()
    {
        Assert.That(LogDirectories.IsElevated, Is.True);
        Assert.That(Path.IsPathFullyQualified(LogDirectories.LocalPath), Is.True);
        if (Directory.Exists(LogDirectories.ServicePath)) Assert.DoesNotThrow(LogDirectories.ValidateServicePath);
        else Assert.Throws<UnauthorizedAccessException>(LogDirectories.ValidateServicePath);
        if (Directory.Exists(LogDirectories.AppPath)) Assert.DoesNotThrow(LogDirectories.ValidateAppPath);
        else Assert.Throws<UnauthorizedAccessException>(LogDirectories.ValidateAppPath);
    }

    private static T Method<T>(string name) where T : Delegate => typeof(LogDirectories)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();
}
