using System.Reflection;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class StoppedServiceCharacterizationTests
{
    private static readonly Func<string?, string?, uint, SafeScHandle> OpenManager = Bind<Func<string?, string?, uint, SafeScHandle>>("OpenScManager");
    private static readonly Func<SafeScHandle, string, uint, SafeScHandle> OpenService = Bind<Func<SafeScHandle, string, uint, SafeScHandle>>("OpenService");
    private static readonly Func<SafeScHandle, ServiceStatusProcess> Status = Bind<Func<SafeScHandle, ServiceStatusProcess>>("Status");
    private static readonly Action<SafeScHandle, CancellationToken> Stop = Bind<Action<SafeScHandle, CancellationToken>>("Stop");

    [Test]
    public void AlreadyStoppedServiceReturnsIdempotentlyWithQueryOnlyAccessAndCallerOwnership()
    {
        using var manager = OpenManager(null, null, 1);
        Assert.That(manager.IsInvalid, Is.False);
        using var service = OpenService(manager, "RemoteRegistry", 4);
        if (service.IsInvalid) Assert.Ignore("A query-only RemoteRegistry handle is unavailable on this installation.");
        if (Status(service).State != 1) Assert.Ignore("This case requires an already stopped RemoteRegistry service.");
        Stop(service, CancellationToken.None);
        Stop(service, CancellationToken.None);
        Assert.That(Status(service).State, Is.EqualTo(1));
        Assert.That(service.IsClosed, Is.False);
        Assert.That(service.IsInvalid, Is.False);
        Assert.That(manager.IsClosed, Is.False);
    }

    private static T Bind<T>(string name) where T : Delegate => typeof(NativeService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<T>();
}
