using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.Monitoring;

/// <summary>Verifies and locks the signed payload consumed by the elevated setup helper.</summary>
public sealed partial class NetworkSetupPackage : IDisposable
{
    private const int SchemaVersion = 1;
    private const string Runtime = "win-x64";
    private const string HelperFileName = "WinMon.NetworkSetup.exe";
    private const string ArchiveFileName = "network-service.zip";
    private const string ManifestFileName = "network-service-manifest.json";
    private const string SignatureFileName = "network-service-manifest.sig";
    private const string ExpectedPublisher = "Galen Blakeman";
    private const long MaximumHelperBytes = 200L * 1024 * 1024;
    private const long MaximumArchiveBytes = 500L * 1024 * 1024;

    private NetworkSetupPackage(NetworkSetupManifest manifest, FileStream helperStream, FileStream archiveStream)
    {
        Manifest = manifest;
        HelperStream = helperStream;
        ArchiveStream = archiveStream;
    }

    public NetworkSetupManifest Manifest { get; }
    public FileStream HelperStream { get; }
    public FileStream ArchiveStream { get; }

    public static NetworkSetupPackage Open(string directory, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A package directory is required.", nameof(directory));
        var root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new InvalidDataException("Network setup package directory is missing.");

        var manifestBytes = ReadSmallFile(root, ManifestFileName, 64 * 1024);
        var signature = Encoding.ASCII.GetString(ReadSmallFile(root, SignatureFileName, 16 * 1024)).Trim();
        var manifest = VerifyManifest(manifestBytes, signature, expectedVersion);

        FileStream? helper = null;
        FileStream? archive = null;
        try
        {
            helper = OpenAsset(root, manifest.Helper);
            archive = OpenAsset(root, manifest.ServiceArchive);
            VerifyAsset(helper, manifest.Helper, MaximumHelperBytes);
            VerifyAsset(archive, manifest.ServiceArchive, MaximumArchiveBytes);
            VerifyPublisher(helper.Name);
            helper.Position = 0;
            archive.Position = 0;
            return new NetworkSetupPackage(manifest, helper, archive);
        }
        catch
        {
            helper?.Dispose();
            archive?.Dispose();
            throw;
        }
    }

    public static NetworkSetupManifest VerifyManifest(byte[] manifestBytes, string signature, string expectedVersion) =>
        VerifyManifest(manifestBytes, signature, expectedVersion, ReadEmbeddedPublicKey());

    internal static NetworkSetupManifest VerifyManifest(byte[] manifestBytes, string signature, string expectedVersion, string publicKeyPem)
    {
        if (manifestBytes is null || manifestBytes.Length is <= 0 or > 64 * 1024) throw new InvalidDataException("Network setup manifest size is invalid.");
        if (string.IsNullOrWhiteSpace(signature)) throw new InvalidDataException("Network setup manifest signature is missing.");
        if (string.IsNullOrWhiteSpace(expectedVersion)) throw new ArgumentException("An expected version is required.", nameof(expectedVersion));
        if (string.IsNullOrWhiteSpace(publicKeyPem)) throw new ArgumentException("A public key is required.", nameof(publicKeyPem));

        byte[] signatureBytes;
        try { signatureBytes = Convert.FromBase64String(signature); }
        catch (FormatException exception) { throw new InvalidDataException("Network setup manifest signature is invalid.", exception); }

        using var rsa = RSA.Create();
        try { rsa.ImportFromPem(publicKeyPem); }
        catch (CryptographicException exception) { throw new InvalidDataException("Network setup public key is invalid.", exception); }
        if (!rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) {
            throw new InvalidDataException("Network setup manifest signature is invalid.");
        }

        NetworkSetupManifest manifest;
        try { manifest = JsonSerializer.Deserialize(manifestBytes, NetworkSetupManifestJsonContext.Default.NetworkSetupManifest) ?? throw new InvalidDataException("Network setup manifest is invalid."); }
        catch (JsonException exception) { throw new InvalidDataException("Network setup manifest is invalid.", exception); }

        if (manifest.SchemaVersion != SchemaVersion || manifest.Version != expectedVersion || manifest.Runtime != Runtime || manifest.Protocol != NetworkSetupWire.Version) {
            throw new InvalidDataException("Network setup manifest is not compatible with this application.");
        }
        ValidateAsset(manifest.Helper, HelperFileName, MaximumHelperBytes);
        ValidateAsset(manifest.ServiceArchive, ArchiveFileName, MaximumArchiveBytes);
        return manifest;
    }

    /// <summary>Checks Windows Authenticode trust and pins the release publisher.</summary>
    public static void VerifyPublisher(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new InvalidDataException("Network setup helper is missing.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Network setup verification requires Windows.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[2];
        if (stream.Read(header) != header.Length || header[0] != (byte)'M' || header[1] != (byte)'Z')
            throw new InvalidDataException("Network setup helper is not a Windows executable.");
        VerifyTrustedPublisher(path);
    }

    public void Dispose()
    {
        HelperStream.Dispose();
        ArchiveStream.Dispose();
    }

    private static FileStream OpenAsset(string root, NetworkSetupAsset asset)
    {
        var path = Path.Combine(root, asset.Name);
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Network setup asset path is invalid.");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
    }

    private static byte[] ReadSmallFile(string root, string name, int maximumLength)
    {
        using var stream = new FileStream(Path.Combine(root, name), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        if (stream.Length <= 0 || stream.Length > maximumLength) throw new InvalidDataException("Network setup package metadata size is invalid.");
        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static void VerifyAsset(FileStream stream, NetworkSetupAsset asset, long maximumBytes)
    {
        if (stream.Length != asset.Size || stream.Length <= 0 || stream.Length > maximumBytes) throw new InvalidDataException("Network setup asset size is invalid.");
        byte[] expectedHash;
        try { expectedHash = Convert.FromHexString(asset.Sha256); }
        catch (FormatException exception) { throw new InvalidDataException("Network setup asset hash is invalid.", exception); }
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), expectedHash)) {
            throw new InvalidDataException("Network setup asset hash is invalid.");
        }
    }

