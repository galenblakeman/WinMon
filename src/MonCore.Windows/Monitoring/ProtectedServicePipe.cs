using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace MonCore.Windows.Monitoring;

/// <summary>Creates and authenticates the fixed local pipe for a protected service endpoint.</summary>
public static class ProtectedServicePipe
{
    public static NamedPipeServerStream CreateServer(string pipeName, IReadOnlyCollection<string> allowedSids,
        string serverSid, int maximumClients, int messageBytes, bool firstInstance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(allowedSids);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverSid);
        var validated = ProtectedServicePolicy.ValidateUsers(allowedSids);
        var sddl = "D:P(A;;GA;;;SY)" + (serverSid == "S-1-5-18" ? "" : $"(A;;GA;;;{serverSid})") +
            string.Concat(validated.Where(sid => !string.Equals(sid, serverSid, StringComparison.Ordinal))
                .Select(sid => $"(D;;0x4;;;{sid})(A;;0x12019B;;;{sid})"));
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new UnauthorizedAccessException("Cannot create protected service pipe security descriptor.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        try
        {
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), SecurityDescriptor = descriptor };
            var mode = PipeRejectRemoteClients;
            var access = PipeAccessDuplex | FileFlagOverlapped | (firstInstance ? PipeFirstInstance : 0);
            var handle = CreateNamedPipe($"\\\\.\\pipe\\{pipeName}", access, mode, checked((uint)maximumClients + 1),
                checked((uint)messageBytes), checked((uint)messageBytes), 10000, ref attributes);
            if (handle.IsInvalid) throw new UnauthorizedAccessException("Cannot create protected service pipe.",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        }
        finally { _ = LocalFree(descriptor); }
    }

    public static string AuthenticateClient(NamedPipeServerStream pipe, IReadOnlyCollection<string> allowedSids)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(allowedSids);
        string? sid = null;
        pipe.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
            sid = identity.User?.Value;
        });
        if (sid is null || !allowedSids.Contains(sid, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Pipe client is not provisioned.");
        return sid;
    }

    public static string GetCurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User?.Value ?? throw new InvalidOperationException("Protected service has no Windows SID.");
    }

    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint PipeFirstInstance = 0x00080000;
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr SecurityDescriptor; public IntPtr InheritHandle; }
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maximumInstances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
