using System.IO.Compression;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class PackageExtractionTests
{
    [TestCase("allowed-users.json")]
    [TestCase("WinMon.NetworkSetup.exe")]
    [TestCase("installed-files.json")]
    public void ExtractServiceArchive_RejectsProvisionedState(string name)
    {
        using var archive = Archive(("WinMon.NetworkService.exe", "service"), (name, "injected"));
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        try { Assert.That(() => Extract(archive, root), Throws.TypeOf<InvalidDataException>()); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestCase("../WinMon.NetworkService.exe")]
    [TestCase("dir/../WinMon.NetworkService.exe")]
    [TestCase("C:/WinMon.NetworkService.exe")]
    [TestCase("\\server\\share")]
    [TestCase("NUL.dll")]
    [TestCase("x/COM1")]
    [TestCase("x/evil. ")]
    public void ValidateEntryName_RejectsUnsafeOrAliasedPaths(string path)
    {
        Assert.That(() => PackageExtraction.ValidateEntryName(path), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public void ExtractServiceArchive_RejectsCaseCollisionsBeforeWritingSecondFile()
    {
        using var archive = Archive(("WinMon.NetworkService.exe", "a"), ("winmon.networkservice.exe", "b"));
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            Assert.That(() => Extract(archive, root), Throws.InstanceOf<InvalidDataException>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ExtractServiceArchive_ExtractsBoundedRelativeTree()
    {
        using var archive = Archive(("WinMon.NetworkService.exe", "service"), ("runtime/dep.dll", "dependency"));
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            Extract(archive, root);
            Assert.That(File.ReadAllText(Path.Combine(root, "runtime", "dep.dll")), Is.EqualTo("dependency"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ExtractServiceArchive_UsesCreationFactoriesForEveryStageChild()
    {
        using var archive = Archive(("WinMon.NetworkService.exe", "service"), ("runtime/dep.dll", "dependency"));
        var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        var directories = new List<string>();
        var files = new List<string>();
        try
        {
            PackageExtraction.ExtractServiceArchive(archive, root,
                path => { directories.Add(path); Directory.CreateDirectory(path); },
                path => { files.Add(path); return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); });
            Assert.That(directories, Does.Contain(root));
            Assert.That(directories, Does.Contain(Path.Combine(root, "runtime")));
            Assert.That(files, Is.EquivalentTo(new[] { Path.Combine(root, "WinMon.NetworkService.exe"), Path.Combine(root, "runtime", "dep.dll") }));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static MemoryStream Archive(params (string Name, string Value)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, value) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(value);
            }
        stream.Position = 0;
        return stream;
    }

    private static void Extract(Stream archive, string root) => PackageExtraction.ExtractServiceArchive(archive, root,
        path => { Directory.CreateDirectory(path); }, path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));
}