    private static void ValidateAsset(NetworkSetupAsset asset, string expectedName, long maximumBytes)
    {
        if (asset is null || string.IsNullOrEmpty(asset.Name) || string.IsNullOrEmpty(asset.Sha256) || asset.Name != expectedName || Path.GetFileName(asset.Name) != asset.Name || asset.Size <= 0 || asset.Size > maximumBytes ||
            asset.Sha256.Length != 64 || !asset.Sha256.All(static character => Uri.IsHexDigit(character))) {
            throw new InvalidDataException("Network setup manifest asset is invalid.");
        }
    }

    private static string ReadEmbeddedPublicKey()
    {
        using var stream = typeof(NetworkSetupPackage).Assembly.GetManifestResourceStream("WinMon.NetworkSetupPublicKey")
            ?? throw new InvalidOperationException("Network setup trust key is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void VerifyTrustedPublisher(string path)
    {
        using var fileInfo = new WinTrustFileInfoHandle(path);
        var data = new WinTrustData(fileInfo.Pointer);
        var action = WinTrustActionGenericVerifyV2;
        try
        {
            if (WinVerifyTrustNative(IntPtr.Zero, ref action, ref data) != 0)
                throw new InvalidDataException("Network setup helper Authenticode verification failed.");
            // Read the signer from the verified trust state, rather than an unrelated certificate in the PE file.
            var provider = WTHelperProvDataFromStateData(data.StateData);
            var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            var providerCertificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
            if (providerCertificate == IntPtr.Zero) throw new InvalidDataException("Missing verified signing certificate.");
            var certificatePointer = Marshal.PtrToStructure<ProviderCertificateHeader>(providerCertificate).Certificate;
            if (certificatePointer == IntPtr.Zero) throw new InvalidDataException("Missing verified signing certificate.");
            var context = Marshal.PtrToStructure<CertificateContextHeader>(certificatePointer);
            if (context.Encoded == IntPtr.Zero || context.Length is 0 or > 64 * 1024) throw new InvalidDataException("Invalid verified signing certificate.");
            var bytes = new byte[context.Length];
            Marshal.Copy(context.Encoded, bytes, 0, bytes.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(bytes);
            if (!string.Equals(certificate.GetNameInfo(X509NameType.SimpleName, false), ExpectedPublisher, StringComparison.Ordinal))
                throw new InvalidDataException("Network setup helper publisher is not trusted.");
        }
        finally
        {
            if (data.StateData != IntPtr.Zero)
            {
                data.StateAction = 2; // WTD_STATEACTION_CLOSE owns all provider/certificate pointers above.
                _ = WinVerifyTrustNative(IntPtr.Zero, ref action, ref data);
            }
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", EntryPoint = "WinVerifyTrust", ExactSpelling = true, SetLastError = true)]
    private static extern int WinVerifyTrustNative(IntPtr windowHandle, ref Guid action, ref WinTrustData data);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);

    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificateHeader { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContextHeader { public uint Encoding; public IntPtr Encoded; public uint Length; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeWinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    private sealed class WinTrustFileInfoHandle : IDisposable
    {
        public WinTrustFileInfoHandle(string path)
        {
            var info = new NativeWinTrustFileInfo {
                StructSize = checked((uint)Marshal.SizeOf<NativeWinTrustFileInfo>()),
                FilePath = Marshal.StringToHGlobalUni(path),
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };
            FilePath = info.FilePath;
            Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeWinTrustFileInfo>());
            Marshal.StructureToPtr(info, Pointer, false);
        }

        private IntPtr FilePath { get; }
        public IntPtr Pointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(FilePath);
            Marshal.FreeHGlobal(Pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;

        public WinTrustData(IntPtr fileInfo)
        {
            StructSize = checked((uint)Marshal.SizeOf<WinTrustData>());
            PolicyCallbackData = IntPtr.Zero;
            SipClientData = IntPtr.Zero;
            UiChoice = 2; // WTD_UI_NONE
            RevocationChecks = 0;
            UnionChoice = 1; // WTD_CHOICE_FILE
            FileInfo = fileInfo;
            StateAction = 1; // WTD_STATEACTION_VERIFY retains the verified signer until CLOSE.
            StateData = IntPtr.Zero;
            UrlReference = IntPtr.Zero;
            ProviderFlags = 0x80; // WTD_REVOCATION_CHECK_CHAIN_EXCLUDE_ROOT
            UiContext = 0;
            SignatureSettings = IntPtr.Zero;
        }
    }
}

public sealed record NetworkSetupManifest(int SchemaVersion, string Version, string Runtime, int Protocol, NetworkSetupAsset Helper, NetworkSetupAsset ServiceArchive);
public sealed record NetworkSetupAsset(string Name, string Sha256, long Size);

[JsonSerializable(typeof(NetworkSetupManifest))]
public sealed partial class NetworkSetupManifestJsonContext : JsonSerializerContext;
