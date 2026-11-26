using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using MonCore.Windows.Diagnostics;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

internal static class Program
{
    internal static int Main(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (args.Length != 0 || identity.User?.IsWellKnown(WellKnownSidType.LocalSystemSid) != true)
        {
            Console.Error.WriteLine("This executable must be started by SCM as the provisioned LocalSystem sensor service.");
            return 1;
        }

        using var log = DiagnosticLog.StartService("SensorService");
        using var service = new SensorMonitoringService();
        ServiceBase.Run(service);
        return service.ExitCode;
    }
}

internal sealed class SensorMonitoringService : ServiceBase
{
    private readonly object _state = new();
    private CancellationTokenSource? _lifetime;
    private SensorCollectorManager? _collectors;
    private Task? _listener;

    internal SensorMonitoringService()
    {
        ServiceName = SensorServiceProtocol.ServiceName;
        CanStop = true;
        CanShutdown = true;
        CanHandlePowerEvent = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            var users = ProtectedServicePolicy.LoadAllowedUsers(ProtectedServiceEndpoints.Sensor);
            ProtectedServiceProcess.ApplySecurity(ProtectedServiceEndpoints.Sensor, users);
            _lifetime = new CancellationTokenSource();
            _collectors = new SensorCollectorManager(users, () => new LibreHardwareMonitorProvider());
            _listener = RunListenerAsync(new SensorPipeServer(users, _collectors.SubscribeAsync, _collectors.Snapshot),
                _lifetime.Token);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Sensor service start failed: {0}", exception);
            throw;
        }
    }

    protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
    {
        if (powerStatus is not (PowerBroadcastStatus.Suspend or PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend))
            return true;
        SensorCollectorManager? collectors;
        CancellationToken token;
        lock (_state)
        {
            collectors = _collectors;
            if (collectors is null || _lifetime is null) return true;
            token = _lifetime.Token;
        }
        _ = ApplyPowerStateAsync(collectors, powerStatus == PowerBroadcastStatus.Suspend, token);
        return true;
    }

    protected override void OnShutdown() => OnStop();

    protected override void OnStop()
    {
        CancellationTokenSource? lifetime;
        Task? listener;
        SensorCollectorManager? collectors;
        lock (_state)
        {
            lifetime = _lifetime;
            listener = _listener;
            collectors = _collectors;
            _lifetime = null;
            _listener = null;
            _collectors = null;
        }
        if (lifetime is null) return;
        lifetime.Cancel();
        try
        {
            var listenerDrain = listener ?? Task.CompletedTask;
            var collectorDrain = collectors?.DisposeAsync().AsTask() ?? Task.CompletedTask;
            Task.WhenAll(listenerDrain, collectorDrain).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            ExitCode = 1;
            Trace.TraceError("Sensor service shutdown failed: {0}", exception);
        }
        finally { lifetime.Dispose(); }
    }

    private async Task RunListenerAsync(SensorPipeServer server, CancellationToken token)
    {
        try { await server.RunAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("Sensor service listener failed: {0}", exception);
            ExitCode = 1;
            _ = Task.Run(Stop);
        }
    }

    private static async Task ApplyPowerStateAsync(SensorCollectorManager collectors, bool suspended, CancellationToken token)
    {
        try { await collectors.SetSuspendedAsync(suspended, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
        catch (Exception exception) { Trace.TraceError("Sensor service power transition failed: {0}", exception); }
    }
}
