using System.Reflection;
using System.ServiceProcess;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class ServiceHostTests
{
    [Test]
    public void CommandLineArguments_AreRejectedBeforeStartingTheService()
    {
        var error = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            Assert.That(Program.Main(["--interactive"]), Is.EqualTo(1));
            Assert.That(output.ToString(), Does.Contain("must be started by SCM"));
        }
        finally
        {
            Console.SetError(error);
        }
    }

    [Test]
    [Category("Integration")]
    public void StartupOutsideProtectedInstallation_FailsClosed()
    {
        using var service = new NetworkMonitoringService();
        var error = Assert.Throws<TargetInvocationException>(() => Invoke(service, "OnStart", (object)Array.Empty<string>()));
        Assert.That(error!.InnerException, Is.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    [Category("Integration")]
    public void UnstartedService_CanReceivePowerAndRepeatedShutdownNotifications()
    {
        using var service = new NetworkMonitoringService();
        Assert.Multiple(() =>
        {
            Assert.That(service.ServiceName, Is.EqualTo(NetworkServiceProtocol.ServiceName));
            Assert.That(service.CanStop && service.CanShutdown && service.CanHandlePowerEvent, Is.True);
            Assert.That(service.AutoLog, Is.False);
        });
        foreach (var status in new[] { PowerBroadcastStatus.Suspend, PowerBroadcastStatus.ResumeAutomatic,
                     PowerBroadcastStatus.ResumeSuspend, PowerBroadcastStatus.PowerStatusChange })
            Assert.That(Invoke(service, "OnPowerEvent", status), Is.True);
        Assert.DoesNotThrow(() => Invoke(service, "OnShutdown"));
        Assert.DoesNotThrow(() => Invoke(service, "OnStop"));
    }

    private static object? Invoke(NetworkMonitoringService service, string method, params object[] arguments) =>
        typeof(NetworkMonitoringService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, arguments);
}
