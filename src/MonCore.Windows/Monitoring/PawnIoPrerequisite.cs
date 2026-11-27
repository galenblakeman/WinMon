using Microsoft.Win32;

namespace MonCore.Windows.Monitoring;

/// <summary>Checks the machine-wide PawnIO driver package registration.</summary>
public static class PawnIoPrerequisite
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    /// <summary>Rejects installation unless the official PawnIO uninstall registration has a parseable version.</summary>
    public static void EnsureInstalled()
    {
        if (!IsInstalled())
            throw new InvalidOperationException("PawnIO must be installed before the WinMon CPU temperature service.");
    }

    internal static bool IsInstalled()
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(UninstallKey, writable: false);
        return IsValidRegistration(key?.GetValue("DisplayName"), key?.GetValue("DisplayVersion"));
    }

    internal static bool IsValidRegistration(object? displayName, object? displayVersion) =>
        displayName is string { Length: > 0 } name &&
        string.Equals(name, "PawnIO", StringComparison.Ordinal) &&
        displayVersion is string { Length: > 0 } version &&
        Version.TryParse(version, out _);
}
