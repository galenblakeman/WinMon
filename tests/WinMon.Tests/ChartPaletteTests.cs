using NUnit.Framework;
using System.Windows.Media;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ChartPaletteTests
{
    [TestCase(null, "Classic")]
    [TestCase("unknown", "Classic")]
    [TestCase(" ocean ", "Ocean")]
    [TestCase("COLORBLIND", "Colorblind")]
    [TestCase(" neon ", "Neon")]
    [TestCase("ARCADE", "Arcade")]
    [TestCase("tropical", "Tropical")]
    [TestCase(" candy ", "Candy")]
    [TestCase("wildberry", "Wildberry")]
    [TestCase("Cyberpunk / Neon", "Neon")]
    public void Validate_NormalizesPalette(string? input, string expected)
    {
        Assert.That(PreferenceStore.Validate(new MonitorPreferences { ChartPalette = input! }).ChartPalette, Is.EqualTo(expected));
    }

    [TestCase("Neon", "#00E5FF,#FF1493,#B6FF00,#FF6D00,#A100FF,#FFE600", "#0090C1,#F0127F,#4E9700,#E55900,#9A00F0,#B07D00")]
    [TestCase("Arcade", "#FF1744,#2979FF,#00E676,#FFEA00,#D500F9,#FF9100", "#F0153F,#2266F0,#009C4E,#B07D00,#B800D9,#C24E00")]
    [TestCase("Tropical", "#FF3D71,#00B8D9,#FFAB00,#6554FF,#00C896,#FF5630", "#FF3D71,#00B8D9,#FFAB00,#6554FF,#00C896,#FF5630")]
    [TestCase("Candy", "#7EE36E,#5FA8FF,#FF9C52,#FF7DB8,#45E0C6,#FFD25E", "#2E7D32,#1565C0,#C2410A,#C2185B,#00796B,#7A5200")]
    [TestCase("Wildberry", "#99D87A,#80A8FF,#FF9F5A,#FF6FA8,#AA7DD8,#FFD56C", "#4D8A2E,#185BCF,#B4510C,#C2195B,#7436A8,#724700")]
    public void CustomPalette_PreservesRequestedResourceOrderInEachTheme(string palette, string darkColors, string lightColors)
    {
        Assert.Multiple(() =>
        {
            Assert.That(ChartPalettes.Colors(palette, false), Is.EqualTo(darkColors.Split(',')));
            Assert.That(ChartPalettes.Colors(palette, true), Is.EqualTo(lightColors.Split(',')));
            Assert.That(ChartPalettes.Names, Does.Contain(palette));
        });
    }

    [TestCase("High-Contrast & Inclusive", false)]
    [TestCase("High-Contrast & Inclusive", true)]
    [TestCase("Sophisticated Pastel", true)]
    [TestCase("Vibrant Monochromatic Shift", false)]
    [TestCase("Vibrant Monochromatic Shift", true)]
    [TestCase("Warm Earth Tones", false)]
    [TestCase("Signal", true)]
    [TestCase("Spectrum", false)]
    [TestCase("Spectrum", true)]
    [TestCase("Neon", true)]
    [TestCase("Arcade", true)]
    [TestCase("Candy", false)]
    [TestCase("Candy", true)]
    [TestCase("Wildberry", false)]
    [TestCase("Wildberry", true)]
    public void HandTunedVariant_KeepsDesignedChartColorsOnEveryThemeInItsMode(string palette, bool light)
    {
        foreach (var theme in new[] { light ? "Light" : "Dark" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            if (finish is not null && finish.Light != light) continue;
            Assert.That(ChartPalettes.ChartColorsForTheme(palette, light, finish),
                Is.EqualTo(ChartPalettes.Colors(palette, light)), $"{theme} / {palette}");
        }
    }

    [Test]
    public void EveryPaletteColor_ClearsPlotAndReadoutContrastOnEveryTheme()
    {
        foreach (var theme in new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            var light = theme == "Light" || finish?.Light == true;
            var surface = ColorOf(finish?.Surface ?? (light ? "#F0F5FA" : "#15191E"));
            var plot = ColorOf(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
            foreach (var palette in ChartPalettes.Names)
            {
                var colors = ChartPalettes.ColorsForTheme(palette, light, finish);
                Assert.That(colors, Has.Length.EqualTo(6), $"{theme} / {palette}");
                foreach (var color in colors)
                {
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), surface), Is.GreaterThanOrEqualTo(4.5),
                        $"Gauge text: {theme} / {palette} / {color}");
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), plot), Is.GreaterThanOrEqualTo(3.0),
                        $"Chart trace: {theme} / {palette} / {color}");
                }
            }
        }
    }

    // Machado, Oliveira and Fernandes (2009) full-severity simulations, applied in linear sRGB.
    private static readonly (string Name, double Minimum, double[,] Matrix)[] ColorVision =
    [
        ("Deuteranopia", 15, new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } }),
        ("Protanopia", 15, new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } }),
        ("Tritanopia", 10, new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } })
    ];

    [TestCase("Colorblind")]
    [TestCase("Signal")]
    [TestCase("High-Contrast & Inclusive")]
    public void AccessiblePalette_KeepsResourcesApartUnderColorVisionDeficiency(string palette)
    {
        foreach (var theme in new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            var light = theme == "Light" || finish?.Light == true;
            var colors = ChartPalettes.ChartColorsForTheme(palette, light, finish).Select(ColorOf).ToArray();
            foreach (var (name, minimum, matrix) in ColorVision)
            {
                var lab = colors.Select(color => Lab(color, matrix)).ToArray();
                for (var i = 0; i < lab.Length; i++)
                    for (var j = i + 1; j < lab.Length; j++)
                        Assert.That(Distance(lab[i], lab[j]), Is.GreaterThanOrEqualTo(minimum),
                            $"{name}: {theme} / {palette} / resources {i} and {j}");
            }
        }
    }

    // CIE76 distance in CIELAB (D65) after simulating the color-vision deficiency.
    private static (double L, double A, double B) Lab(Color color, double[,] matrix)
    {
        static double Linear(byte value)
        {
            var normalized = value / 255d;
            return normalized <= 0.04045 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        double[] rgb = [Linear(color.R), Linear(color.G), Linear(color.B)];
        var simulated = new double[3];
        for (var row = 0; row < 3; row++)
            simulated[row] = Math.Clamp(matrix[row, 0] * rgb[0] + matrix[row, 1] * rgb[1] + matrix[row, 2] * rgb[2], 0, 1);
        var x = (0.4124 * simulated[0] + 0.3576 * simulated[1] + 0.1805 * simulated[2]) / 0.95047;
        var y = 0.2126 * simulated[0] + 0.7152 * simulated[1] + 0.0722 * simulated[2];
        var z = (0.0193 * simulated[0] + 0.1192 * simulated[1] + 0.9505 * simulated[2]) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16d / 116;
        return (116 * F(y) - 16, 500 * (F(x) - F(y)), 200 * (F(y) - F(z)));
    }

    private static double Distance((double L, double A, double B) first, (double L, double A, double B) second) =>
        Math.Sqrt(Math.Pow(first.L - second.L, 2) + Math.Pow(first.A - second.A, 2) + Math.Pow(first.B - second.B, 2));

    [Test]
    public void ChartColors_ClearPlotContrastWithoutApplyingGaugeTextThreshold()
    {
        foreach (var theme in new[] { "Dark", "Light" }.Concat(ThemeFinishes.Names))
        {
            var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
            var light = theme == "Light" || finish?.Light == true;
            var plot = ColorOf(finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
            foreach (var palette in ChartPalettes.Names)
                foreach (var color in ChartPalettes.ChartColorsForTheme(palette, light, finish))
                    Assert.That(ChartPalettes.ContrastRatio(ColorOf(color), plot), Is.GreaterThanOrEqualTo(3.0),
                        $"Chart trace: {theme} / {palette} / {color}");
        }

        var candy = ThemeFinishes.For("Candy");
        Assert.That(ChartPalettes.ChartColorsForTheme("Neon", candy.Light, candy)[2],
            Is.Not.EqualTo(ChartPalettes.ColorsForTheme("Neon", candy.Light, candy)[2]),
            "A bright chart trace should not inherit the gauge text threshold.");
    }

    [Test]
    public void PaletteSwatches_FollowSelectedFinish()
    {
        var choice = new PaletteChoice("Tropical");
        var candy = ThemeFinishes.For("Candy");
        choice.UpdateTheme(candy.Light, candy);
        Assert.That(choice.Colors, Is.EqualTo(ChartPalettes.ChartColorsForTheme(choice.Name, candy.Light, candy)));
        Assert.That(choice.Colors, Is.Not.EqualTo(ChartPalettes.Colors(choice.Name, candy.Light)));
    }

    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    [TestCaseSource(typeof(ChartPalettes), nameof(ChartPalettes.Names))]
    public async Task Palette_RoundTripsWithoutChangingThemeOrDiskSelection(string palette)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(directory, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { ChartPalette = palette, Theme = "Light", PrimaryDrive = "D:", SecondaryDrive = "E:" });
            var loaded = await store.LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(loaded.ChartPalette, Is.EqualTo(palette));
                Assert.That(loaded.Theme, Is.EqualTo("Light"));
                Assert.That(loaded.PrimaryDrive, Is.EqualTo("D:"));
                Assert.That(loaded.SecondaryDrive, Is.EqualTo("E:"));
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
