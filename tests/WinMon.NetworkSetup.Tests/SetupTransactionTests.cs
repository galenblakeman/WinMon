using NUnit.Framework;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class SetupTransactionTests
{
    [TestCase("3.5.1.0", "3.5.1")]
    [TestCase("3.5.0.0", "3.5.1")]
    public void SameVersionAndForwardRepairAreAllowed(string previous, string next) =>
        Assert.DoesNotThrow(() => SetupService.ValidateUpgrade(previous, next));

    [TestCase("3.5.2.0", "3.5.1")]
    [TestCase("3.5.1.1", "3.5.1")]
    public void RepairCannotDowngradeANewerService(string previous, string next) =>
        Assert.Throws<InvalidOperationException>(() => SetupService.ValidateUpgrade(previous, next));

    [Test]
    public void AnUnknownInstalledServiceVersionFailsClosed() =>
        Assert.Throws<InvalidDataException>(() => SetupService.ValidateUpgrade(null, "3.5.1"));

    [Test]
    public void ElevatedNativeImportsOnlySearchSystem32()
    {
        var policy = typeof(SetupTransaction).Assembly.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
        Assert.That(policy?.Paths, Is.EqualTo(DllImportSearchPath.System32));
        var overrides = typeof(SetupTransaction).Assembly.GetTypes().SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Where(method => method.IsDefined(typeof(DllImportAttribute)))
            .Select(method => method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>())
            .Where(attribute => attribute is not null);
        Assert.That(overrides.All(attribute => attribute!.Paths == DllImportSearchPath.System32), Is.True);
    }

    [Test]
    public void ProtectedCreationDescriptorsPinAdministratorsOwnerAndUsersReadOnly()
    {
        var administrators = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
        var directory = ProtectedInstallation.Security();
        var file = ProtectedInstallation.FileSecurity();

        Assert.That(directory.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)), Is.EqualTo(administrators));
        Assert.That(file.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)), Is.EqualTo(administrators));
        Assert.That(directory.AreAccessRulesProtected && file.AreAccessRulesProtected, Is.True);
        Assert.That(file.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Single(rule => rule.IdentityReference == users).FileSystemRights, Is.EqualTo(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize));
    }

    [Test]
    public void RegistrationDescriptorPinsAdministratorsOwnerAndUsersReadOnly()
    {
        var administrators = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);
        var security = SetupRegistration.Security();

        Assert.That(security.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)), Is.EqualTo(administrators));
        Assert.That(security.AreAccessRulesProtected, Is.True);
        Assert.That(security.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Cast<RegistryAccessRule>()
            .Single(rule => rule.IdentityReference == users).RegistryRights, Is.EqualTo(RegistryRights.ReadKey));
        Assert.DoesNotThrow(() => SetupRegistration.ValidateSecurity(security));
    }

    [Test]
    public void RegistrationDescriptorRejectsUntrustedOwnerOrWriter()
    {
        var user = new System.Security.Principal.SecurityIdentifier("S-1-5-21-1-2-3-4");
        var owner = SetupRegistration.Security();
        owner.SetOwner(user);
        Assert.That(() => SetupRegistration.ValidateSecurity(owner), Throws.TypeOf<UnauthorizedAccessException>());

        var writer = SetupRegistration.Security();
        writer.AddAccessRule(new RegistryAccessRule(user, RegistryRights.SetValue, AccessControlType.Allow));
        Assert.That(() => SetupRegistration.ValidateSecurity(writer), Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test, Explicit("Requires elevation to assign the protected Administrators owner.")]
    public void ElevatedSecureFactories_ProtectNewStagingChildrenAndLockImmediately()
    {
        var root = Path.Combine(Path.GetTempPath(), $"WinMon-network-setup-{Guid.NewGuid():N}");
        try
        {
            ProtectedInstallation.CreateDirectory(root);
            var intermediate = Path.Combine(root, "runtime");
            var nested = Path.Combine(intermediate, "native");
            ProtectedInstallation.CreateDirectory(nested);
            var payload = Path.Combine(nested, "payload.dll");
            using (ProtectedInstallation.CreateFile(payload, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var lockPath = Path.Combine(root, ".network-setup.lock");
            using (ProtectedInstallation.CreateFile(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            using (ProtectedInstallation.CreateFile(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }

            foreach (FileSystemInfo item in new FileSystemInfo[] { new DirectoryInfo(root), new DirectoryInfo(intermediate), new DirectoryInfo(nested), new FileInfo(payload), new FileInfo(lockPath) })
                Assert.DoesNotThrow(() => ProtectedInstallation.Check(item));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void FailedDeploymentRollsBackBeforeReturningAndReleasesLease()
    {
        var files = "old";
        var lease = new Lease();
        Assert.That(() => SetupTransaction.Start(() => { files = "new"; throw new IOException("SCM failed"); },
            () => files = "old", () => { }, lease), Throws.TypeOf<IOException>());
        Assert.That(files, Is.EqualTo("old"));
        Assert.That(lease.Disposed, Is.True);
    }

    [Test]
    public async Task MissingClientCommitRollsBackAndReleasesLease()
    {
        var state = "new";
        var lease = new Lease();
        var transaction = SetupTransaction.Start(() => { }, () => state = "old", () => state = "committed", lease);
        await transaction.DisposeAsync();
        await transaction.DisposeAsync();
        Assert.That(state, Is.EqualTo("old"));
        Assert.That(lease.Disposed, Is.True);
    }

    [Test]
    public async Task CancelledAcknowledgementCannotCommit()
    {
        var committed = false;
        var rolledBack = false;
        await using (var transaction = SetupTransaction.Start(() => { }, () => rolledBack = true, () => committed = true))
            Assert.That(() => transaction.CommitAsync(new CancellationToken(true)), Throws.TypeOf<OperationCanceledException>());
        Assert.That(committed, Is.False);
        Assert.That(rolledBack, Is.True);
    }

    [Test]
    public async Task FailureCleaningBackupAfterCommitDoesNotRollBackHealthyService()
    {
        var rolledBack = false;
        await using (var transaction = SetupTransaction.Start(() => { }, () => rolledBack = true, () => throw new IOException()))
            Assert.That(() => transaction.CommitAsync(default), Throws.TypeOf<IOException>());
        Assert.That(rolledBack, Is.False);
    }

    [Test]
    public void DeploymentAndRollbackFailuresAreBothReported()
    {
        Assert.That(() => SetupTransaction.Start(() => throw new IOException("deploy"),
            () => throw new UnauthorizedAccessException("restore"), () => { }), Throws.TypeOf<AggregateException>());
    }

    [Test]
    public void SetupFailure_PreservesFixedPhaseAndNumericCode()
    {
        var failure = SetupFailure.From(new IOException("sensitive details"));
        Assert.That(failure.Phase, Is.EqualTo("unknown"));
        Assert.That(failure.ErrorCode, Is.Not.EqualTo(0));
        var wrapped = Assert.Throws<SetupFailure>(() => SetupFailure.At("service", () => throw new System.ComponentModel.Win32Exception(2)));
        Assert.That(wrapped!.Phase, Is.EqualTo("service"));
        Assert.That(wrapped.ErrorCode, Is.EqualTo(2));
        var denied = Assert.Throws<SetupFailure>(() => SetupFailure.At("service", () => throw new System.ComponentModel.Win32Exception(5)));
        Assert.That(denied!.ErrorCode, Is.EqualTo(5));
        Assert.That(() => SetupFailure.At("service", () => throw new OperationCanceledException()), Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public void RevocationTransaction_RollsBackAfterRestartFailureAndStopsCachedServiceFirst()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policy = Path.Combine(root, "allowed-users.json");
        var replacement = Path.Combine(root, "replacement.json");
        var backup = Path.Combine(root, "backup.json");
        File.WriteAllText(policy, "old"); File.WriteAllText(replacement, "new");
        var calls = new List<string>();
        try
        {
            Assert.That(() => RevocationTransaction.Execute(new(true, true, []), replacement, policy, backup,
                () => calls.Add("stop"), () => calls.Add("update"), () => throw new IOException("start"), () => calls.Add("restore"), File.Copy), Throws.TypeOf<IOException>());
            Assert.That(File.ReadAllText(policy), Is.EqualTo("old"));
            Assert.That(calls, Is.EqualTo(new[] { "stop", "update", "stop", "restore" }));
            Assert.That(File.Exists(backup), Is.False);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void RevocationTransaction_RestoresStateWhenAtomicReplaceFails()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policy = Path.Combine(root, "allowed-users.json");
        File.WriteAllText(policy, "old");
        var calls = new List<string>();
        try
        {
            Assert.That(() => RevocationTransaction.Execute(new(true, true, []), Path.Combine(root, "missing.json"), policy, Path.Combine(root, "backup.json"),
                () => calls.Add("stop"), () => calls.Add("update"), () => calls.Add("start"), () => calls.Add("restore"), File.Copy), Throws.InstanceOf<IOException>());
            Assert.That(File.ReadAllText(policy), Is.EqualTo("old"));
            Assert.That(calls, Is.EqualTo(new[] { "stop", "stop", "restore" }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void RevocationTransaction_PreservesRecoveryBackupWhenRollbackFails()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policy = Path.Combine(root, "allowed-users.json");
        var replacement = Path.Combine(root, "replacement.json");
        var backup = Path.Combine(root, "backup.json");
        File.WriteAllText(policy, "old"); File.WriteAllText(replacement, "new");
        try
        {
            Assert.That(() => RevocationTransaction.Execute(new(true, true, []), replacement, policy, backup,
                () => { }, () => { }, () => throw new IOException("start"), () => throw new IOException("restore"), File.Copy), Throws.TypeOf<AggregateException>());
            Assert.That(File.Exists(backup), Is.True);
            Assert.That(File.ReadAllText(policy), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(backup), Is.EqualTo("old"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void RevocationTransaction_DoesNotStartOriginallyStoppedService()
    {
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var policy = Path.Combine(root, "allowed-users.json");
        var replacement = Path.Combine(root, "replacement.json");
        var backup = Path.Combine(root, "backup.json");
        File.WriteAllText(policy, "old"); File.WriteAllText(replacement, "new");
        var started = false;
        try
        {
            Assert.DoesNotThrow(() => RevocationTransaction.Execute(new(true, false, []), replacement, policy, backup,
                () => { }, () => { }, () => started = true, () => { }, File.Copy));
            Assert.That(started, Is.False);
            Assert.That(File.ReadAllText(policy), Is.EqualTo("new"));
            Assert.That(File.Exists(backup), Is.False);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Test]
    public void ProvisionedUsersCanOnlyQueryAndStartService()
    {
        const string sid = "S-1-5-21-1-2-3-4";
        var descriptor = new RawSecurityDescriptor(NativeService.Descriptor([sid]), 0);
        var rules = descriptor.DiscretionaryAcl!.Cast<CommonAce>().ToArray();
        Assert.That(rules, Has.Length.EqualTo(3));
        Assert.That(rules.Single(rule => rule.SecurityIdentifier.Value == sid).AccessMask, Is.EqualTo(0x15));
        Assert.That(rules.Where(rule => rule.SecurityIdentifier.Value != sid).All(rule => rule.AccessMask == 0xF01FF), Is.True);
        Assert.That(descriptor.ControlFlags.HasFlag(ControlFlags.DiscretionaryAclProtected), Is.True);
    }

    [TestCase("O:BAD:P(A;;GA;;;BA)(A;;0x15;;;S-1-5-21-1-2-3-4)", true)]
    [TestCase("O:BAD:P(A;;GA;;;BA)(A;;0x2;;;S-1-5-21-1-2-3-4)", false)]
    [TestCase("O:S-1-5-21-1-2-3-4D:P(A;;GA;;;BA)", false)]
    [TestCase("O:BAD:NO_ACCESS_CONTROL", false)]
    public void ExistingServiceCannotGrantAnUntrustedAccountOwnershipOrConfiguration(string sddl, bool valid)
    {
        var security = new RawSecurityDescriptor(sddl);
        var bytes = new byte[security.BinaryLength];
        security.GetBinaryForm(bytes, 0);
        if (valid) Assert.DoesNotThrow(() => NativeService.ValidateSecurity(bytes));
        else Assert.Throws<UnauthorizedAccessException>(() => NativeService.ValidateSecurity(bytes));
    }

    [TestCase("../outside")]
    [TestCase("C:/outside")]
    [TestCase("WinMon.NetworkService.exe:stream")]
    public void InstalledInventoryCannotAuthorizePathsOutsideOwnedTree(string name)
    {
        var manifest = new InstalledFileList("WinMonNetworkMonitor.v1", [SetupPaths.ServiceExecutableName, SetupPaths.SetupExecutableName, SetupPaths.PolicyFileName, name]);
        Assert.That(() => InstalledFiles.ValidateList(manifest), Throws.InstanceOf<InvalidDataException>());
    }

    private sealed class Lease : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
