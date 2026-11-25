using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class ProtectedInstallationCharacterizationTests
{
    [TestCase("program-files")]
    [TestCase("system-directory")]
    [TestCase("system-library")]
    [Category("Integration")]
    public void NativeProtectionAcceptsExistingProtectedWindowsEntriesWithoutChangingTheirSecurity(string entry)
    {
        FileSystemInfo item = entry switch
        {
            "program-files" => new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
            "system-directory" => new DirectoryInfo(Environment.SystemDirectory),
            _ => new FileInfo(Path.Combine(Environment.SystemDirectory, "kernel32.dll"))
        };
        Assert.That(item.Exists, Is.True, "The Windows protection fixture must exist.");
        var before = Security(item).GetSecurityDescriptorBinaryForm();

        Assert.DoesNotThrow(() => ProtectedInstallation.Check(item));

        Assert.That(Security(item).GetSecurityDescriptorBinaryForm(), Is.EqualTo(before));
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void NativeProtectionRejectsUnsafeOwnedTemporaryFilesAndDirectories(bool directory)
    {
        using var tree = new ProtectionDirectory();
        FileSystemInfo item;
        var grant = new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Write, AccessControlType.Allow);
        if (directory)
        {
            item = tree.Root;
            var security = tree.Root.GetAccessControl();
            security.AddAccessRule(grant);
            tree.Root.SetAccessControl(security);
        }
        else
        {
            var file = new FileInfo(Path.Combine(tree.Root.FullName, "empty-file"));
            File.WriteAllText(file.FullName, "");
            var security = file.GetAccessControl();
            security.AddAccessRule(grant);
            file.SetAccessControl(security);
            item = file;
        }
        var before = Security(item).GetSecurityDescriptorBinaryForm();

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ProtectedInstallation.Check(item))!.Message,
            Is.AnyOf("Untrusted installation owner.", "Untrusted installation writer."));

        Assert.That(Security(item).GetSecurityDescriptorBinaryForm(), Is.EqualTo(before));
    }

    [Test]
    [Category("Integration")]
    public void NativeProtectionRejectsAnOwnedJunctionBeforeInspectingItsTargetSecurity()
    {
        using var tree = new ProtectionDirectory();
        var target = tree.Root.CreateSubdirectory("target");
        var link = new DirectoryInfo(Path.Combine(tree.Root.FullName, "junction"));
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link.FullName, target.FullName })
            start.ArgumentList.Add(argument);
        try
        {
            using (var process = Process.Start(start)!)
            {
                if (!process.WaitForExit(10_000))
                {
                    process.Kill(true);
                    process.WaitForExit();
                    Assert.Fail("Creating the owned junction timed out.");
                }
                Assert.That(process.ExitCode, Is.Zero, process.StandardError.ReadToEnd());
            }
            link.Refresh();
            Assert.That(link.Attributes & FileAttributes.ReparsePoint, Is.EqualTo(FileAttributes.ReparsePoint));
            var before = target.GetAccessControl().GetSecurityDescriptorBinaryForm();

            Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ProtectedInstallation.Check(link))!.Message,
                Is.EqualTo("Reparse installation path."));

            Assert.That(target.GetAccessControl().GetSecurityDescriptorBinaryForm(), Is.EqualTo(before));
            Assert.That(target.EnumerateFileSystemInfos(), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(link.FullName)) Directory.Delete(link.FullName);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    [Category("Integration")]
    public void MissingEntriesRetainTheExistingReparseRejection(bool directory)
    {
        using var tree = new ProtectionDirectory();
        var path = Path.Combine(tree.Root.FullName, "absent");
        FileSystemInfo item = directory ? new DirectoryInfo(path) : new FileInfo(path);
        Assert.That(item.Exists, Is.False);

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => ProtectedInstallation.Check(item))!.Message,
            Is.EqualTo("Reparse installation path."));

        Assert.That(tree.Root.EnumerateFileSystemInfos(), Is.Empty);
    }

    [Test]
    public void NullEntryRetainsItsOriginalException()
    {
        Assert.Throws<NullReferenceException>(() => ProtectedInstallation.Check(null!));
    }

    private static FileSystemSecurity Security(FileSystemInfo item) => item is DirectoryInfo directory
        ? directory.GetAccessControl() : ((FileInfo)item).GetAccessControl();

    private sealed class ProtectionDirectory : IDisposable
    {
        internal readonly DirectoryInfo Root = Directory.CreateTempSubdirectory("WinMon-setup-protection-");

        public void Dispose()
        {
            if (!string.Equals(Root.Parent?.FullName,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !Root.Name.StartsWith("WinMon-setup-protection-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the owned protection fixture.");
            Root.Delete(true);
        }
    }
}
