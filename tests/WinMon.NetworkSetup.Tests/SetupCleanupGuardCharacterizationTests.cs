using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class SetupCleanupGuardCharacterizationTests
{
    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("missing")]
    [TestCase("file")]
    public void TreeCleanupIsANoOpForPathsThatAreNotDirectories(string kind)
    {
        using var fixture = new OwnedTree();
        var path = kind switch
        {
            "null" => null!, "empty" => "", "missing" => Path.Combine(fixture.Root.FullName, "absent"),
            "file" => fixture.Inventory, _ => throw new AssertionException("Unknown path partition.")
        };
        var before = fixture.Snapshot();
        var restart = SetupService.RestartRequired;

        Assert.DoesNotThrow(() => DeleteTree(path));

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(SetupService.RestartRequired, Is.EqualTo(restart));
    }

    [TestCase("null")]
    [TestCase("empty")]
    public void InstallationCleanupRetainsDirectoryArgumentErrors(string kind)
    {
        using var fixture = new OwnedTree();
        var before = fixture.Snapshot();
        var restart = SetupService.RestartRequired;
        var path = kind == "null" ? null! : "";

        var error = kind == "null"
            ? Assert.Throws<ArgumentNullException>(() => DeleteInstallation(path))
            : Assert.Throws<ArgumentException>(() => DeleteInstallation(path));

        Assert.That(error!.ParamName, Is.EqualTo("path"));
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(SetupService.RestartRequired, Is.EqualTo(restart));
    }

    [Test]
    public void MissingInstallationIsRejectedWithoutCreatingOrDeletingAnything()
    {
        using var fixture = new OwnedTree();
        var missing = Path.Combine(fixture.Root.FullName, "absent");
        var before = fixture.Snapshot();
        var restart = SetupService.RestartRequired;

        Assert.That(Assert.Throws<UnauthorizedAccessException>(() => DeleteInstallation(missing))!.Message,
            Is.EqualTo("Reparse installation path."));

        Assert.That(Directory.Exists(missing), Is.False);
        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(SetupService.RestartRequired, Is.EqualTo(restart));
    }

    [TestCase("tree")]
    [TestCase("installation")]
    public void UntrustedCleanupRootsAreRejectedBeforeInventoryAccessAndPreserveEveryEntry(string operation)
    {
        using var fixture = new OwnedTree();
        var before = fixture.Snapshot();
        var restart = SetupService.RestartRequired;
        var cleanup = operation == "tree" ? DeleteTree : DeleteInstallation;
        // Even malformed, exclusively locked inventory must not outrank the root trust check.
        using (var locked = new FileStream(fixture.Inventory, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.That(locked.Length, Is.GreaterThan(0));
            Assert.That(Assert.Throws<UnauthorizedAccessException>(() => cleanup(fixture.Root.FullName))!.Message,
                Is.AnyOf("Untrusted installation owner.", "Untrusted installation writer."));
        }

        Assert.That(fixture.Snapshot(), Is.EqualTo(before));
        Assert.That(SetupService.RestartRequired, Is.EqualTo(restart));
    }

    private static readonly Action<string> DeleteTree = Bind("DeleteTree");
    private static readonly Action<string> DeleteInstallation = Bind("DeleteInstallation");

    private static Action<string> Bind(string name) => typeof(SetupService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Action<string>>();

    private sealed class OwnedTree : IDisposable
    {
        internal DirectoryInfo Root { get; } = Directory.CreateTempSubdirectory("WinMon-cleanup-guard-");
        internal string Inventory => Path.Combine(Root.FullName, InstalledFiles.FileName);

        internal OwnedTree()
        {
            File.WriteAllText(Inventory, "{not-an-installed-inventory}");
            var child = Root.CreateSubdirectory("nested");
            File.WriteAllBytes(Path.Combine(child.FullName, "sentinel.bin"), [0, 1, 2, 127, 255]);
            child.CreateSubdirectory("empty");
            var security = Root.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Write, AccessControlType.Allow));
            Root.SetAccessControl(security);
        }

        internal string[] Snapshot() => new FileSystemInfo[] { Root }
            .Concat(Root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            .OrderBy(item => item.FullName, StringComparer.Ordinal)
            .Select(item =>
            {
                item.Refresh();
                var directory = item is DirectoryInfo;
                var security = directory ? ((DirectoryInfo)item).GetAccessControl().GetSecurityDescriptorBinaryForm()
                    : ((FileInfo)item).GetAccessControl().GetSecurityDescriptorBinaryForm();
                var bytes = directory ? "directory" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.FullName)));
                return $"{Path.GetRelativePath(Root.FullName, item.FullName)}|{item.Attributes}|{item.LastWriteTimeUtc.Ticks}|{Convert.ToHexString(security)}|{bytes}";
            }).ToArray();

        public void Dispose()
        {
            if (!string.Equals(Root.Parent?.FullName,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !Root.Name.StartsWith("WinMon-cleanup-guard-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the owned fixture.");
            Root.Delete(true);
        }
    }
}
