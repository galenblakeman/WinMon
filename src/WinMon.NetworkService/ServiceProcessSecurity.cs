using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinMon.NetworkService;

/// <summary>Limits provisioned users to the one process right needed to authenticate the pipe server.</summary>
internal static class ServiceProcessSecurity
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;

    internal static void Apply(IReadOnlyCollection<string> users)
    {
        var sddl = BuildSddl(users);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot build service process security descriptor.");
        try
        {
            if (!SetKernelObjectSecurity(GetCurrentProcess(), DaclSecurityInformation, descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot secure network service process.");
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    internal static string BuildSddl(IReadOnlyCollection<string> users)
    {
        var validated = ServicePolicy.ValidateUsers(users.ToArray());
        return "D:P(A;;GA;;;SY)(A;;GA;;;BA)" + string.Concat(validated.Select(sid =>
            $"(A;;0x{ProcessQueryLimitedInformation:X};;;{sid})"));
    }

    private const uint DaclSecurityInformation = 0x00000004;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision,
        out IntPtr securityDescriptor, out uint securityDescriptorSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(IntPtr handle, uint securityInformation,
        IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}