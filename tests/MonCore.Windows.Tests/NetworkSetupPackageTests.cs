using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkSetupPackageTests
{
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
