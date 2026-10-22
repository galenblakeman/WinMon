using System.Numerics;
// ReSharper disable ConvertIfStatementToSwitchStatement
namespace WinMon.Shortcuts;

/// <summary>Modifier keys of a global shortcut, valued as the RegisterHotKey MOD_ flags.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

/// <summary>A global key combination: modifiers plus one virtual key, stored as text like "Ctrl+Shift+M".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    /// <summary>The stored text for a disabled shortcut.</summary>
    public const string NoneText = "None";

    /// <summary>Ctrl+Shift+M, "M" for monitor.</summary>
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'M');

    private const int DeleteKey = 0x2E;

    private static readonly Dictionary<int, string> NamedKeys = new()
    {
        [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
        [0x2D] = "Insert", [DeleteKey] = "Delete"
    };

    /// <summary>Gets the name of a key a shortcut may use, or <see langword="null"/> for any other key.</summary>
    internal static string? KeyName(int virtualKey) => virtualKey switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        _ => NamedKeys.GetValueOrDefault(virtualKey)
    };

    /// <summary>Gets the spaced form shown in Settings, such as "Ctrl + Shift + M".</summary>
    public string DisplayText => Format(" + ");

    /// <summary>Gets why the combination cannot be used, or <see langword="null"/> when it can.</summary>
    public string? Problem
    {
        get
        {
            if (KeyName(VirtualKey) is null) return Strings.UseALetterDigitFunctionOrNavigationKey;
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) return Strings.TheWindowsKeyIsReservedForWindowsShortcuts;
            // A single modifier would take over everyday shortcuts such as Ctrl+C in every application.
            if ((Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt)) == 0 || BitOperations.PopCount((uint)Modifiers) < 2)
                return Strings.UseAtLeastTwoOfCtrlAltAndShiftIncluding;
            if (Modifiers == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && VirtualKey == DeleteKey) return Strings.CtrlAltDeleteIsReservedByWindows;
            return null;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Format("+");

    /// <summary>Parses stored text; "None" succeeds with a <see langword="null"/> gesture.</summary>
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Trim().Equals(NoneText, StringComparison.OrdinalIgnoreCase)) return true;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyModifiers.Control,
                "alt" => HotkeyModifiers.Alt,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };
            if (modifier == HotkeyModifiers.None || modifiers.HasFlag(modifier)) return false;
            modifiers |= modifier;
        }
        if (VirtualKeyFromName(parts[^1]) is not { } key) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    /// <summary>Returns canonical stored text: a usable gesture, "None", or the default for anything else.</summary>
    internal static string Normalize(string? text) =>
        !TryParse(text, out var gesture) ? Default.ToString()
        : gesture is null ? NoneText
        : gesture.Problem is null ? gesture.ToString() : Default.ToString();

    private static int? VirtualKeyFromName(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length > 1 && name[0] is 'F' or 'f' && int.TryParse(name.AsSpan(1), out var number) && number is >= 1 and <= 24) return 0x6F + number;
        foreach (var (key, keyName) in NamedKeys)
            if (keyName.Equals(name, StringComparison.OrdinalIgnoreCase)) return key;
        return null;
    }

    private string Format(string separator)
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return string.Join(separator, parts);
    }
}
