using Microsoft.Win32;

namespace MonCore.Windows.Monitoring;

/// <summary>Requires the separately installed PawnIO driver package before a sensor service is provisioned.</summary>
public static class PawnIoPrerequisite
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>Rejects installation unless the official PawnIO uninstall registration has a parseable version.</summary>
    public static void EnsureInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(UninstallKey, writable: false);
        if (!IsValidRegistration(key?.GetValue("DisplayName"), key?.GetValue("DisplayVersion")))
            throw new InvalidOperationException("PawnIO must be installed before the WinMon CPU temperature service.");
    }

    internal static bool IsValidRegistration(object? displayName, object? displayVersion) =>
        displayName is string { Length: > 0 } name &&
        string.Equals(name, "PawnIO", StringComparison.Ordinal) &&
        displayVersion is string { Length: > 0 } version &&
        Version.TryParse(version, out _);
}
