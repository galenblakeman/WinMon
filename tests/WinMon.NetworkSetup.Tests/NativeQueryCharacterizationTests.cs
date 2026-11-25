using Win32Exception = System.ComponentModel.Win32Exception;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class NativeQueryCharacterizationTests
{
    private const uint QueryConfiguration = 1;
    private const uint ReadControl = 0x20000;
    private static readonly Func<string?, string?, uint, SafeScHandle> OpenManager =
        Bind<Func<string?, string?, uint, SafeScHandle>>("OpenScManager");
    private static readonly Func<SafeScHandle, string, uint, SafeScHandle> OpenService =
        Bind<Func<SafeScHandle, string, uint, SafeScHandle>>("OpenService");
    private static readonly Action<SafeScHandle> EnsureConfiguration = Bind<Action<SafeScHandle>>("EnsureExactConfiguration");
    private static readonly Func<SafeScHandle, byte[]> ReadSecurity = Bind<Func<SafeScHandle, byte[]>>("QuerySecurity");

    [Test]
    public void UnrelatedServiceConfigurationIsRejectedWithoutClosingTheCallerHandleOrChangingItsAcl()
    {
        using var manager = Connect();
        using var service = OpenReadOnlyService(manager, "EventLog", QueryConfiguration | ReadControl);
        var before = ReadSecurity(service);

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => EnsureConfiguration(service))!.Message,
            Is.EqualTo("Service name belongs to a different configuration."));

        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
        Assert.That(ReadSecurity(service), Is.EqualTo(before));
    }

    [Test]
    public void PermissionQueriesReturnIndependentCompleteOwnerAndDaclSnapshots()
    {
        using var manager = Connect();
        using var service = OpenReadOnlyService(manager, "EventLog", ReadControl);
        var first = ReadSecurity(service);
        var original = (byte[])first.Clone();
        var descriptor = new RawSecurityDescriptor(first, 0);

        Assert.That(first.Length, Is.InRange(1, 64 * 1024));
        Assert.That(descriptor.ControlFlags & ControlFlags.SelfRelative, Is.EqualTo(ControlFlags.SelfRelative));
        Assert.That(descriptor.Owner!.Value, Is.EqualTo("S-1-5-18"));
        Assert.That(descriptor.Group, Is.Null);
        Assert.That(descriptor.SystemAcl, Is.Null);
        var rules = descriptor.DiscretionaryAcl!.OfType<CommonAce>().ToArray();
        Assert.That(rules.Any(rule => rule.AceQualifier == AceQualifier.AccessAllowed &&
            rule.SecurityIdentifier.Value == "S-1-5-18" && (rule.AccessMask & 0x20004) == 0x20004), Is.True);
        Assert.That(rules.Any(rule => rule.AceQualifier == AceQualifier.AccessAllowed &&
            rule.SecurityIdentifier.Value == "S-1-5-32-544" && (rule.AccessMask & 0xF01FF) == 0xF01FF), Is.True);
        Array.Clear(first);

        var second = ReadSecurity(service);

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second, Is.EqualTo(original));
        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
    }

    [TestCase("configuration")]
    [TestCase("permissions")]
    public void InvalidHandlesRetainTheBoundedNativeSizeRejection(string operation)
    {
        using var service = new SafeScHandle();
        Assert.That(service.IsInvalid, Is.True);

        Assert.That(Assert.Throws<InvalidDataException>(() => Query(operation, service))!.Message,
            Is.EqualTo(SizeError(operation)));

        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.True);
    }

    [TestCase("configuration")]
    [TestCase("permissions")]
    public void ClosedHandlesRetainTheInteropDisposedException(string operation)
    {
        using var service = new SafeScHandle();
        service.Dispose();

        Assert.That(Assert.Throws<ObjectDisposedException>(() => Query(operation, service))!.ObjectName,
            Is.EqualTo("WinMon.NetworkSetup.SafeScHandle"));

        Assert.That(service.IsClosed, Is.True);
    }

    [TestCase("configuration")]
    [TestCase("permissions")]
    public void NullHandlesRetainTheInteropArgumentException(string operation)
    {
        Assert.That(Assert.Throws<ArgumentNullException>(() => Query(operation, null!))!.ParamName,
            Is.EqualTo("pHandle"));
    }

    [TestCase("configuration")]
    [TestCase("permissions")]
    public void InsufficientQueryRightsRetainTheSizeErrorAndCallerHandle(string operation)
    {
        using var manager = Connect();
        using var service = OpenReadOnlyService(manager, "EventLog", operation == "configuration" ? ReadControl : QueryConfiguration);

        Assert.That(Assert.Throws<InvalidDataException>(() => Query(operation, service))!.Message,
            Is.EqualTo(SizeError(operation)));

        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
        if (operation == "configuration")
            Assert.That(new RawSecurityDescriptor(ReadSecurity(service), 0).Owner!.Value, Is.EqualTo("S-1-5-18"));
        else
            Assert.That(Assert.Throws<UnauthorizedAccessException>(() => EnsureConfiguration(service))!.Message,
                Is.EqualTo("Service name belongs to a different configuration."));
    }

    [Test]
    [Explicit("Requires the installed WinMon network service and permission to query its configuration.")]
    public void InstalledWinMonConfigurationIsAcceptedWithoutTakingOwnershipOfItsQueryHandle()
    {
        using var manager = Connect();
        using var service = OpenReadOnlyService(manager, SetupPaths.ServiceName, QueryConfiguration);

        Assert.DoesNotThrow(() => EnsureConfiguration(service));

        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
    }

    private static string SizeError(string operation) => operation == "configuration"
        ? "Invalid service configuration size." : "Invalid service permissions size.";

    private static void Query(string operation, SafeScHandle service)
    {
        if (operation == "configuration") EnsureConfiguration(service);
        else _ = ReadSecurity(service);
    }

    private static T Bind<T>(string name) where T : Delegate => typeof(NativeService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();

    private static SafeScHandle Connect()
    {
        var manager = OpenManager(null, null, 1);
        if (!manager.IsInvalid) return manager;
        var failure = Marshal.GetLastWin32Error();
        manager.Dispose();
        throw new Win32Exception(failure);
    }

    private static SafeScHandle OpenReadOnlyService(SafeScHandle manager, string name, uint rights)
    {
        var service = OpenService(manager, name, rights);
        if (!service.IsInvalid) return service;
        var failure = Marshal.GetLastWin32Error();
        service.Dispose();
        throw new Win32Exception(failure);
    }
}
