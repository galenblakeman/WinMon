using System.Diagnostics;

// ReSharper disable StringLiteralTypo

namespace WinMon.Updates;

/// <summary>Runs only the staged, published executable as an updater, before normal application startup.</summary>
internal static class UpdateRuntime
{
    internal const string UpdateMutexName = @"Local\WinMon.Update";
    private static string? _resultToken;
    private static string? _resultDirectory;
    internal static bool IsExpectedRestart => UpdateRestartIdentity.IsExpectedRestart(_resultToken);

    /// <summary>The history the previous version saved for this restart, with the token it must carry.</summary>
    internal static (string Path, string Token)? RestartHistory => UpdateRestartIdentity.RestartHistory(_resultToken, _resultDirectory);

    internal static bool IsUpdating()
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return false;
        using var mutex = new Mutex(false, UpdateMutexName);
        try
        {
            if (!mutex.WaitOne(0)) return true;
        }
        catch (AbandonedMutexException)
        {
        }

        mutex.ReleaseMutex();
        return false;
    }

    internal static void AcceptResultArguments(string[] args)
    {
        // Keep ordinary startup arguments on the original rejection path.
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        if (args is not ["--update-result", _, _]) return;
        AcceptResultArguments(args, ref _resultToken, ref _resultDirectory, null, null, null);
    }

    internal static void AcceptResultArguments(string[] args, ref string? resultToken, ref string? resultDirectory, string? stagingRoot, Func<string>? processPath,
        Func<string, EventWaitHandle?>? openHealthy)
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        if (args is not ["--update-result", var directory, var token]) return;
        try
        {
            directory = UpdateFiles.ValidateDirectory(directory, stagingRoot);
            var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
            if (job.Token != token || !SamePath(job.Target, processPath is null ? Environment.ProcessPath! : processPath())) return;
            var eventName = UpdateLauncher.EventName(token, "Healthy");
            EventWaitHandle? healthy;
            if (openHealthy is null)
            {
                if (!EventWaitHandle.TryOpenExisting(eventName, out healthy)) return;
            }
            else
            {
                healthy = openHealthy(eventName);
                if (healthy is null) return;
            }
            healthy.Dispose();
            resultToken = token;
            resultDirectory = directory;
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon update result arguments: {0}", ex);
        }
    }

    internal static void SignalReady()
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        try
        {
            if (_resultToken is not null)
            {
                using var ready = EventWaitHandle.OpenExisting(UpdateLauncher.EventName(_resultToken, "Healthy"));
                ready.Set();
            }

            // Recover a completed replacement after power loss/helper termination, once a real sample proves startup,
            // and remove what earlier sessions' abandoned transactions left behind.
            using var current = Process.GetCurrentProcess();
            var sessionStart = current.StartTime.ToUniversalTime();
            foreach (var directory in Transactions())
                try
                {
                    CompleteInterruptedTransaction(directory, Environment.ProcessPath!, AppInfo.Version, _resultToken,
                        sessionStart);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("WinMon stale update cleanup: {0}", ex.Message);
                }
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon update ready signal: {0}", ex);
        }
    }

    /// <param name="sessionStartUtc">When this process started. Only transactions created before it can be
    /// abandoned: this session has not begun an update of its own when startup is proven.</param>
    /// <param name="directory"></param>
    /// <param name="target"></param>
    /// <param name="currentVersion"></param>
    /// <param name="activeToken">The token of the transaction this session's helper still owns, if any.</param>
    internal static void CompleteInterruptedTransaction(string directory, string target, string currentVersion,
        string? activeToken,
        DateTime? sessionStartUtc = null)
    {
        var earlier = sessionStartUtc is { } start && Directory.GetCreationTimeUtc(directory) < start;
        var jobPath = Path.Combine(directory, "job.json");
        if (!File.Exists(jobPath))
        {
            // The job is written last, so this download was interrupted before it finished.
            if (earlier) TryDeleteDirectory(directory);
            return;
        }

        var job = UpdateFiles.ReadJson<UpdateJob>(jobPath);
        // The live helper owns this transaction until it has committed readiness and removed its backup.
        if (job.Token == activeToken) return;
        var statePath = Path.Combine(directory, "state.json");
        if (!Guid.TryParseExact(job.Token, "N", out _)) return;
        if (!SamePath(job.Target, target))
        {
            CleanupAbandonedForeignTransaction(directory, job, statePath, sessionStartUtc);
            return;
        }
        if (!File.Exists(statePath))
        {
            // Prepared but never applied: the helper writes a state before it changes anything outside this directory.
            if (earlier) TryDeleteDirectory(directory);
            return;
        }

        var status = UpdateFiles.ReadJson<UpdateState>(statePath).Status;
        if (!TryRecoverInterruptedCompanions(job, statePath, earlier, currentVersion, ref status)) return;
        if (FinalizeHealthyReplacement(job, statePath, currentVersion, status)) status = "succeeded";
        CleanupCompletedTransaction(directory, job, status, earlier);
    }

    private static void CleanupAbandonedForeignTransaction(string directory, UpdateJob job, string statePath,
        DateTime? sessionStartUtc)
    {
        // Removed portable/test copies cannot return to clean their own staging folders.
        if (sessionStartUtc is not { } sessionStart ||
            Directory.GetLastWriteTimeUtc(directory) >= sessionStart.AddDays(-7) ||
            Directory.Exists(Path.GetDirectoryName(job.Target)) ||
            IsProcessRunning(job.ParentId, job.ParentStartTicks)) return;
        using var mutex = new Mutex(false, UpdateMutexName);
        try
        {
            if (!mutex.WaitOne(0)) return;
        }
        catch (AbandonedMutexException)
        {
        }

        try
        {
            if (!File.Exists(statePath) || UpdateFiles.ReadJson<UpdateState>(statePath).Status is
                "succeeded" or "failed" or "rolled-back")
                TryDeleteDirectory(directory);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool TryRecoverInterruptedCompanions(UpdateJob job, string statePath, bool earlier,
        string currentVersion, ref string status)
    {
        if (!job.Installed && (job.CliExisted is not null || job.McpExisted is not null) && earlier &&
            UpdateManifest.ParseVersion(job.Version) > UpdateManifest.ParseVersion(currentVersion) &&
            status is "replacing" or "awaiting-ready")
        {
            using var guard = new Mutex(false, UpdateMutexName);
            try
            {
                if (!guard.WaitOne(0)) return false;
            }
            catch (AbandonedMutexException)
            {
            }
            try
            {
                RestoreCompanions(job);
                UpdateFiles.WriteJson(statePath, new UpdateState("rolled-back"));
                status = "rolled-back";
            }
            finally { guard.ReleaseMutex(); }
        }
        return true;
    }

    private static bool FinalizeHealthyReplacement(UpdateJob job, string statePath, string currentVersion,
        string status)
    {
        if (job.Version == currentVersion && status is "replacing" or "awaiting-ready")
        {
            if (job.CliExisted is not null)
                UpdateClient.ValidateExecutableVersion(UpdateFiles.CliJob(job).Target, job.Version);
            if (job.McpExisted is not null)
                UpdateClient.ValidateExecutableVersion(UpdateFiles.McpJob(job).Target, job.Version);
            UpdateFiles.WriteJson(statePath, new UpdateState("succeeded"));
            return true;
        }
        return false;
    }

    private static void CleanupCompletedTransaction(string directory, UpdateJob job, string status, bool earlier)
    {
        if (status is not ("succeeded" or "failed" or "rolled-back")) return;
        if (status != "succeeded" && !earlier) return;
        // The small job and state files stay until the result notice has been shown.
        if (earlier && File.Exists(Path.Combine(directory, "notice-shown")) && File.Exists(job.Target))
        {
            TryDelete(UpdateFiles.BackupPath(job));
            TryDelete(UpdateFiles.StagedPath(job));
            CleanupCompanions(job);
            TryDeleteDirectory(directory);
            return;
        }

        // A backup is the only copy of WinMon while the target is missing.
        if (File.Exists(job.Target)) TryDelete(UpdateFiles.BackupPath(job));
        TryDelete(UpdateFiles.StagedPath(job));
        CleanupCompanions(job);
        TryDelete(Path.Combine(directory, "WinMon.Update.exe"));
        TryDelete(Path.Combine(directory, "WinMon.exe"));
        TryDelete(Path.Combine(directory, "WinMon.Cli.exe"));
        TryDelete(Path.Combine(directory, "WinMon.Mcp.exe"));
        TryDelete(Path.Combine(directory, "verified-cli.exe"));
        TryDelete(Path.Combine(directory, "verified.exe"));
        TryDelete(Path.Combine(directory, $"WinMon-{job.Version}-win-x64.zip"));
        TryDelete(Path.Combine(directory, $"WinMon-{job.Version}-Setup-x64.exe"));
    }

    internal static string? ConsumeNotice()
        => ConsumeNotice(null, null);

    internal static string? ConsumeNotice(Func<IEnumerable<string>>? transactions, Func<string>? processPath)
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return null;
        foreach (var directory in transactions is null ? Transactions() : transactions())
            try
            {
                var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
                if (!SamePath(job.Target, processPath is null ? Environment.ProcessPath! : processPath())) continue;
                var statePath = Path.Combine(directory, "state.json");
                if (!File.Exists(statePath) || File.Exists(Path.Combine(directory, "notice-shown"))) continue;
                var state = UpdateFiles.ReadJson<UpdateState>(statePath);
                var text = state.Status switch
                {
                    "succeeded" => Strings.Format(Strings.UpdateSucceeded, job.Version),
                    "rolled-back" => Strings.UpdateRolledBack,
                    "failed" => Strings.UpdateFailed,
                    _ => null
                };
                if (text is null) continue;
                File.WriteAllText(Path.Combine(directory, "notice-shown"), "");
                return text;
            }
            catch (Exception ex)
            {
                Trace.TraceError("WinMon update notice: {0}", ex);
            }

        return null;
    }

    private static IEnumerable<string> Transactions()
    {
        if (!Directory.Exists(UpdateClient.UpdateRoot)) return [];
        try
        {
            return
            [
                .. Directory.GetDirectories(UpdateClient.UpdateRoot).Where(d =>
                    Guid.TryParseExact(Path.GetFileName(d), "N", out _) &&
                    (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
            ];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal static int RunHelper(string directory, string token, UpdateHelperHost? host = null)
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return 1;
        host ??= new UpdateHelperHost();
        UpdateJob? job = null;
        IUpdateHelperProcess? child = null;
        var replaced = false;
        var validatedJob = false;
        var validatedDirectory = false;
        try
        {
            directory = UpdateFiles.ValidateDirectory(directory, host.StagingRoot);
            validatedDirectory = true;
            job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
            var download = ValidateHelperDownload(directory, token, job, host);
            // Bind the job to the process that requested it, including its start time and executable path.
            using var parent = host.OpenProcess(job.ParentId);
            if (parent.StartTicks != job.ParentStartTicks || !SamePath(parent.FileName, job.Target))
                throw new InvalidDataException("The original WinMon process changed.");
            validatedJob = true;
            using var mutex = host.CreateMutex();
            bool owned;
            try
            {
                owned = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            if (!owned) throw new IOException("Another update is running.");
            try
            {
                using var ready = host.Signal(token, "Ready");
                using var commit = host.Signal(token, "Commit");
                using var cancel = host.Signal(token, "Cancel");
                ready.Set();
                if (!WaitForUpdateHandoff(parent, commit, cancel)) return 1;
                host.WriteState(directory, new UpdateState("replacing"));
                if (job.Installed)
                {
                    RunUpdateInstaller(directory, download, job, host);
                }
                else
                {
                    var extracted = Path.Combine(directory, "verified.exe");
                    UpdateClient.ExtractExecutableAsync(download, extracted, job.Version, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    File.Copy(extracted, UpdateFiles.StagedPath(job), false);
                    PortableUpdate.Apply(UpdateFiles.StagedPath(job), job.Target, UpdateFiles.BackupPath(job));
                    replaced = true;
                }

                host.WriteState(directory, new UpdateState("awaiting-ready"));
                using var healthy = host.Signal(token, "Healthy", create: true);
                // Only the transaction's authenticated restart may pass the startup guard until readiness.
                child = Launch(job, directory, host);
                WaitForHealthyUpdate(child, healthy, host);
                // The new version is healthy, so no failure from here may roll it back. Its first poll for the
                // result notice can briefly hold the state file; if the write never lands, the next launch of this
                // version records the success itself (CompleteInterruptedTransaction) and removes the backup.
                for (var attempt = 1;; attempt++)
                    try
                    {
                        host.WriteState(directory, new UpdateState("succeeded"));
                        break;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        if (attempt < 5)
                        {
                            host.Sleep(100);
                            continue;
                        }

                        Trace.TraceWarning("WinMon updater could not record the healthy update: {0}", ex.Message);
                        return 0;
                    }

                TryDelete(UpdateFiles.BackupPath(job));
                TryDelete(download);
                TryDelete(Path.Combine(directory, "WinMon.exe"));
                TryDelete(Path.Combine(directory, "verified.exe"));
                return 0;
            }
            finally
            {
                if (owned) mutex.ReleaseMutex();
            }
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon updater: {0}", ex);
            if (validatedDirectory)
                try
                {
                    File.WriteAllText(Path.Combine(directory, "helper-error.log"), ex.ToString());
                }
                catch (Exception logError)
                {
                    Trace.TraceWarning("WinMon could not save update error: {0}", logError.Message);
                }

            if (job is not null && validatedJob)
                RecoverHelperFailure(directory, job, child, replaced, ex, host);

            return 1;
        }
        finally
        {
            child?.Dispose();
        }
    }

    private static string ValidateHelperDownload(string directory, string token, UpdateJob job, UpdateHelperHost host)
    {
        if (job.Token != token || !Guid.TryParseExact(token, "N", out _) ||
            !SamePath(Path.GetDirectoryName(host.ProcessPath)!, directory) ||
            !Path.GetFileName(job.Target).Equals("WinMon.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid update job.");
        var bytes = File.ReadAllBytes(Path.Combine(directory, "update-manifest.json"));
        var signature = File.ReadAllText(Path.Combine(directory, "update-manifest.sig"));
        var manifest = UpdateManifest.Verify(bytes, signature, host.PublicKey, job.Version);
        if (UpdateManifest.ParseVersion(job.Version) <= UpdateManifest.ParseVersion(host.CurrentVersion))
            throw new InvalidDataException("Refusing an update downgrade.");
        if (job.Installed != host.IsInstalled(job.Target))
            throw new InvalidDataException("Installation registration changed.");
        var asset = job.Installed ? manifest.Installer : manifest.Portable;
        var download = Path.Combine(directory, asset.Name);
        UpdateManifest.VerifyFileAsync(download, asset, CancellationToken.None).GetAwaiter().GetResult();
        return download;
    }

    private static bool WaitForUpdateHandoff(IUpdateHelperProcess parent, WaitHandle commit, WaitHandle cancel)
    {
        // The app commits after its exit work, which has no fixed bound: a running service restart defers
        // its close. Wait while it lives; it signals Commit or Cancel before it exits.
        var signal = WaitHandle.WaitTimeout;
        while (signal == WaitHandle.WaitTimeout)
        {
            var exited = parent.HasExited;
            signal = WaitHandle.WaitAny([commit, cancel], exited ? 0 : 250);
            if (exited && signal == WaitHandle.WaitTimeout) return false;
        }

        if (signal != 0) return false;
        if (!parent.WaitForExit(60000))
            throw new IOException("WinMon did not exit in time; the update was cancelled.");
        return !cancel.WaitOne(0);
    }

    private static void RunUpdateInstaller(string directory, string download, UpdateJob job, UpdateHelperHost host)
    {
        var installer = new ProcessStartInfo(download) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[]
                 {
                     "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS",
                     "/NORESTARTAPPLICATIONS", "/DIR=" + Path.GetDirectoryName(job.Target),
                     "/LOG=" + Path.Combine(directory, "installer.log")
                 }) installer.ArgumentList.Add(arg);
        using var setup = host.Start(installer) ??
                          throw new IOException("Cannot start the update installer.");
        if (!setup.WaitForExit(300000))
        {
            host.WriteState(directory,
                new UpdateState("installer-running",
                    "Setup has exceeded five minutes; see installer.log."));
            // Setup may still be writing files. Retain the update guard until it has actually exited.
            setup.WaitForExit();
        }

        if (setup.ExitCode != 0)
            throw new IOException("Update installer failed with exit code " + setup.ExitCode + ".");
        UpdateClient.ValidateExecutableVersion(job.Target, job.Version);
    }

    private static void WaitForHealthyUpdate(IUpdateHelperProcess child, WaitHandle healthy, UpdateHelperHost host)
    {
        var deadline = host.UtcNow.AddSeconds(60);
        while (!healthy.WaitOne(100))
            if (child.HasExited || host.UtcNow >= deadline)
                throw new IOException("The updated app did not become ready.");
    }

    private static void RecoverHelperFailure(string directory, UpdateJob job, IUpdateHelperProcess? child,
        bool replaced, Exception error, UpdateHelperHost host)
    {
        try
        {
            if (job.Installed && child is { HasExited: false })
            {
                // Setup has installed and started the new version, but sampling is slow. Launching
                // another copy would just signal this one, and "failed" would be untrue. The state stays
                // awaiting-ready, which the next launch of this version records as succeeded.
                Trace.TraceWarning("WinMon updater left the slow new version running.");
                return;
            }

            if (replaced)
            {
                if (child is { HasExited: false })
                {
                    child.CloseMainWindow();
                    if (!child.WaitForExit(15000))
                    {
                        child.Kill();
                        child.WaitForExit(15000);
                    }
                }

                PortableUpdate.Restore(job.Target, UpdateFiles.BackupPath(job));
            }
            else if (!job.Installed)
            {
                PortableUpdate.RecoverIncompleteReplace(job.Target, UpdateFiles.BackupPath(job));
            }

            if (!job.Installed) RestoreCompanions(job);

            host.WriteState(directory,
                new UpdateState(replaced ? "rolled-back" : "failed", error.Message));
            if (File.Exists(job.Target) && !host.IsProcessRunning(job.ParentId, job.ParentStartTicks))
                Launch(job, directory, host).Dispose();
        }
        catch (Exception recoveryError)
        {
            Trace.TraceError("WinMon update recovery: {0}", recoveryError);
        }
    }

    internal static bool IsProcessRunning(int id, long ticks)
    {
        try
        {
            using var process = Process.GetProcessById(id);

            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == ticks;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void RestoreCompanions(UpdateJob job)
    {
        RestoreCompanion(UpdateFiles.CliJob(job), job.CliExisted);
        RestoreCompanion(UpdateFiles.McpJob(job), job.McpExisted);
    }

    private static void RestoreCompanion(UpdateJob companion, bool? existed)
    {
        if (existed is null) return;
        if (File.Exists(UpdateFiles.BackupPath(companion)))
            PortableUpdate.Restore(companion.Target, UpdateFiles.BackupPath(companion));
        else if (existed == false && !File.Exists(UpdateFiles.StagedPath(companion)))
            File.Delete(companion.Target);
    }

    private static void CleanupCompanions(UpdateJob job)
    {
        CleanupCompanion(UpdateFiles.CliJob(job), job.CliExisted);
        CleanupCompanion(UpdateFiles.McpJob(job), job.McpExisted);
    }

    private static void CleanupCompanion(UpdateJob companion, bool? existed)
    {
        if (existed is null) return;
        if (File.Exists(companion.Target)) TryDelete(UpdateFiles.BackupPath(companion));
        TryDelete(UpdateFiles.StagedPath(companion));
    }

    private static IUpdateHelperProcess Launch(UpdateJob job, string directory, UpdateHelperHost host)
    {
        var start = new ProcessStartInfo(job.Target)
            { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(job.Target)! };
        start.ArgumentList.Add("--update-result");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add(job.Token);
        return host.Start(start) ?? throw new IOException("Cannot restart WinMon.");
    }

    private static bool SamePath(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // A helper that is still exiting keeps its executable open; a later startup retries.
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
