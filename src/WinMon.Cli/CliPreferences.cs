using System.Text.Json;

namespace WinMon.Cli;

public sealed record CliPin(string Identity, string Name)
{
    public static string? Executable(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
        ? path.Replace('/', '\\') : null;
}

public sealed record CliTabPreferences
{
    public int Sort { get; set; }
    public int Top { get; set; } = 10;
    public bool AppsOnly { get; set; }
    public bool Grouped { get; set; }
    public bool PinnedOnly { get; set; }
    public bool Workers { get; set; } = true;
    public int Status { get; set; }
    public int TrafficSeconds { get; set; } = 300;
    public CliPin[] Pins { get; set; } = [];

    internal void Normalize(TerminalTab tab)
    {
        if (Top is < 0 or > 1000) Top = 0;
        var maximum = tab switch { TerminalTab.Services => 3, TerminalTab.Usage => 4, _ => 5 };
        if (Sort < 0 || Sort > maximum || tab == TerminalTab.Services && Sort == 3 && Top != 0) Sort = 0;
        if (Status is < 0 or > 2) Status = 0;
        if (TrafficSeconds is not (0 or 60 or 300 or 1800)) TrafficSeconds = 300;
        if (tab == TerminalTab.Services) AppsOnly = false;
        if (tab != TerminalTab.Processes) Grouped = false;
        // Preferences are input, including null array entries and obsolete PID-based identities.
        Pins = (Pins ?? []).Where(pin => pin is not null && pin.Identity is { Length: > 0 and <= 32768 } &&
            pin.Name is { Length: > 0 and <= 256 })
            .Select(pin => pin.Identity.StartsWith("exe:", StringComparison.Ordinal) ? pin with { Identity = pin.Identity[4..] } : pin)
            .Where(pin => pin.Identity.StartsWith("service:", StringComparison.Ordinal) && tab == TerminalTab.Services ||
                tab != TerminalTab.Services && Path.IsPathFullyQualified(pin.Identity))
            .DistinctBy(pin => pin.Identity, StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
    }

    internal static void Merge(CliTabPreferences before, CliTabPreferences after, CliTabPreferences target)
    {
        if (before.Sort != after.Sort) target.Sort = after.Sort;
        if (before.Top != after.Top) target.Top = after.Top;
        if (before.AppsOnly != after.AppsOnly) target.AppsOnly = after.AppsOnly;
        if (before.Grouped != after.Grouped) target.Grouped = after.Grouped;
        if (before.PinnedOnly != after.PinnedOnly) target.PinnedOnly = after.PinnedOnly;
        if (before.Workers != after.Workers) target.Workers = after.Workers;
        if (before.Status != after.Status) target.Status = after.Status;
        if (before.TrafficSeconds != after.TrafficSeconds) target.TrafficSeconds = after.TrafficSeconds;
        var removed = before.Pins.Select(pin => pin.Identity).Except(after.Pins.Select(pin => pin.Identity), StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = after.Pins.Where(pin => !before.Pins.Any(old => StringComparer.OrdinalIgnoreCase.Equals(old.Identity, pin.Identity)));
        target.Pins = target.Pins.Where(pin => !removed.Contains(pin.Identity)).Concat(added)
            .DistinctBy(pin => pin.Identity, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed record CliPreferences
{
    public int Version { get; set; } = 1;
    public TerminalTab ActiveTab { get; set; }
    public bool Network { get; set; } = true;
    public CliTabPreferences[] Tabs { get; set; } = [new(), new(), new(), new()];

    internal void Normalize()
    {
        if (Version != 1) throw new InvalidDataException("Unsupported CLI settings version.");
        if (!Enum.IsDefined(ActiveTab)) ActiveTab = TerminalTab.Processes;
        var tabs = Tabs ?? [];
        Tabs = Enumerable.Range(0, 4).Select(index => index < tabs.Length ? tabs[index] ?? new() : new CliTabPreferences()).ToArray();
        for (var i = 0; i < Tabs.Length; i++) Tabs[i].Normalize((TerminalTab)i);
    }

    internal static void Merge(CliPreferences before, CliPreferences after, CliPreferences target)
    {
        if (before.ActiveTab != after.ActiveTab) target.ActiveTab = after.ActiveTab;
        if (before.Network != after.Network) target.Network = after.Network;
        for (var i = 0; i < 4; i++) CliTabPreferences.Merge(before.Tabs[i], after.Tabs[i], target.Tabs[i]);
    }

    internal static string Serialize(CliPreferences preferences) => JsonSerializer.Serialize(preferences, CliSettingsStore.JsonOptions);
}
