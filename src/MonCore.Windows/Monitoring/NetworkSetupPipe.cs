using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

public static class NetworkSetupPipe
{
    public static NamedPipeServerStream Create(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new UnauthorizedAccessException("Missing requester identity.");
        // Admin credentials can differ from the requesting user; only data rights are granted to their token.
        var descriptor = $"D:P(A;;GA;;;{sid})(A;;0x12019B;;;BA)(A;;0x12019B;;;SY)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(descriptor, 1, out var security, out _))
            throw new Win32Exception();
        try
        {
            var attributes = new SecurityAttributes
                { Length = Marshal.SizeOf<SecurityAttributes>(), SecurityDescriptor = security };
            var handle = CreateNamedPipe("\\\\.\\pipe\\" + name, 0x40080003, 0x8, 1, 4096, 4096, 0, ref attributes);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new Win32Exception();
            }

            try
            {
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
        finally
        {
            LocalFree(security);
        }
    }

    public static int ClientProcessId(NamedPipeServerStream pipe)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id)) throw new Win32Exception();
        return id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        internal int Length;
        internal nint SecurityDescriptor;
        internal int InheritHandle;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode,
        uint maximumInstances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);

    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision,
        out nint security, out uint length);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);
}