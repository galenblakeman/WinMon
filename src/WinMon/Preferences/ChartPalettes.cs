using System.Windows.Media;

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

    // The same accent is used for a fine chart trace and small text inside a gauge.
    // Leave recognizable palette colors alone when they already clear both surfaces.
    internal static string[] ColorsForTheme(string? name, bool light, ThemeFinishes.Finish? finish)
    {
        var surface = (Color)ColorConverter.ConvertFromString(finish?.Surface ?? (light ? "#F0F5FA" : "#15191E"));
        var plot = (Color)ColorConverter.ConvertFromString(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
        return Colors(name, light).Select(hex => EnsureContrast(
            (Color)ColorConverter.ConvertFromString(hex), surface, plot, light)).ToArray();
    }

    internal static double ContrastRatio(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255d;
                return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static string EnsureContrast(Color color, Color surface, Color plot, bool light)
    {
        const double readoutMinimum = 4.75;
        const double traceMinimum = 3.5;
        bool Passes(Color candidate) => ContrastRatio(candidate, surface) >= readoutMinimum &&
                                        ContrastRatio(candidate, plot) >= traceMinimum;
        if (Passes(color)) return $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        // Move along the original hue and saturation, changing lightness only.
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var maximum = Math.Max(r, Math.Max(g, b));
        var minimum = Math.Min(r, Math.Min(g, b));
        var delta = maximum - minimum;
        var originalLightness = (maximum + minimum) / 2;
        var saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * originalLightness - 1));
        var hue = delta == 0 ? 0 : maximum == r ? ((g - b) / delta + 6) % 6
            : maximum == g ? (b - r) / delta + 2 : (r - g) / delta + 4;

        Color At(double value)
        {
            var chroma = (1 - Math.Abs(2 * value - 1)) * saturation;
            var x = chroma * (1 - Math.Abs(hue % 2 - 1));
            var offset = value - chroma / 2;
            var (red, green, blue) = hue switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };
            return Color.FromRgb((byte)Math.Round((red + offset) * 255),
                (byte)Math.Round((green + offset) * 255), (byte)Math.Round((blue + offset) * 255));
        }

        var low = light ? 0d : originalLightness;
        var high = light ? originalLightness : 1d;
        for (var i = 0; i < 20; i++)
        {
            var middle = (low + high) / 2;
            if (Passes(At(middle)) == light) low = middle;
            else high = middle;
        }
        var adjusted = At(light ? low : high);
        return $"#{adjusted.R:X2}{adjusted.G:X2}{adjusted.B:X2}";
    }
}
