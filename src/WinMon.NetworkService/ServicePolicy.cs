using System.Security.AccessControl;
using MonCore.Windows.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Compatibility facade for the fixed network-service policy.</summary>
internal static class ServicePolicy
{
    internal static string InstallDirectory => ProtectedServiceEndpoints.Network.InstallDirectory;
    internal static IReadOnlyCollection<string> Load()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing service executable.");
        if (!string.Equals(Path.GetDirectoryName(executable), InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Network service must run from its protected installation directory.");
        return ProtectedServicePolicy.LoadAllowedUsers(ProtectedServiceEndpoints.Network);
    }
    internal static IReadOnlyCollection<string> ReadInstallation(DirectoryInfo directory, string programFiles,
        Action<FileSystemInfo> checkProtected) =>
        ProtectedServicePolicy.ReadInstallation(directory, programFiles, checkProtected);
    internal static IReadOnlyCollection<string> ValidateUsers(string[] users) =>
        ProtectedServicePolicy.ValidateUsers(users);
    internal static void ValidateProtection(FileSystemSecurity security) =>
        ProtectedServicePolicy.ValidateProtection(security);

    private static void CheckProtected(FileSystemInfo item) => ProtectedServicePolicy.ValidateInstalledItem(item);
}
