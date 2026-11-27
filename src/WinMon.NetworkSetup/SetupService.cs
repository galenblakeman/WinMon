// Close handles and cancel callbacks synchronously before releasing ownership or continuing shutdown.
// ReSharper disable MethodHasAsyncOverload

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using MonCore.Windows.Monitoring;

namespace WinMon.NetworkSetup;

internal static class SetupService
{
    internal static bool RestartRequired { get; private set; }

    internal static async Task<SetupTransaction> BeginAsync(string operation, string requesterSid,
        NetworkSetupPackage package, CancellationToken token, SetupHost? host = null)
    {
        if (operation is not ("install" or "remove")) throw new InvalidDataException("Unsupported setup operation.");
        host ??= new SetupHost();
        host.PrepareParent();
        var lease = await AcquireLockAsync(token, host).ConfigureAwait(false);
        try
        {
            host.EnsureOwnedOrAbsent();
#if SENSOR_SETUP
            if (RequiresSensorPrerequisite(operation))
                await host.EnsureSensorPrerequisite(host.InstallDirectory, token).ConfigureAwait(false);
#endif
            var state = host.CaptureState();
            var complete = true;
            if (Directory.Exists(host.InstallDirectory))
                InstalledFiles.Validate(host.InstallDirectory, true, out complete);
            else if (state.Exists) throw new UnauthorizedAccessException("Missing owned service installation.");
            var registration = host.CaptureRegistration();
            return operation == "install"
                ? Install(requesterSid, package, state, registration, lease, token, !complete, host)
                : Remove(lease, host);
        }
        catch (OperationCanceledException)
        {
            lease.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            lease.Dispose();
            throw SetupFailure.Wrap("prepare", exception);
        }
    }

    internal static async Task RemoveAsync(CancellationToken token, SetupHost? host = null)
    {
        host ??= new SetupHost();
        host.PrepareParent();
        using var lease = await AcquireLockAsync(token, host).ConfigureAwait(false);
        host.EnsureOwnedOrAbsent();
        var state = host.CaptureState();
        if (Directory.Exists(host.InstallDirectory))
            InstalledFiles.Validate(host.InstallDirectory, true, out _);
        else if (state.Exists) throw new UnauthorizedAccessException("Missing owned service installation.");
        _ = host.CaptureRegistration();
        await using var transaction = Remove(null, host);
        await transaction.CommitAsync(token).ConfigureAwait(false);
    }

    internal static async Task RevokeAsync(string sid, CancellationToken token, SetupHost? host = null)
    {
        sid = AuthorizationPolicy.ValidateSid(sid);
        host ??= new SetupHost();
        host.PrepareParent();
        var lease = await AcquireLockAsync(token, host).ConfigureAwait(false);
        try
        {
            host.EnsureOwnedOrAbsent();
            var state = host.CaptureState();
            if (!Directory.Exists(host.InstallDirectory) || !state.Exists)
                throw new UnauthorizedAccessException("Missing owned service installation.");
            InstalledFiles.Validate(host.InstallDirectory);
            _ = host.CaptureRegistration();
            var policy = Path.Combine(host.InstallDirectory, SetupPaths.PolicyFileName);
            var users = AuthorizationPolicy.RemoveAuthorizedUser(AuthorizationPolicy.Load(policy), sid);
            if (users.Count == 0)
            {
                await using var remove = Remove(lease, host);
                lease = null!;
                await remove.CommitAsync(token).ConfigureAwait(false);
                return;
            }

            var replacement = StagingPath("revoke-policy", host.InstallDirectory);
            var backup = StagingPath("revoke-backup", host.InstallDirectory);
            AuthorizationPolicy.Save(replacement, users);
            RevocationTransaction.Execute(state, replacement, policy, backup,
                () => host.StopOwnedIfRunning(token),
                () => host.CreateOrUpdateOwned(users),
                () => host.StartAndVerify(token),
                () => host.RestoreService(state, CancellationToken.None),
                (source, destination) => AuthorizationPolicy.Save(destination, AuthorizationPolicy.Load(source)));
        }
        // Ownership transfer sets lease to null before this finally runs.
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        finally
        {
            lease?.Dispose();
        }
    }

