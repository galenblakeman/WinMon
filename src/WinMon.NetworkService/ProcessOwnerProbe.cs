using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace WinMon.NetworkService;

/// <summary>Reads the process token and lifetime together, so a reused PID cannot inherit another owner's traffic.</summary>
internal static class ProcessOwnerProbe
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;

    internal static bool TryReadCreation(int processId, out long creationTicks)
    {
        creationTicks = 0;
        if (processId <= 0) return false;
        using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        return !process.IsInvalid && GetProcessTimes(process, out creationTicks, out var exit, out _, out _) &&
               exit == 0;
    }

    internal static bool TryRead(int processId, out ProcessOwner owner)
    {
        owner = default!;
        if (processId <= 0) return false;
        try
        {
            using var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (process.IsInvalid) return false;
            if (!GetProcessTimes(process, out var creation, out var exit, out _, out _) || exit != 0) return false;
            if (!OpenProcessToken(process, TokenQuery, out var token) || token.IsInvalid) return false;
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            {
                var sid = identity.User?.Value;
                if (string.IsNullOrWhiteSpace(sid)) return false;
                owner = new ProcessOwner(processId, creation, sid);
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit,
        out long kernel, out long user);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool
        OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}

internal sealed record ProcessOwner(int ProcessId, long CreationTicks, string Sid);