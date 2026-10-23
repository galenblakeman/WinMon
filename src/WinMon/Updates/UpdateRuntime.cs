using System.Diagnostics;

// ReSharper disable StringLiteralTypo

namespace WinMon.Updates;

/// <summary>Runs only the staged, published executable as an updater, before normal application startup.</summary>
internal static class UpdateRuntime
{
    internal const string UpdateMutexName = @"Local\WinMon.Update";
    private static string? _resultToken;
    internal static bool IsExpectedRestart => _resultToken is not null;

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
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        if (args is not ["--update-result", var directory, var token]) return;
        try
        {
            directory = UpdateFiles.ValidateDirectory(directory);
            var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
            if (job.Token != token || !SamePath(job.Target, Environment.ProcessPath!)) return;
            if (!EventWaitHandle.TryOpenExisting(UpdateLauncher.EventName(token, "Healthy"), out var healthy)) return;
            healthy.Dispose();
            _resultToken = token;
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
        if (!SamePath(job.Target, target) || !Guid.TryParseExact(job.Token, "N", out _)) return;
        if (!File.Exists(statePath))
        {
            // Prepared but never applied: the helper writes a state before it changes anything outside this directory.
            if (earlier) TryDeleteDirectory(directory);
            return;
        }

        var state = UpdateFiles.ReadJson<UpdateState>(statePath);
        if (job.Version == currentVersion && state.Status is "replacing" or "awaiting-ready")
        {
            UpdateFiles.WriteJson(statePath, new UpdateState("succeeded"));
            state = new UpdateState("succeeded");
        }

        if (state.Status is not ("succeeded" or "failed" or "rolled-back")) return;
        if (state.Status != "succeeded" && !earlier) return;
        // The small job and state files stay until the result notice has been shown.
        if (earlier && File.Exists(Path.Combine(directory, "notice-shown")) && File.Exists(job.Target))
        {
            TryDelete(UpdateFiles.BackupPath(job));
            TryDelete(UpdateFiles.StagedPath(job));
            TryDeleteDirectory(directory);
            return;
        }

        // A backup is the only copy of WinMon while the target is missing.
        if (File.Exists(job.Target)) TryDelete(UpdateFiles.BackupPath(job));
        TryDelete(UpdateFiles.StagedPath(job));
        TryDelete(Path.Combine(directory, "WinMon.Update.exe"));
        TryDelete(Path.Combine(directory, "WinMon.exe"));
        TryDelete(Path.Combine(directory, "verified.exe"));
        TryDelete(Path.Combine(directory, $"WinMon-{job.Version}-win-x64.zip"));
        TryDelete(Path.Combine(directory, $"WinMon-{job.Version}-Setup-x64.exe"));
    }

