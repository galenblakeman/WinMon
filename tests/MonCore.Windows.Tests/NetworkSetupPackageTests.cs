using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkSetupPackageTests
{
    private const string SignedBundleReason = "Requires a signed network setup release bundle via WINMON_NETWORK_SETUP_TEST_PACKAGE.";

    [Test, Category("Integration"), Explicit(SignedBundleReason)]
    public void SignedPackage_VerifiesPinsLocksAndReleasesAssets()
    {
        var root = Environment.GetEnvironmentVariable("WINMON_NETWORK_SETUP_TEST_PACKAGE");
        Assert.That(root, Is.Not.Null.And.Not.Empty);
        var version = typeof(NetworkSetupPackage).Assembly.GetName().Version!.ToString(3);
        var helper = Path.Combine(root!, "WinMon.NetworkSetup.exe");
        var archive = Path.Combine(root!, "network-service.zip");
        using (var package = NetworkSetupPackage.Open(root!, version))
        {
            Assert.That(package.Manifest.Version, Is.EqualTo(version));
            Assert.That(package.HelperStream.Position, Is.Zero);
            Assert.That(package.ArchiveStream.Position, Is.Zero);
            Assert.Throws<IOException>(() => File.Open(helper, FileMode.Open, FileAccess.Read, FileShare.None).Dispose());
            Assert.Throws<IOException>(() => File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.None).Dispose());
        }
        using var releasedHelper = File.Open(helper, FileMode.Open, FileAccess.Read, FileShare.None);
        using var releasedArchive = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Test, Category("Integration"), Explicit(SignedBundleReason)]
    public void SignedPackage_FailedAssetVerificationReleasesBothHandles()
    {
        var bundle = Environment.GetEnvironmentVariable("WINMON_NETWORK_SETUP_TEST_PACKAGE");
        Assert.That(bundle, Is.Not.Null.And.Not.Empty);
        var root = Path.Combine(Path.GetTempPath(), "WinMon-signed-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[] { "WinMon.NetworkSetup.exe", "network-service.zip",
                         "network-service-manifest.json", "network-service-manifest.sig" })
                File.Copy(Path.Combine(bundle!, name), Path.Combine(root, name));
            var archive = Path.Combine(root, "network-service.zip");
            using (var stream = new FileStream(archive, FileMode.Append, FileAccess.Write, FileShare.None)) stream.WriteByte(0);
            Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.Open(root,
                typeof(NetworkSetupPackage).Assembly.GetName().Version!.ToString(3)));
            using var helper = File.Open(Path.Combine(root, "WinMon.NetworkSetup.exe"), FileMode.Open, FileAccess.Read, FileShare.None);
            using var releasedArchive = File.Open(archive, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Open_RequiresPackageDirectory(string? directory)
    {
        Assert.Throws<ArgumentException>(() => NetworkSetupPackage.Open(directory!, "3.5.1"));
    }

    [Test]
    public void Open_RejectsMissingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "WinMon-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.Open(path, "3.5.1"));
    }

    [TestCase(0, 4)]
    [TestCase(65537, 4)]
    [TestCase(4, 0)]
    [TestCase(4, 16385)]
    [TestCase(4, 5)]
    [Category("Integration")]
    public void Open_RejectsUnboundedOrUnsignedMetadataBeforeOpeningAssets(int manifestSize, int signatureSize)
    {
        var root = Path.Combine(Path.GetTempPath(), "WinMon-package-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "network-service-manifest.json"), new string('x', manifestSize));
            File.WriteAllText(Path.Combine(root, "network-service-manifest.sig"), new string('x', signatureSize));
            var error = Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.Open(root, "3.5.1"));
            Assert.That(error!.Message, Does.Contain(manifestSize == 4 && signatureSize == 5
                ? "signature is invalid" : "metadata size is invalid"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("not base64")]
    public void VerifyManifest_RejectsMissingOrMalformedSignature(string signature)
    {
        using var key = RSA.Create(2048);
        Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyManifest([1], signature, "3.5.1",
            key.ExportSubjectPublicKeyInfoPem()));
    }

    [TestCase("null")]
    [TestCase("{")]
    public void VerifyManifest_RejectsSignedInvalidJson(string json)
    {
        using var key = RSA.Create(2048);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var error = Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyManifest(bytes,
            Sign(key, bytes), "3.5.1", key.ExportSubjectPublicKeyInfoPem()));
        Assert.That(error!.Message, Is.EqualTo("Network setup manifest is invalid."));
    }

    [TestCase(0)]
    [TestCase(65537)]
    public void VerifyManifest_RejectsOutOfBoundsSize(int size)
    {
        Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyManifest(new byte[size], "signature",
            "3.5.1", "key"));
    }

    [TestCase("", "key")]
    [TestCase("3.5.1", "")]
    public void VerifyManifest_RequiresVersionAndPublicKey(string version, string publicKey)
    {
        Assert.Throws<ArgumentException>(() =>
            NetworkSetupPackage.VerifyManifest([1], "signature", version, publicKey));
    }

    [Test]
    public void VerifyManifest_AcceptsSignedCompatibleManifest()
    {
        using var key = RSA.Create(2048);
        var manifest = CreateManifest();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest,
            NetworkSetupManifestJsonContext.Default.NetworkSetupManifest);
        var verified =
            NetworkSetupPackage.VerifyManifest(bytes, Sign(key, bytes), "3.5.1", key.ExportSubjectPublicKeyInfoPem());

        Assert.That(verified, Is.EqualTo(manifest));
    }

    [Test]
    public void VerifyManifest_RejectsTamperingAndWrongVersion()
    {
        using var key = RSA.Create(2048);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(CreateManifest(),
            NetworkSetupManifestJsonContext.Default.NetworkSetupManifest);
        var signature = Sign(key, bytes);

        var tampered = bytes.ToArray();
        tampered[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() =>
            NetworkSetupPackage.VerifyManifest(tampered, signature, "3.5.1", key.ExportSubjectPublicKeyInfoPem()));
        Assert.Throws<InvalidDataException>(() =>
            NetworkSetupPackage.VerifyManifest(bytes, signature, "3.5.2", key.ExportSubjectPublicKeyInfoPem()));
    }

    [TestCase("../WinMon.NetworkSetup.exe")]
    [TestCase("other.exe")]
    public void VerifyManifest_RejectsAssetPathsAndNames(string helperName)
    {
        using var key = RSA.Create(2048);
        var manifest = CreateManifest() with { Helper = CreateManifest().Helper with { Name = helperName } };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest,
            NetworkSetupManifestJsonContext.Default.NetworkSetupManifest);

        Assert.Throws<InvalidDataException>(() =>
            NetworkSetupPackage.VerifyManifest(bytes, Sign(key, bytes), "3.5.1", key.ExportSubjectPublicKeyInfoPem()));
    }

    [Test]
    public void VerifyManifest_RejectsRuntimeProtocolAndInvalidHash()
    {
        using var key = RSA.Create(2048);
        var invalid = new[]
        {
            CreateManifest() with { Runtime = "win-arm64" },
            CreateManifest() with { Protocol = NetworkSetupWire.Version + 1 },
            CreateManifest() with { ServiceArchive = CreateManifest().ServiceArchive with { Sha256 = "not-a-hash" } },
            CreateManifest() with { ServiceArchive = CreateManifest().ServiceArchive with { Sha256 = null! } }
        };

        foreach (var manifest in invalid)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest,
                NetworkSetupManifestJsonContext.Default.NetworkSetupManifest);
            Assert.Throws<InvalidDataException>(() =>
                NetworkSetupPackage.VerifyManifest(bytes, Sign(key, bytes), "3.5.1",
                    key.ExportSubjectPublicKeyInfoPem()));
        }
    }

    [Test]
    [Category("Integration")]
    public void VerifyPublisher_RejectsUnsignedFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"WinMon-{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(file, "not an executable");
            Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyPublisher(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    [Repeat(3)]
    [Category("Integration")]
    public void VerifyPublisher_RejectsRealUnsignedExecutableWithoutCrashingNativeCleanup()
    {
        Assert.Throws<InvalidDataException>(() =>
            NetworkSetupPackage.VerifyPublisher(typeof(NetworkSetupPackage).Assembly.Location));
    }

    [Test]
    [Category("Integration")]
    public void VerifyPublisher_ExtractsAuthenticodeCertificateBeforeRejectingAnotherPublisher()
    {
        var host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet",
            "dotnet.exe");
        if (!File.Exists(host)) Assert.Ignore("The signed .NET host is not installed at the standard path.");
        var error = Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyPublisher(host));
        Assert.That(error!.Message, Does.Contain("publisher is not trusted"));
    }

    [Test]
    [Category("Integration")]
    public void VerifyAsset_AcceptsLowercaseHashAndRejectsMismatch()
    {
        var file = Path.Combine(Path.GetTempPath(), $"WinMon-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            using (var valid = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.DoesNotThrow(() =>
                    NetworkSetupPackage.VerifyAsset(valid, new NetworkSetupAsset("asset", hash, valid.Length), 1024));
            }

            using var invalid = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Throws<InvalidDataException>(() => NetworkSetupPackage.VerifyAsset(invalid,
                new NetworkSetupAsset("asset", new string('0', 64), invalid.Length), 1024));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task Wire_RejectsOversizedFrames()
    {
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(NetworkSetupWire.MaximumMessageBytes + 1));
        stream.Position = 0;

        Assert.ThrowsAsync<InvalidDataException>(async () => await NetworkSetupWire.ReadAsync(stream,
            NetworkSetupWireJsonContext.Default.NetworkSetupReply, TestContext.CurrentContext.CancellationToken));
    }

    [Test]
    public async Task Wire_RoundTripsBoundedFailureMetadata()
    {
        await using var stream = new MemoryStream();
        var expected = new NetworkSetupReply(NetworkSetupWire.Version, "failed", "service", unchecked((int)0x80070005));
        await NetworkSetupWire.WriteAsync(stream, expected, NetworkSetupWireJsonContext.Default.NetworkSetupReply,
            default);
        stream.Position = 0;
        Assert.That(
            await NetworkSetupWire.ReadAsync(stream, NetworkSetupWireJsonContext.Default.NetworkSetupReply, default),
            Is.EqualTo(expected));
    }

    [TestCase(0, 4, "00")]
    [TestCase(5, 4, "00")]
    [TestCase(4, 3, "00")]
    [TestCase(4, 4, "zz")]
    [TestCase(4, 4, "0")]
    [Category("Integration")]
    public void VerifyAsset_RejectsInvalidSizeAndHashEncoding(long size, long maximumBytes, string hash)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            using var stream = File.OpenRead(path);
            Assert.Throws<InvalidDataException>(() =>
                NetworkSetupPackage.VerifyAsset(stream, new NetworkSetupAsset("asset", hash, size), maximumBytes));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static NetworkSetupManifest CreateManifest()
    {
        return new NetworkSetupManifest(
            1, "3.5.1", "win-x64", NetworkSetupWire.Version,
            new NetworkSetupAsset("WinMon.NetworkSetup.exe", new string('a', 64), 123),
            new NetworkSetupAsset("network-service.zip", new string('b', 64), 456));
    }

    private static string Sign(RSA key, byte[] bytes)
    {
        return Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