    private static SetupTransaction Install(string sid, NetworkSetupPackage package, NativeService.ServiceState state,
        Dictionary<string, object>? registration, IDisposable lease, CancellationToken token, bool preserveBackup,
        SetupHost host)
    {
        AuthorizationPolicy.ValidateSid(sid);
        var serviceExecutable = Path.Combine(host.InstallDirectory, SetupPaths.ServiceExecutableName);
        if (File.Exists(serviceExecutable))
            ValidateUpgrade(FileVersionInfo.GetVersionInfo(serviceExecutable).FileVersion,
                package.Manifest.Version);
        else if (registration?.GetValueOrDefault("DisplayVersion") is string version)
            ValidateUpgrade(version, package.Manifest.Version);
        var stage = StagingPath("staging", host.InstallDirectory);
        if (Directory.Exists(stage) || File.Exists(stage)) throw new IOException("Staging path already exists.");
        string? backup = null;
        var swapped = false;
        var stopped = false;
        var serviceChanged = false;

        void Rollback()
        {
            if (serviceChanged) host.StopOwnedIfRunning(CancellationToken.None);
            if (swapped) DeleteTree(host.InstallDirectory);
            if (backup is not null && Directory.Exists(backup)) Directory.Move(backup, host.InstallDirectory);
            if (stopped || serviceChanged) host.RestoreService(state, CancellationToken.None);
            host.RestoreRegistration(registration);
            DeleteTree(stage);
        }

        return SetupTransaction.Start(() =>
        {
            token.ThrowIfCancellationRequested();
            // The protected parent prevents an unelevated process from replacing staging paths during extraction.
            package.ArchiveStream.Position = 0;
            SetupFailure.At("archive", () => PackageExtraction.ExtractServiceArchive(package.ArchiveStream, stage));
            package.HelperStream.Position = 0;
            using (var output = ProtectedInstallation.CreateFile(Path.Combine(stage, SetupPaths.SetupExecutableName),
                       FileMode.CreateNew, FileAccess.Write, FileShare.None))
                // At invokes its callback synchronously before output leaves the using scope.
                // ReSharper disable once AccessToDisposedClosure
            {
                SetupFailure.At("helper", () => package.HelperStream.CopyTo(output));
            }

            var policy = Path.Combine(host.InstallDirectory, SetupPaths.PolicyFileName);
            var users = AuthorizationPolicy.AddAuthorizedUser(
                File.Exists(policy) ? AuthorizationPolicy.Load(policy) : [], sid);
            SetupFailure.At("policy",
                () => AuthorizationPolicy.Save(Path.Combine(stage, SetupPaths.PolicyFileName), users));
            SetupFailure.At("protect", () => ProtectTree(stage));
            SetupFailure.At("inventory", () => InstalledFiles.Write(stage));
            SetupFailure.At("protect", () => ProtectTree(stage));
            SetupFailure.At("verify",
                () => host.VerifyPublisher(Path.Combine(stage, SetupPaths.ServiceExecutableName)));
            token.ThrowIfCancellationRequested();
            // Mark before the native call: it can stop successfully and then throw while waiting for status.
            stopped = true;
            SetupFailure.At("stop", () => host.StopOwnedIfRunning(token));
            if (Directory.Exists(host.InstallDirectory))
            {
                backup = StagingPath(preserveBackup ? "quarantine" : "backup", host.InstallDirectory);
                SetupFailure.At("swap", () => Directory.Move(host.InstallDirectory, backup));
            }

            SetupFailure.At("swap", () => Directory.Move(stage, host.InstallDirectory));
            swapped = true;
            serviceChanged = true;
            SetupFailure.At("service", () => host.CreateOrUpdateOwned(users));
            try
            {
                host.ProvisionLogs(sid);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or SecurityException)
            {
                // Logging must not block service installation; the app retains its per-user fallback.
                Trace.TraceWarning("Logging.SharedProvisionFailed: {0}", exception);
            }

            SetupFailure.At("service", () => host.StartAndVerify(token));
            SetupFailure.At("registration", host.WriteRegistration);
        }, Rollback, () =>
        {
            if (backup is not null && !preserveBackup) DeleteTree(backup);
        }, lease);
    }

