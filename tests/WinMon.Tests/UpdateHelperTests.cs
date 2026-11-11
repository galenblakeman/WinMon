using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class UpdateHelperTests
{
#if WINMON_STORE
    [Test]
    public void StoreRejectsHelperBeforeReadingItsEnvironment()
    {
        Assert.That(UpdateRuntime.RunHelper(null!, null!, new UnavailableHost()), Is.EqualTo(1));
    }

    private sealed class UnavailableHost : UpdateHelperHost
    {
        internal override string StagingRoot => throw new AssertionException("Store must not access the updater.");
    }
#else
    [Test]
    [Category("Integration")]
    public void NativeHostReadsTheCurrentProcessIdentityAndEmbeddedTrustKey()
    {
        var host = new UpdateHelperHost();
        using var current = Process.GetCurrentProcess();
        using var observed = host.OpenProcess(current.Id);
        Assert.Multiple(() =>
        {
            Assert.That(observed.StartTicks, Is.EqualTo(current.StartTime.ToUniversalTime().Ticks));
            Assert.That(observed.FileName, Is.EqualTo(current.MainModule!.FileName));
            Assert.That(observed.HasExited, Is.False);
            Assert.That(observed.WaitForExit(0), Is.False);
            Assert.That(host.IsProcessRunning(current.Id, observed.StartTicks), Is.True);
            Assert.That(host.PublicKey, Is.EqualTo(UpdateClient.ReadPublicKey()).And.Not.Empty);
            Assert.That(host.CurrentVersion, Is.EqualTo(AppInfo.Version));
            Assert.That(host.ProcessPath, Is.EqualTo(Environment.ProcessPath));
            Assert.That(host.StagingRoot, Is.EqualTo(UpdateClient.UpdateRoot));
        });
    }

    [TestCase("directory", "")]
    [TestCase("token", "Invalid update job")]
    [TestCase("helper", "Invalid update job")]
    [TestCase("target", "Invalid update job")]
    [TestCase("signature", "signature is invalid")]
    [TestCase("downgrade", "Refusing an update downgrade")]
    [TestCase("registration", "Installation registration changed")]
    [TestCase("hash", "checksum is invalid")]
    [TestCase("parent-time", "original WinMon process changed")]
    [TestCase("parent-path", "original WinMon process changed")]
    public void InvalidTransactionNeverModifiesTheTargetOrStartsAProcess(string invalid, string error)
    {
        using var fixture = new Fixture();
        switch (invalid)
        {
            case "directory": fixture.Host.Root = fixture.Application; break;
            case "token": fixture.Job = fixture.Job with { Token = Guid.NewGuid().ToString("N") }; break;
            case "helper": fixture.Host.HelperPath = Path.Combine(fixture.Application, "WinMon.Update.exe"); break;
            case "target": fixture.Job = fixture.Job with { Target = Path.Combine(fixture.Application, "foreign.exe") }; break;
            case "signature":
                var manifestPath = Path.Combine(fixture.Directory, "update-manifest.json");
                File.AppendAllText(manifestPath, " ");
                break;
            case "downgrade": fixture.Host.Version = fixture.Job.Version; break;
            case "registration": fixture.Host.Installed = true; break;
            case "hash":
                var bytes = File.ReadAllBytes(fixture.Download);
                bytes[^1] ^= 1;
                File.WriteAllBytes(fixture.Download, bytes);
                break;
            case "parent-time": fixture.Host.Parent.StartTicks++; break;
            case "parent-path": fixture.Host.Parent.FileName = Path.Combine(fixture.Application, "other.exe"); break;
        }
        fixture.SaveJob();

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            Assert.That(fixture.Host.Starts, Is.Empty);
            Assert.That(fixture.Host.States, Is.Empty);
            Assert.That(File.Exists(UpdateFiles.BackupPath(fixture.Job)), Is.False);
            if (invalid != "directory")
                Assert.That(File.ReadAllText(Path.Combine(fixture.Directory, "helper-error.log")), Does.Contain(error));
            else
                Assert.That(File.Exists(Path.Combine(fixture.Directory, "helper-error.log")), Is.False);
        });
    }

    [Test]
    public void UnwritableErrorLogDoesNotAllowAnInvalidTransactionToRun()
    {
        using var fixture = new Fixture();
        fixture.Job = fixture.Job with { Token = Guid.NewGuid().ToString("N") };
        fixture.SaveJob();
        Directory.CreateDirectory(Path.Combine(fixture.Directory, "helper-error.log"));

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
        Assert.That(fixture.Host.Starts, Is.Empty);
        Assert.That(fixture.Host.States, Is.Empty);
    }

    [TestCase("no-commit")]
    [TestCase("cancel")]
    [TestCase("exit-timeout")]
    [TestCase("late-cancel")]
    public void AbortedHandoffLeavesTheOriginalAndDownloadUntouched(string reason)
    {
        using var fixture = new Fixture();
        if (reason is "no-commit" or "cancel") fixture.Host.Event("Commit").Reset();
        if (reason == "cancel") fixture.Host.Event("Cancel").Set();
        if (reason == "exit-timeout") fixture.Host.Parent.ExitsWithinTimeout = false;
        if (reason == "late-cancel") fixture.Host.Parent.OnWait = () => fixture.Host.Event("Cancel").Set();

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Host.Event("Ready").WaitOne(0), Is.True);
            Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            Assert.That(File.Exists(fixture.Download), Is.True);
            Assert.That(fixture.Host.Starts, Is.Empty);
            Assert.That(fixture.Host.Parent.Disposed, Is.True);
            Assert.That(fixture.Host.States, Is.EqualTo(reason == "exit-timeout" ? new[] { "failed" } : []));
        });
    }

    [Test]
    public void HealthyPortableReplacementCommitsAndDeletesOnlyTransactionFiles()
    {
        using var fixture = new Fixture();
        var adjacent = Path.Combine(fixture.Application, "WinMon.preferences.json");
        File.WriteAllText(adjacent, "keep");

        Assert.That(fixture.Run(), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
            Assert.That(File.ReadAllText(adjacent), Is.EqualTo("keep"));
            Assert.That(fixture.Host.States, Is.EqualTo(new[] { "replacing", "awaiting-ready", "succeeded" }));
            Assert.That(fixture.State.Status, Is.EqualTo("succeeded"));
            Assert.That(File.Exists(UpdateFiles.BackupPath(fixture.Job)), Is.False);
            Assert.That(File.Exists(UpdateFiles.StagedPath(fixture.Job)), Is.False);
            Assert.That(File.Exists(fixture.Download), Is.False);
            Assert.That(File.Exists(Path.Combine(fixture.Directory, "verified.exe")), Is.False);
            Assert.That(fixture.Host.Child.Disposed, Is.True);
            Assert.That(fixture.Host.Parent.Disposed, Is.True);
            Assert.That(fixture.Host.Starts.Single().ArgumentList,
                Is.EqualTo(new[] { "--update-result", fixture.Directory, fixture.Job.Token }));
            Assert.That(fixture.Host.Starts.Single().FileName, Is.EqualTo(fixture.Target));
            Assert.That(fixture.Host.Starts.Single().WorkingDirectory, Is.EqualTo(fixture.Application));
            Assert.That(fixture.Host.Starts.Single().UseShellExecute, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BundledPortableUpdateCommitsBothOrRestoresBoth(bool existingCli)
    {
        foreach (var healthy in new[] { true, false })
        {
            using var fixture = new Fixture(includeCli: true, existingCli: existingCli);
            fixture.Host.Healthy = healthy;
            fixture.Host.Child.HasExited = !healthy;
            var cliJob = UpdateFiles.CliJob(fixture.Job);

            Assert.That(fixture.Run(), Is.EqualTo(healthy ? 0 : 1));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.State.Status, Is.EqualTo(healthy ? "succeeded" : "rolled-back"));
                if (healthy)
                {
                    Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
                    Assert.That(File.ReadAllBytes(cliJob.Target), Is.EqualTo(fixture.Executable));
                }
                else
                {
                    Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
                    if (existingCli) Assert.That(File.ReadAllText(cliJob.Target), Is.EqualTo("old CLI"));
                    else Assert.That(File.Exists(cliJob.Target), Is.False);
                }
                Assert.That(File.Exists(UpdateFiles.BackupPath(cliJob)), Is.False);
                Assert.That(File.Exists(UpdateFiles.StagedPath(cliJob)), Is.False);
            });
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BundledPortableUpdateCommitsOrRestoresMcpWithTheDesktop(bool existingMcp)
    {
        foreach (var healthy in new[] { true, false })
        {
            using var fixture = new Fixture(includeMcp: true, existingMcp: existingMcp);
            fixture.Host.Healthy = healthy;
            fixture.Host.Child.HasExited = !healthy;
            var mcpJob = UpdateFiles.McpJob(fixture.Job);

            Assert.That(fixture.Run(), Is.EqualTo(healthy ? 0 : 1));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.State.Status, Is.EqualTo(healthy ? "succeeded" : "rolled-back"));
                if (healthy)
                {
                    Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
                    Assert.That(File.ReadAllBytes(mcpJob.Target), Is.EqualTo(fixture.Executable));
                }
                else
                {
                    Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
                    if (existingMcp) Assert.That(File.ReadAllText(mcpJob.Target), Is.EqualTo("old MCP"));
                    else Assert.That(File.Exists(mcpJob.Target), Is.False);
                }
                Assert.That(File.Exists(UpdateFiles.BackupPath(mcpJob)), Is.False);
                Assert.That(File.Exists(UpdateFiles.StagedPath(mcpJob)), Is.False);
            });
        }
    }

    [TestCase("WinMon.Cli.exe")]
    [TestCase("WinMon.exe")]
    [Category("Integration")]
    public void LockedBundleMemberDoesNotLeaveEitherExecutableUpdated(string lockedName)
    {
        using var fixture = new Fixture(includeCli: true, existingCli: true);
        using var locked = new FileStream(Path.Combine(fixture.Application, lockedName),
            FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            Assert.That(File.ReadAllText(UpdateFiles.CliJob(fixture.Job).Target), Is.EqualTo("old CLI"));
            Assert.That(fixture.State.Status, Is.EqualTo("failed"));
        });
    }

    [Test]
    public void DesktopOnlyArchiveCannotLeaveAnExistingCliAtAnOlderVersion()
    {
        using var fixture = new Fixture(existingCli: true);

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            Assert.That(File.ReadAllText(UpdateFiles.CliJob(fixture.Job).Target), Is.EqualTo("old CLI"));
            Assert.That(fixture.State.Status, Is.EqualTo("failed"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UpdateGuardRejectsAnotherOwnerAndRecoversAnAbandonedOwner(bool abandoned)
    {
        using var fixture = new Fixture();
        using var guard = new Mutex(false, fixture.Host.GuardName);
        if (abandoned)
        {
            var owner = new Thread(() => guard.WaitOne());
            owner.Start();
            Assert.That(owner.Join(TimeSpan.FromSeconds(3)), Is.True);
            Assert.That(fixture.Run(), Is.Zero);
            Assert.That(guard.WaitOne(0), Is.True, "The helper releases its recovered ownership.");
            guard.ReleaseMutex();
        }
        else
        {
            guard.WaitOne();
            try
            {
                Assert.That(Task.Run(fixture.Run).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult(), Is.EqualTo(1));
                Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
                Assert.That(fixture.Host.Starts, Is.Empty);
                Assert.That(fixture.State.Status, Is.EqualTo("failed"));
            }
            finally
            {
                guard.ReleaseMutex();
            }
        }
    }

    [TestCase("crash")]
    [TestCase("timeout")]
    [TestCase("launch")]
    public void FailedPortableRestartRestoresTheOriginalAndRecordsRollback(string failure)
    {
        using var fixture = new Fixture();
        fixture.Host.Healthy = false;
        fixture.Host.Child.HasExited = failure == "crash";
        fixture.Host.AdvanceClock = failure == "timeout";
        fixture.Host.FailFirstLaunch = failure == "launch";
        fixture.Host.ParentStillRunning = false;
        fixture.Host.Child.ExitsWithinTimeout = false;

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            Assert.That(fixture.State.Status, Is.EqualTo("rolled-back"));
            Assert.That(File.Exists(UpdateFiles.BackupPath(fixture.Job)), Is.False);
            Assert.That(fixture.Host.Starts, Has.Count.EqualTo(2), "Recovery restarts the restored executable.");
            Assert.That(fixture.Host.Child.Closed, Is.EqualTo(failure == "timeout"));
            Assert.That(fixture.Host.Child.Killed, Is.EqualTo(failure == "timeout"));
        });
    }

    [Test]
    [Category("Integration")]
    public void FailedRollbackRetainsTheOriginalBackupAndDoesNotClaimSuccess()
    {
        using var fixture = new Fixture();
        fixture.Host.Healthy = false;
        fixture.Host.Child.HasExited = true;
        fixture.Host.ParentStillRunning = false;
        FileStream? lockedTarget = null;
        fixture.Host.OnLaunch = () => lockedTarget = new FileStream(fixture.Target,
            FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.That(fixture.Run(), Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(lockedTarget, Is.Not.Null);
                Assert.That(File.ReadAllText(UpdateFiles.BackupPath(fixture.Job)), Is.EqualTo("old executable"));
                Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
                Assert.That(fixture.State.Status, Is.EqualTo("awaiting-ready"));
                Assert.That(fixture.Host.States, Does.Not.Contain("rolled-back").And.Not.Contain("succeeded"));
                Assert.That(fixture.Host.Starts, Has.Count.EqualTo(1), "Failed recovery must not restart the replacement.");
                Assert.That(File.ReadAllText(Path.Combine(fixture.Directory, "helper-error.log")),
                    Does.Contain("The updated app did not become ready."));
                Assert.That(fixture.Host.Child.Disposed, Is.True);
            });
        }
        finally
        {
            lockedTarget?.Dispose();
        }
    }

    [Test]
    public void IncompletePortableReplaceRecoversTheOnlyOriginalBeforeRestarting()
    {
        using var fixture = new Fixture();
        fixture.Host.FailReplacing = () =>
        {
            File.Move(fixture.Target, UpdateFiles.BackupPath(fixture.Job));
            throw new IOException("replacement interrupted");
        };
        fixture.Host.ParentStillRunning = false;

        Assert.That(fixture.Run(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
        Assert.That(fixture.State.Status, Is.EqualTo("failed"));
        Assert.That(fixture.Host.Starts, Has.Count.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SuccessJournalFailureNeverRollsBackAHealthyReplacement(bool persistent)
    {
        using var fixture = new Fixture();
        fixture.Host.SuccessWriteFailures = persistent ? 5 : 2;

        Assert.That(fixture.Run(), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
            Assert.That(fixture.State.Status, Is.EqualTo(persistent ? "awaiting-ready" : "succeeded"));
            Assert.That(fixture.Host.Sleeps, Is.EqualTo(persistent ? 4 : 2));
            Assert.That(fixture.Host.Child.Killed, Is.False);
            Assert.That(File.Exists(UpdateFiles.BackupPath(fixture.Job)), Is.EqualTo(persistent));
            Assert.That(File.Exists(fixture.Download), Is.EqualTo(persistent));
            Assert.That(fixture.Host.Starts, Has.Count.EqualTo(1));
        });
    }

    [TestCase("healthy")]
    [TestCase("slow-installer")]
    [TestCase("installer-exit")]
    [TestCase("installer-start")]
    [TestCase("slow-child")]
    [TestCase("crashed-child")]
    public void InstalledLifecycleRetainsItsDistinctFailureAndReadinessBehavior(string outcome)
    {
        using var fixture = new Fixture(installed: true);
        fixture.Host.Setup.ExitsWithinTimeout = outcome != "slow-installer";
        fixture.Host.Setup.ExitCode = outcome == "installer-exit" ? 5 : 0;
        fixture.Host.FailInstallerStart = outcome == "installer-start";
        fixture.Host.Healthy = outcome is not ("slow-child" or "crashed-child");
        fixture.Host.AdvanceClock = outcome == "slow-child";
        fixture.Host.Child.HasExited = outcome == "crashed-child";

        Assert.That(fixture.Run(), Is.EqualTo(outcome is "healthy" or "slow-installer" ? 0 : 1));
        Assert.Multiple(() =>
        {
            Assert.That(fixture.State.Status, Is.EqualTo(outcome switch
            {
                "slow-child" => "awaiting-ready",
                "healthy" or "slow-installer" => "succeeded",
                _ => "failed"
            }));
            Assert.That(fixture.Host.Child.Killed, Is.False, "An installed app is never portably rolled back.");
            Assert.That(fixture.Host.Setup.UnboundedWaits, Is.EqualTo(outcome == "slow-installer" ? 1 : 0));
            Assert.That(fixture.Host.Starts[0].UseShellExecute, Is.False);
            Assert.That(fixture.Host.Starts[0].CreateNoWindow, Is.True);
            Assert.That(fixture.Host.Starts[0].ArgumentList, Does.Contain("/NORESTART"));
            Assert.That(fixture.Host.Starts[0].ArgumentList, Does.Contain("/DIR=" + fixture.Application));
            if (outcome is "installer-exit" or "installer-start")
                Assert.That(File.ReadAllText(fixture.Target), Is.EqualTo("old executable"));
            else
                Assert.That(File.ReadAllBytes(fixture.Target), Is.EqualTo(fixture.Executable));
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "WinMon.HelperTests", Guid.NewGuid().ToString("N"));
        internal Fixture(bool installed = false, bool includeCli = false, bool existingCli = false,
            bool includeMcp = false, bool existingMcp = false)
        {
            Application = Path.Combine(_root, "app");
            Target = Path.Combine(Application, "WinMon.exe");
            Directory = Path.Combine(_root, "staging", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Application);
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Target, "old executable");
            Executable = File.ReadAllBytes(typeof(UpdateRuntime).Assembly.Location);
            Job = new UpdateJob(Target, 42, 123, AppInfo.Version, installed, Guid.NewGuid().ToString("N"));
            if (existingCli) File.WriteAllText(UpdateFiles.CliJob(Job).Target, "old CLI");
            if (existingMcp) File.WriteAllText(UpdateFiles.McpJob(Job).Target, "old MCP");
            Token = Job.Token;
            byte[] portable;
            using (var memory = new MemoryStream())
            {
                using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
                {
                    using (var entry = archive.CreateEntry("WinMon.exe").Open()) entry.Write(Executable);
                    if (includeCli)
                        using (var entry = archive.CreateEntry("WinMon.Cli.exe").Open()) entry.Write(Executable);
                    if (includeMcp)
                        using (var entry = archive.CreateEntry("WinMon.Mcp.exe").Open()) entry.Write(Executable);
                }
                portable = memory.ToArray();
            }
            var manifest = new UpdateManifest(1, Job.Version, "win-x64",
                Asset($"WinMon-{Job.Version}-win-x64.zip", portable),
                Asset($"WinMon-{Job.Version}-Setup-x64.exe", Executable));
            Download = Path.Combine(Directory, installed ? manifest.Installer.Name : manifest.Portable.Name);
            File.WriteAllBytes(Download, installed ? Executable : portable);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            using var key = RSA.Create(2048);
            File.WriteAllBytes(Path.Combine(Directory, "update-manifest.json"), bytes);
            File.WriteAllText(Path.Combine(Directory, "update-manifest.sig"),
                Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
            Host = new FakeHost
            {
                Root = Path.GetDirectoryName(Directory)!, HelperPath = Path.Combine(Directory, "WinMon.Update.exe"),
                Key = key.ExportSubjectPublicKeyInfoPem(), Installed = installed, Target = Target, Executable = Executable
            };
            Host.Parent.FileName = Target;
            SaveJob();
        }

        internal string Application { get; }
        internal string Target { get; }
        internal string Directory { get; }
        internal string Download { get; }
        internal byte[] Executable { get; }
        internal UpdateJob Job { get; set; }
        internal string Token { get; }
        internal FakeHost Host { get; }
        internal UpdateState State => UpdateFiles.ReadJson<UpdateState>(Path.Combine(Directory, "state.json"));
        internal void SaveJob() => UpdateFiles.WriteJson(Path.Combine(Directory, "job.json"), Job);
        internal int Run() => UpdateRuntime.RunHelper(Directory, Token, Host);

        public void Dispose()
        {
            Host.Dispose();
            System.IO.Directory.Delete(_root, true);
        }

        private static UpdateAsset Asset(string name, byte[] bytes) =>
            new(name, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }

    private sealed class FakeHost : UpdateHelperHost, IDisposable
    {
        private readonly string _events = @"Local\WinMon.HelperTests." + Guid.NewGuid().ToString("N") + ".";
        private readonly Dictionary<string, EventWaitHandle> _signals = [];
        private int _clockReads;
        internal string Root = "";
        internal string HelperPath = "";
        internal string Key = "";
        internal string Version = "0.0.1";
        internal string Target = "";
        internal byte[] Executable = [];
        internal bool Installed;
        internal bool Healthy = true;
        internal bool AdvanceClock;
        internal bool ParentStillRunning = true;
        internal bool FailFirstLaunch;
        internal bool FailInstallerStart;
        internal int SuccessWriteFailures;
        internal int Sleeps;
        internal Action? FailReplacing;
        internal Action? OnLaunch;
        internal FakeProcess Parent { get; } = new() { HasExited = true };
        internal FakeProcess Child { get; } = new();
        internal FakeProcess Setup { get; } = new();
        internal List<ProcessStartInfo> Starts { get; } = [];
        internal List<string> States { get; } = [];
        internal override string StagingRoot => Root;
        internal override string ProcessPath => HelperPath;
        internal override string PublicKey => Key;
        internal override string CurrentVersion => Version;
        internal override DateTime UtcNow => DateTime.UnixEpoch.AddSeconds(AdvanceClock ? 70 * _clockReads++ : 0);
        internal override bool IsInstalled(string target) => Installed;
        internal override bool IsProcessRunning(int id, long ticks) => ParentStillRunning;
        internal override IUpdateHelperProcess OpenProcess(int id)
        {
            Assert.That(id, Is.EqualTo(42));
            return Parent;
        }
        internal string GuardName => _events + "Guard";
        internal override Mutex CreateMutex() => new(false, GuardName);
        internal EventWaitHandle Event(string name)
        {
            if (!_signals.TryGetValue(name, out var signal))
                _signals[name] = signal = new EventWaitHandle(name == "Commit", EventResetMode.ManualReset, _events + name);
            return signal;
        }
        internal override EventWaitHandle Signal(string token, string name, bool create = false)
        {
            Event(name);
            return EventWaitHandle.OpenExisting(_events + name);
        }
        internal override IUpdateHelperProcess? Start(ProcessStartInfo start)
        {
            Starts.Add(start);
            if (Path.GetFileName(start.FileName).EndsWith("-Setup-x64.exe", StringComparison.Ordinal))
            {
                if (FailInstallerStart) return null;
                if (Setup.ExitCode == 0)
                {
                    File.WriteAllBytes(Target, Executable);
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Target)!, "WinMon.Cli.exe"), Executable);
                    File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(Target)!, "WinMon.Mcp.exe"), Executable);
                }
                return Setup;
            }
            if (FailFirstLaunch && Starts.Count == 1) return null;
            OnLaunch?.Invoke();
            if (Healthy) Event("Healthy").Set();
            return Child;
        }
        internal override void WriteState(string directory, UpdateState state)
        {
            if (state.Status == "replacing") FailReplacing?.Invoke();
            if (state.Status == "succeeded" && SuccessWriteFailures-- > 0) throw new IOException("state locked");
            States.Add(state.Status);
            base.WriteState(directory, state);
        }
        internal override void Sleep(int milliseconds)
        {
            Assert.That(milliseconds, Is.EqualTo(100));
            Sleeps++;
        }
        public void Dispose()
        {
            foreach (var signal in _signals.Values) signal.Dispose();
        }
    }

    private sealed class FakeProcess : IUpdateHelperProcess
    {
        public bool HasExited { get; set; }
        public int ExitCode { get; set; }
        public long StartTicks { get; set; } = 123;
        public string FileName { get; set; } = "";
        internal bool ExitsWithinTimeout = true;
        internal bool Disposed;
        internal bool Closed;
        internal bool Killed;
        internal int UnboundedWaits;
        internal Action? OnWait;
        public bool WaitForExit(int milliseconds)
        {
            OnWait?.Invoke();
            return ExitsWithinTimeout;
        }
        public void WaitForExit() => UnboundedWaits++;
        public void CloseMainWindow() => Closed = true;
        public void Kill() => Killed = true;
        public void Dispose() => Disposed = true;
    }
#endif
}
