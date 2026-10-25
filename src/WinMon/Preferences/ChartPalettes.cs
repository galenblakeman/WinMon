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
            "Warm Earth Tones" => Strings.PaletteEarth,
            "Cool Tech" => Strings.PaletteTech,
            "Studio" => Strings.PaletteStudio,
            "Signal" => Strings.PaletteSignal,
            "Botanical" => Strings.PaletteBotanical,
            "Spectrum" => Strings.PaletteSpectrum,
            "Neon" => Strings.PaletteNeonVivid,
            "Arcade" => Strings.PaletteArcade,
            "Tropical" => Strings.PaletteTropical,
            "Candy" => Strings.PaletteCandy,
            "Wildberry" => Strings.PaletteWildberry,
            "Copper" => Strings.PaletteCopper,
            "Gunmetal" => Strings.PaletteGunmetal,
            _ => Strings.PaletteClassic
        };
    }

    internal static IReadOnlyList<string> Names { get; } =
    [
        "Classic", "Ocean", "Colorblind",
        "High-Contrast & Inclusive", "Sophisticated Pastel", "Vibrant Monochromatic Shift",
        "Warm Earth Tones", "Cool Tech",
        "Studio", "Signal", "Botanical", "Spectrum", "Neon", "Arcade", "Tropical", "Candy", "Wildberry",
        "Copper", "Gunmetal"
    ];

    internal static string Description(string name)
    {
        return Normalize(name) switch
        {
            "Neon" => Strings.PaletteNeonVividDescription,
            "Arcade" => Strings.PaletteArcadeDescription,
            "Tropical" => Strings.PaletteTropicalDescription,
            "Candy" => Strings.PaletteCandyDescription,
            "Wildberry" => Strings.PaletteWildberryDescription,
            "Copper" => Strings.PaletteCopperDescription,
            "Gunmetal" => Strings.PaletteGunmetalDescription,
            _ => DisplayName(name)
        };
    }

    /// <summary>The palette paired with an appearance; Settings follows it until the user picks another palette.</summary>
    internal static string ForTheme(string? theme) => theme switch
    {
        "Candy" or "Wildberry" or "Copper" or "Gunmetal" => theme,
        "Crystal" => "Cool Tech",
        "EvergreenInstrument" or "Porcelain" => "Warm Earth Tones",
        "FrostedPorcelain" => "High-Contrast & Inclusive",
        "MidnightInk" => "Neon",
        "Prism" => "Arcade",
        "SmokedSapphire" => "Tropical",
        "Slate" => "Ocean",
        _ => "Classic"
    };

    internal static string Normalize(string? value)
    {
        // Cyberpunk / Neon used the same six hue families as Neon, so saved settings load as Neon.
        if (string.Equals(value?.Trim(), "Cyberpunk / Neon", StringComparison.OrdinalIgnoreCase)) return "Neon";
        return Names.FirstOrDefault(n =>
            string.Equals(n, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Classic";
    }

    internal static string[] Colors(string? name, bool light)
    {
        return (Normalize(name), light) switch
        {
            // Fixed hues in both themes. Order is CPU, memory, primary disk, network, GPU, secondary disk.
            ("Cool Tech", _) => ["#00A8E8", "#00C9A7", "#FF6F59", "#4B6584", "#80ED99", "#845EC2"],
            ("Studio", _) => ["#59A14F", "#4E79A7", "#F28E2B", "#B07AA1", "#76B7B2", "#EDC949"],
            ("Botanical", _) => ["#66A61E", "#7570B3", "#D95F02", "#E7298A", "#1B9E77", "#E6AB02"],
            ("Tropical", _) => ["#FF3D71", "#00B8D9", "#FFAB00", "#6554FF", "#00C896", "#FF5630"],
            // Hand-tuned sets clear every plot in their mode at 3.5:1, so chart traces keep the designed color.
            ("High-Contrast & Inclusive", false) => ["#05C49A", "#5C7CFF", "#FC581E", "#A67FE0", "#87CEFA", "#ECB925"],
            ("High-Contrast & Inclusive", true) => ["#008A6A", "#0000CD", "#EF4B0E", "#663791", "#1280C3", "#744E00"],
            ("Sophisticated Pastel", false) => ["#D5E8A3", "#C5CBEF", "#DFA895", "#C99AA4", "#B4E3D3", "#CDBA96"],
            ("Sophisticated Pastel", true) => ["#6E8C3F", "#6673C2", "#BF6F55", "#A86478", "#3E8F7A", "#957A4E"],
            ("Vibrant Monochromatic Shift", false) => ["#2E9E5E", "#3A9AE0", "#4F8F84", "#4A7FE8", "#8AD6CF", "#B8C9A8"],
            ("Vibrant Monochromatic Shift", true) => ["#014421", "#0072B5", "#2F5F57", "#0047AB", "#1F8F87", "#6B8752"],
            // The dark CPU green stays clear of Evergreen's accent greens.
            ("Warm Earth Tones", false) => ["#60CA81", "#E29F3D", "#D7673B", "#B7A8E2", "#E7CCA3", "#CAAF60"],
            ("Warm Earth Tones", true) => ["#428156", "#A06D20", "#963712", "#573669", "#6F4E33", "#706F33"],
            ("Signal", false) => ["#009E73", "#0072B2", "#D55E00", "#CC79A7", "#56B4E9", "#E69F00"],
            ("Signal", true) => ["#007A57", "#0B4F8A", "#A33C00", "#B0588A", "#298DCD", "#B57A00"],
            ("Spectrum", false) => ["#22A884", "#3A8FA8", "#7AD151", "#6F75C8", "#B06CD8", "#FDE725"],
            ("Spectrum", true) => ["#1E8F70", "#2A6F8E", "#4F8F1F", "#3E4A8F", "#5A0F6E", "#8A7A00"],
            ("Neon", false) => ["#00E5FF", "#FF1493", "#B6FF00", "#FF6D00", "#A100FF", "#FFE600"],
            ("Neon", true) => ["#0090C1", "#F0127F", "#4E9700", "#E55900", "#9A00F0", "#B07D00"],
            ("Arcade", false) => ["#FF1744", "#2979FF", "#00E676", "#FFEA00", "#D500F9", "#FF9100"],
            ("Arcade", true) => ["#F0153F", "#2266F0", "#009C4E", "#B07D00", "#B800D9", "#C24E00"],
            ("Candy", false) => ["#7EE36E", "#5FA8FF", "#FF9C52", "#FF7DB8", "#45E0C6", "#FFD25E"],
            ("Candy", true) => ["#1F6A2D", "#1265C0", "#C2430A", "#910B43", "#0E7A6C", "#694100"],
            ("Wildberry", false) => ["#9BD87B", "#81A8FF", "#FD9E5A", "#FF5BA0", "#AA7BD6", "#FFD56C"],
            ("Wildberry", true) => ["#4D8A2E", "#185BCF", "#B4510C", "#C2195B", "#7436A8", "#724700"],
            // Copper's dark set clears 4.75:1 on its brown panel, and avoids its peach and gold chrome.
            ("Copper", false) => ["#77DC95", "#AFC0E5", "#F3D447", "#FD9CB6", "#97F4F0", "#C9A9FF"],
            ("Copper", true) => ["#206B31", "#2F60C6", "#8A6A00", "#B52162", "#1A7B87", "#602994"],
            ("Gunmetal", false) => ["#5CE47F", "#6BB7FA", "#FFAF21", "#F05FCE", "#9A86FD", "#FF6B4A"],
            ("Gunmetal", true) => ["#297F3B", "#2B60B7", "#B66400", "#B0257F", "#443BBA", "#B32F21"],
            ("Ocean", false) => ["#55CFFF", "#B0A0FF", "#FFAE70", "#FF89B5", "#60E0BD", "#E5D66A"],
            ("Ocean", true) => ["#006C96", "#6745A5", "#9E4810", "#A02E62", "#08765C", "#776600"],
            ("Colorblind", false) => ["#56B4E9", "#E69F00", "#F0E442", "#CC79A7", "#3DCCA2", "#FF8053"],
            ("Colorblind", true) => ["#0173B3", "#A8733B", "#908000", "#8C356B", "#0A7A59", "#973B00"],
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
            (Color)ColorConverter.ConvertFromString(hex), surface, plot, light, 4.75, 3.5)).ToArray();
    }

    // Chart strokes only need to clear the plot. Gauge readouts keep the stronger text threshold.
    internal static string[] ChartColorsForTheme(string? name, bool light, ThemeFinishes.Finish? finish)
    {
        var plot = (Color)ColorConverter.ConvertFromString(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
        return Colors(name, light).Select(hex => EnsureContrast(
            (Color)ColorConverter.ConvertFromString(hex), plot, plot, light, 3.5, 3.5)).ToArray();
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

    private static string EnsureContrast(Color color, Color surface, Color plot, bool light,
        double readoutMinimum, double traceMinimum)
    {
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
            // Darkened yellow reads as olive, so turn yellows toward amber as they darken; limes stay green.
            var shifted = light && hue is > 0.65 and < 1.1
                ? hue - (hue - 0.65) * Math.Min(1, (originalLightness - value) / 0.25)
                : hue;
            var x = chroma * (1 - Math.Abs(shifted % 2 - 1));
            var offset = value - chroma / 2;
            var (red, green, blue) = shifted switch
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
