namespace WinMon.Preferences;

/// <summary>Curated resource colors, in CPU, memory, disk, network, GPU, second-disk order.</summary>
internal static class ChartPalettes
{
    internal static IReadOnlyList<string> Names { get; } = new[] { "Classic", "Ocean", "Colorblind" };

    internal static string Normalize(string? value) => Names.FirstOrDefault(n =>
        string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Classic";

    internal static string[] Colors(string? name, bool light) => (Normalize(name), light) switch
    {
        ("Ocean", false) => ["#55CFFF", "#B0A0FF", "#FFAE70", "#FF89B5", "#60E0BD", "#E5D66A"],
        ("Ocean", true) => ["#006C96", "#6745A5", "#9E4810", "#A02E62", "#08765C", "#776600"],
        // Based on the Okabe–Ito hues. Line patterns and labels remain available in high contrast.
        ("Colorblind", false) => ["#56B4E9", "#E69F00", "#F0E442", "#CC79A7", "#00BFA0", "#FF8054"],
        ("Colorblind", true) => ["#0072B2", "#925D00", "#756B00", "#974F7C", "#007C5C", "#B04C00"],
        (_, true) => ["#36751B", "#1469B5", "#B8461B", "#7944B4", "#007B83", "#876300"],
        _ => ["#73FF35", "#4285FF", "#FF773D", "#F657E5", "#00F0C8", "#FFD044"]
    };
}
