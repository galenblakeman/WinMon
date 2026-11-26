using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MonCore.Windows.Monitoring;

/// <summary>Restricts the privileged service process to the provisioned users' pipe-authentication query right.</summary>
public static class ProtectedServiceProcess
{
    public const uint ProcessQueryLimitedInformation = 0x1000;

    public static void ApplySecurity(ProtectedServiceEndpoint endpoint, IReadOnlyCollection<string> users)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var sddl = BuildSddl(users);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot build service process security descriptor.");
        try
        {
            if (!SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot secure privileged service process.");
        }
        finally { _ = LocalFree(descriptor); }
    }

    public static string BuildSddl(IReadOnlyCollection<string> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        var validated = ProtectedServicePolicy.ValidateUsers(users);
        return "D:P(A;;GA;;;SY)(A;;GA;;;BA)" + string.Concat(validated.Select(sid =>
            $"(A;;0x{ProcessQueryLimitedInformation:X};;;{sid})"));
    }

    private const uint DaclSecurityInformation = 0x00000004;
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr securityDescriptor, out uint size);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation, IntPtr descriptor);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
