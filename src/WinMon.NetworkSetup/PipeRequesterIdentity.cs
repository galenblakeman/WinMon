using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinMon.NetworkSetup;

internal sealed partial class PipeRequesterIdentity : IDisposable
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserInformationClass = 1;
    private readonly SafeProcessHandle _process;
    private readonly long _creationTime;

    private PipeRequesterIdentity(SafeProcessHandle process, long creationTime, string sid)
    {
        _process = process;
        _creationTime = creationTime;
        Sid = sid;
    }

    internal string Sid { get; }

    internal static PipeRequesterIdentity FromServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId) || processId == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to identify setup pipe server.");
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect setup pipe server.");
        try
        {
            return new PipeRequesterIdentity(process, GetCreationTime(process), GetSid(process));
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    internal void VerifyStillSameProcess()
    {
        if (GetCreationTime(_process) != _creationTime)
            throw new UnauthorizedAccessException("Setup pipe server changed.");
    }

    public void Dispose()
    {
        _process.Dispose();
    }

    private static long GetCreationTime(SafeProcessHandle process)
    {
        if (!GetProcessTimes(process, out var creation, out var exit, out _, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect setup pipe server.");
        if (exit != 0) throw new UnauthorizedAccessException("Setup requester exited.");
        return creation;
    }

    private static string GetSid(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect setup requestor.");
        using (token)
        {
            GetTokenInformation(token, TokenUserInformationClass, IntPtr.Zero, 0, out var bytes);
            if (bytes <= 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect setup requestor.");
            var buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                if (!GetTokenInformation(token, TokenUserInformationClass, buffer, bytes, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to inspect setup requestor.");
                var user = Marshal.PtrToStructure<TokenUser>(buffer);
                return AuthorizationPolicy.ValidateSid(new SecurityIdentifier(user.User.Sid).Value);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUser
    {
        public SidAndAttributes User;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetProcessTimes(SafeProcessHandle process, out long creationTime, out long exitTime,
        out long kernelTime, out long userTime);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess,
        out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass,
        IntPtr information, uint informationLength, out uint returnLength);
}