using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using MonCore.Windows.Diagnostics;
using MonCore.Windows.Monitoring;

namespace WinMon.Monitoring;

/// <summary>Runs the signed, elevated CPU-sensor setup helper over a private authenticated pipe.</summary>
internal sealed class SensorSetupBroker
{
    private static readonly (string Resource, string File)[] Assets =
    [
        ("WinMon.SensorSetup.exe", "WinMon.SensorSetup.exe"),
        ("WinMon.SensorSetup.Service.zip", "sensor-service.zip"),
        ("WinMon.SensorSetup.Manifest.json", "sensor-service-manifest.json"),
        ("WinMon.SensorSetup.Manifest.sig", "sensor-service-manifest.sig")
    ];

    internal static bool IsAvailable => Supported && Assets.All(asset =>
        typeof(SensorSetupBroker).Assembly.GetManifestResourceInfo(asset.Resource) is not null);

    /// <summary>The installed service predates this app, so setup would replace it with the bundled version.</summary>
    internal static bool ServiceOutdated => IsAvailable &&
        ProtectedServiceEndpoints.Sensor.IsInstalledOlderThan(typeof(SensorSetupBroker).Assembly.GetName().Version!);

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
        ArgumentNullException.ThrowIfNull(verify);
        if (!IsAvailable) throw new InvalidOperationException("Signed sensor setup is unavailable.");
        if (operation is not ("install" or "remove")) throw new ArgumentException("Invalid setup operation.", nameof(operation));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(operation == "install" ? TimeSpan.FromMinutes(4) : TimeSpan.FromSeconds(90));
        var root = Path.Combine(Path.GetTempPath(), "WinMon.SensorSetup." + Guid.NewGuid().ToString("N"));
        var pipeName = ProtectedServiceEndpoints.Sensor.SetupPipePrefix + Guid.NewGuid().ToString("N");
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var pipe = NetworkSetupPipe.Create(pipeName);
        Process? process = null;
        var waitForHelper = false;
        try
        {
            await Task.Run(() => Extract(root, timeout.Token), timeout.Token).ConfigureAwait(false);
            var version = typeof(SensorSetupBroker).Assembly.GetName().Version!.ToString(3);
            using var package = await Task.Run(() => NetworkSetupPackage.Open(root, version,
                ProtectedServiceEndpoints.SensorPackage), timeout.Token).ConfigureAwait(false);
            var startInfo = new ProcessStartInfo(package.HelperStream.Name)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"--pipe {pipeName} --token {nonce}", WorkingDirectory = root
            };
            process = await Task.Run(() => Process.Start(startInfo) ??
                throw new InvalidOperationException("Sensor setup did not start."), CancellationToken.None)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            _ = process.Handle;
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (NetworkSetupPipe.ClientProcessId(pipe) != process.Id || process.HasExited)
                throw new UnauthorizedAccessException("Unexpected sensor setup process.");
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupRequest(NetworkSetupWire.Version, nonce, operation),
                NetworkSetupWireJsonContext.Default.NetworkSetupRequest, timeout.Token).ConfigureAwait(false);
            await ExpectAsync(pipe, "ready", timeout.Token).ConfigureAwait(false);
            await verify(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupCommit(NetworkSetupWire.Version, true),
                NetworkSetupWireJsonContext.Default.NetworkSetupCommit, timeout.Token).ConfigureAwait(false);
            await ExpectAsync(pipe, "complete", timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("Sensor setup failed.");
            if (operation == "install")
            {
                try { LogDirectories.ValidateAppPath(); LogDirectories.ValidateServicePath(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
                { Trace.TraceWarning("Sensor.SharedLogProvisionUnavailable: {0}", error); }
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
            if (waitForHelper && process is { HasExited: false })
            {
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(exitTimeout.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            process?.Dispose();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static async Task ExpectAsync(Stream pipe, string phase, CancellationToken token)
    {
        var reply = await NetworkSetupWire.ReadAsync(pipe, NetworkSetupWireJsonContext.Default.NetworkSetupReply, token)
            .ConfigureAwait(false);
        if (reply.Version != NetworkSetupWire.Version) throw new InvalidDataException("Unexpected sensor setup response.");
        if (reply.Phase == "failed" && IsFailurePhase(reply.FailurePhase) && reply.ErrorCode != 0)
            throw new SensorSetupFailedException(reply.FailurePhase!, reply.ErrorCode);
        if (reply.Phase != phase || reply.FailurePhase is not null || reply.ErrorCode != 0)
            throw new InvalidDataException("Unexpected sensor setup response.");
    }

    private static bool IsFailurePhase(string? phase) => phase is "prepare" or "archive" or "helper" or "policy" or
        "protect" or "inventory" or "verify" or "stop" or "swap" or "service" or "registration" or "pawnio" or "unknown";

    private static void Extract(string root, CancellationToken token)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User ?? throw new UnauthorizedAccessException("Missing requester identity.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var allowed in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                     new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(allowed, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
        FileSystemAclExtensions.CreateDirectory(security, root);
        foreach (var asset in Assets)
        {
            token.ThrowIfCancellationRequested();
            using var source = typeof(SensorSetupBroker).Assembly.GetManifestResourceStream(asset.Resource)
                ?? throw new InvalidDataException("Missing signed sensor setup asset.");
            using var destination = new FileStream(Path.Combine(root, asset.File), FileMode.CreateNew,
                FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
        }
    }
}

internal sealed class SensorSetupFailedException(string phase, int errorCode)
    : InvalidOperationException($"Sensor setup failed during {phase} ({errorCode}).")
{
    internal string Phase { get; } = phase;
    internal int ErrorCode { get; } = errorCode;
}
