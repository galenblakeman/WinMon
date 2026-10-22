using System.Runtime.InteropServices;
using NUnit.Framework;
using WinMon.Shortcuts;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class StartMenuShortcutTests
{
    [Test]
    public void ClearHotkey_PreservesTheShortcut_AndDoesNotRewriteItAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "WinMon.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            var target = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            try
            {
                shortcut.TargetPath = target;
                shortcut.Arguments = "example.txt";
                shortcut.WorkingDirectory = directory;
                shortcut.Description = "Keep the Start menu entry";
                shortcut.Hotkey = "CTRL+SHIFT+M";
                shortcut.Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }

            StartMenuShortcut.ClearHotkey(path);

            shortcut = shell.CreateShortcut(path);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That((string)shortcut.Hotkey, Is.Empty);
                    Assert.That((string)shortcut.TargetPath, Is.EqualTo(target).IgnoreCase);
                    Assert.That((string)shortcut.Arguments, Is.EqualTo("example.txt"));
                    Assert.That((string)shortcut.WorkingDirectory, Is.EqualTo(directory).IgnoreCase);
                    Assert.That((string)shortcut.Description, Is.EqualTo("Keep the Start menu entry"));
                });
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }

            var contents = File.ReadAllBytes(path);
            var saved = File.GetLastWriteTimeUtc(path);
            StartMenuShortcut.ClearHotkey(path);
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(contents));
            Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(saved));
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ClearHotkey_MissingShortcutDoesNotCreateOne()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "WinMon.lnk");
        Assert.DoesNotThrow(() => StartMenuShortcut.ClearHotkey(path));
        Assert.That(File.Exists(path), Is.False);
    }
}
