using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Shortcuts;

namespace WinMon.Tests;

[TestFixture]
public sealed class HotkeyGestureTests
{
    private const HotkeyModifiers CtrlAltShift = HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    [TestCase("Ctrl+Alt+Shift+W", "Ctrl+Alt+Shift+W")]
    [TestCase(" shift + control + alt + w ", "Ctrl+Alt+Shift+W")]
    [TestCase("Ctrl+Shift+F12", "Ctrl+Shift+F12")]
    [TestCase("ctrl+alt+pageup", "Ctrl+Alt+PageUp")]
    [TestCase("Alt+Shift+7", "Alt+Shift+7")]
    public void TryParse_RoundTripsCanonicalText(string text, string expected)
    {
        Assert.That(HotkeyGesture.TryParse(text, out var gesture), Is.True);
        Assert.That(gesture!.ToString(), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("Ctrl+Ctrl+W")]
    [TestCase("Ctrl+Alt+Escape")]
    [TestCase("Hyper+W")]
    [TestCase("Ctrl+Alt+F25")]
    public void TryParse_RejectsUnknownText(string text) => Assert.That(HotkeyGesture.TryParse(text, out _), Is.False);

    [Test]
    public void TryParse_NoneTurnsTheShortcutOff()
    {
        Assert.That(HotkeyGesture.TryParse("none", out var gesture), Is.True);
        Assert.That(gesture, Is.Null);
    }

    [Test]
    public void Default_IsCtrlShiftM()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyGesture.Default, Is.EqualTo(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'M')));
            Assert.That(HotkeyGesture.Default.DisplayText, Is.EqualTo("Ctrl + Shift + M"));
            Assert.That(HotkeyGesture.Default.Problem, Is.Null);
            Assert.That(new MonitorPreferences().ShowGesture, Is.EqualTo(HotkeyGesture.Default));
        });
    }

    [TestCase("Ctrl+Alt+Shift+W", true)]
    [TestCase("Ctrl+Shift+J", true)]
    [TestCase("Alt+Shift+F12", true)]
    [TestCase("Ctrl+W", false)]
    [TestCase("Shift+W", false)]
    [TestCase("W", false)]
    [TestCase("Alt+F4", false)]
    [TestCase("Ctrl+Alt+Delete", false)]
    [TestCase("Win+Shift+W", false)]
    [TestCase("Ctrl+Win+W", false)]
    public void Problem_AllowsOnlySafeCombinations(string text, bool allowed)
    {
        Assert.That(HotkeyGesture.TryParse(text, out var gesture), Is.True);
        Assert.That(gesture!.Problem is null, Is.EqualTo(allowed));
    }

    [Test]
    public void Problem_RejectsUnsupportedKeys() =>
        Assert.That(new HotkeyGesture(CtrlAltShift, 0x1B).Problem, Is.Not.Null);

    [Test]
    public void ShellLinkHotkey_PacksModifiersAboveTheKey()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyGesture.Default.ShellLinkHotkey, Is.EqualTo(0x034D));
            Assert.That(new HotkeyGesture(CtrlAltShift, 'W').ShellLinkHotkey, Is.EqualTo(0x0757));
            Assert.That(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B).ShellLinkHotkey, Is.EqualTo(0x037B));
        });
    }

    [TestCase(null, "Ctrl+Shift+M")]
    [TestCase("garbage", "Ctrl+Shift+M")]
    [TestCase("Ctrl+W", "Ctrl+Shift+M")]
    [TestCase("NONE", "None")]
    [TestCase("alt+ctrl+m", "Ctrl+Alt+M")]
    public void Validate_NormalizesShortcut(string? stored, string expected) =>
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ShowShortcut = stored! }).ShowShortcut, Is.EqualTo(expected));

    [Test]
    public async Task Shortcut_RoundTripsThroughTheStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { ShowShortcut = "Ctrl+Shift+F12" });
            var loaded = await store.LoadAsync();
            Assert.That(loaded.ShowGesture, Is.EqualTo(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x7B)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
