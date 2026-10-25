#if WINMON_STORE
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
#else
using Microsoft.Win32;
#endif

// ReSharper disable StringLiteralTypo

namespace WinMon.Preferences;

/// <summary>Whether Settings can offer to start WinMon at sign-in, and why not when it cannot.</summary>
internal enum StartupAvailability
{
    /// <summary>The option is hidden: a Store build running without its package, as tests and smoke modes do.</summary>
    Unavailable,

    Available,

    /// <summary>The user turned WinMon off in Windows' startup apps; only Windows can turn it back on.</summary>
    TurnedOffInWindows,

    /// <summary>Group policy decides, either way.</summary>
    ManagedByPolicy
}

/// <summary>
/// Manages the opt-in current-user Windows startup registration.
/// </summary>
/// <remarks>
/// Installer and portable builds write the HKCU Run key. An MSIX package's HKCU writes go to a private per-app copy
/// of the registry that Explorer never reads, so Store builds use the package's StartupTask instead.
/// </remarks>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WinMon";

    /// <summary>The windows.startupTask TaskId in packaging/msix/AppxManifest.xml.</summary>
    internal const string StartupTaskId = "WinMonStartup";

    /// <summary>Whether Settings can offer the option, and whether the user may change it there.</summary>
    internal static StartupAvailability Availability
    {
        get
        {
#if WINMON_STORE
            return IsPackaged() ? PackagedAvailability() : StartupAvailability.Unavailable;
#else
            return StartupAvailability.Available;
#endif
        }
    }

    /// <summary>Gets a value indicating whether WinMon is registered to start with the current user session.</summary>
    public static bool IsEnabled
    {
        get
        {
#if WINMON_STORE
            return IsPackaged() && PackagedIsEnabled();
#else
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return IsRegistration(key?.GetValue(ValueName) as string, Environment.ProcessPath);
#endif
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
#if WINMON_STORE
        if (IsPackaged()) PackagedSetEnabled(enabled);
#else
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
#endif
    }

    internal static bool CanRegisterExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        return !executableName.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
               !executableName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
    }

#if WINMON_STORE
    internal static StartupAvailability AvailabilityOf(StartupTaskState state)
    {
        return state switch
        {
            StartupTaskState.Disabled or StartupTaskState.Enabled => StartupAvailability.Available,
            StartupTaskState.DisabledByUser => StartupAvailability.TurnedOffInWindows,
            _ => StartupAvailability.ManagedByPolicy
        };
    }

    /// <summary>Whether this process has package identity, without loading the Windows Runtime projection.</summary>
    private static bool IsPackaged()
    {
        var length = 0;
        // ERROR_INSUFFICIENT_BUFFER when packaged; APPMODEL_ERROR_NO_PACKAGE (15700) otherwise.
        return GetCurrentPackageFullName(ref length, null) != 15700;
    }

    // The projection assembly is large, so only these methods reference it, and they run only from Settings.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupAvailability PackagedAvailability()
    {
        return StoreTask() is { } task ? AvailabilityOf(task.State) : StartupAvailability.Unavailable;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PackagedIsEnabled()
    {
        return StoreTask()?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PackagedSetEnabled(bool enabled)
    {
        var task = StoreTask() ?? throw new InvalidOperationException("The package has no startup task.");
        if (!enabled)
        {
            task.Disable();
            return;
        }

        // A packaged desktop app is enabled without a prompt, unless the user or a policy turned it off;
        // Settings then shows why rather than offering the option.
        var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
        if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
            Trace.TraceWarning("WinMon could not turn on its startup task: {0}", state);
    }

    private static StartupTask? StoreTask()
    {
        try
        {
            // Completes on a pool thread, so blocking the UI thread on it cannot deadlock.
            return StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            // A package built without the extension, or a TaskId that does not match it.
            Trace.TraceWarning("WinMon startup task unavailable: {0}", ex.Message);
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
#endif
}
