using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class InstallationRecoveryTests
{
    private static InstalledFileList Manifest => new("WinMonNetworkMonitor.v1",
        [SetupPaths.ServiceExecutableName, SetupPaths.SetupExecutableName, SetupPaths.PolicyFileName, @"runtime\native.dll"]);

    [Test]
    public void RecoveryAcceptsOnlyMissingExpectedFilesAndReportsIncomplete()
    {
        string[] remaining = [InstalledFiles.FileName, SetupPaths.PolicyFileName];
        Assert.That(InstalledFiles.ValidateContents(Manifest, remaining, [".", "runtime"], true), Is.False);
        Assert.That(() => InstalledFiles.ValidateContents(Manifest, remaining, ["."], false),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(InstalledFiles.ValidateContents(Manifest,
            Manifest.Files.Append(InstalledFiles.FileName), [".", "runtime"], true), Is.True);
    }

    [Test]
    public void RecoveryRejectsUnexpectedFilesDirectoriesAndMissingInventory()
    {
        string[] remaining = [InstalledFiles.FileName, SetupPaths.PolicyFileName];
        Assert.That(() => InstalledFiles.ValidateContents(Manifest, remaining.Append("unrelated.txt"), ["."], true),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => InstalledFiles.ValidateContents(Manifest, remaining, [".", "unrelated"], true),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => InstalledFiles.ValidateContents(Manifest, [SetupPaths.PolicyFileName], ["."], true),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => InstalledFiles.ValidateContents(Manifest with { Owner = "other" }, remaining, ["."], true),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(() => InstalledFiles.ValidateContents(Manifest with { Files = ["../outside"] }, remaining, ["."], true),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RemovalVacatesCanonicalPathBeforeCleanupEvenWhenInterrupted(bool interrupted)
    {
        var parent = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "NetworkMonitor");
        var retired = Path.Combine(parent, "retired");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "payload"), "original");
        try
        {
            SetupService.RetireInstallation(root, retired, path =>
            {
                Assert.That(Directory.Exists(root), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(path, "payload")), Is.EqualTo("original"));
                if (interrupted) throw new IOException("locked file");
                Directory.Delete(path, true);
            });
            Assert.That(Directory.Exists(root), Is.False);
            Assert.That(Directory.Exists(retired), Is.EqualTo(interrupted));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "replacement"), "healthy");
            Assert.That(File.ReadAllText(Path.Combine(root, "replacement")), Is.EqualTo("healthy"));
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Test]
    public void FailedQuarantinePreservesInstallationAndDoesNotRunCleanup()
    {
        var parent = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "NetworkMonitor");
        var retired = Path.Combine(parent, "retired");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(retired);
        try
        {
            Assert.That(() => SetupService.RetireInstallation(root, retired, _ => Assert.Fail("Cleanup ran")),
                Throws.InstanceOf<IOException>());
            Assert.That(Directory.Exists(root), Is.True);
        }
        finally
        {
            Directory.Delete(parent, true);
        }
    }

    [Test, Category("Integration"), Category("Elevated")]
    [Explicit("Requires elevation to assign protected owners and permissions.")]
    public void RecoveryValidatesProtectedTreeBeforeAcceptingMissingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"WinMon-recovery-{Guid.NewGuid():N}");
        try
        {
            ProtectedInstallation.CreateDirectory(root);
            using (var stream = ProtectedInstallation.CreateFile(Path.Combine(root, InstalledFiles.FileName),
                       FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, Manifest, InstallationJsonContext.Default.InstalledFileList);
            InstalledFiles.Validate(root, true, out var complete);
            Assert.That(complete, Is.False);
            Assert.That(() => InstalledFiles.Validate(root), Throws.TypeOf<InvalidDataException>());

            var extra = Path.Combine(root, "unrelated.txt");
            using (ProtectedInstallation.CreateFile(extra, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            Assert.That(() => InstalledFiles.Validate(root, true, out _), Throws.TypeOf<InvalidDataException>());
            File.Delete(extra);

            var policy = Path.Combine(root, SetupPaths.PolicyFileName);
            using (ProtectedInstallation.CreateFile(policy, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            var fileSecurity = ProtectedInstallation.FileSecurity();
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.WriteData, AccessControlType.Allow));
            new FileInfo(policy).SetAccessControl(fileSecurity);
            Assert.That(() => InstalledFiles.Validate(root, true, out _), Throws.TypeOf<UnauthorizedAccessException>());
            new FileInfo(policy).SetAccessControl(ProtectedInstallation.FileSecurity());

            var security = ProtectedInstallation.Security();
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.WriteData, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(security);
            Assert.That(() => InstalledFiles.Validate(root, true, out _), Throws.TypeOf<UnauthorizedAccessException>());
            new DirectoryInfo(root).SetAccessControl(ProtectedInstallation.Security());

            Directory.CreateSymbolicLink(Path.Combine(root, "runtime"), Path.GetTempPath());
            Assert.That(() => InstalledFiles.Validate(root, true, out _), Throws.TypeOf<UnauthorizedAccessException>());
            Directory.Delete(Path.Combine(root, "runtime"));

            File.Delete(Path.Combine(root, InstalledFiles.FileName));
            Assert.That(() => InstalledFiles.Validate(root, true, out _), Throws.TypeOf<FileNotFoundException>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
