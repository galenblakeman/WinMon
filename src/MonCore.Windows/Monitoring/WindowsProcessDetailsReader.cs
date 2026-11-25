using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>Reads sensitive process details only on an explicit user request.</summary>
public static class WindowsProcessDetailsReader
{
    private const int SignatureCacheCapacity = 256;
    private static readonly ProcessSignatureCache<FileIdentity> SignatureCache = new(SignatureCacheCapacity);
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int ProcessCommandLineInformation = 60;
    private const int TokenUser = 1;
    private const int TokenElevation = 20;
    private const int TokenSessionId = 12;

    public static ProcessInspection Read(int processId, long creationTicks)
    {
        try
        {
            using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process.IsInvalid) return Unavailable();
            if (!GetProcessTimes(process, out var created, out var exited, out _, out _)) return Unavailable();
            if (created != creationTicks || exited != 0)
                return new ProcessInspection(true, null, null, new ProcessSignature(ProcessSignatureStatus.Unavailable, null));
            var path = ReadImagePath(process);
            return new ProcessInspection(false, ReadCommandLine(process), ReadOwner(process), ReadSignature(path));
        }
        catch (Exception exception) when (exception is Win32Exception or UnauthorizedAccessException or IOException or
            ExternalException or CryptographicException or ArgumentException or InvalidOperationException)
        {
            return Unavailable();
        }
    }

    private static ProcessInspection Unavailable() => new(false, null, null, new ProcessSignature(ProcessSignatureStatus.Unavailable, null));

    private static string? ReadCommandLine(SafeProcessHandle process)
    {
        _ = NtQueryInformationProcess(process, ProcessCommandLineInformation, IntPtr.Zero, 0, out var bytes);
        if (bytes is <= 0 or > 1024 * 1024) return null;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            if (NtQueryInformationProcess(process, ProcessCommandLineInformation, buffer, bytes, out _) != 0) return null;
            var length = unchecked((ushort)Marshal.ReadInt16(buffer));
            var pointer = Marshal.ReadIntPtr(buffer, IntPtr.Size == 8 ? 8 : 4);
            return length > 0 && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer, length / 2) : string.Empty;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? ReadImagePath(SafeProcessHandle process)
    {
        var buffer = new char[32768];
        uint length = (uint)buffer.Length;
        return QueryFullProcessImageNameW(process, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
    }

    private static ProcessOwner? ReadOwner(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token) || token.IsInvalid) return null;
        using (token)
        {
            string? account = null;
            _ = GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out var size);
            if (size > 0)
            {
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetTokenInformation(token, TokenUser, buffer, size, out _))
                    {
                        var sid = Marshal.ReadIntPtr(buffer);
                        var nameLength = 0; var domainLength = 0;
                        _ = LookupAccountSidW(null, sid, null, ref nameLength, null, ref domainLength, out _);
                        if (nameLength > 0)
                        {
                            var name = new StringBuilder(nameLength); var domain = new StringBuilder(domainLength);
                            if (LookupAccountSidW(null, sid, name, ref nameLength, domain, ref domainLength, out _))
                                account = domain.Length == 0 ? name.ToString() : domain + "\\" + name;
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            var sessionId = ReadTokenInt(token, TokenSessionId);
            bool? elevated = ReadTokenInt(token, TokenElevation) is { } value ? value != 0 : null;
            return new ProcessOwner(account, sessionId, elevated);
        }
    }

    private static int? ReadTokenInt(SafeAccessTokenHandle token, int type)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try { return GetTokenInformation(token, type, buffer, sizeof(int), out _) ? Marshal.ReadInt32(buffer) : null; }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static ProcessSignature ReadSignature(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new(ProcessSignatureStatus.Unavailable, null);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!GetFileInformationByHandle(file.SafeFileHandle, out var information)) return new(ProcessSignatureStatus.Unavailable, null);
            var identity = new FileIdentity(information.VolumeSerialNumber, information.FileIndexHigh, information.FileIndexLow,
                information.FileSizeHigh, information.FileSizeLow, information.LastWriteTimeHigh, information.LastWriteTimeLow);
            return SignatureCache.GetOrAdd(identity, () =>
            {
                var result = VerifyTrust(file.SafeFileHandle, path);
                return result.Status == ProcessSignatureStatus.NotSigned ? VerifyCatalogTrust(file.SafeFileHandle, path) : result;
            });
        }
        catch (IOException) { return new(ProcessSignatureStatus.Unavailable, null); }
        catch (UnauthorizedAccessException) { return new(ProcessSignatureStatus.Unavailable, null); }
    }

    internal static ProcessSignature ReadSignatureForTest(string path) => ReadSignature(path);

    internal static ProcessSignature ReadCatalogSignatureForTest(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return VerifyCatalogTrust(file.SafeFileHandle, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ExternalException)
        {
            return new(ProcessSignatureStatus.Unavailable, null);
        }
    }

    private static ProcessSignature VerifyTrust(SafeFileHandle handle, string path)
    {
        using var fileInfo = new WinTrustFileInfoHandle(path, handle.DangerousGetHandle());
        var data = new WinTrustData(fileInfo.Pointer);
        var action = WinTrustActionGenericVerifyV2;
        try
        {
            var status = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (status != 0)
                return new(status == unchecked((int)0x800B0100) ? ProcessSignatureStatus.NotSigned : ProcessSignatureStatus.Invalid, null);
            return VerifiedSigner(data.StateData);
        }
        finally
        {
            if (data.StateData != IntPtr.Zero) { data.StateAction = 2; _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data); }
        }
    }

    private static ProcessSignature VerifyCatalogTrust(SafeFileHandle file, string memberPath)
    {
        if (!CryptCATAdminAcquireContext2(out var administrator, IntPtr.Zero, null, IntPtr.Zero, 0)) return new(ProcessSignatureStatus.Unavailable, null);
        try
        {
            uint hashLength = 0;
            if (!CryptCATAdminCalcHashFromFileHandle2(administrator, file, ref hashLength, null, 0) || hashLength is 0 or > 1024)
                return new(ProcessSignatureStatus.Unavailable, null);
            var hash = new byte[hashLength];
            if (!CryptCATAdminCalcHashFromFileHandle2(administrator, file, ref hashLength, hash, 0)) return new(ProcessSignatureStatus.Unavailable, null);
            var tag = Convert.ToHexString(hash.AsSpan(0, checked((int)hashLength)));
            var previous = IntPtr.Zero;
            var foundCatalog = false;
            try
            {
                for (;;)
                {
                    var catalog = CryptCATAdminEnumCatalogFromHash(administrator, hash, hashLength, 0, ref previous);
                    if (catalog == IntPtr.Zero) return new(foundCatalog ? ProcessSignatureStatus.Invalid : ProcessSignatureStatus.NotSigned, null);
                    previous = catalog;
                    foundCatalog = true;
                    var info = new CatalogInfo { Size = (uint)Marshal.SizeOf<CatalogInfo>() };
                    if (!CryptCATCatalogInfoFromContext(catalog, ref info, 0)) continue;
                    var pinnedHash = GCHandle.Alloc(hash, GCHandleType.Pinned);
                    try
                    {
                        var catalogInfo = new WinTrustCatalogInfo
                        {
                            Size = (uint)Marshal.SizeOf<WinTrustCatalogInfo>(), CatalogPath = info.Name, MemberTag = tag,
                            MemberPath = memberPath, MemberFile = file.DangerousGetHandle(), Hash = pinnedHash.AddrOfPinnedObject(), HashLength = hashLength, Administrator = administrator
                        };
                        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustCatalogInfo>());
                        try
                        {
                            Marshal.StructureToPtr(catalogInfo, pointer, false);
                            var data = new WinTrustData(pointer) { UnionChoice = 2 };
                            var action = WinTrustActionGenericVerifyV2;
                            try
                            {
                                if (WinVerifyTrust(IntPtr.Zero, ref action, ref data) != 0) continue;
                                return VerifiedSigner(data.StateData);
                            }
                            finally { if (data.StateData != IntPtr.Zero) { data.StateAction = 2; _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data); } }
                        }
                        finally { Marshal.DestroyStructure<WinTrustCatalogInfo>(pointer); Marshal.FreeHGlobal(pointer); }
                    }
                    finally { pinnedHash.Free(); }
                }
            }
            finally { if (previous != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(administrator, previous, 0); }
        }
        finally { CryptCATAdminReleaseContext(administrator, 0); }
    }

    private static ProcessSignature VerifiedSigner(IntPtr state)
    {
        var provider = WTHelperProvDataFromStateData(state);
        var signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
        var certificate = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
        if (certificate == IntPtr.Zero) return new(ProcessSignatureStatus.Verified, null);
        var certificatePointer = Marshal.PtrToStructure<ProviderCertificateHeader>(certificate).Certificate;
        if (certificatePointer == IntPtr.Zero) return new(ProcessSignatureStatus.Verified, null);
        return new(ProcessSignatureStatus.Verified, SignerCommonName(certificatePointer));
    }

    private static string? SignerCommonName(IntPtr certificate)
    {
        var oid = Marshal.StringToHGlobalAnsi("2.5.4.3"); // OIDs remain narrow strings even for CertGetNameStringW.
        try
        {
            var length = CertGetNameStringW(certificate, 3, 0, oid, null, 0);
            if (length <= 1) return null;
            var value = new StringBuilder(length);
            return CertGetNameStringW(certificate, 3, 0, oid, value, value.Capacity) > 1 ? value.ToString() : null;
        }
        finally { Marshal.FreeHGlobal(oid); }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
    private readonly record struct FileIdentity(uint Volume, uint IndexHigh, uint IndexLow, uint SizeHigh, uint SizeLow, uint WriteHigh, uint WriteLow);
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation { public uint Attributes; public uint CreationTimeLow; public uint CreationTimeHigh; public uint LastAccessTimeLow; public uint LastAccessTimeHigh; public uint LastWriteTimeLow; public uint LastWriteTimeHigh; public uint VolumeSerialNumber; public uint FileSizeHigh; public uint FileSizeLow; public uint NumberOfLinks; public uint FileIndexHigh; public uint FileIndexLow; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CatalogInfo { public uint Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WinTrustCatalogInfo { public uint Size; public uint Version; public string CatalogPath; public string MemberTag; public string MemberPath; public IntPtr MemberFile; public IntPtr Hash; public uint HashLength; public IntPtr CatalogContext; public IntPtr Administrator; }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificateHeader { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContextHeader { public uint Encoding; public IntPtr Encoded; public uint Length; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NativeWinTrustFileInfo { public uint Size; public IntPtr FilePath; public IntPtr FileHandle; public IntPtr KnownSubject; }
    private sealed class WinTrustFileInfoHandle : IDisposable
    {
        private readonly IntPtr _path;
        public WinTrustFileInfoHandle(string path, IntPtr handle)
        {
            _path = Marshal.StringToHGlobalUni(path); Pointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeWinTrustFileInfo>());
            Marshal.StructureToPtr(new NativeWinTrustFileInfo { Size = (uint)Marshal.SizeOf<NativeWinTrustFileInfo>(), FilePath = _path, FileHandle = handle }, Pointer, false);
        }
        public IntPtr Pointer { get; }
        public void Dispose() { Marshal.FreeHGlobal(_path); Marshal.FreeHGlobal(Pointer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WinTrustData
    {
        public uint Size; public IntPtr PolicyCallbackData; public IntPtr SipClientData; public uint UIChoice; public uint RevocationChecks; public uint UnionChoice; public IntPtr FileInfo; public uint StateAction; public IntPtr StateData; public IntPtr UrlReference; public uint ProviderFlags; public uint UIContext; public IntPtr SignatureSettings;
        public WinTrustData(IntPtr fileInfo) { Size = (uint)Marshal.SizeOf<WinTrustData>(); PolicyCallbackData = IntPtr.Zero; SipClientData = IntPtr.Zero; UIChoice = 2; RevocationChecks = 0; UnionChoice = 1; FileInfo = fileInfo; StateAction = 1; StateData = IntPtr.Zero; UrlReference = IntPtr.Zero; ProviderFlags = 0x1000; UIContext = 0; SignatureSettings = IntPtr.Zero; }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] path, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, IntPtr buffer, int length, out int required);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool LookupAccountSidW(string? system, IntPtr sid, StringBuilder? name, ref int nameLength, StringBuilder? domain, ref int domainLength, out int use);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, IntPtr information, int length, out int returnLength);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminAcquireContext2(out IntPtr administrator, IntPtr subsystem, string? hashAlgorithm, IntPtr strongHashPolicy, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr administrator, SafeFileHandle file, ref uint hashLength, byte[]? hash, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr administrator, byte[] hash, uint hashLength, uint flags, ref IntPtr previous);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalog, ref CatalogInfo info, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr administrator, IntPtr catalog, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseContext(IntPtr administrator, uint flags);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode)] private static extern int CertGetNameStringW(IntPtr certificate, uint type, uint flags, IntPtr typeParameter, StringBuilder? name, int length);
}

/// <summary>Bounded, concurrent LRU used only for expensive on-demand file trust checks.</summary>
internal sealed class ProcessSignatureCache<TKey> where TKey : notnull
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, ProcessSignature Value)>> _entries = [];
    private readonly LinkedList<(TKey Key, ProcessSignature Value)> _lru = [];

    internal ProcessSignatureCache(int capacity) { _capacity = capacity; }
    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal ProcessSignature GetOrAdd(TKey key, Func<ProcessSignature> read)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var hit)) { _lru.Remove(hit); _lru.AddFirst(hit); _entries.Add(key, hit); return hit.Value.Value; }
            // Keep a single reader for each key so concurrent Properties opens do not duplicate trust work or LRU nodes.
            var value = read();
            var node = _lru.AddFirst((key, value)); _entries.Add(key, node);
            if (_entries.Count > _capacity) { var last = _lru.Last!; _entries.Remove(last.Value.Key); _lru.RemoveLast(); }
            return value;
        }
    }
}
