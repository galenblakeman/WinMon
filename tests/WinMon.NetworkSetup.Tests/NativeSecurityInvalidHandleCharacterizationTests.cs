using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class NativeSecurityInvalidHandleCharacterizationTests
{
    private static readonly Action<SafeScHandle, byte[]> SetSecurity = typeof(NativeService)
        .GetMethod("SetSecurity", BindingFlags.NonPublic | BindingFlags.Static)!
        .CreateDelegate<Action<SafeScHandle, byte[]>>();
    private static readonly Func<string?, string?, uint, SafeScHandle> OpenManager = Bind<Func<string?, string?, uint, SafeScHandle>>("OpenScManager");
    private static readonly Func<SafeScHandle, string, uint, SafeScHandle> OpenService = Bind<Func<SafeScHandle, string, uint, SafeScHandle>>("OpenService");
    private static readonly Func<SafeScHandle, ServiceStatusProcess> Status = Bind<Func<SafeScHandle, ServiceStatusProcess>>("Status");
    private static readonly Action<SafeScHandle, uint, CancellationToken> Wait = Bind<Action<SafeScHandle, uint, CancellationToken>>("Wait");

    [Test]
    public void ValidDaclInputsWithUnusableHandlesPreserveErrorsOwnershipAndDescriptorBytes(
        [Values("null", "invalid", "closed")] string kind,
        [Values(false, true)] bool protect,
        [Values(false, true)] bool empty)
    {
        using var handle = Handle(kind);
        var closed = handle?.IsClosed;
        var invalid = handle?.IsInvalid;
        var descriptor = Descriptor(protect, empty, missing: false);
        var original = (byte[])descriptor.Clone();

        if (kind == "null")
            Assert.That(Assert.Throws<ArgumentNullException>(() => SetSecurity(handle!, descriptor))!.ParamName, Is.EqualTo("pHandle"));
        else if (kind == "closed")
            Assert.Throws<ObjectDisposedException>(() => SetSecurity(handle!, descriptor));
        else
        {
            Assert.That(handle!.DangerousGetHandle(), Is.EqualTo(IntPtr.Zero));
            Assert.That(handle.IsInvalid, Is.True);
            Assert.That(Assert.Throws<Win32Exception>(() => SetSecurity(handle, descriptor))!.NativeErrorCode, Is.EqualTo(6));
        }

        Assert.That(handle?.IsClosed, Is.EqualTo(closed));
        Assert.That(handle?.IsInvalid, Is.EqualTo(invalid));
        Assert.That(descriptor, Is.EqualTo(original));
    }

    [TestCase("null")]
    [TestCase("invalid")]
    [TestCase("closed")]
    public void MissingDaclIsRejectedBeforeInspectingTheHandle(string kind)
    {
        using var handle = Handle(kind);
        var closed = handle?.IsClosed;
        var invalid = handle?.IsInvalid;
        var descriptor = Descriptor(protect: false, empty: false, missing: true);
        var original = (byte[])descriptor.Clone();
        Assert.That(Assert.Throws<InvalidDataException>(() => SetSecurity(handle!, descriptor))!.Message,
            Is.EqualTo("Missing service DACL."));
        Assert.That(handle?.IsClosed, Is.EqualTo(closed));
        Assert.That(handle?.IsInvalid, Is.EqualTo(invalid));
        Assert.That(descriptor, Is.EqualTo(original));
    }

    [Test]
    public void PreCanceledStartPreservesTheCallerTokenBeforeServiceAcquisition()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<OperationCanceledException>(() => NativeService.StartAndVerify(cancellation.Token));
        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public void MatchingRunningStateReturnsUsingOnlyAReadOnlyServiceHandle()
    {
        using var manager = OpenManager(null, null, 1); // SC_MANAGER_CONNECT only.
        Assert.That(manager.IsInvalid, Is.False);
        using var service = OpenService(manager, "EventLog", 4); // SERVICE_QUERY_STATUS only.
        Assert.That(service.IsInvalid, Is.False);
        Assert.That(Status(service).State, Is.EqualTo(4));
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        Wait(service, 4, limit.Token);
        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
        Assert.That(manager.IsClosed, Is.False);
        Assert.That(Status(service).State, Is.EqualTo(4));
    }

    [Test]
    public void StoppedReadOnlyServicePreservesTheReportedStartFailure()
    {
        using var manager = OpenManager(null, null, 1);
        Assert.That(manager.IsInvalid, Is.False);
        using var service = OpenService(manager, "RemoteRegistry", 4);
        if (service.IsInvalid) Assert.Ignore("A query-only RemoteRegistry handle is unavailable on this installation.");
        var before = Status(service);
        if (before.State != 1) Assert.Ignore("This query-only case requires an already stopped RemoteRegistry service.");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var error = Assert.Throws<Win32Exception>(() => Wait(service, 4, limit.Token));
        Assert.That(error!.NativeErrorCode, Is.EqualTo(before.Win32Exit == 0 ? 1067 : unchecked((int)before.Win32Exit)));
        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
        Assert.That(manager.IsClosed, Is.False);
        Assert.That(Status(service).State, Is.EqualTo(1));
    }

    private static SafeScHandle? Handle(string kind)
    {
        if (kind == "null") return null;
        var handle = new SafeScHandle();
        Assert.That(handle.DangerousGetHandle(), Is.EqualTo(IntPtr.Zero));
        if (kind == "closed") handle.Dispose();
        return handle;
    }

    private static T Bind<T>(string name) where T : Delegate => typeof(NativeService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();

    private static byte[] Descriptor(bool protect, bool empty, bool missing)
    {
        var owner = new SecurityIdentifier("S-1-5-18");
        RawAcl? acl = missing ? null : new RawAcl(2, empty ? 0 : 1);
        if (!missing && !empty)
            acl!.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, 0xF01FF, owner, false, null));
        var flags = ControlFlags.SelfRelative;
        if (!missing) flags |= ControlFlags.DiscretionaryAclPresent;
        if (protect) flags |= ControlFlags.DiscretionaryAclProtected;
        var security = new RawSecurityDescriptor(flags, owner, null, null, acl);
        var bytes = new byte[security.BinaryLength];
        security.GetBinaryForm(bytes, 0);
        var roundTrip = new RawSecurityDescriptor(bytes, 0);
        Assert.That(roundTrip.DiscretionaryAcl?.Count, Is.EqualTo(missing ? null : empty ? 0 : 1));
        Assert.That(roundTrip.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), Is.EqualTo(protect));
        return bytes;
    }
}
