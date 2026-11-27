namespace MonCore.Windows.Monitoring;

/// <summary>Fixed identity and installation paths for a privileged WinMon service.</summary>
public sealed record ProtectedServiceEndpoint(
    string ServiceName,
    string PipeName,
    string InstallDirectoryName,
    string ServiceExecutableName,
    string SetupExecutableName,
    string SetupPipePrefix,
    string InstallationOwner)
{
    public string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMon", InstallDirectoryName);

    public string InstalledServiceExecutable => Path.Combine(InstallDirectory, ServiceExecutableName);
    public string InstalledSetupExecutable => Path.Combine(InstallDirectory, SetupExecutableName);

    /// <summary>True when the installed service is older than <paramref name="version"/>; false when it is absent.</summary>
    public bool IsInstalledOlderThan(Version version) => File.Exists(InstalledServiceExecutable) &&
        IsOlder(System.Diagnostics.FileVersionInfo.GetVersionInfo(InstalledServiceExecutable).FileVersion, version);

    /// <summary>Compares major, minor and build; an unreadable installed version counts as current.</summary>
    public static bool IsOlder(string? installed, Version version) =>
        Version.TryParse(installed, out var parsed) &&
        new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)) <
        new Version(version.Major, version.Minor, Math.Max(0, version.Build));
}

/// <summary>Immutable signed-package names for a privileged-service setup helper.</summary>
public sealed record ProtectedSetupPackageDescriptor(
    string HelperFileName,
    string ArchiveFileName,
    string ManifestFileName,
    string SignatureFileName,
    string PublicKeyResourceName);

/// <summary>Well-known privileged-service endpoints. These values are never client supplied.</summary>
public static class ProtectedServiceEndpoints
{
    public static ProtectedServiceEndpoint Network { get; } = new(
        "WinMonNetworkMonitor", "WinMonNetworkMonitor.v1", "NetworkMonitor",
        "WinMon.NetworkService.exe", "WinMon.NetworkSetup.exe", "WinMon.NetworkSetup.",
        "WinMonNetworkMonitor.v1");

    public static ProtectedServiceEndpoint Sensor { get; } = new(
        "WinMonSensorMonitor", "WinMonSensorMonitor.v1", "SensorMonitor",
        "WinMon.SensorService.exe", "WinMon.SensorSetup.exe", "WinMon.SensorSetup.",
        "WinMonSensorMonitor.v1");

    public static ProtectedSetupPackageDescriptor NetworkPackage { get; } = new(
        "WinMon.NetworkSetup.exe", "network-service.zip", "network-service-manifest.json",
        "network-service-manifest.sig", "WinMon.NetworkSetupPublicKey");

    public static ProtectedSetupPackageDescriptor SensorPackage { get; } = new(
        "WinMon.SensorSetup.exe", "sensor-service.zip", "sensor-service-manifest.json",
        "sensor-service-manifest.sig", "WinMon.NetworkSetupPublicKey");
}
