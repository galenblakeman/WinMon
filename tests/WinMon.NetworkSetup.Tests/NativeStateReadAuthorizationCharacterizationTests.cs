using Win32Exception = System.ComponentModel.Win32Exception;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class NativeStateReadAuthorizationCharacterizationTests
{
    private static readonly Func<string?, string?, uint, SafeScHandle> OpenManager =
        typeof(NativeService).GetMethod("OpenScManager", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string?, string?, uint, SafeScHandle>>();
    private static readonly Func<SafeScHandle, string, uint, SafeScHandle> OpenService =
        typeof(NativeService).GetMethod("OpenService", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<SafeScHandle, string, uint, SafeScHandle>>();

    [TestCase("capture-state"), TestCase("ensure-owned"), TestCase("restore-without-descriptor")]
    public void ServiceReadAndIncompleteRestoreGuardsPreserveAccessDeniedForARestrictedInstalledService(string operation)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Ignore("Requires an unelevated token; this test never requests elevation.");

        using var manager = OpenManager(null, null, 1);
        if (manager.IsInvalid) Assert.Ignore("Requires read-only SCM connection access.");
        using var readable = OpenService(manager, SetupPaths.ServiceName, 4);
        if (readable.IsInvalid) Assert.Ignore("Requires query-status access to an existing WinMon service; no service is created.");
        using var fullAccess = OpenService(manager, SetupPaths.ServiceName, 0xF01FF);
        var nativeError = Marshal.GetLastWin32Error();
        if (!fullAccess.IsInvalid) Assert.Ignore("Requires full service access to be denied; no permissions are changed.");
        Assert.That(nativeError, Is.EqualTo(5), "The independent native precondition must be access denied.");

        var error = Assert.Throws<Win32Exception>(() =>
        {
            switch (operation)
            {
                case "capture-state": NativeService.CaptureState(); break;
                case "ensure-owned": NativeService.EnsureOwnedOrAbsent(); break;
                // Exists=true avoids deletion; a null descriptor cannot reach a permissions write
                // or start/stop even if service access changes after the independent precondition.
                case "restore-without-descriptor": NativeService.Restore(new(true, false, null), CancellationToken.None); break;
                default: throw new AssertionException("Unknown authorization operation");
            }
        });

        Assert.That(error!.NativeErrorCode, Is.EqualTo(5));
        Assert.That(manager.IsClosed, Is.False);
        Assert.That(readable.IsClosed, Is.False);
        Assert.That(readable.IsInvalid, Is.False);
    }
}
