using Microsoft.Win32;

// ReSharper disable StringLiteralTypo

namespace WinMon.Preferences;

/// <summary>
/// Manages the opt-in current-user Windows startup registration.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinMon";

    /// <summary>Whether this build can register itself to start at sign-in.</summary>
    /// <remarks>
    /// An MSIX package's HKCU writes go to a private per-app copy of the registry that Explorer never reads, so a
    /// Store build would show the option as on while nothing starts. Packages start through a StartupTask instead.
    /// </remarks>
    internal static bool IsSupported =>
#if WINMON_STORE
        false;
#else
        true;
#endif

    /// <summary>Gets a value indicating whether WinMon is registered to start with the current user session.</summary>
    public static bool IsEnabled
    {
        get
        {
            if (!IsSupported) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return IsRegistration(key?.GetValue(ValueName) as string, Environment.ProcessPath);
        }
    }

    /// <summary>Whether a Run value starts this executable rather than another copy, such as a moved portable folder.</summary>
    /// <remarks>Turning the option on then rewrites a stale value; turning it off cannot remove another copy's.</remarks>
    internal static bool IsRegistration(string? value, string? executablePath)
    {
        return !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(executablePath) &&
               string.Equals(value.Trim().Trim('"'), executablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Enables or removes the current-user Windows startup registration.</summary>
    /// <param name="enabled">Whether WinMon should start with Windows.</param>
    /// <exception cref="InvalidOperationException">Thrown when running from a development host instead of the application executable.</exception>
    public static void SetEnabled(bool enabled)
    {
        if (!IsSupported) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executablePath = Environment.ProcessPath ??
                             throw new InvalidOperationException("The application executable path is unavailable.");
        if (!CanRegisterExecutablePath(executablePath))
            throw new InvalidOperationException(
                "Startup registration is unavailable while running from a development or test host.");

        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
    }

    internal static bool CanRegisterExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        return !executableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
               !executableName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
    }
}
