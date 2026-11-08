namespace WinMon.NetworkSetup;

internal static class SetupPaths
{
    internal const string ServiceName = "WinMonNetworkMonitor";
    internal const string ServiceExecutableName = "WinMon.NetworkService.exe";
    internal const string SetupExecutableName = "WinMon.NetworkSetup.exe";
    internal const string PolicyFileName = "allowed-users.json";

    internal static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMon", "NetworkMonitor");

    internal static string ServiceExecutable => Path.Combine(InstallDirectory, ServiceExecutableName);
    internal static string InstalledSetupExecutable => Path.Combine(InstallDirectory, SetupExecutableName);

    internal static string RequireOwnDirectory()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing setup executable.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(executable)) ??
                        throw new InvalidOperationException("Missing setup directory.");
        if (Path.IsPathFullyQualified(directory) is false || IsReparsePoint(directory))
            throw new UnauthorizedAccessException("Untrusted setup directory.");
        return directory;
    }

    internal static void RequireInstalledHelper()
    {
        var executable = Path.GetFullPath(Environment.ProcessPath ??
                                          throw new InvalidOperationException("Missing setup executable."));
        if (!string.Equals(executable, InstalledSetupExecutable, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Removal must use the installed setup component.");
    }

    private static bool IsReparsePoint(string path)
    {
        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }
}