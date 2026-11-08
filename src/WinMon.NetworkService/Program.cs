using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using WinMon.Monitoring;
using WinMon.Diagnostics;

namespace WinMon.NetworkService;

internal static class Program
{
    internal static int Main(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (args.Length != 0 || identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true)
        {
            Console.Error.WriteLine("This executable must be started by SCM as the provisioned LocalSystem network service.");
            return 1;
        }
        using var log = DiagnosticLog.StartService();
        Trace.TraceInformation("Service.ProcessStart version={0} runtime={1} windows={2}",
            typeof(Program).Assembly.GetName().Version!, Environment.Version, Environment.OSVersion.Version);
        using var service = new NetworkMonitoringService();
        ServiceBase.Run(service);
        return service.ExitCode;
    }
}

internal sealed class NetworkMonitoringService : ServiceBase
{
    private CancellationTokenSource? _lifetime;
    private NetworkCollectorManager? _collectors;
    private Task? _run;
    private readonly object _state = new();

    internal NetworkMonitoringService()
    {
        ServiceName = NetworkServiceProtocol.ServiceName;
        CanStop = true;
        CanShutdown = true;
        CanHandlePowerEvent = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        Trace.TraceInformation("Service.Starting");
        try
        {
            var users = ServicePolicy.Load();
            ServiceProcessSecurity.Apply(users);
            _lifetime = new CancellationTokenSource();
            _collectors = new NetworkCollectorManager(users, () => new EtwNetworkCollector(users));
            var server = new NetworkPipeServer(users, _collectors.SubscribeAsync, _collectors.Snapshot);
            _run = RunAsync(server, _lifetime.Token);
            Trace.TraceInformation("Service.ListenerStarted");
        }
        catch (Exception exception)
        {
            Trace.TraceError("Service.StartFailed: {0}", exception);
            throw;
        }
    }

    private async Task RunAsync(NetworkPipeServer server, CancellationToken token)
    {
        try
        {
            await server.RunAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Trace.TraceError("Network service listener failed: {0}", ex);
            ExitCode = 1;
            // Do not call OnStop while it is waiting for this task.
            _ = Task.Run(Stop);
        }
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        Trace.TraceInformation("Service.Power state={0}", powerStatus);
        NetworkCollectorManager? collectors;
        CancellationToken token;
        lock (_state)
        {
            collectors = _collectors;
            if (collectors is null || _lifetime is null) return true;
            token = _lifetime.Token;
        }
        if (powerStatus == PowerBroadcastStatus.Suspend ||
            powerStatus is PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend)
            _ = ApplyPowerStateAsync(collectors, powerStatus == PowerBroadcastStatus.Suspend, token);
        return true;
    }

    private static async Task ApplyPowerStateAsync(NetworkCollectorManager collectors, bool suspended, CancellationToken token)
    {
        try { await collectors.SetSuspendedAsync(suspended, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { Trace.TraceError("Network collector power transition failed: {0}", ex); }
    }

    protected override void OnShutdown() => OnStop();

    protected override void OnStop()
    {
        CancellationTokenSource? lifetime;
        Task? run;
        NetworkCollectorManager? collectors;
        lock (_state)
        {
            lifetime = _lifetime;
            run = _run;
            collectors = _collectors;
            _lifetime = null;
            _run = null;
            _collectors = null;
        }
        if (lifetime is null) return;
        Trace.TraceInformation("Service.Stopping");
        lifetime.Cancel();
        try
        {
            if (run is not null && !run.Wait(TimeSpan.FromSeconds(15))) ExitCode = 1;
            if (collectors is not null)
                collectors.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ExitCode = 1;
            Trace.TraceError("Network service shutdown failed: {0}", ex);
        }
        finally
        {
            lifetime.Dispose();
            Trace.TraceInformation("Service.Stopped code={0}", ExitCode);
        }
    }
}
