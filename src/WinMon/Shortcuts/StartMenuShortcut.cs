using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using WinMon.Preferences;

namespace WinMon.Shortcuts;

/// <summary>
/// Manages the opt-in Start menu shortcut whose hotkey Explorer watches, so the show shortcut also starts WinMon.
/// </summary>
internal static class StartMenuShortcut
{
    private const string Description = "WinMon resource monitor";

    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinMon.lnk");

    /// <summary>Gets a value indicating whether the shortcut exists; its presence is the setting.</summary>
    public static bool Exists => File.Exists(ShortcutPath);

    /// <summary>Gets the hotkey on the existing shortcut, or <see langword="null"/>.</summary>
    public static ushort? Hotkey
    {
        get
        {
            if (!Exists) return null;
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(ShortcutPath, 0);
                link.GetHotkey(out var hotkey);
                return hotkey;
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }
    }

    /// <summary>Write or update the shortcut to start this executable with <paramref name="gesture"/>.</summary>
    /// <exception cref="InvalidOperationException">Thrown when running from a development or test host.</exception>
    public static void Write(HotkeyGesture gesture)
    {
        var executable = Environment.ProcessPath;
        if (!StartupRegistration.CanRegisterExecutablePath(executable))
            throw new InvalidOperationException("The Start menu shortcut is unavailable while running from a development or test host.");
        if (IsCurrent(executable!, gesture)) return;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(executable!);
            link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
            link.SetDescription(Description);
            link.SetIconLocation(executable!, 0);
            link.SetHotkey(gesture.ShellLinkHotkey);
            Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath)!);
            ((IPersistFile)link).Save(ShortcutPath, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    /// <summary>Remove the shortcut; Explorer then releases its hotkey.</summary>
    public static void Delete()
    {
        if (Exists) File.Delete(ShortcutPath);
    }

    // Rewriting an unchanged shortcut makes Explorer release and retake the hotkey, so skip it.
    private static bool IsCurrent(string executable, HotkeyGesture gesture)
    {
        if (!Exists) return false;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(ShortcutPath, 0);
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            link.GetHotkey(out var hotkey);
            return hotkey == gesture.ShellLinkHotkey && string.Equals(path.ToString(), executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (COMException) { return false; }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxIconPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
