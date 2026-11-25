using System.Buffers.Binary;
using System.IO.Compression;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
public sealed class ArchiveExtractionCharacterizationTests
{
    private const string Service = "WinMon.NetworkService.exe";

    [TestCase("archive")]
    [TestCase("stagingDirectory")]
    [TestCase("createDirectory")]
    [TestCase("createFile")]
    [Category("Integration")]
    public void NullArgumentsAreRejectedBeforeAnyFactoryRuns(string argument)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]));
        var calls = 0;
        Action<string> directory = _ => calls++;
        FileStream FileFactory(string path) { calls++; return new(path, FileMode.CreateNew); }

        var error = Assert.Throws<ArgumentNullException>(() => PackageExtraction.ExtractServiceArchive(
            argument == "archive" ? null! : archive,
            argument == "stagingDirectory" ? null! : tree.Stage,
            argument == "createDirectory" ? null! : directory,
            argument == "createFile" ? null! : FileFactory));

        Assert.That(error!.ParamName, Is.EqualTo(argument));
        Assert.That(calls, Is.Zero);
        Assert.That(tree.Root.EnumerateFileSystemInfos(), Is.Empty);
        Assert.That(archive.Position, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(65_535)]
    [TestCase(65_536)]
    [TestCase(65_537)]
    [Category("Integration")]
    public void CopyPreservesEveryByteAcrossBufferBoundariesAndLeavesTheArchiveOpen(int length)
    {
        using var tree = new ExtractionDirectory();
        var bytes = Enumerable.Range(0, length).Select(index => (byte)(index * 31)).ToArray();
        using var archive = Archive((Service, []), ("runtime/payload.bin", bytes));
        var original = archive.ToArray();

        tree.Extract(archive);

        Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, Service)), Is.Empty);
        Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, "runtime", "payload.bin")), Is.EqualTo(bytes));
        Assert.That(tree.Directories, Is.EqualTo(new[] { ".", ".", "runtime" }));
        Assert.That(tree.Files, Is.EqualTo(new[] { Service, Path.Combine("runtime", "payload.bin") }));
        foreach (var name in tree.Files)
            using (File.Open(Path.Combine(tree.Stage, name), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Assert.That(archive.CanRead, Is.True);
        Assert.That(archive.ToArray(), Is.EqualTo(original));
    }

    [TestCase(0, false)]
    [TestCase(256, true)]
    [TestCase(257, false)]
    [Category("Integration")]
    public void EntryCountBoundsAreCheckedBeforeCreatingAnyEntry(int count, bool accepted)
    {
        using var tree = new ExtractionDirectory();
        var entries = Enumerable.Range(0, count)
            .Select(index => (index == 0 ? Service : $"file-{index}.bin", Array.Empty<byte>())).ToArray();
        using var archive = Archive(entries);

        if (accepted)
        {
            tree.Extract(archive);
            Assert.That(Directory.GetFiles(tree.Stage).Length, Is.EqualTo(count));
            Assert.That(tree.Files.Count, Is.EqualTo(count));
        }
        else
        {
            Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message,
                Is.EqualTo("Invalid service archive."));
            Assert.That(tree.Directories, Is.EqualTo(new[] { "." }));
            Assert.That(tree.Files, Is.Empty);
            Assert.That(Directory.EnumerateFileSystemEntries(tree.Stage), Is.Empty);
        }
        Assert.That(archive.CanRead, Is.True);
    }

    [Test]
    [Category("Integration")]
    public void ExistingStagingDirectoryIsRejectedBeforeReadingEvenAnInvalidArchive()
    {
        using var tree = new ExtractionDirectory();
        Directory.CreateDirectory(tree.Stage);
        var existing = Path.Combine(tree.Stage, "existing.txt");
        File.WriteAllText(existing, "retain");
        using var archive = new MemoryStream([1, 2, 3]);

        Assert.That(Assert.Throws<IOException>(() => tree.Extract(archive))!.Message,
            Is.EqualTo("Staging directory already exists."));

        Assert.That(archive.Position, Is.Zero);
        Assert.That(tree.Directories, Is.Empty);
        Assert.That(tree.Files, Is.Empty);
        Assert.That(File.ReadAllText(existing), Is.EqualTo("retain"));
    }

    [Test]
    [Category("Integration")]
    public void MalformedArchiveLeavesItsCreatedStagingDirectoryAndInputOpen()
    {
        using var tree = new ExtractionDirectory();
        using var archive = new MemoryStream([1, 2, 3]);

        Assert.Throws<InvalidDataException>(() => tree.Extract(archive));

        Assert.That(tree.Directories, Is.EqualTo(new[] { "." }));
        Assert.That(tree.Files, Is.Empty);
        Assert.That(Directory.EnumerateFileSystemEntries(tree.Stage), Is.Empty);
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase("ALLOWED-USERS.JSON")]
    [TestCase("winmon.networksetup.EXE")]
    [TestCase("INSTALLED-FILES.JSON")]
    [Category("Integration")]
    public void SetupOwnedRootNamesAreRejectedCaseInsensitivelyBeforeCreatingAFile(string name)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((name, [1]));

        Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message,
            Is.EqualTo("Archive contains setup-owned state or a symbolic link."));

        Assert.That(tree.Files, Is.Empty);
        Assert.That(tree.Directories, Is.EqualTo(new[] { "." }));
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase("allowed-users.json")]
    [TestCase("WinMon.NetworkSetup.exe")]
    [TestCase("installed-files.json")]
    [Category("Integration")]
    public void SetupOwnedNamesRemainAllowedBelowTheArchiveRoot(string name)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]), ($"nested/{name}", [2, 3]));

        tree.Extract(archive);

        Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, "nested", name)), Is.EqualTo(new byte[] { 2, 3 }));
        Assert.That(tree.Files, Is.EqualTo(new[] { Service, Path.Combine("nested", name) }));
    }

    [Test]
    [Category("Integration")]
    public void UnixSymbolicLinkMetadataIsRejectedBeforeOpeningAnOutput()
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]));
        PatchCentralDirectory(archive, 38, 0xA0000000);

        Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message,
            Is.EqualTo("Archive contains setup-owned state or a symbolic link."));

        Assert.That(tree.Files, Is.Empty);
        Assert.That(tree.Directories, Is.EqualTo(new[] { "." }));
    }

    [Test]
    [Category("Integration")]
    public void CaseAndSeparatorCollisionsRetainTheFirstPayloadAndNeverCreateTheSecond()
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]), ("runtime/dep.dll", [2]), ("runtime\\DEP.dll", [3]));

        Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message,
            Is.EqualTo("Archive contains colliding paths."));

        Assert.That(tree.Files, Is.EqualTo(new[] { Service, Path.Combine("runtime", "dep.dll") }));
        Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, "runtime", "dep.dll")), Is.EqualTo(new byte[] { 2 }));
        Assert.That(Directory.GetFiles(tree.Stage, "*", SearchOption.AllDirectories).Length, Is.EqualTo(2));
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase(0u, "Archive entry exceeds its declared size.", true)]
    [TestCase(2u, "Truncated service archive entry.", true)]
    [TestCase(268_435_456u, "Truncated service archive entry.", true)]
    [TestCase(268_435_457u, "Archive is too large.", false)]
    [Category("Integration")]
    public void DeclaredLengthsRetainTheirLimitAndCopyFailureBoundaries(uint declared, string message, bool createsFile)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [42]));
        PatchCentralDirectory(archive, 24, declared);

        Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message, Is.EqualTo(message));

        Assert.That(tree.Files, Is.EqualTo(createsFile ? new[] { Service } : Array.Empty<string>()));
        if (createsFile)
        {
            Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, Service)),
                Is.EqualTo(declared == 0 ? Array.Empty<byte>() : new byte[] { 42 }));
            using (File.Open(Path.Combine(tree.Stage, Service), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        else Assert.That(tree.Directories, Is.EqualTo(new[] { "." }));
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase(1, true)]
    [TestCase(2, false)]
    [Category("Integration")]
    public void CombinedUncompressedBytesAcceptTheLimitAndRejectTheNextByteBeforeOpeningAnotherFile(int finalBytes, bool accepted)
    {
        using var tree = new ExtractionDirectory();
        using var archive = new MemoryStream();
        const long firstLength = 268_435_455;
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Create, true))
        {
            using (var first = zip.CreateEntry(Service, CompressionLevel.Fastest).Open())
            {
                var zeros = new byte[65_536];
                for (long remaining = firstLength; remaining > 0;)
                {
                    var count = (int)Math.Min(remaining, zeros.Length);
                    first.Write(zeros, 0, count);
                    remaining -= count;
                }
            }
            using var last = zip.CreateEntry("last.bin", CompressionLevel.Fastest).Open();
            last.Write(new byte[finalBytes]);
        }
        archive.Position = 0;
        var outputs = new List<CountingFileStream>();
        FileStream CreateFile(string path)
        {
            var output = new CountingFileStream(path);
            outputs.Add(output);
            return output;
        }
        void Extract() => PackageExtraction.ExtractServiceArchive(archive, tree.Stage,
            path => { Directory.CreateDirectory(path); }, CreateFile);

        if (accepted) Assert.DoesNotThrow(Extract);
        else Assert.That(Assert.Throws<InvalidDataException>(Extract)!.Message, Is.EqualTo("Archive is too large."));

        Assert.That(outputs.Select(output => output.BytesWritten),
            Is.EqualTo(accepted ? new long[] { firstLength, finalBytes } : [firstLength]));
        Assert.That(outputs.All(output => !output.CanWrite), Is.True);
        Assert.That(File.Exists(Path.Combine(tree.Stage, "last.bin")), Is.EqualTo(accepted));
        Assert.That(archive.CanRead, Is.True);
    }

    [Test]
    [Category("Integration")]
    public void MissingServiceExecutableIsReportedAfterOtherFilesHaveBeenCopied()
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive(("other.bin", [4, 5]));

        Assert.That(Assert.Throws<InvalidDataException>(() => tree.Extract(archive))!.Message,
            Is.EqualTo("Service executable is missing."));

        Assert.That(File.ReadAllBytes(Path.Combine(tree.Stage, "other.bin")), Is.EqualTo(new byte[] { 4, 5 }));
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase("stage")]
    [TestCase("parent")]
    [TestCase("file")]
    [Category("Integration")]
    public void CreationFactoryFailuresPropagateWithoutRollbackOrClosingTheInput(string phase)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]), ("later.bin", [2]));
        var failure = new IOException("Controlled factory failure.");
        var directories = 0;
        var files = 0;
        void CreateDirectory(string path)
        {
            directories++;
            if ((phase == "stage" && directories == 1) || (phase == "parent" && directories == 2)) throw failure;
            Directory.CreateDirectory(path);
        }
        FileStream CreateFile(string path) { files++; throw failure; }

        Assert.That(Assert.Throws<IOException>(() => PackageExtraction.ExtractServiceArchive(
            archive, tree.Stage, CreateDirectory, CreateFile)), Is.SameAs(failure));

        Assert.That(directories, Is.EqualTo(phase == "stage" ? 1 : 2));
        Assert.That(files, Is.EqualTo(phase == "file" ? 1 : 0));
        Assert.That(Directory.Exists(tree.Stage), Is.EqualTo(phase != "stage"));
        Assert.That(archive.CanRead, Is.True);
    }

    [TestCase("write")]
    [TestCase("length")]
    [TestCase("dispose")]
    [TestCase("write-and-dispose")]
    [Category("Integration")]
    public void OutputFailuresDisposeTheFileAndKeepTheOriginalExceptionPrecedence(string phase)
    {
        using var tree = new ExtractionDirectory();
        using var archive = Archive((Service, [1]), ("later.bin", [2]));
        var operationFailure = new IOException("Controlled output failure.");
        var disposeFailure = new IOException("Controlled disposal failure.");
        ObservedFileStream? output = null;
        var files = 0;
        FileStream CreateFile(string path)
        {
            files++;
            return output = new ObservedFileStream(path, phase, operationFailure, disposeFailure);
        }

        Assert.That(Assert.Throws<IOException>(() => PackageExtraction.ExtractServiceArchive(
                archive, tree.Stage, path => { Directory.CreateDirectory(path); }, CreateFile)),
            Is.SameAs(phase is "dispose" or "write-and-dispose" ? disposeFailure : operationFailure));

        Assert.That(files, Is.EqualTo(1));
        Assert.That(output!.Disposed, Is.True);
        Assert.That(output.CanWrite, Is.False);
        Assert.That(File.Exists(Path.Combine(tree.Stage, "later.bin")), Is.False);
        using (File.Open(Path.Combine(tree.Stage, Service), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Assert.That(archive.CanRead, Is.True);
    }

    private static MemoryStream Archive(params (string Name, byte[] Bytes)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.Write(bytes);
            }
        stream.Position = 0;
        return stream;
    }

    private static void PatchCentralDirectory(MemoryStream archive, int fieldOffset, uint value)
    {
        var bytes = archive.GetBuffer();
        var end = checked((int)archive.Length - 22);
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end, 4)), Is.EqualTo(0x06054B50u));
        var central = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16, 4)));
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(central, 4)), Is.EqualTo(0x02014B50u));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + fieldOffset, 4), value);
    }

    private sealed class ObservedFileStream(string path, string phase, IOException operationFailure, IOException disposeFailure)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        internal bool Disposed { get; private set; }
        public override long Length => phase == "length" ? throw operationFailure : base.Length;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (phase is "write" or "write-and-dispose") throw operationFailure;
            base.Write(buffer, offset, count);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            Disposed = true;
            if (phase is "dispose" or "write-and-dispose") throw disposeFailure;
        }
    }

    private sealed class CountingFileStream(string path) : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        internal long BytesWritten { get; private set; }
        public override long Length => BytesWritten;
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;
    }

    private sealed class ExtractionDirectory : IDisposable
    {
        internal readonly DirectoryInfo Root = Directory.CreateTempSubdirectory("WinMon-archive-characterization-");
        internal string Stage => Path.Combine(Root.FullName, "stage");
        internal readonly List<string> Directories = [];
        internal readonly List<string> Files = [];
        internal void Extract(Stream archive) => PackageExtraction.ExtractServiceArchive(archive, Stage,
            path => { Directories.Add(Path.GetRelativePath(Stage, path)); Directory.CreateDirectory(path); },
            path => { Files.Add(Path.GetRelativePath(Stage, path)); return new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); });
        public void Dispose()
        {
            if (!string.Equals(Root.Parent?.FullName,
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase) ||
                !Root.Name.StartsWith("WinMon-archive-characterization-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the owned archive fixture.");
            Root.Delete(true);
        }
    }
}
