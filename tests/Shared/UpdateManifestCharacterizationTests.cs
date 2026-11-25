using WinMon.Updates;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.Tests;

public abstract class UpdateManifestCharacterizationTests
{
    private protected abstract string AssetStem { get; }
    private const string Release = "1.2.3";
    private const string Hash = "0123456789aBcDeF0123456789aBcDeF0123456789aBcDeF0123456789aBcDeF";
    private const string ConfigurationError = "Trusted update signing is not configured or the manifest is invalid.";
    private const string SignatureError = "The update signature is invalid.";
    private const string ReleaseError = "The update manifest does not match this release.";

    [TestCase("WinMon")]
    [TestCase("WinMon-Cli")]
    [TestCase("WinMon-Mcp")]
    public void SignedManifestsPreserveAllFieldsForSupportedProducts(string stem)
    {
        using var key = RSA.Create(2048);
        var signed = Sign(key, Document(stem));
        var result = UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, stem);
        Assert.That(result.SchemaVersion, Is.EqualTo(1));
        Assert.That(result.Version, Is.EqualTo(Release));
        Assert.That(result.Runtime, Is.EqualTo("win-x64"));
        Assert.That(result.Portable, Is.EqualTo(new UpdateAsset(stem + "-1.2.3-win-x64.zip", Hash, 1)));
        Assert.That(result.Installer, Is.EqualTo(new UpdateAsset(stem + "-1.2.3-Setup-x64.exe", Hash, 536870912)));
    }

    [Test]
    public void OmittedProductArgumentRetainsTheDesktopAssetContract()
    {
        using var key = RSA.Create(2048); var signed = Sign(key, Document("WinMon"));
        var result = UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release);
        Assert.That(result.Portable.Name, Is.EqualTo("WinMon-1.2.3-win-x64.zip"));
        Assert.That(result.Installer.Name, Is.EqualTo("WinMon-1.2.3-Setup-x64.exe"));
    }

    [TestCase("manifest")]
    [TestCase("signature")]
    [TestCase("key-null")]
    [TestCase("key-empty")]
    [TestCase("key-space")]
    public void InputBoundsPrecedePemAndBase64Parsing(string kind)
    {
        var bytes = kind == "manifest" ? new byte[65537] : Array.Empty<byte>();
        var signature = kind == "signature" ? new string('x', 4097) : "!";
        var publicKey = kind switch { "key-null" => null!, "key-empty" => "", "key-space" => " \t\r\n ", _ => "not PEM" };
        InvalidData(() => UpdateManifest.Verify(bytes, signature, publicKey, Release, AssetStem), ConfigurationError);
    }

    [TestCase(65535)]
    [TestCase(65536)]
    public void ExactlyBoundedSignedManifestBytesAreAccepted(int length)
    {
        using var key = RSA.Create(2048);
        var json = JsonSerializer.SerializeToUtf8Bytes(Document(AssetStem));
        var bytes = Enumerable.Repeat((byte)' ', length).ToArray(); json.CopyTo(bytes, 0);
        var result = UpdateManifest.Verify(bytes, Signature(key, bytes), key.ExportSubjectPublicKeyInfoPem(), Release, AssetStem);
        Assert.That(bytes.Length, Is.EqualTo(length));
        Assert.That(result.Portable.Name, Is.EqualTo(AssetStem + "-1.2.3-win-x64.zip"));
    }

    [TestCase(4095)]
    [TestCase(4096)]
    public void ExactlyBoundedWhitespacePaddedSignaturesAreAccepted(int length)
    {
        using var key = RSA.Create(2048); var signed = Sign(key, Document(AssetStem));
        var signature = signed.Signature.PadRight(length, ' ');
        var result = UpdateManifest.Verify(signed.Bytes, signature, signed.PublicKey, Release, AssetStem);
        Assert.That(signature.Length, Is.EqualTo(length));
        Assert.That(result.Version, Is.EqualTo(Release));
    }

    [TestCase("tampered")]
    [TestCase("wrong-key")]
    [TestCase("wrong-hash")]
    [TestCase("wrong-padding")]
    [TestCase("short-signature")]
    public void AuthenticationFailuresPrecedeJsonAndProductValidation(string kind)
    {
        using var key = RSA.Create(2048);
        var bytes = Encoding.UTF8.GetBytes("not JSON"); var signature = Signature(key, bytes);
        if (kind == "tampered") bytes[0] ^= 1;
        if (kind == "wrong-hash") signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1));
        if (kind == "wrong-padding") signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        if (kind == "short-signature") signature = "AA==";
        using var other = RSA.Create(2048);
        var publicKey = (kind == "wrong-key" ? other : key).ExportSubjectPublicKeyInfoPem();
        InvalidData(() => UpdateManifest.Verify(bytes, signature, publicKey, Release, "unsupported"), SignatureError);
    }

    [Test]
    public void WeakSigningKeysAreRejectedBeforeBase64Parsing()
    {
        using var key = RSA.Create(1024);
        InvalidData(() => UpdateManifest.Verify([], "!", key.ExportSubjectPublicKeyInfoPem(), Release, AssetStem), SignatureError);
    }

    [Test]
    public void MalformedBase64RetainsItsFormatException()
    {
        using var key = RSA.Create(2048);
        Assert.That(() => UpdateManifest.Verify([], "!", key.ExportSubjectPublicKeyInfoPem(), Release, AssetStem), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void MalformedPemRetainsItsArgumentException()
    {
        Assert.That(() => UpdateManifest.Verify([], "AA==", "not PEM", Release, AssetStem), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("null")]
    [TestCase("invalid")]
    [TestCase("array")]
    [TestCase("empty")]
    public void SignedPayloadShapeFailuresRetainTheirExceptions(string kind)
    {
        using var key = RSA.Create(2048);
        var bytes = Encoding.UTF8.GetBytes(kind switch { "null" => "null", "invalid" => "not JSON", "array" => "[]", _ => "{}" });
        var signature = Signature(key, bytes); var publicKey = key.ExportSubjectPublicKeyInfoPem();
        if (kind == "null") InvalidData(() => UpdateManifest.Verify(bytes, signature, publicKey, Release, AssetStem), "Missing update manifest.");
        else if (kind == "empty") Assert.That(() => UpdateManifest.Verify(bytes, signature, publicKey, Release, AssetStem), Throws.TypeOf<ArgumentNullException>());
        else Assert.That(() => UpdateManifest.Verify(bytes, signature, publicKey, Release, AssetStem), Throws.TypeOf<JsonException>());
    }

    [TestCase("schema")]
    [TestCase("runtime")]
    [TestCase("runtime-case")]
    [TestCase("version")]
    [TestCase("combined")]
    public void ReleaseMismatchPrecedesUnsupportedProductAndInvalidAssets(string kind)
    {
        using var key = RSA.Create(2048); var document = Document(AssetStem);
        if (kind is "schema" or "combined") document["schemaVersion"] = 2;
        if (kind is "runtime" or "combined") document["runtime"] = "win-arm64";
        if (kind == "runtime-case") document["runtime"] = "WIN-X64";
        document["portable"] = null;
        var signed = Sign(key, document);
        InvalidData(() => UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, kind is "version" or "combined" ? "1.2.4" : Release, "unsupported"), ReleaseError);
    }

    [TestCase("empty")]
    [TestCase("parts")]
    [TestCase("suffix")]
    [TestCase("large-component")]
    [TestCase("null")]
    public void ManifestVersionValidationPrecedesSchemaMatching(string kind)
    {
        using var key = RSA.Create(2048); var document = Document(AssetStem);
        document["schemaVersion"] = 2;
        document["version"] = kind switch { "empty" => "", "parts" => "1.2", "suffix" => "1.2.3-beta", "large-component" => "100000.2.3", _ => null };
        var signed = Sign(key, document);
        if (kind == "null") Assert.That(() => UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, AssetStem), Throws.TypeOf<ArgumentNullException>());
        else InvalidData(() => UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, AssetStem), "Invalid release version.");
    }

    [TestCase("0.0.0")]
    [TestCase("99999.99999.99999")]
    [TestCase("01.02.03")]
    public void SupportedVersionSpellingsRemainUnnormalized(string version)
    {
        using var key = RSA.Create(2048); var signed = Sign(key, Document(AssetStem, version));
        var result = UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, version, AssetStem);
        Assert.That(result.Version, Is.EqualTo(version));
        Assert.That(result.Portable.Name, Is.EqualTo(AssetStem + "-" + version + "-win-x64.zip"));
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("case")]
    [TestCase("suffix")]
    public void UnsupportedProductRetainsItsNamedArgumentException(string kind)
    {
        using var key = RSA.Create(2048); var signed = Sign(key, Document(AssetStem));
        var stem = kind switch { "null" => null!, "empty" => "", "case" => "winmon", _ => AssetStem + "-other" };
        var error = Assert.Throws<ArgumentException>(() => UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, stem));
        Assert.That(error!.ParamName, Is.EqualTo("productAssetStem"));
    }

    [TestCase("portable", "null")]
    [TestCase("installer", "null")]
    [TestCase("portable", "name-null")]
    [TestCase("installer", "name-null")]
    [TestCase("portable", "name-path")]
    [TestCase("installer", "name-path")]
    [TestCase("portable", "name-case")]
    [TestCase("installer", "name-case")]
    [TestCase("portable", "size-negative")]
    [TestCase("installer", "size-negative")]
    [TestCase("portable", "size-zero")]
    [TestCase("installer", "size-zero")]
    [TestCase("portable", "size-high")]
    [TestCase("installer", "size-high")]
    [TestCase("portable", "hash-null")]
    [TestCase("installer", "hash-null")]
    [TestCase("portable", "hash-short")]
    [TestCase("installer", "hash-short")]
    [TestCase("portable", "hash-long")]
    [TestCase("installer", "hash-long")]
    [TestCase("portable", "hash-nonhex")]
    [TestCase("installer", "hash-nonhex")]
    public void SignedAssetValidationRejectsEachInvalidMember(string member, string kind)
    {
        using var key = RSA.Create(2048); var document = Document(AssetStem);
        var asset = (Dictionary<string, object?>)document[member]!;
        if (kind == "null") document[member] = null;
        else if (kind.StartsWith("name", StringComparison.Ordinal)) asset["name"] = kind == "name-null" ? null : kind == "name-path" ? "..\\payload.exe" : ((string)asset["name"]!).ToLowerInvariant();
        else if (kind.StartsWith("size", StringComparison.Ordinal)) asset["size"] = kind == "size-negative" ? -1L : kind == "size-zero" ? 0L : 536870913L;
        else asset["sha256"] = kind switch { "hash-null" => null, "hash-short" => new string('a', 63), "hash-long" => new string('a', 65), _ => new string('g', 64) };
        var signed = Sign(key, document);
        InvalidData(() => UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, AssetStem), "Invalid update asset.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CaseInsensitivePropertiesAndUnknownFieldsKeepTheSamePayload(bool upper)
    {
        using var key = RSA.Create(2048); var document = Document(AssetStem);
        document["unknown"] = new { value = "ignored" };
        if (upper)
        {
            foreach (var member in new[] { "portable", "installer" })
                document[member] = ((Dictionary<string, object?>)document[member]!).ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => pair.Value);
            document = document.ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => pair.Value);
        }
        var signed = Sign(key, document);
        var result = UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, AssetStem);
        Assert.That(result.SchemaVersion, Is.EqualTo(1));
        Assert.That(result.Portable.Sha256, Is.EqualTo(Hash));
        Assert.That(result.Installer.Size, Is.EqualTo(536870912));
    }

    [TestCase("portable")]
    [TestCase("installer")]
    public void SignedHashesWithFinalLineFeedRetainTheirExistingAcceptance(string member)
    {
        using var key = RSA.Create(2048); var document = Document(AssetStem);
        ((Dictionary<string, object?>)document[member]!)["sha256"] = Hash + "\n";
        var signed = Sign(key, document);
        var result = UpdateManifest.Verify(signed.Bytes, signed.Signature, signed.PublicKey, Release, AssetStem);
        var asset = member == "portable" ? result.Portable : result.Installer;
        Assert.That(asset.Sha256, Is.EqualTo(Hash + "\n"));
        Assert.That(asset.Sha256.Length, Is.EqualTo(65));
    }

    private static Dictionary<string, object?> Document(string stem, string version = Release) => new()
    {
        ["schemaVersion"] = 1, ["version"] = version, ["runtime"] = "win-x64",
        ["portable"] = new Dictionary<string, object?> { ["name"] = stem + "-" + version + "-win-x64.zip", ["sha256"] = Hash, ["size"] = 1L },
        ["installer"] = new Dictionary<string, object?> { ["name"] = stem + "-" + version + "-Setup-x64.exe", ["sha256"] = Hash, ["size"] = 536870912L }
    };
    private static string Signature(RSA key, byte[] bytes) => Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    private static (byte[] Bytes, string Signature, string PublicKey) Sign(RSA key, object? document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        return (bytes, Signature(key, bytes), key.ExportSubjectPublicKeyInfoPem());
    }
    private static void InvalidData(Action action, string message) => Assert.That(action, Throws.TypeOf<InvalidDataException>().With.Message.EqualTo(message));
}
