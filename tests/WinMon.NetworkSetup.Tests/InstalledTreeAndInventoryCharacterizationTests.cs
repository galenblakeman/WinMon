using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class InstalledTreeAndInventoryCharacterizationTests
{
    [TestCase("tree")]
    [TestCase("strict-inventory")]
    [TestCase("repair-inventory")]
    public void NullRootsRetainTheDirectoryArgumentError(string operation)
    {
        var complete = true;
        Assert.That(Assert.Throws<ArgumentNullException>(() => Read(operation, null!, ref complete))!.ParamName, Is.EqualTo("path"));
        Assert.That(complete, Is.True);
    }

    [TestCase("tree")]
    [TestCase("strict-inventory")]
    [TestCase("repair-inventory")]
    public void EmptyRootsRetainTheDirectoryArgumentError(string operation)
    {
        var complete = true;
        Assert.That(Assert.Throws<ArgumentException>(() => Read(operation, "", ref complete))!.ParamName, Is.EqualTo("path"));
        Assert.That(complete, Is.True);
    }

    [TestCase("tree")]
    [TestCase("strict-inventory")]
    [TestCase("repair-inventory")]
    public void MissingRootsRetainTheExistingReparseRejectionWithoutCreatingFiles(string operation)
    {
        using var fixture = new OwnedDirectory();
        var missing = Path.Combine(fixture.Root.FullName, "absent");
        var complete = true;

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => Read(operation, missing, ref complete))!.Message,
            Is.EqualTo("Reparse installation path."));

        Assert.That(complete, Is.True);
        Assert.That(fixture.Root.EnumerateFileSystemInfos(), Is.Empty);
    }

    [TestCase("tree")]
    [TestCase("strict-inventory")]
    [TestCase("repair-inventory")]
    public void UnsafeRootsAreRejectedBeforeTheirMalformedInventoryIsRead(string operation)
    {
        using var fixture = new OwnedDirectory();
        var root = fixture.Root.CreateSubdirectory("unsafe");
        var manifest = Path.Combine(root.FullName, InstalledFiles.FileName);
        File.WriteAllText(manifest, "{not-an-inventory}");
        var security = root.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Write, AccessControlType.Allow));
        root.SetAccessControl(security);
        var before = Snapshot([root, new FileInfo(manifest)]);
        var complete = true;

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => Read(operation, root.FullName, ref complete))!.Message,
            Is.AnyOf("Untrusted installation owner.", "Untrusted installation writer."));

        Assert.That(complete, Is.True);
        Assert.That(Snapshot([root, new FileInfo(manifest)]), Is.EqualTo(before));
        Assert.That(File.ReadAllText(manifest), Is.EqualTo("{not-an-inventory}"));
        Assert.That(root.EnumerateFileSystemInfos().Select(item => item.Name), Is.EqualTo(new[] { InstalledFiles.FileName }));
    }

    [TestCase("tree")]
    [TestCase("strict-inventory")]
    [TestCase("repair-inventory")]
    public void JunctionRootsAreRejectedWithoutTraversingOrChangingTheirOwnedTarget(string operation)
    {
        using var fixture = new OwnedDirectory();
        var target = fixture.Root.CreateSubdirectory("target");
        var link = Path.Combine(fixture.Root.FullName, "junction");
        try
        {
            CreateJunction(link, target.FullName);
            var before = Snapshot([target]);
            var complete = true;

            Assert.That(Assert.Throws<UnauthorizedAccessException>(() => Read(operation, link, ref complete))!.Message,
                Is.EqualTo("Reparse installation path."));

            Assert.That(complete, Is.True);
            Assert.That(Snapshot([target]), Is.EqualTo(before));
            Assert.That(target.EnumerateFileSystemInfos(), Is.Empty);
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    [Test]
    [Explicit("Requires the protected WinMon network installation and read permission.")]
    public void TrustedInstalledTreeIsCanonicalUniqueAndUnchanged()
    {
        var root = new DirectoryInfo(SetupPaths.InstallDirectory);
        var expected = EnumerateFixture(root);
        var before = Snapshot(expected);

        var actual = ProtectedInstallation.Tree(root.FullName);

        AssertTree(actual, root, expected);
        Assert.That(Snapshot(EnumerateFixture(root)), Is.EqualTo(before));
    }

    [TestCase(false, TestName = "InstalledInventoryMatchesProtectedFileSetWhenStrict")]
    [TestCase(true, TestName = "InstalledInventoryMatchesProtectedFileSetWhenRepairIsAllowed")]
    [Explicit("Requires the protected WinMon network installation and read permission.")]
    public void InstalledInventoryMatchesProtectedFileSet(bool allowMissing)
    {
        var root = new DirectoryInfo(SetupPaths.InstallDirectory);
        var expected = EnumerateFixture(root);
        var before = Snapshot(expected);
        var path = Path.Combine(root.FullName, InstalledFiles.FileName);
        var bytes = File.ReadAllBytes(path);
        using var manifest = JsonDocument.Parse(bytes);
        Assert.That(manifest.RootElement.GetProperty("Owner").GetString(), Is.EqualTo("WinMonNetworkMonitor.v1"));
        var listed = manifest.RootElement.GetProperty("Files").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.That(listed, Does.Contain(SetupPaths.ServiceExecutableName));
        Assert.That(listed, Does.Contain(SetupPaths.SetupExecutableName));
        Assert.That(listed, Does.Contain(SetupPaths.PolicyFileName));
        var expectedFiles = listed.Append(InstalledFiles.FileName).ToArray();

        var actual = InstalledFiles.Validate(root.FullName, allowMissing, out var complete);

        Assert.That(complete, Is.True);
        AssertTree(actual, root, expected);
        Assert.That(actual.OfType<FileInfo>().Select(file => Path.GetRelativePath(root.FullName, file.FullName)),
            Is.EquivalentTo(expectedFiles).Using<string>((IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase));
        if (!allowMissing)
            Assert.That(InstalledFiles.Validate(root.FullName).Select(item => item.FullName),
                Is.EqualTo(actual.Select(item => item.FullName)));
        Assert.That(SHA256.HashData(File.ReadAllBytes(path)), Is.EqualTo(SHA256.HashData(bytes)));
        Assert.That(Snapshot(EnumerateFixture(root)), Is.EqualTo(before));
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
    }

    private static void Read(string operation, string root, ref bool complete)
    {
        if (operation == "tree") _ = ProtectedInstallation.Tree(root);
        else _ = InstalledFiles.Validate(root, operation == "repair-inventory", out complete);
    }

    private static FileSystemInfo[] EnumerateFixture(DirectoryInfo root)
    {
        root.Refresh();
        Assert.That(root.Exists, Is.True, "The explicit installation fixture must exist.");
        Assert.That(root.Attributes & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
        var entries = root.EnumerateFileSystemInfos("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        }).Prepend<FileSystemInfo>(root).ToArray();
        Assert.That(entries.Length, Is.InRange(5, 4096));
        return entries;
    }

    private static void AssertTree(IReadOnlyList<FileSystemInfo> actual, DirectoryInfo root, FileSystemInfo[] expected)
    {
        Assert.That(actual.Count, Is.InRange(5, 4096));
        Assert.That(actual[0].FullName, Is.EqualTo(root.FullName).IgnoreCase);
        Assert.That(actual.Select(item => item.FullName),
            Is.EquivalentTo(expected.Select(item => item.FullName)).Using<string>((IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase));
        Assert.That(actual.Select(item => item.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(actual.Count));
        Assert.That(actual.ToDictionary(item => item.FullName, item => item is DirectoryInfo, StringComparer.OrdinalIgnoreCase),
            Is.EqualTo(expected.ToDictionary(item => item.FullName, item => item is DirectoryInfo, StringComparer.OrdinalIgnoreCase)));
        var positions = actual.Select((item, index) => (item.FullName, Index: index))
            .ToDictionary(item => item.FullName, item => item.Index, StringComparer.OrdinalIgnoreCase);
        var prefix = Path.TrimEndingDirectorySeparator(root.FullName) + Path.DirectorySeparatorChar;
        foreach (var item in actual.Skip(1))
        {
            Assert.That(item.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(item.Attributes & FileAttributes.ReparsePoint, Is.EqualTo((FileAttributes)0));
            Assert.That(positions[Path.GetDirectoryName(item.FullName)!], Is.LessThan(positions[item.FullName]));
        }
    }

    private static Dictionary<string, EntryState> Snapshot(IEnumerable<FileSystemInfo> entries) => entries.ToDictionary(
        item => item.FullName, item =>
        {
            item.Refresh();
            FileSystemSecurity security = item is DirectoryInfo directory
                ? directory.GetAccessControl() : ((FileInfo)item).GetAccessControl();
            return new EntryState(item is DirectoryInfo, item is FileInfo file ? file.Length : 0,
                item.LastWriteTimeUtc.Ticks, Convert.ToHexString(security.GetSecurityDescriptorBinaryForm()));
        }, StringComparer.OrdinalIgnoreCase);

    private static void CreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        if (!process.WaitForExit(10_000))
        {
            process.Kill(true);
            process.WaitForExit();
            Assert.Fail("Creating the owned junction timed out.");
        }
        Assert.That(process.ExitCode, Is.Zero, process.StandardError.ReadToEnd());
        Assert.That(new DirectoryInfo(link).Attributes & FileAttributes.ReparsePoint, Is.EqualTo(FileAttributes.ReparsePoint));
    }

    private sealed record EntryState(bool Directory, long Length, long LastWriteTicks, string Security);

    private sealed class OwnedDirectory : IDisposable
    {
        internal readonly DirectoryInfo Root = Directory.CreateTempSubdirectory("WinMon-installed-readonly-");
        public void Dispose()
        {
            if (!string.Equals(Root.Parent?.FullName,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !Root.Name.StartsWith("WinMon-installed-readonly-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the owned inventory fixture.");
            Root.Delete(true);
        }
    }
}