    private static SetupTransaction Remove(IDisposable? lease, SetupHost host)
    {
        // No mutation until the requesting client acknowledges the all-users removal.
        return SetupTransaction.Start(() => { }, () => { }, () =>
        {
            host.StopAndDeleteOwned(CancellationToken.None);
            host.RestoreRegistration(null);
            if (Directory.Exists(host.InstallDirectory))
            {
                InstalledFiles.Validate(host.InstallDirectory, true, out _);
                RetireInstallation(host.InstallDirectory, StagingPath("removed", host.InstallDirectory), DeleteInstallation);
            }
        }, lease);
    }

    internal static string StagingPath(string kind, string? installDirectory = null)
    {
        return Path.Combine(Path.GetDirectoryName(installDirectory ?? SetupPaths.InstallDirectory)!,
#if SENSOR_SETUP
            $".SensorMonitor-{kind}-{Guid.NewGuid():N}");
#else
            $".NetworkMonitor-{kind}-{Guid.NewGuid():N}");
#endif
    }

#if SENSOR_SETUP
    internal static bool RequiresSensorPrerequisite(string operation) => operation == "install";
#endif

    internal static void ValidateUpgrade(string? installed, string requested)
    {
        if (!Version.TryParse(installed, out var previous) || !Version.TryParse(requested, out var next))
            throw new InvalidDataException("Cannot verify installed service version.");

        static Version Normalize(Version value)
        {
            return new Version(value.Major, value.Minor, Math.Max(0, value.Build), Math.Max(0, value.Revision));
        }

        if (Normalize(previous) > Normalize(next))
            throw new InvalidOperationException("A newer network service is installed; setup cannot downgrade it.");
    }

    private static async Task<FileStream> AcquireLockAsync(CancellationToken token, SetupHost host)
    {
        var path = Path.Combine(Path.GetDirectoryName(host.InstallDirectory)!,
#if SENSOR_SETUP
            ".sensor-setup.lock");
#else
            ".network-setup.lock");
#endif
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path)) ProtectedInstallation.Check(new FileInfo(path));
            try
            {
                return ProtectedInstallation.CreateFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    internal static void ProtectTree(string root)
    {
        // New children inherit this ACL at creation; explicitly pin owners and remove unexpected ACEs too.
        new DirectoryInfo(root).SetAccessControl(ProtectedInstallation.Security());
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        var count = 0;
        while (pending.TryPop(out var directory))
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (++count > 4096 || (item.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Invalid staging tree.");
                if (item is DirectoryInfo child)
                {
                    child.SetAccessControl(ProtectedInstallation.Security());
                    pending.Push(child);
                }
                else
                {
                    var security = new FileSecurity();
                    security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
                    security.SetAccessRuleProtection(true, false);
                    foreach (var (sid, rights) in new[]
                             {
                                 (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                                 (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                                 (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute)
                             })
                        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
                            AccessControlType.Allow));
                    ((FileInfo)item).SetAccessControl(security);
                }
            }
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var item in ProtectedInstallation.Tree(path).OrderByDescending(item => item.FullName.Length))
            if (item is DirectoryInfo directory) directory.Delete();
            else ((FileInfo)item).Delete();
    }

    internal static void RetireInstallation(string root, string retired, Action<string> cleanup)
    {
        // Vacate the canonical path before cleanup can leave a partial inventory behind.
        Directory.Move(root, retired);
        try
        {
            cleanup(retired);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Network.RemovalCleanupDeferred: {0}", exception);
        }
    }

    private static void DeleteInstallation(string root)
    {
        var tree = InstalledFiles.Validate(root, true, out _);
        foreach (var item in tree.OrderByDescending(item => item.FullName.Length))
            try
            {
                if (item is DirectoryInfo directory) directory.Delete();
                else ((FileInfo)item).Delete();
            }
            catch (IOException) when (string.Equals(Environment.ProcessPath, SetupPaths.InstalledSetupExecutable,
                                          StringComparison.OrdinalIgnoreCase) &&
                                      (item is DirectoryInfo || string.Equals(item.FullName,
                                          Path.Combine(root, SetupPaths.SetupExecutableName), StringComparison.OrdinalIgnoreCase)))
            {
                if (!MoveFileEx(item.FullName, null, 4)) throw new Win32Exception(Marshal.GetLastWin32Error());
                RestartRequired = true;
            }
    }

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existing, string? destination, uint flags);
}

