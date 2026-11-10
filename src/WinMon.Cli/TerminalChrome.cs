using System.Reflection;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace WinMon.Cli;

internal static partial class TerminalChrome
{
    internal static readonly string ProductName = "WinMon " +
        (typeof(TerminalChrome).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TerminalChrome).Assembly.GetName().Version?.ToString(3));
    internal static readonly Color Titanium = new(0x87, 0x95, 0xA5);
    internal static readonly Color Green = new(0x3F, 0xB9, 0x50);
    internal static readonly Color Gold = new(0xD2, 0x99, 0x22);
    internal static readonly Color Red = new(0xFF, 0x7B, 0x72);
    internal static readonly Color Selection = new(0x26, 0x4F, 0x78);
    internal static readonly Color Foreground = new(0xE6, 0xED, 0xF3);
    internal static readonly Style Accent = new(Titanium, decoration: Decoration.Bold);
    internal static readonly Style Key = new(new Color(0x00, 0xD8, 0xDB));
    internal static readonly Style KeyBracket = Key;
    internal static readonly Style Selected = new(Foreground, Selection, Decoration.Bold);
    internal static readonly Style SelectedKey = new(Key.Foreground, Selection, Decoration.Bold);
    internal static readonly Style Cpu = new(new Color(0xFD, 0x6C, 0x0F));
    internal static readonly Style Memory = new(new Color(0x11, 0x95, 0xFD));
    internal static readonly Style Disk = new(new Color(0xFC, 0xEA, 0x1D));
    internal static readonly Style Network = new(new Color(0xDC, 0x77, 0xFB));
    internal static readonly Style Gpu = new(new Color(0x00, 0xF2, 0xC3));

    internal static Paragraph Keys(string value, int width)
    {
        var text = string.Join('\n', value.Split('\n')
            .Select(line => TerminalText.Fit(KeyNames().Replace(line, "[$0]"), width)));
        var result = new Paragraph();
        var offset = 0;
        foreach (Match match in KeyCaps().Matches(text))
        {
            result.Append(text[offset..match.Index]);
            if (match.Value is "[x]" or "[✓]") result.Append(match.Value, new Style(Green, decoration: Decoration.Bold));
            else result.Append("[", KeyBracket).Append(match.Value[1..^1], Key).Append("]", KeyBracket);
            offset = match.Index + match.Length;
        }
        return result.Append(text[offset..]);
    }

    [GeneratedRegex(@"(?<![\w\[])(?:F1-F4|F[1-4]|1-6|Enter|Space|Esc|Left/Right|Arrows/Page|[ABGNPQRSTW?/])(?=\s|:|$)")]
    private static partial Regex KeyNames();

    [GeneratedRegex(@"\[(?:F1-F4|F[1-4]|[1-6]|1-6|Enter|Space|Esc|Left/Right|Arrows/Page|[ABGNPQRSTW?x✓/])\]")]
    private static partial Regex KeyCaps();
}
