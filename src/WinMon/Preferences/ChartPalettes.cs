namespace WinMon.Preferences;

/// <summary>Curated resource colors, in CPU, memory, disk, network, GPU, second-disk order.</summary>
internal static class ChartPalettes
{
    internal static string DisplayName(string name)
    {
        return Normalize(name) switch
        {
            "Ocean" => Strings.PaletteOcean,
            "Colorblind" => Strings.PaletteColorblind,
            "High-Contrast & Inclusive" => Strings.PaletteHighContrast,
            "Sophisticated Pastel" => Strings.PalettePastel,
            "Vibrant Monochromatic Shift" => Strings.PaletteMonochromatic,
            "Cyberpunk / Neon" => Strings.PaletteNeon,
            "Warm Earth Tones" => Strings.PaletteEarth,
            "Cool Tech" => Strings.PaletteTech,
            "Studio" => Strings.PaletteStudio,
            "Signal" => Strings.PaletteSignal,
            "Botanical" => Strings.PaletteBotanical,
            "Spectrum" => Strings.PaletteSpectrum,
            "Neon" => Strings.PaletteNeonVivid,
            "Arcade" => Strings.PaletteArcade,
            "Tropical" => Strings.PaletteTropical,
            _ => Strings.PaletteClassic
        };
    }

    internal static IReadOnlyList<string> Names { get; } =
    [
        "Classic", "Ocean", "Colorblind",
        "High-Contrast & Inclusive", "Sophisticated Pastel", "Vibrant Monochromatic Shift",
        "Cyberpunk / Neon", "Warm Earth Tones", "Cool Tech",
        "Studio", "Signal", "Botanical", "Spectrum", "Neon", "Arcade", "Tropical"
    ];

    internal static string Description(string name)
    {
        return Normalize(name) switch
        {
            "Neon" => Strings.PaletteNeonVividDescription,
            "Arcade" => Strings.PaletteArcadeDescription,
            "Tropical" => Strings.PaletteTropicalDescription,
            _ => DisplayName(name)
        };
    }

    internal static string Normalize(string? value)
    {
        return Names.FirstOrDefault(n =>
            string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Classic";
    }

    internal static string[] Colors(string? name, bool light)
    {
        return (Normalize(name), light) switch
        {
            // Fixed hues in both themes. Order is CPU, memory, primary disk, network, GPU, secondary disk.
            ("High-Contrast & Inclusive", _) => ["#009B77", "#0000CD", "#FF4500", "#7851A9", "#87CEFA", "#D4A017"],
            ("Sophisticated Pastel", _) => ["#D5E8A3", "#C5CBEF", "#DFA895", "#C99AA4", "#B4E3D3", "#CDBA96"],
            ("Vibrant Monochromatic Shift", _) => ["#014421", "#0072B5", "#3D7168", "#0047AB", "#80CBC4", "#B8C9A8"],
            ("Cyberpunk / Neon", _) => ["#39FF14", "#00E5FF", "#FF007F", "#B000FF", "#FFE600", "#FF9100"],
            ("Warm Earth Tones", _) => ["#5B8C5A", "#E2A03F", "#C85A32", "#6C5B7B", "#D4A373", "#9E472A"],
            ("Cool Tech", _) => ["#00A8E8", "#00C9A7", "#FF6F59", "#4B6584", "#80ED99", "#845EC2"],
            ("Studio", _) => ["#59A14F", "#4E79A7", "#F28E2B", "#B07AA1", "#76B7B2", "#EDC949"],
            ("Signal", _) => ["#009E73", "#0072B2", "#D55E00", "#CC79A7", "#56B4E9", "#E69F00"],
            ("Botanical", _) => ["#66A61E", "#7570B3", "#D95F02", "#E7298A", "#1B9E77", "#E6AB02"],
            ("Spectrum", _) => ["#22A884", "#2A788E", "#7AD151", "#414487", "#440154", "#FDE725"],
            ("Neon", _) => ["#00E5FF", "#FF1493", "#B6FF00", "#FF6D00", "#A100FF", "#FFE600"],
            ("Arcade", _) => ["#FF1744", "#2979FF", "#00E676", "#FFEA00", "#D500F9", "#FF9100"],
            ("Tropical", _) => ["#FF3D71", "#00B8D9", "#FFAB00", "#6554FF", "#00C896", "#FF5630"],
            ("Ocean", false) => ["#55CFFF", "#B0A0FF", "#FFAE70", "#FF89B5", "#60E0BD", "#E5D66A"],
            ("Ocean", true) => ["#006C96", "#6745A5", "#9E4810", "#A02E62", "#08765C", "#776600"],
            ("Colorblind", false) => ["#56B4E9", "#E69F00", "#F0E442", "#CC79A7", "#00BFA0", "#FF8054"],
            ("Colorblind", true) => ["#0072B2", "#925D00", "#756B00", "#974F7C", "#007C5C", "#B04C00"],
            (_, true) => ["#36751B", "#1469B5", "#B8461B", "#9B4DCA", "#007B83", "#876300"],
            _ => ["#73FF35", "#4285FF", "#FF773D", "#9B4DCA", "#00F0C8", "#FFD044"]
        };
    }
}