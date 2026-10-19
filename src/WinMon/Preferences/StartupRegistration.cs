using Microsoft.Win32;

namespace WinMon.Preferences;

/// <summary>
/// Manages the opt-in current-user Windows startup registration.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string ValueName = "WinMon";

    /// <summary>Gets a value indicating whether WinMon is registered to start with the current user session.</summary>
    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    /// <summary>Enables or removes the current-user Windows startup registration.</summary>
    /// <param name="enabled">Whether WinMon should start with Windows.</param>
    /// <exception cref="InvalidOperationException">Thrown when running from a development host instead of the application executable.</exception>
    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable.");
        if (!CanRegisterExecutablePath(executablePath))
        {
            throw new InvalidOperationException("Startup registration is unavailable while running from a development or test host.");
        }

        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
    }

    internal static bool CanRegisterExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string executableName = Path.GetFileNameWithoutExtension(executablePath);
        return !executableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
               !executableName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
    }
}
