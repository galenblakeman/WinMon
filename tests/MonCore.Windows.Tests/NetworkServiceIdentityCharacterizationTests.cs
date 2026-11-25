using System.IO.Pipes;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, Category("Integration")]
public sealed class NetworkServiceIdentityCharacterizationTests
{
    private delegate bool ReadServerId(SafePipeHandle pipe, out uint processId);
    private static readonly ReadServerId ServerId = typeof(NetworkServiceIdentity)
        .GetMethod("GetNamedPipeServerProcessId", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<ReadServerId>();

    [Test]
    public void InvalidPipeHandlesKeepTheirIdentityErrorAndCallerOwnership()
    {
        using var handle = new SafePipeHandle(IntPtr.Zero, false);

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => NetworkServiceIdentity.Verify(handle))!.Message,
            Is.EqualTo("Cannot identify network service pipe server."));

        Assert.That(handle.IsInvalid, Is.True);
        Assert.That(handle.IsClosed, Is.False);
    }

    [Test]
    public void ClosedPipeHandlesKeepTheirInteropDisposedException()
    {
        using var handle = new SafePipeHandle(IntPtr.Zero, false);
        handle.Dispose();

        Assert.That(Assert.Throws<ObjectDisposedException>(() => NetworkServiceIdentity.Verify(handle))!.ObjectName,
            Is.EqualTo("Microsoft.Win32.SafeHandles.SafePipeHandle"));

        Assert.That(handle.IsClosed, Is.True);
    }

    [Test]
    public void NullPipeHandlesKeepTheirInteropArgumentException()
    {
        Assert.That(Assert.Throws<ArgumentNullException>(() => NetworkServiceIdentity.Verify(null!))!.ParamName,
            Is.EqualTo("pHandle"));
    }

    [Test]
    public async Task UnrelatedPipeCannotBeAuthenticatedOrHaveItsStreamConsumed()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var name = "WinMon-identity-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name,
            PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync(timeout.Token);
        using var client = await NetworkServiceClient.ConnectProtectedPipeAsync(name, timeout.Token);
        await accepted;
        Assert.That(ServerId(client.SafePipeHandle, out var serverId), Is.True);
        Assert.That(serverId, Is.EqualTo((uint)Environment.ProcessId));
        Exception? rejection = null;
        try { NetworkServiceIdentity.Verify(client.SafePipeHandle); }
        catch (UnauthorizedAccessException failure)
        {
            rejection = failure;
            Assert.That(failure.Message, Is.AnyOf("Cannot inspect service manager.",
                "Registered network service configuration is not trusted.",
                "Network service pipe is not owned by the registered service."));
        }
        catch (InvalidOperationException failure)
        {
            rejection = failure;
            Assert.That(failure.Message, Is.EqualTo("Network monitoring service is not installed or accessible."));
        }
        Assert.That(rejection, Is.Not.Null);
        Assert.That(client.SafePipeHandle.IsClosed, Is.False);
        byte[] marker = [19, 41, 73];
        var received = new byte[marker.Length];
        var receive = client.ReadExactlyAsync(received, timeout.Token);
        await server.WriteAsync(marker, timeout.Token);
        await receive;
        Assert.That(received, Is.EqualTo(marker));
    }

    [Test]
    [Explicit("Requires the running protected WinMon network service and pipe access.")]
    public async Task InstalledServicePipeIdentityAndScmPidAgreeWithoutProtocolRequests()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = await NetworkServiceClient.ConnectProtectedPipeAsync(NetworkServiceProtocol.PipeName, timeout.Token);
        Assert.That(ServerId(pipe.SafePipeHandle, out var before), Is.True);
        Assert.That(before, Is.Not.EqualTo((uint)Environment.ProcessId));
        Assert.That(before, Is.GreaterThan(0));

        Assert.DoesNotThrow(() => NetworkServiceIdentity.Verify(pipe.SafePipeHandle));
        var observation = NetworkServiceProcess.Read();

        Assert.That(observation.Unreadable, Is.False);
        Assert.That(observation.ProcessId, Is.EqualTo(checked((int)before)));
        Assert.That(ServerId(pipe.SafePipeHandle, out var after), Is.True);
        Assert.That(after, Is.EqualTo(before));
        Assert.That(pipe.SafePipeHandle.IsClosed, Is.False);
        Assert.That(pipe.IsConnected, Is.True);
    }

    [Test]
    public void CurrentProcessTokenRoleMatchesTheIndependentWindowsIdentity()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();

        var actual = (bool)InvokeBoundary("IsLocalSystemProcess", process.SafeHandle, null)!;

        Assert.That(actual, Is.EqualTo(identity.IsSystem));
        Assert.That(process.SafeHandle.IsClosed, Is.False);
        Assert.That(process.HasExited, Is.False);
    }

    [Test]
    public void InvalidProcessTokenIsNotAcceptedWithoutAnAuthoritativeServiceFallback()
    {
        using var process = new SafeProcessHandle(IntPtr.Zero, false);

        Assert.That(InvokeBoundary("IsLocalSystemProcess", process, null), Is.EqualTo(false));

        Assert.That(process.IsClosed, Is.False);
        Assert.That(process.IsInvalid, Is.True);
    }

    [Test]
    public void InvalidProcessIdKeepsItsNativeOpenFailure()
    {
        var failure = Assert.Throws<System.ComponentModel.Win32Exception>(() => InvokeBoundary("VerifyProcessIdentity", null, 0u));

        Assert.That(failure!.NativeErrorCode, Is.EqualTo(87));
        Assert.That(failure.Message, Does.StartWith("Cannot open network service process for limited inspection."));
    }

    [Test]
    public void CurrentProcessCannotBeSubstitutedForAnotherServicesLifetime()
    {
        using var manager = (SafeHandle)InvokeBoundary("OpenSCManager", null, null, 1u)!;
        Assert.That(manager.IsInvalid, Is.False);
        using var service = (SafeHandle)InvokeBoundary("OpenService", manager, "EventLog", 4u)!;
        Assert.That(service.IsInvalid, Is.False);
        using var process = Process.GetCurrentProcess();

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => InvokeBoundary(
            "VerifyProcessIdentity", service, (uint)process.Id))!.Message, Is.EqualTo("Network service identity changed."));

        Assert.That(service.IsClosed, Is.False);
        Assert.That(process.HasExited, Is.False);
    }

    private static object? InvokeBoundary(string name, params object?[] arguments)
    {
        try
        {
            return typeof(NetworkServiceIdentity).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, arguments);
        }
        catch (TargetInvocationException failure) when (failure.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
            throw;
        }
    }
}