internal static class RevocationTransaction
{
    internal static void Execute(NativeService.ServiceState state, string replacement, string policy, string backup,
        Action stop, Action update, Action start, Action restore, Action<string, string> copyPolicy)
    {
        var stopped = false;
        var replaced = false;
        var changed = false;
        var rolledBack = false;
        var committed = false;
        try
        {
            stopped = true;
            stop();
            File.Replace(replacement, policy, backup);
            replaced = true;
            changed = true;
            update();
            if (state.WasRunning) start();
            committed = true;
        }
        catch (Exception failure)
        {
            try
            {
                if (stopped || changed) stop();
                if (replaced && File.Exists(backup))
                {
                    copyPolicy(backup, replacement);
                    File.Replace(replacement, policy, null);
                }

                if (stopped || changed) restore();
                rolledBack = true;
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Revocation and rollback failed; recovery policy was retained.", failure,
                    rollbackFailure);
            }

            throw;
        }
        finally
        {
            if (File.Exists(replacement)) File.Delete(replacement);
            if ((committed || rolledBack) && File.Exists(backup)) File.Delete(backup);
        }
    }
}

internal sealed class SetupTransaction : IAsyncDisposable
{
    private readonly Action _rollback;
    private readonly Action _commit;
    private readonly IDisposable? _lease;
    private bool _committed;
    private bool _disposed;

    private SetupTransaction(Action rollback, Action commit, IDisposable? lease)
    {
        _rollback = rollback;
        _commit = commit;
        _lease = lease;
    }

    internal static SetupTransaction Start(Action deploy, Action rollback, Action commit, IDisposable? lease = null)
    {
        try
        {
            deploy();
            return new SetupTransaction(rollback, commit, lease);
        }
        catch (Exception failure)
        {
            try
            {
                rollback();
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Setup and rollback failed; protected recovery files were retained.",
                    failure, rollbackFailure);
            }
            finally
            {
                lease?.Dispose();
            }

            throw;
        }
    }

    internal Task CommitAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || _committed) throw new InvalidOperationException("Setup transaction already ended.");
        // Once acknowledged, cleanup failure must not roll back a healthy installation or recreate removed files.
        _committed = true;
        _commit();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        try
        {
            if (!_committed) _rollback();
        }
        finally
        {
            _lease?.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class SetupFailure(string phase, int errorCode, Exception? innerException = null)
    : Exception("Network setup failed.", innerException)
{
    internal string Phase { get; } = phase;
    internal int ErrorCode { get; } = errorCode == 0 ? unchecked((int)0x80004005) : errorCode;

    internal static SetupFailure Wrap(string phase, Exception exception)
    {
        return exception is SetupFailure failure
            ? failure
            : new SetupFailure(phase,
                exception is Win32Exception win32 ? win32.NativeErrorCode : exception.HResult,
                exception);
    }

    internal static void At(string phase, Action action)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw Wrap(phase, exception);
        }
    }

    internal static SetupFailure From(Exception exception)
    {
        if (exception is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.OfType<SetupFailure>().FirstOrDefault() is { } staged) return staged;
        while (exception is AggregateException aggregateFailure && aggregateFailure.InnerExceptions.Count == 1)
            exception = aggregateFailure.InnerException!;
        return exception is SetupFailure failure ? failure : Wrap("unknown", exception);
    }
}
