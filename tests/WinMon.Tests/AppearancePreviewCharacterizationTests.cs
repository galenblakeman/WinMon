using System.Collections;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class AppearancePreviewCharacterizationTests
{
    private static readonly string[] Accents =
        ["CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent"];
    private readonly Dictionary<object, object?> _resources = [];

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        app.Resources["PanelSurface"] = Brushes.Magenta;
        app.Resources["CpuAccent"] = Brushes.Cyan;
    }

    [TearDown]
    public void TearDown()
    {
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    [TestCase("Dark")]
    [TestCase("Light")]
    [TestCase("System")]
    [TestCase("Candy")]
    [TestCase("Copper")]
    [TestCase("Crystal")]
    [TestCase("EvergreenInstrument")]
    [TestCase("FrostedPorcelain")]
    [TestCase("Gunmetal")]
    [TestCase("MidnightInk")]
    [TestCase("Porcelain")]
    [TestCase("Prism")]
    [TestCase("SmokedSapphire")]
    [TestCase("Slate")]
    [TestCase("Wildberry")]
    [TestCase("unknown")]
    [TestCase("light")]
    [TestCase(" Light ")]
    [TestCase(null)]
    public void ThemeResourcesUseExistingMaterialColorsOpacityAndLocalScope(string? theme)
    {
        var preview = new AppearancePreview();
        AssertUpdate(preview, theme, "Ocean", GaugeStyle.Needle);
    }

    [TestCase(GaugeStyle.Bowl)]
    [TestCase(GaugeStyle.Needle)]
    [TestCase(GaugeStyle.SegmentedArc)]
    [TestCase(GaugeStyle.LeftBar)]
    [TestCase(GaugeStyle.BottomBar)]
    [TestCase(GaugeStyle.SegmentedBar)]
    [TestCase(GaugeStyle.None)]
    public void EachGaugeStyleUpdatesAllSixReadoutsWithoutReplacingControlsOrStaticSampleText(GaugeStyle style)
    {
        var preview = new AppearancePreview();
        AssertUpdate(preview, "Dark", "Colorblind", style);
    }

    [TestCase("Colorblind", true)]
    [TestCase("colorblind", false)]
    [TestCase(" Colorblind ", false)]
    [TestCase("Classic", false)]
    [TestCase("Ocean", false)]
    [TestCase("Cyberpunk / Neon", false)]
    [TestCase("invalid", false)]
    [TestCase(null, false)]
    public void PaletteColorsNormalizeButSeriesPatternSelectionKeepsTheExistingExactIdentifierRule(string? palette,
        bool patterns)
    {
        var preview = new AppearancePreview();
        AssertUpdate(preview, "Light", palette, GaugeStyle.BottomBar);
        Assert.That(preview.ChartPreview.UseSeriesPatterns, Is.EqualTo(patterns));
    }

    [Test]
    public void RepeatedThemePaletteAndStyleTransitionsReplaceOnlyLocalBrushesAndResetPatterns()
    {
        var preview = new AppearancePreview();
        var sentinel = new object();
        preview.Resources["owned-unrelated"] = sentinel;
        foreach (var (theme, palette, style) in new[]
                 {
                     ("Dark", "Classic", GaugeStyle.Bowl), ("Copper", "Ocean", GaugeStyle.Needle),
                     ("Crystal", "Colorblind", GaugeStyle.None), ("Porcelain", "Ocean", GaugeStyle.BottomBar),
                     ("Light", "Classic", GaugeStyle.SegmentedBar), ("Dark", "Colorblind", GaugeStyle.LeftBar),
                     ("Dark", "Classic", GaugeStyle.Bowl)
                 })
        {
            AssertUpdate(preview, theme, palette, style);
            Assert.That(preview.Resources["owned-unrelated"], Is.SameAs(sentinel));
        }
    }

    [Test]
    public void IdenticalUpdatesKeepValuesAndControlIdentityButRetainBrushReplacementSemantics()
    {
        var preview = new AppearancePreview();
        AssertUpdate(preview, "Copper", "Ocean", GaugeStyle.Needle);
        var old = ResourceBrushes(preview);
        var values = old.ToDictionary(pair => pair.Key, pair => XamlWriter.Save(pair.Value));
        AssertUpdate(preview, "Copper", "Ocean", GaugeStyle.Needle);
        var current = ResourceBrushes(preview);
        Assert.Multiple(() =>
        {
            foreach (var (key, brush) in current)
            {
                Assert.That(XamlWriter.Save(brush), Is.EqualTo(values[key]), key);
                Assert.That(ReferenceEquals(brush, old[key]), Is.EqualTo(SystemParameters.HighContrast), key);
            }
        });
    }

    private static Dictionary<string, Brush> ResourceBrushes(AppearancePreview preview) =>
        preview.Resources.Keys.Cast<object>().OfType<string>().Where(key => key != "owned-unrelated")
            .ToDictionary(key => key, key => (Brush)preview.Resources[key]);

    private static void AssertUpdate(AppearancePreview preview, string? theme, string? palette, GaugeStyle style)
    {
        var global = Application.Current.Resources.Cast<DictionaryEntry>().ToDictionary(entry => entry.Key, entry => entry.Value);
        var readouts = preview.MetricPreviews.ToArray();
        var labels = readouts.Select(readout => readout.Label).ToArray();
        var details = readouts.Select(readout => readout.DetailLabel.Text).ToArray();
        var values = readouts.Select(readout => readout.ValueLabel.Text).ToArray();
        var chart = preview.ChartPreview;
        var highContrast = SystemParameters.HighContrast;
        var systemLight = theme == "System" && MainWindow.IsSystemLight();

        preview.Update(theme!, palette!, style);

        Assert.Multiple(() =>
        {
            Assert.That(SystemParameters.HighContrast, Is.EqualTo(highContrast), "The native accessibility configuration must stay stable during this integration call.");
            if (theme == "System") Assert.That(MainWindow.IsSystemLight(), Is.EqualTo(systemLight));
            Assert.That(preview.ChartPreview, Is.SameAs(chart));
            Assert.That(preview.MetricPreviews, Is.EqualTo(readouts));
            Assert.That(readouts.Select(readout => readout.Label), Is.EqualTo(labels));
            Assert.That(readouts.Select(readout => readout.DetailLabel.Text), Is.EqualTo(details));
            Assert.That(readouts.Select(readout => readout.ValueLabel.Text), Is.EqualTo(values));
            Assert.That(labels, Is.EqualTo(new[] { Strings.CPU, Strings.Memory, Strings.Format(Strings.DiskLabel, "C:"),
                Strings.Network, Strings.GPU, Strings.Format(Strings.DiskLabel, "D:") }));
            Assert.That(values, Is.EqualTo(new[] { 32.8, 51.6, 42.0, 28.0, 9.0, 24.0 }
                .Select(value => Strings.Format(Strings.PercentOneDecimal, value))));
            Assert.That(chart.ShowGrid, Is.True);
            Assert.That(chart.VisibleMetrics, Is.EqualTo(31));
            Assert.That(chart.UseSeriesPatterns, Is.EqualTo(palette == "Colorblind"));
            Assert.That(preview.IsHitTestVisible, Is.False);
            foreach (var readout in readouts)
            {
                Assert.That(readout.GaugeStyle, Is.EqualTo(style));
                Assert.That(readout.Height, Is.EqualTo(MetricReadout.LayoutHeight));
                Assert.That(readout.Gauge.Visibility, Is.EqualTo(style == GaugeStyle.None ? Visibility.Hidden : Visibility.Visible));
                Assert.That(readout.IsHitTestVisible, Is.False);
                Assert.That(readout.Toggle.Focusable, Is.False);
            }
            Assert.That(Application.Current.Resources.Keys.Cast<object>(), Is.EquivalentTo(global.Keys));
            foreach (var (key, value) in global) Assert.That(Application.Current.Resources[key], Is.SameAs(value), key.ToString());
        });
        var expectedKeys = new[] { "PanelSurface", "SectionHeaderSurface", "PlotSurface", "Text", "Muted", "Line", "GaugeTrack", "Accent" }
            .Concat(Accents).Concat(Accents.Select(key => key.Replace("Accent", "ChartAccent", StringComparison.Ordinal))).ToArray();
        var brushes = ResourceBrushes(preview);
        Assert.That(brushes.Keys, Is.EquivalentTo(expectedKeys));
        foreach (var (key, brush) in brushes)
        {
            Assert.That(brush.IsFrozen, Is.True, key);
            Assert.That(chart.TryFindResource(key), Is.SameAs(brush), key);
        }
        if (highContrast)
        {
            foreach (var key in new[] { "PanelSurface", "SectionHeaderSurface", "PlotSurface" })
                Assert.That(brushes[key], Is.SameAs(SystemColors.WindowBrush), key);
            foreach (var key in new[] { "Text", "Muted", "Line" }.Concat(Accents)
                         .Concat(Accents.Select(key => key.Replace("Accent", "ChartAccent", StringComparison.Ordinal))))
                Assert.That(brushes[key], Is.SameAs(SystemColors.WindowTextBrush), key);
            Assert.That(brushes["GaugeTrack"], Is.SameAs(SystemColors.GrayTextBrush));
            Assert.That(brushes["Accent"], Is.SameAs(SystemColors.HighlightBrush));
            return;
        }
        var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme!) : null;
        var light = theme == "Light" || systemLight || finish?.Light == true;
        if (finish?.Brushed == true)
        {
            Assert.That(XamlWriter.Save(brushes["PanelSurface"]), Is.EqualTo(XamlWriter.Save(ThemeFinishes.PanelBrush(finish))));
            Assert.That(XamlWriter.Save(brushes["SectionHeaderSurface"]), Is.EqualTo(XamlWriter.Save(ThemeFinishes.ChromeBrush(finish, true))));
        }
        else
        {
            AssertColor(brushes["PanelSurface"], finish?.Surface ?? (light ? "#F0F5FA" : "#15191E"), finish?.PanelOpacity ?? 255);
            if (finish?.GlassSheen == true)
                Assert.That(XamlWriter.Save(brushes["SectionHeaderSurface"]), Is.EqualTo(XamlWriter.Save(ThemeFinishes.GlassHeaderBrush(finish, finish.HeaderOpacity))));
            else AssertColor(brushes["SectionHeaderSurface"], finish?.Header ?? (light ? "#E5EEF7" : "#252A31"), finish?.HeaderOpacity ?? 255);
        }
        AssertColor(brushes["PlotSurface"], finish?.Plot ?? (light ? "#FFFFFF" : "#15191E"));
        AssertColor(brushes["Text"], finish?.Text ?? (light ? "#20334A" : "#EDF2F6"));
        AssertColor(brushes["Muted"], finish?.Muted ?? (light ? "#465D78" : "#B3BDC8"));
        AssertColor(brushes["Line"], finish?.Line ?? (light ? "#CCD9E5" : "#35404B"));
        AssertColor(brushes["GaugeTrack"], finish?.GaugeTrack ?? (light ? "#CCD9E5" : "#35404B"));
        var colors = ChartPalettes.ColorsForTheme(palette!, light, finish);
        var chartColors = ChartPalettes.ChartColorsForTheme(palette!, light, finish);
        for (var i = 0; i < Accents.Length; i++)
        {
            AssertColor(brushes[Accents[i]], colors[i]);
            AssertColor(brushes[Accents[i].Replace("Accent", "ChartAccent", StringComparison.Ordinal)], chartColors[i]);
            Assert.That(readouts[i].TryFindResource(Accents[i]), Is.SameAs(brushes[Accents[i]]));
        }
        AssertColor(brushes["Accent"], finish?.UiAccent ?? (light ? "#1469B5" : colors[0]));
    }

    private static void AssertColor(Brush brush, string hex, byte opacity = 255)
    {
        Assert.That(brush, Is.TypeOf<SolidColorBrush>());
        var color = (Color)ColorConverter.ConvertFromString(hex);
        Assert.That(((SolidColorBrush)brush).Color, Is.EqualTo(Color.FromArgb(opacity, color.R, color.G, color.B)));
    }
}
