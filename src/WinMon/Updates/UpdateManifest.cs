using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMon.Updates;

internal sealed record UpdateAsset(string Name, string Sha256, long Size);
internal sealed record UpdateManifest(int SchemaVersion, string Version, string Runtime, UpdateAsset Portable, UpdateAsset Installer)
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    internal const long MaximumAssetBytes = 512L * 1024 * 1024;

    internal static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value, @"^\d{1,5}\.\d{1,5}\.\d{1,5}$") || !System.Version.TryParse(value, out var version))
            throw new InvalidDataException("Invalid release version.");
        return version;
    }

    internal static UpdateManifest Verify(byte[] bytes, string signature, string publicKey, string expectedVersion)
    {
        if (bytes.Length > 65536 || signature.Length > 4096 || string.IsNullOrWhiteSpace(publicKey))
            throw new InvalidDataException("Trusted update signing is not configured or the manifest is invalid.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 2048 || !rsa.VerifyData(bytes, Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The update signature is invalid.");
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing update manifest.");
        ParseVersion(manifest.Version);
        if (manifest.SchemaVersion != 1 || manifest.Runtime != "win-x64" || manifest.Version != expectedVersion)
            throw new InvalidDataException("The update manifest does not match this release.");
        ValidateAsset(manifest.Portable, $"WinMon-{manifest.Version}-win-x64.zip");
        ValidateAsset(manifest.Installer, $"WinMon-{manifest.Version}-Setup-x64.exe");
        return manifest;
    }

    private static void ValidateAsset(UpdateAsset? asset, string name)
    {
        if (asset is null || asset.Name != name || asset.Size <= 0 || asset.Size > MaximumAssetBytes ||
            asset.Sha256 is null || !Regex.IsMatch(asset.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Invalid update asset.");
    }

    internal static async Task VerifyFileAsync(string path, UpdateAsset asset, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        if (file.Length != asset.Size) throw new InvalidDataException("The update download has the wrong size.");
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false));
        if (!hash.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update checksum is invalid.");
    }
}

internal sealed record UpdateOffer(string Version, Uri ReleaseNotesUrl, UpdateManifest Manifest, byte[] ManifestBytes, string Signature);
internal sealed record PreparedUpdate(string TransactionDirectory);
