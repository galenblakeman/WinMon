using WinMon.Monitoring;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class NetworkSetupExtractionCharacterizationTests
{
    private static readonly Action<string, CancellationToken> Extract = typeof(NetworkSetupBroker)
        .GetMethod("Extract", BindingFlags.NonPublic | BindingFlags.Static)!
        .CreateDelegate<Action<string, CancellationToken>>();
    private static readonly (string Resource, string File)[] Assets =
    [
        ("WinMon.NetworkSetup.exe", "WinMon.NetworkSetup.exe"),
        ("WinMon.NetworkSetup.Service.zip", "network-service.zip"),
        ("WinMon.NetworkSetup.Manifest.json", "network-service-manifest.json"),
        ("WinMon.NetworkSetup.Manifest.sig", "network-service-manifest.sig")
    ];
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        Assert.That(NetworkSetupBroker.Supported, Is.True);
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.SetupExtraction." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        var path = Path.GetFullPath(_directory);
        Assert.That(Path.GetDirectoryName(path), Is.EqualTo(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(path), Does.StartWith("WinMon.SetupExtraction."));
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    [Test]
    public void PreCanceledExtractionCreatesAProtectedRequesterDirectoryBeforeObservingCancellation()
    {
        var root = Path.Combine(_directory, "fresh");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.Throws<OperationCanceledException>(() => Extract(root, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Directory.Exists(root), Is.True);
            Assert.That(Directory.GetFileSystemEntries(root), Is.Empty);
        });
        using var identity = WindowsIdentity.GetCurrent();
        var requester = identity.User ?? throw new AssertionException("The fixture requires a requester SID");
        var expected = new[]
        {
            requester.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value
        }.Distinct().ToArray();
        var security = new DirectoryInfo(root).GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(security.AreAccessRulesProtected, Is.True);
            Assert.That(rules.Select(rule => rule.IdentityReference.Value), Is.EquivalentTo(expected));
            Assert.That(rules.All(rule => !rule.IsInherited && rule.AccessControlType == AccessControlType.Allow &&
                rule.FileSystemRights == FileSystemRights.FullControl && rule.PropagationFlags == PropagationFlags.None &&
                rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit)), Is.True);
        });
    }

    [Test]
    public void PreCanceledExtractionKeepsAnExistingDirectoryAndItsFiles()
    {
        var root = Path.Combine(_directory, "existing");
        Directory.CreateDirectory(root);
        var sentinel = Path.Combine(root, "keep.bin");
        byte[] bytes = [0, 255, 17, 34];
        File.WriteAllBytes(sentinel, bytes);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.Throws<OperationCanceledException>(() => Extract(root, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Directory.GetFiles(root), Is.EqualTo(new[] { sentinel }));
            Assert.That(File.ReadAllBytes(sentinel), Is.EqualTo(bytes));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FileAtExtractionRootFailsBeforeCancellationWithoutChangingItsContents(bool canceled)
    {
        var root = Path.Combine(_directory, "occupied");
        File.WriteAllText(root, "owned sentinel");
        using var cancellation = new CancellationTokenSource();
        if (canceled) cancellation.Cancel();

        Assert.Throws<IOException>(() => Extract(root, cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(root), Is.EqualTo("owned sentinel"));
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.EqualTo(new[] { root }));
        });
    }

    [Test]
    public void ExtractionPreservesEmbeddedBytesAndReportsTheFirstMissingAssetWithoutInventingAFile()
    {
        var root = Path.Combine(_directory, "assets");
        var assembly = typeof(NetworkSetupBroker).Assembly;
        var missing = Array.FindIndex(Assets, asset => assembly.GetManifestResourceInfo(asset.Resource) is null);

        if (missing >= 0)
        {
            var error = Assert.Throws<InvalidDataException>(() => Extract(root, default));
            Assert.That(error!.Message, Is.EqualTo("Missing signed network setup asset."));
        }
        else Extract(root, default);

        var written = Assets.Take(missing < 0 ? Assets.Length : missing).ToArray();
        Assert.That(Directory.GetFiles(root).Select(Path.GetFileName), Is.EquivalentTo(written.Select(asset => asset.File)));
        foreach (var asset in written)
        {
            using var source = assembly.GetManifestResourceStream(asset.Resource)!;
            using var destination = File.OpenRead(Path.Combine(root, asset.File));
            Assert.That(SHA256.HashData(destination), Is.EqualTo(SHA256.HashData(source)), asset.File);
        }
    }

    [Test]
    public void ExtractionNeverOverwritesAnExistingDestinationAndChecksItsSourceFirst()
    {
        var root = Path.Combine(_directory, "collision");
        Directory.CreateDirectory(root);
        foreach (var asset in Assets) File.WriteAllText(Path.Combine(root, asset.File), "keep " + asset.File);
        var sourceExists = typeof(NetworkSetupBroker).Assembly.GetManifestResourceInfo(Assets[0].Resource) is not null;

        if (sourceExists) Assert.Throws<IOException>(() => Extract(root, default));
        else
        {
            var error = Assert.Throws<InvalidDataException>(() => Extract(root, default));
            Assert.That(error!.Message, Is.EqualTo("Missing signed network setup asset."));
        }

        Assert.Multiple(() =>
        {
            Assert.That(Directory.GetFiles(root).Select(Path.GetFileName), Is.EquivalentTo(Assets.Select(asset => asset.File)));
            foreach (var asset in Assets)
                Assert.That(File.ReadAllText(Path.Combine(root, asset.File)), Is.EqualTo("keep " + asset.File));
        });
    }
}