    internal static string? ConsumeNotice()
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return null;
        foreach (var directory in Transactions())
            try
            {
                var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
                if (!SamePath(job.Target, Environment.ProcessPath!)) continue;
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

    internal static int RunHelper(string directory, string token)
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return 1;
        UpdateJob? job = null;
        Process? child = null;
        var replaced = false;
        var validatedJob = false;
        var validatedDirectory = false;
        try
        {
            directory = UpdateFiles.ValidateDirectory(directory);
            validatedDirectory = true;
            job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
            if (job.Token != token || !Guid.TryParseExact(token, "N", out _) ||
                !SamePath(Path.GetDirectoryName(Environment.ProcessPath!)!, directory) ||
                !Path.GetFileName(job.Target).Equals("WinMon.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid update job.");
            var bytes = File.ReadAllBytes(Path.Combine(directory, "update-manifest.json"));
            var signature = File.ReadAllText(Path.Combine(directory, "update-manifest.sig"));
            var manifest = UpdateManifest.Verify(bytes, signature, UpdateClient.ReadPublicKey(), job.Version);
            if (UpdateManifest.ParseVersion(job.Version) <= UpdateManifest.ParseVersion(AppInfo.Version))
                throw new InvalidDataException("Refusing an update downgrade.");
            if (job.Installed != UpdateInstallation.IsInstalled(job.Target))
                throw new InvalidDataException("Installation registration changed.");
            var asset = job.Installed ? manifest.Installer : manifest.Portable;
            var download = Path.Combine(directory, asset.Name);
            UpdateManifest.VerifyFileAsync(download, asset, CancellationToken.None).GetAwaiter().GetResult();
            // Bind the job to the process that requested it, including its start time and executable path.
            using var parent = Process.GetProcessById(job.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != job.ParentStartTicks ||
                !SamePath(parent.MainModule!.FileName, job.Target))
                throw new InvalidDataException("The original WinMon process changed.");
            validatedJob = true;
            using var mutex = new Mutex(false, UpdateMutexName);
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
                using var ready = EventWaitHandle.OpenExisting(UpdateLauncher.EventName(token, "Ready"));
                using var commit = EventWaitHandle.OpenExisting(UpdateLauncher.EventName(token, "Commit"));
                using var cancel = EventWaitHandle.OpenExisting(UpdateLauncher.EventName(token, "Cancel"));
                ready.Set();
                if (WaitHandle.WaitAny([commit, cancel], TimeSpan.FromSeconds(60)) != 0) return 1;
                if (!parent.WaitForExit(60000))
                    throw new IOException("WinMon did not exit in time; the update was cancelled.");
                if (cancel.WaitOne(0)) return 1;
                UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), new UpdateState("replacing"));
                if (job.Installed)
                {
                    var installer = new ProcessStartInfo(download) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[]
                             {
                                 "/VERYSILENT", "/SUPPRESSMSGBOXES", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS",
                                 "/NORESTARTAPPLICATIONS", "/DIR=" + Path.GetDirectoryName(job.Target),
                                 "/LOG=" + Path.Combine(directory, "installer.log")
                             }) installer.ArgumentList.Add(arg);
                    using var setup = Process.Start(installer) ??
                                      throw new IOException("Cannot start the update installer.");
                    if (!setup.WaitForExit(300000))
                    {
                        UpdateFiles.WriteJson(Path.Combine(directory, "state.json"),
                            new UpdateState("installer-running",
                                "Setup has exceeded five minutes; see installer.log."));
                        // Setup may still be writing files. Retain the update guard until it has actually exited.
                        setup.WaitForExit();
                    }

                    if (setup.ExitCode != 0)
                        throw new IOException("Update installer failed with exit code " + setup.ExitCode + ".");
                    UpdateClient.ValidateExecutableVersion(job.Target, job.Version);
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

                UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), new UpdateState("awaiting-ready"));
                using var healthy = new EventWaitHandle(false, EventResetMode.ManualReset,
                    UpdateLauncher.EventName(token, "Healthy"));
                // Only the transaction's authenticated restart may pass the startup guard until readiness.
                child = Launch(job, directory);
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (!healthy.WaitOne(100))
                    if (child.HasExited || DateTime.UtcNow >= deadline)
                        throw new IOException("The updated app did not become ready.");
                UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), new UpdateState("succeeded"));
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

            // ReSharper disable once InvertIf
            if (job is not null && validatedJob)
                try
                {
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

                    UpdateFiles.WriteJson(Path.Combine(directory, "state.json"),
                        new UpdateState(replaced ? "rolled-back" : "failed", ex.Message));
                    if (File.Exists(job.Target) && !IsProcessRunning(job.ParentId, job.ParentStartTicks))
                        Launch(job, directory).Dispose();
                }
                catch (Exception recoveryError)
                {
                    Trace.TraceError("WinMon update recovery: {0}", recoveryError);
                }

            return 1;
        }
        finally
        {
            child?.Dispose();
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

    private static Process Launch(UpdateJob job, string directory)
    {
        var start = new ProcessStartInfo(job.Target)
            { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(job.Target)! };
        start.ArgumentList.Add("--update-result");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add(job.Token);
        return Process.Start(start) ?? throw new IOException("Cannot restart WinMon.");
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