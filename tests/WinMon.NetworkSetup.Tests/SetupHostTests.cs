using System.IO.Compression;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration"), Category("Elevated")]
[Explicit("Runs isolated protected-folder setup transactions with simulated OS service boundaries.")]
public sealed class SetupHostTests
{
    private const string User = "S-1-5-21-1-2-3-1001";
    private const string PreviousUser = "S-1-5-21-1-2-3-1002";

    [Test]
    public async Task Handshake_AuthenticatesRequestsCommitsOrRollsBackAndPreservesFailures()
    {
        foreach (var mode in new[] { "commit", "reject", "invalid-version", "invalid-request", "deploy-failure", "disconnect" })
        {
            using var context = new Context(true, mode == "deploy-failure" ? "start" : null);
            var name = "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N");
            var token = new string('a', 64);
            var invocation = SetupArguments.Parse(["--pipe", name, "--token", token]);
            using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var handshake = Program.RunHandshakeAsync(invocation, () => context.Package, context.Host);
            try
            {
                await server.WaitForConnectionAsync(limit.Token);
                await NetworkSetupWire.WriteAsync(server, new NetworkSetupRequest(NetworkSetupWire.Version,
                    mode == "invalid-request" ? new string('b', 64) : token, "install"),
                    NetworkSetupWireJsonContext.Default.NetworkSetupRequest, limit.Token);
                if (mode == "invalid-request")
                {
                    Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await handshake);
                }
                else
                {
                    var ready = await NetworkSetupWire.ReadAsync(server, NetworkSetupWireJsonContext.Default.NetworkSetupReply, limit.Token);
                    if (mode == "deploy-failure")
                    {
                        Assert.That(ready.Phase, Is.EqualTo("failed"));
                        Assert.That(ready.FailurePhase, Is.EqualTo("service"));
                        Assert.ThrowsAsync<SetupFailure>(async () => await handshake);
                    }
                    else
                    {
                        Assert.That(ready.Phase, Is.EqualTo("ready"));
                        if (mode == "disconnect")
                        {
                            server.Dispose();
                            Assert.ThrowsAsync<EndOfStreamException>(async () => await handshake);
                        }
                        else
                        {
                            await NetworkSetupWire.WriteAsync(server,
                                new NetworkSetupCommit(NetworkSetupWire.Version + (mode == "invalid-version" ? 1 : 0), mode != "reject"),
                                NetworkSetupWireJsonContext.Default.NetworkSetupCommit, limit.Token);
                            if (mode == "commit")
                            {
                                var completed = await NetworkSetupWire.ReadAsync(server, NetworkSetupWireJsonContext.Default.NetworkSetupReply, limit.Token);
                                Assert.That(completed.Phase, Is.EqualTo("complete"));
                                await handshake;
                            }
                            else Assert.ThrowsAsync<OperationCanceledException>(async () => await handshake);
                        }
                    }
                }
                if (mode == "commit") Assert.That(File.ReadAllText(context.HelperPath), Is.EqualTo("new helper"));
                else context.AssertOriginal(true);
                if (mode != "invalid-request") context.AssertLeaseReleased();
            }
            finally
            {
                server.Dispose();
                try { await handshake; }
                catch (Exception) { }
            }
        }
    }

    [Test]
    public async Task Install_CommitsProtectedFilesPolicyAndRegistration()
    {
        foreach (var existing in new[] { false, true })
        {
            using var context = new Context(existing);
            await using (var transaction = await SetupService.BeginAsync("install", User, context.Package,
                             CancellationToken.None, context.Host))
            {
                Assert.Throws<IOException>(() => File.Open(context.LockPath, FileMode.Open, FileAccess.ReadWrite,
                    FileShare.None).Dispose());
                Assert.That(File.ReadAllText(context.HelperPath), Is.EqualTo("new helper"));
                Assert.That(AuthorizationPolicy.Load(context.PolicyPath),
                    Is.EquivalentTo(existing ? new[] { User, PreviousUser } : new[] { User }));
                Assert.That(context.ServiceExists && context.Running, Is.True);
                Assert.That(InstalledFiles.Validate(context.Install).OfType<FileInfo>().Count(), Is.EqualTo(5));
                await transaction.CommitAsync(CancellationToken.None);
            }
            context.AssertLeaseReleased();
            Assert.That(context.Registration!["DisplayVersion"], Is.EqualTo("new"));
            Assert.That(Directory.GetDirectories(context.Root, ".NetworkMonitor-*").Length, Is.Zero);
        }
    }

    [Test]
    public async Task MissingCommit_RestoresPreviousTreeAndServiceState()
    {
        foreach (var existing in new[] { false, true })
        {
            using var context = new Context(existing);
            var transaction = await SetupService.BeginAsync("install", User, context.Package,
                CancellationToken.None, context.Host);
            await transaction.DisposeAsync();
            context.AssertOriginal(existing);
            context.AssertLeaseReleased();
        }
    }

    [Test]
    public void InstallFailures_RestorePreviousFilesPolicyRegistrationAndService()
    {
        foreach (var phase in new[] { "ensure", "archive", "publisher", "stop", "service", "start", "registration" })
        {
            using var context = new Context(true, phase);
            var error = Assert.ThrowsAsync<SetupFailure>(async () =>
                await SetupService.BeginAsync("install", User, context.Package, CancellationToken.None, context.Host));
            Assert.That(error!.Phase, Is.EqualTo(phase switch
            {
                "ensure" => "prepare", "publisher" => "verify", "start" => "service", _ => phase
            }), phase);
            context.AssertOriginal(true);
            context.AssertLeaseReleased();
        }
    }

    [Test]
    public async Task LoggingFailure_DoesNotRollBackHealthyInstallation()
    {
        using var context = new Context(false, "logging");
        await using (var transaction = await SetupService.BeginAsync("install", User, context.Package,
                         CancellationToken.None, context.Host))
            await transaction.CommitAsync(CancellationToken.None);
        Assert.That(context.ServiceExists && context.Running, Is.True);
        Assert.That(File.ReadAllText(context.HelperPath), Is.EqualTo("new helper"));
        context.AssertLeaseReleased();
    }

    [Test]
    public async Task Removal_LeavesStateUntilAcknowledgedThenRetiresTheOwnedTree()
    {
        using var context = new Context(true);
        await using (await SetupService.BeginAsync("remove", User, context.Package, CancellationToken.None, context.Host))
            context.AssertOriginal(true);
        context.AssertOriginal(true);
        await SetupService.RemoveAsync(CancellationToken.None, context.Host);
        Assert.That(Directory.Exists(context.Install), Is.False);
        Assert.That(context.ServiceExists, Is.False);
        Assert.That(context.Registration, Is.Null);
        context.AssertLeaseReleased();
    }

    [Test]
    public async Task Revoke_PreservesOtherUsersThenRemovesTheLastUsersInstallation()
    {
        using var context = new Context(true);
        File.Delete(context.PolicyPath);
        AuthorizationPolicy.Save(context.PolicyPath, [PreviousUser, User]);
        await SetupService.RevokeAsync(User, CancellationToken.None, context.Host);
        Assert.That(AuthorizationPolicy.Load(context.PolicyPath), Is.EqualTo(new[] { PreviousUser }));
        Assert.That(context.ServiceExists && context.Running, Is.True);
        Assert.That(context.AuthorizedUsers, Is.EqualTo(new[] { PreviousUser }));
        await SetupService.RevokeAsync(PreviousUser, CancellationToken.None, context.Host);
        Assert.That(Directory.Exists(context.Install), Is.False);
        Assert.That(context.ServiceExists, Is.False);
        context.AssertLeaseReleased();
    }

    [Test]
    public void RevokeRestartFailure_RestoresThePolicyAndPreviousService()
    {
        using var context = new Context(true, "start");
        File.Delete(context.PolicyPath);
        AuthorizationPolicy.Save(context.PolicyPath, [PreviousUser, User]);
        Assert.ThrowsAsync<IOException>(async () =>
            await SetupService.RevokeAsync(User, CancellationToken.None, context.Host));
        Assert.That(AuthorizationPolicy.Load(context.PolicyPath), Is.EquivalentTo(new[] { PreviousUser, User }));
        Assert.That(context.ServiceExists && context.Running, Is.True);
        Assert.That(Directory.GetFiles(context.Root, ".NetworkMonitor-*").Length, Is.Zero);
        context.AssertLeaseReleased();
    }

    [Test]
    public void ServiceWithoutOwnedInstallation_IsRejectedBeforeMutation()
    {
        using var context = new Context(false) { ServiceExists = true };
        var failure = Assert.ThrowsAsync<SetupFailure>(async () =>
            await SetupService.BeginAsync("install", User, context.Package, CancellationToken.None, context.Host));
        Assert.That(failure!.Phase, Is.EqualTo("prepare"));
        Assert.That(failure.InnerException, Is.TypeOf<UnauthorizedAccessException>());
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await SetupService.RemoveAsync(CancellationToken.None, context.Host));
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await SetupService.RevokeAsync(User, CancellationToken.None, context.Host));
        Assert.That(context.ServiceExists, Is.True);
        Assert.That(Directory.Exists(context.Install), Is.False);
        context.AssertLeaseReleased();
    }

    [Test]
    public async Task CompetingSetup_CanBeCancelledWhileTheFirstHoldsTheLock()
    {
        using var context = new Context(false);
        await using var first = await SetupService.BeginAsync("install", User, context.Package,
            CancellationToken.None, context.Host);
        using var cancellation = new CancellationTokenSource();
        var pending = SetupService.BeginAsync("remove", User, context.Package, cancellation.Token, context.Host);
        Assert.That(pending.IsCompleted, Is.False);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
        Assert.That(context.ServiceExists && context.Running, Is.True);
    }

    private sealed class Context : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "WinMon-setup-host-" + Guid.NewGuid().ToString("N"));
        internal string Install => Path.Combine(Root, "NetworkMonitor");
        internal string HelperPath => Path.Combine(Install, SetupPaths.SetupExecutableName);
        internal string PolicyPath => Path.Combine(Install, SetupPaths.PolicyFileName);
        internal string LockPath => Path.Combine(Root, ".network-setup.lock");
        internal readonly SetupHost Host;
        internal readonly NetworkSetupPackage Package;
        internal bool ServiceExists;
        internal bool Running;
        internal Dictionary<string, object>? Registration;
        internal IReadOnlyCollection<string> AuthorizedUsers = [];

        internal Context(bool existing, string? fail = null)
        {
            using var identity = WindowsIdentity.GetCurrent();
            Assert.That(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Is.True);
            ProtectedInstallation.CreateDirectory(Root);
            var service = File.ReadAllBytes(typeof(SetupService).Assembly.Location);
            if (existing)
            {
                ProtectedInstallation.CreateDirectory(Install);
                ProtectedInstallation.CreateDirectory(Path.Combine(Install, "runtime"));
                Write(Path.Combine(Install, SetupPaths.ServiceExecutableName), service);
                Write(HelperPath, Encoding.UTF8.GetBytes("old helper"));
                Write(Path.Combine(Install, "runtime", "dep.dll"), [1]);
                AuthorizationPolicy.Save(PolicyPath, [PreviousUser]);
                InstalledFiles.Write(Install);
                ServiceExists = Running = true;
                Registration = new Dictionary<string, object> { ["DisplayVersion"] = "old" };
            }
            var helper = Path.Combine(Root, "package-helper");
            var archive = Path.Combine(Root, "package-archive");
            File.WriteAllText(helper, "new helper");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry(SetupPaths.ServiceExecutableName).Open()) stream.Write(service);
                using (var stream = zip.CreateEntry("runtime/dep.dll").Open()) stream.Write([2]);
            }
            if (fail == "archive") File.WriteAllText(archive, "invalid archive");
            var manifest = new NetworkSetupManifest(1, typeof(SetupService).Assembly.GetName().Version!.ToString(3),
                "win-x64", NetworkSetupWire.Version, new(SetupPaths.SetupExecutableName, "", 1), new("network-service.zip", "", 1));
            // The production entry point supplies an already verified package; this fixture never invokes a publisher bypass there.
            Package = (NetworkSetupPackage)typeof(NetworkSetupPackage).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single().Invoke([manifest, File.OpenRead(helper), File.OpenRead(archive)]);
            Host = new SetupHost
            {
                InstallDirectory = Install,
                PrepareParent = () => ProtectedInstallation.Check(new DirectoryInfo(Root)),
                EnsureOwnedOrAbsent = () => Fail("ensure"),
                CaptureState = () => new(ServiceExists, Running, null),
                StopOwnedIfRunning = _ => { Running = false; Fail("stop"); },
                CreateOrUpdateOwned = users => { AuthorizedUsers = users.ToArray(); ServiceExists = true; Fail("service"); },
                StartAndVerify = _ => { Running = true; Fail("start"); },
                RestoreService = (state, _) => { ServiceExists = state.Exists; Running = state.WasRunning; },
                StopAndDeleteOwned = _ => ServiceExists = Running = false,
                CaptureRegistration = () => Registration,
                WriteRegistration = () => { Registration = new() { ["DisplayVersion"] = "new" }; Fail("registration"); },
                RestoreRegistration = value => Registration = value,
                VerifyPublisher = path => { Assert.That(File.Exists(path), Is.True); Fail("publisher"); },
                ProvisionLogs = _ => Fail("logging")
            };
            void Fail(string phase)
            {
                if (phase == fail) throw new IOException("Injected " + phase + " failure");
            }
        }

        internal void AssertOriginal(bool existing)
        {
            Assert.That(ServiceExists, Is.EqualTo(existing));
            Assert.That(Running, Is.EqualTo(existing));
            Assert.That(Directory.Exists(Install), Is.EqualTo(existing));
            if (existing)
            {
                Assert.That(File.ReadAllText(HelperPath), Is.EqualTo("old helper"));
                Assert.That(AuthorizationPolicy.Load(PolicyPath), Is.EqualTo(new[] { PreviousUser }));
                Assert.That(Registration!["DisplayVersion"], Is.EqualTo("old"));
                Assert.DoesNotThrow(() => InstalledFiles.Validate(Install));
            }
            else Assert.That(Registration, Is.Null);
            Assert.That(Directory.GetDirectories(Root, ".NetworkMonitor-*").Length, Is.Zero);
        }

        internal void AssertLeaseReleased()
        {
            using var lease = File.Open(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        private static void Write(string path, byte[] bytes)
        {
            using var stream = ProtectedInstallation.CreateFile(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
        }

        public void Dispose()
        {
            Package.Dispose();
            Directory.Delete(Root, true);
        }
    }
}
