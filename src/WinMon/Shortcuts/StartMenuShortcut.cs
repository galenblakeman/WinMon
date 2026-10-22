using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
#pragma warning disable IDE0051

namespace WinMon.Shortcuts;

// ShellLink implements these interfaces through COM QueryInterface, outside the managed type hierarchy.
// ReSharper disable SuspiciousTypeConversion.Global

/// <summary>Clears the Explorer hotkey left by older WinMon versions, preserving the Start menu entry.</summary>
internal static class StartMenuShortcut
{
    public static void ClearHotkey() => ClearHotkey(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WinMon.lnk"));

    internal static void ClearHotkey(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcutPath, 0);
            link.GetHotkey(out var hotkey);
            if (hotkey == 0) return;
            link.SetHotkey(0);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    // Every member is required in native vtable order, including slots not called by WinMon.
    // ReSharper disable UnusedMember.Global
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
    // ReSharper restore UnusedMember.Global
}
// ReSharper restore SuspiciousTypeConversion.Global
