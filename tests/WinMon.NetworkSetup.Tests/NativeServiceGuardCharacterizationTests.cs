using Win32Exception = System.ComponentModel.Win32Exception;
using System.Reflection;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class NativeServiceGuardCharacterizationTests
{
    private static readonly Action<SafeScHandle, CancellationToken> Stop = Bind<Action<SafeScHandle, CancellationToken>>("Stop");
    private static readonly Action<SafeScHandle, uint, CancellationToken> Wait = Bind<Action<SafeScHandle, uint, CancellationToken>>("Wait");
    private static readonly Action<SafeScHandle, CancellationToken> WaitRemoved = Bind<Action<SafeScHandle, CancellationToken>>("WaitRemoved");

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var operation in new[] { "stop", "wait-running", "wait-stopped", "wait-removed" })
        foreach (var handle in new[] { "null", "invalid", "closed" })
            yield return new TestCaseData(operation, handle);
    }

    private static T Bind<T>(string name) where T : Delegate => typeof(NativeService)
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<T>();

    private static SafeScHandle? Handle(string kind)
    {
        if (kind == "null") return null;
        var handle = new SafeScHandle();
        if (kind == "closed") handle.Dispose();
        return handle;
    }

    private static void Invoke(string operation, SafeScHandle? handle, CancellationToken token)
    {
        switch (operation)
        {
            case "stop": Stop(handle!, token); break;
            case "wait-running": Wait(handle!, 4, token); break;
            case "wait-stopped": Wait(handle!, 1, token); break;
            case "wait-removed": WaitRemoved(handle!, token); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void PreCanceledServiceOperationsPreserveTheTokenBeforeInspectingAnyHandle(string operation, string kind)
    {
        using var handle = Handle(kind);
        var wasClosed = handle?.IsClosed;
        var wasInvalid = handle?.IsInvalid;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.Throws<OperationCanceledException>(() => Invoke(operation, handle, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(handle?.IsClosed, Is.EqualTo(wasClosed));
            Assert.That(handle?.IsInvalid, Is.EqualTo(wasInvalid));
        });
    }

    [TestCaseSource(nameof(Cases))]
    public void ServiceOperationHandleFailuresKeepTheNativeErrorAndCallerOwnership(string operation, string kind)
    {
        using var handle = Handle(kind);
        var wasClosed = handle?.IsClosed;
        var wasInvalid = handle?.IsInvalid;

        if (kind == "null")
            Assert.That(Assert.Throws<ArgumentNullException>(() => Invoke(operation, handle, default))!.ParamName, Is.EqualTo("pHandle"));
        else if (kind == "closed")
            Assert.Throws<ObjectDisposedException>(() => Invoke(operation, handle, default));
        else
        {
            Assert.That(handle!.IsInvalid, Is.True, "Only a zero invalid handle may reach a native query");
            var error = Assert.Throws<Win32Exception>(() => Invoke(operation, handle, default));
            Assert.That(error!.NativeErrorCode, Is.EqualTo(6));
        }

        Assert.Multiple(() =>
        {
            Assert.That(handle?.IsClosed, Is.EqualTo(wasClosed));
            Assert.That(handle?.IsInvalid, Is.EqualTo(wasInvalid));
        });
    }
}
