using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class InstallPreparationCharacterizationTests
{
    private const string User = "S-1-5-21-1-2-3-1001";

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("malformed")]
    [TestCase("everyone")]
    public void InvalidIdentityOutranksVersionAndCancellationWithoutTakingTheLease(string kind)
    {
        using var fixture = new Preparation();
        var sid = kind switch
        {
            "null" => null!, "empty" => "", "malformed" => "not-a-sid", "everyone" => "S-1-1-0",
            _ => throw new AssertionException("Unknown identity partition.")
        };
        var registration = new Dictionary<string, object> { ["DisplayVersion"] = "not-a-version" };
        var before = fixture.Snapshot();

        var error = Assert.Throws<InvalidDataException>(() => fixture.Install(sid, registration));

        Assert.That(error!.Message, Is.EqualTo("Invalid authorization identity."));
        Assert.That(fixture.Events, Is.Empty);
        Assert.That(fixture.Lease.DisposeCalls, Is.Zero);
        fixture.AssertUnchanged(before);
    }

    [TestCase("registration-invalid")]
    [TestCase("registration-downgrade")]
    [TestCase("requested-invalid")]
    [TestCase("file-unversioned")]
    [TestCase("file-downgrade")]
    public void VersionFailuresPrecedeCancellationAndLeaveTheLeaseWithTheCaller(string kind)
    {
        var requested = kind == "requested-invalid" ? "unknown" : "0.0.0";
        using var fixture = new Preparation(requested);
        var installed = kind switch
        {
            "registration-invalid" => "unknown", "registration-downgrade" => "9.0.0", _ => "0.0.0"
        };
        var registration = new Dictionary<string, object> { ["DisplayVersion"] = installed };
        if (kind == "file-unversioned") File.WriteAllText(fixture.Executable, "owned unversioned file");
        if (kind == "file-downgrade") fixture.CopyVersionedExecutable();
        var before = fixture.Snapshot();

        if (kind is "registration-downgrade" or "file-downgrade")
            Assert.That(Assert.Throws<InvalidOperationException>(() => fixture.Install(User, registration))!.Message,
                Is.EqualTo("A newer network service is installed; setup cannot downgrade it."));
        else
            Assert.That(Assert.Throws<InvalidDataException>(() => fixture.Install(User, registration))!.Message,
                Is.EqualTo("Cannot verify installed service version."));

        Assert.That(fixture.Events, Is.Empty);
        Assert.That(fixture.Lease.DisposeCalls, Is.Zero);
        fixture.AssertUnchanged(before);
    }

    [TestCase("none")]
    [TestCase("missing")]
    [TestCase("non-string")]
    [TestCase("equal")]
    [TestCase("older")]
    [TestCase("file-wins")]
    public void PreCanceledPreparationRestoresTheOriginalRegistrationBeforeReleasingTheLease(string kind)
    {
        using var fixture = new Preparation();
        Dictionary<string, object>? registration = kind switch
        {
            "none" => null,
            "missing" => new() { ["Other"] = "preserved" },
            "non-string" => new() { ["DisplayVersion"] = 42 },
            "equal" => new() { ["DisplayVersion"] = fixture.Version },
            "older" => new() { ["DisplayVersion"] = "0.0.0" },
            "file-wins" => new() { ["DisplayVersion"] = "999.0.0" },
            _ => throw new AssertionException("Unknown registration partition.")
        };
        if (kind == "file-wins") fixture.CopyVersionedExecutable();
        var originalValues = registration?.ToArray();
        var before = fixture.Snapshot();

        var error = Assert.Throws<OperationCanceledException>(() => fixture.Install(User, registration,
            preserveBackup: kind == "equal"));

        Assert.That(error!.CancellationToken, Is.EqualTo(fixture.Cancellation.Token));
        Assert.That(fixture.Events, Is.EqualTo(new[] { "restore-registration", "dispose-lease" }));
        Assert.That(fixture.RestoredRegistration, Is.SameAs(registration));
        Assert.That(registration?.ToArray(), Is.EqualTo(originalValues));
        Assert.That(fixture.Lease.DisposeCalls, Is.EqualTo(1));
        fixture.AssertUnchanged(before);
    }

    [Test]
    public void FailedCancellationRollbackReportsBothFailuresAndStillReleasesTheLease()
    {
        var restoreFailure = new IOException("Owned restore failure");
        using var fixture = new Preparation(restoreFailure: restoreFailure);
        var registration = new Dictionary<string, object> { ["DisplayVersion"] = fixture.Version };
        var before = fixture.Snapshot();

        var error = Assert.Throws<AggregateException>(() => fixture.Install(User, registration));

        Assert.That(error!.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(error.InnerExceptions[0], Is.TypeOf<OperationCanceledException>());
        Assert.That(((OperationCanceledException)error.InnerExceptions[0]).CancellationToken,
            Is.EqualTo(fixture.Cancellation.Token));
        Assert.That(error.InnerExceptions[1], Is.SameAs(restoreFailure));
        Assert.That(fixture.Events, Is.EqualTo(new[] { "restore-registration", "dispose-lease" }));
        Assert.That(fixture.RestoredRegistration, Is.SameAs(registration));
        Assert.That(fixture.Lease.DisposeCalls, Is.EqualTo(1));
        fixture.AssertUnchanged(before);
    }

    private static readonly Func<string, NetworkSetupPackage, NativeService.ServiceState,
        Dictionary<string, object>?, IDisposable, CancellationToken, bool, SetupHost, SetupTransaction> InstallCore =
        typeof(SetupService).GetMethod("Install", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, NetworkSetupPackage, NativeService.ServiceState,
                Dictionary<string, object>?, IDisposable, CancellationToken, bool, SetupHost, SetupTransaction>>();

    private sealed class Preparation : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("WinMon-install-preparation-");
        private readonly NetworkSetupPackage _package;
        private readonly SetupHost _host;
        private readonly bool _restart = SetupService.RestartRequired;
        internal string Version { get; }
        internal string Executable => Path.Combine(_host.InstallDirectory, SetupPaths.ServiceExecutableName);
        internal CancellationTokenSource Cancellation { get; } = new();
        internal List<string> Events { get; } = [];
        internal RecordingLease Lease { get; }
        internal Dictionary<string, object>? RestoredRegistration { get; private set; }

        internal Preparation(string? version = null, Exception? restoreFailure = null)
        {
            Version = version ?? typeof(SetupService).Assembly.GetName().Version!.ToString(3);
            var helper = Path.Combine(_root.FullName, "helper");
            var archive = Path.Combine(_root.FullName, "archive");
            File.WriteAllText(helper, "owned helper");
            File.WriteAllText(archive, "owned archive");
            var manifest = new NetworkSetupManifest(1, Version, "win-x64", NetworkSetupWire.Version,
                new(SetupPaths.SetupExecutableName, "", 1), new("network-service.zip", "", 1));
            // Upstream supplies a verified package. This owned fixture is never extracted or trusted as executable code.
            _package = (NetworkSetupPackage)typeof(NetworkSetupPackage)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke([manifest, File.OpenRead(helper), File.OpenRead(archive)]);
            Lease = new RecordingLease(Events);
            Cancellation.Cancel();
            _host = new SetupHost
            {
                InstallDirectory = _root.CreateSubdirectory("install").FullName,
                PrepareParent = () => throw Unexpected(),
                EnsureOwnedOrAbsent = () => throw Unexpected(),
                CaptureState = () => throw Unexpected(),
                StopOwnedIfRunning = _ => throw Unexpected(),
                CreateOrUpdateOwned = _ => throw Unexpected(),
                StartAndVerify = _ => throw Unexpected(),
                RestoreService = (_, _) => throw Unexpected(),
                StopAndDeleteOwned = _ => throw Unexpected(),
                CaptureRegistration = () => throw Unexpected(),
                WriteRegistration = () => throw Unexpected(),
                RestoreRegistration = value =>
                {
                    Events.Add("restore-registration");
                    RestoredRegistration = value;
                    if (restoreFailure is not null) throw restoreFailure;
                },
                VerifyPublisher = _ => throw Unexpected(),
                ProvisionLogs = _ => throw Unexpected()
            };
        }

        private static AssertionException Unexpected() => new("Unexpected setup host operation.");

        internal SetupTransaction Install(string sid, Dictionary<string, object>? registration, bool preserveBackup = false) =>
            InstallCore(sid, _package, new(false, false, null), registration, Lease, Cancellation.Token, preserveBackup, _host);

        internal void CopyVersionedExecutable()
        {
            File.Copy(typeof(SetupService).Assembly.Location, Executable);
            Assert.That(FileVersionInfo.GetVersionInfo(Executable).FileVersion, Is.Not.Null.And.Not.Empty);
        }

        internal string[] Snapshot() => new FileSystemInfo[] { _root }
            .Concat(_root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            .OrderBy(item => item.FullName, StringComparer.Ordinal)
            .Select(item => $"{Path.GetRelativePath(_root.FullName, item.FullName)}|{(item is DirectoryInfo ? "directory" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.FullName))))}")
            .ToArray();

        internal void AssertUnchanged(string[] before)
        {
            Assert.Multiple(() =>
            {
                Assert.That(Snapshot(), Is.EqualTo(before), "No staging, swap, extraction or deletion may occur.");
                Assert.That(_package.HelperStream.CanRead && _package.ArchiveStream.CanRead, Is.True);
                Assert.That(_package.HelperStream.Position, Is.Zero);
                Assert.That(_package.ArchiveStream.Position, Is.Zero);
                Assert.That(SetupService.RestartRequired, Is.EqualTo(_restart));
            });
        }

        public void Dispose()
        {
            _package.Dispose();
            Cancellation.Dispose();
            if (!string.Equals(_root.Parent?.FullName,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !_root.Name.StartsWith("WinMon-install-preparation-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the owned fixture.");
            _root.Delete(true);
        }
    }

    private sealed class RecordingLease(List<string> events) : IDisposable
    {
        internal int DisposeCalls { get; private set; }
        public void Dispose()
        {
            DisposeCalls++;
            events.Add("dispose-lease");
        }
    }
}
