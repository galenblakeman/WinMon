using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

internal sealed class NetworkSetupBroker
{
    private static readonly (string Resource, string File)[] Assets =
    [
        ("WinMon.NetworkSetup.exe", "WinMon.NetworkSetup.exe"),
        ("WinMon.NetworkSetup.Service.zip", "network-service.zip"),
        ("WinMon.NetworkSetup.Manifest.json", "network-service-manifest.json"),
        ("WinMon.NetworkSetup.Manifest.sig", "network-service-manifest.sig")
    ];

    internal static bool IsAvailable => Supported && Assets.All(asset =>
        typeof(NetworkSetupBroker).Assembly.GetManifestResourceInfo(asset.Resource) is not null);

    internal static bool Supported
    {
        get
        {
#if WINMON_STORE
            return false;
#else
            return true;
#endif
        }
    }

    internal async Task RunAsync(string operation, Func<CancellationToken, Task> verify, CancellationToken token)
    {
        if (!IsAvailable) throw new InvalidOperationException("Signed network setup is unavailable.");
        if (operation is not ("install" or "remove")) throw new ArgumentException("Invalid setup operation.", nameof(operation));
        Trace.TraceInformation("Network.SetupStart operation=" + operation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var root = Path.Combine(Path.GetTempPath(), "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N"));
        var pipeName = "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var pipe = NetworkSetupPipe.Create(pipeName);
        Process? process = null;
        var waitForHelper = false;
        try
        {
            await Task.Run(() => Extract(root, timeout.Token), timeout.Token).ConfigureAwait(false);
            var version = typeof(NetworkSetupBroker).Assembly.GetName().Version!.ToString(3);
            using var package = await Task.Run(() => NetworkSetupPackage.Open(root, version), timeout.Token).ConfigureAwait(false);
            var launch = Task.Run(() => Process.Start(new ProcessStartInfo(package.HelperStream.Name)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--pipe {pipeName} --token {nonce}",
                WorkingDirectory = root
            }) ?? throw new InvalidOperationException("Network setup did not start."), CancellationToken.None);
            Process launched;
            try { launched = await launch.WaitAsync(timeout.Token).ConfigureAwait(false); }
            catch
            {
                // UAC itself cannot be cancelled by an unelevated caller. A late helper finds no pipe and exits.
                _ = launch.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) task.Result.Dispose();
                    else _ = task.Exception;
                }, TaskScheduler.Default);
                throw;
            }
            process = launched;
            // Hold the process handle across the exchange so a reused PID cannot authenticate a replacement.
            _ = process.Handle;
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (NetworkSetupPipe.ClientProcessId(pipe) != process.Id || process.HasExited)
                throw new UnauthorizedAccessException("Unexpected network setup process.");
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupRequest(NetworkSetupWire.Version, nonce, operation),
                NetworkSetupWireJsonContext.Default.NetworkSetupRequest, timeout.Token).ConfigureAwait(false);
            await ExpectAsync(pipe, "ready", timeout.Token).ConfigureAwait(false);
            // Dropping this pipe on cancellation or failed health verification makes setup roll back.
            await verify(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupCommit(NetworkSetupWire.Version, true),
                NetworkSetupWireJsonContext.Default.NetworkSetupCommit, timeout.Token).ConfigureAwait(false);
            await ExpectAsync(pipe, "complete", timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("Network setup failed.");
            Trace.TraceInformation("Network.SetupComplete operation=" + operation);
            if (operation == "install")
            {
                try
                {
                    Diagnostics.LogDirectories.ValidateAppPath();
                    Diagnostics.LogDirectories.ValidateServicePath();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    Trace.TraceWarning("Logging.SharedProvisionUnavailable: {0}", exception);
                }
            }
        }
        catch
        {
            waitForHelper = true;
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (waitForHelper && process is not null && !process.HasExited)
            {
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            process?.Dispose();
            // Only this generated directory is removed; never accept a helper-supplied cleanup path.
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static async Task ExpectAsync(Stream pipe, string phase, CancellationToken token)
    {
        var reply = await NetworkSetupWire.ReadAsync(pipe, NetworkSetupWireJsonContext.Default.NetworkSetupReply, token).ConfigureAwait(false);
        if (reply.Version != NetworkSetupWire.Version) throw new InvalidDataException("Unexpected network setup response.");
        if (reply.Phase == "failed" && IsFailurePhase(reply.FailurePhase) && reply.ErrorCode != 0)
        {
            // Only these protocol-defined phases may enter the log template.
            System.Diagnostics.Trace.TraceError("Network.SetupFailed phase=" + reply.FailurePhase + " code={0}", reply.ErrorCode);
            throw new NetworkSetupFailedException(reply.FailurePhase!, reply.ErrorCode);
        }
        if (reply.Phase != phase || reply.FailurePhase is not null || reply.ErrorCode != 0) throw new InvalidDataException("Unexpected network setup response.");
    }

    private static bool IsFailurePhase(string? phase) => phase is "prepare" or "archive" or "helper" or "policy" or "protect" or "inventory" or "verify" or "stop" or "swap" or "service" or "registration" or "unknown";

    private static void Extract(string root, CancellationToken token)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("Missing requester identity.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var allowed in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(allowed, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        FileSystemAclExtensions.CreateDirectory(security, root);
        foreach (var asset in Assets)
        {
            token.ThrowIfCancellationRequested();
            using var source = typeof(NetworkSetupBroker).Assembly.GetManifestResourceStream(asset.Resource)
                ?? throw new InvalidDataException("Missing signed network setup asset.");
            using var destination = new FileStream(Path.Combine(root, asset.File), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
        }
    }

}

internal sealed class NetworkSetupFailedException(string phase, int errorCode) : InvalidOperationException($"Network setup failed during {phase} ({errorCode}).")
{
    internal string Phase { get; } = phase;
    internal int ErrorCode { get; } = errorCode;
}
