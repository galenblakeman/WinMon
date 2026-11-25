using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class ThemeApplicationCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private static readonly string[] AccentKeys =
        ["CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent", "GpuAccent", "SecondaryDiskAccent"];

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private void Apply(string theme, string palette = "Ocean", GaugeStyle style = GaugeStyle.Bowl)
    {
        _window.Preferences = _window.Preferences with { Theme = theme, ChartPalette = palette, GaugeStyle = style };
        _window.ApplyTheme();
        Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        Assert.That(_window.BackdropActive, Is.False);
    }

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static void Solid(string key, string expected)
    {
        Assert.That(Brush(key), Is.TypeOf<SolidColorBrush>(), key);
        Assert.That(((SolidColorBrush)Brush(key)).Color, Is.EqualTo(ColorOf(expected)), key);
    }

    private static void AssertHighContrast()
    {
        foreach (var key in new[] { "Surface", "PanelSurface", "TitleBarSurface", "SectionHeaderSurface", "PlotSurface", "SearchSurface" })
            Assert.That(Brush(key), Is.SameAs(SystemColors.WindowBrush), key);
        Assert.That(Brush("Raised"), Is.SameAs(SystemColors.ControlBrush));
        foreach (var key in new[] { "SelectedSurface", "SegmentSelectedSurface", "SnapshotAccent", "Accent" })
            Assert.That(Brush(key), Is.SameAs(SystemColors.HighlightBrush), key);
        foreach (var key in new[] { "SelectedText", "ChipSelectedText", "SnapshotAccentText", "AccentButtonText" })
            Assert.That(Brush(key), Is.SameAs(SystemColors.HighlightTextBrush), key);
        foreach (var key in new[] { "ChipText", "ProcessPinAccent", "Text", "TitleText", "TitleMuted", "TitleStatus",
                     "Muted", "InactiveTabText", "Line" }.Concat(AccentKeys).Concat(AccentKeys.Select(key => key.Replace("Accent", "ChartAccent", StringComparison.Ordinal))))
            Assert.That(Brush(key), Is.SameAs(SystemColors.WindowTextBrush), key);
        Assert.That(Brush("GaugeTrack"), Is.SameAs(SystemColors.GrayTextBrush));
    }

    private static void AssertPlain(bool light, bool? highContrast = null)
    {
        if (highContrast ?? SystemParameters.HighContrast) { AssertHighContrast(); return; }
        var expected = light
            ? new Dictionary<string, string>
            {
                ["Surface"]="#F0F5FA",["PanelSurface"]="#F0F5FA",["Raised"]="#FFFFFF",
                ["TitleBarSurface"]="#DCE7F1",["SectionHeaderSurface"]="#E5EEF7",["PlotSurface"]="#FFFFFF",
                ["SearchSurface"]="#FFFFFF",["SelectedSurface"]="#1469B5",["SelectedText"]="#FFFFFF",
                ["SegmentSelectedSurface"]="#1469B5",["ChipText"]="#20334A",["ChipSelectedText"]="#FFFFFF",
                ["SnapshotAccentText"]="#07171B",["Text"]="#20334A",["TitleText"]="#20334A",
                ["TitleMuted"]="#465D78",["Muted"]="#465D78",["InactiveTabText"]="#465D78",
                ["Line"]="#CCD9E5",["GaugeTrack"]="#CCD9E5",["Accent"]="#1469B5",
                ["AccentButtonText"]="#FFFFFF",["SnapshotAccent"]="#1469B5",["ProcessPinAccent"]="#1469B5",["TitleStatus"]="#1469B5"
            }
            : new Dictionary<string, string>
            {
                ["Surface"]="#15191E",["PanelSurface"]="#15191E",["Raised"]="#20262D",
                ["TitleBarSurface"]="#30343B",["SectionHeaderSurface"]="#252A31",["PlotSurface"]="#15191E",
                ["SearchSurface"]="#15191E",["SelectedSurface"]="#20262D",["SelectedText"]="#EDF2F6",
                ["SegmentSelectedSurface"]="#3D4855",["ChipText"]="#EDF2F6",["ChipSelectedText"]="#EDF2F6",
                ["SnapshotAccentText"]="#07171B",["Text"]="#EDF2F6",["TitleText"]="#EDF2F6",
                ["TitleMuted"]="#B3BDC8",["Muted"]="#B3BDC8",["InactiveTabText"]="#B3BDC8",
                ["Line"]="#35404B",["GaugeTrack"]="#35404B",["Accent"]="#55CFFF",
                ["AccentButtonText"]="#000000",["SnapshotAccent"]="#55CFFF",["ProcessPinAccent"]="#55CFFF",["TitleStatus"]="#55CFFF"
            };
        foreach (var (key, hex) in expected) Solid(key, hex);
        var accents = (light ? "#006C96,#6745A5,#9E4810,#A02E62,#08765C,#776600"
            : "#55CFFF,#B0A0FF,#FFAE70,#FF89B5,#60E0BD,#E5D66A").Split(',');
        for (var i = 0; i < AccentKeys.Length; i++)
        {
            Solid(AccentKeys[i], accents[i]);
            Solid(AccentKeys[i].Replace("Accent", "ChartAccent", StringComparison.Ordinal), accents[i]);
        }
    }

    [TestCase("Dark", false), TestCase("Light", true), TestCase("unknown", false)]
    [TestCase("light", false), TestCase(" Light ", false)]
    public void PlainThemesAndUnknownNamesKeepEveryResourceRoleAndTheirExactNamePolicy(string theme, bool light)
    {
        Apply(theme);
        AssertPlain(light);
    }

    [Test]
    public void SystemThemeUsesTheCurrentReadOnlyWindowsPreference()
    {
        var before = MainWindow.IsSystemLight();
        Apply("System");
        var after = MainWindow.IsSystemLight();
        if (before != after) Assert.Inconclusive("Windows changed the theme preference during the read-only observation.");
        AssertPlain(before);
    }

    private static IEnumerable<TestCaseData> FinishCases()
    {
        yield return new("Candy", "#FFF8F1", "#2E2134", "#BF155D", "#F7A6C7", "solid");
        yield return new("Copper", "#574033", "#FFF1E6", "#73FF35", "#77513C", "brushed");
        yield return new("Gunmetal", "#1D252C", "#ECF3F5", "#73FF35", "#303B43", "brushed");
        yield return new("Porcelain", "#F5F2EB", "#242B31", "#365F85", "#EEE9E0", "solid");
        yield return new("MidnightInk", "#101A28", "#EAF1F7", "#477DAE", "#1D3045", "solid");
        yield return new("EvergreenInstrument", "#172522", "#EFF3EA", "#87B782", "#273C34", "solid");
        yield return new("FrostedPorcelain", "#F2F4F6", "#202B39", "#38698E", "#F1F5FA", "solid");
        yield return new("SmokedSapphire", "#10213C", "#F1F6FF", "#5C9BE0", "#1A3658", "solid");
        yield return new("Crystal", "#1C2938", "#EDF5FC", "#A5D0F2", "#435E7D", "glass");
        yield return new("Slate", "#2B3A48", "#F1F6F9", "#B6CAD7", "#4D6374", "solid");
        yield return new("Prism", "#1A2031", "#F1F2FF", "#8DCAFF", "#303A63", "glass");
        yield return new("Wildberry", "#211629", "#FFF1FA", "#E36B9D", "#4A2549", "solid");
    }

    [TestCaseSource(nameof(FinishCases))]
    public void NamedFinishesKeepTheirOwnSurfacesTextAccentsAndInactiveBackdropBrushes(string theme,
        string surface, string text, string accent, string header, string kind)
    {
        Apply(theme, "Classic");
        if (SystemParameters.HighContrast) { AssertHighContrast(); return; }
        Solid("Surface", surface);
        Solid("Text", text);
        Solid("TitleText", text);
        Solid("Accent", accent);
        Solid("SnapshotAccent", accent);
        Solid("ProcessPinAccent", accent);
        Solid("TitleStatus", accent);
        if (kind == "brushed")
        {
            Assert.That(Brush("PanelSurface"), Is.TypeOf<DrawingBrush>());
            Assert.That(Brush("TitleBarSurface"), Is.TypeOf<DrawingBrush>());
            var drawing = (DrawingBrush)Brush("SectionHeaderSurface");
            Assert.That(drawing.IsFrozen, Is.True);
            Assert.That(drawing.Viewbox, Is.EqualTo(new Rect(0, 0, 1, 40)));
            var group = (DrawingGroup)drawing.Drawing;
            var background = (GeometryDrawing)group.Children[0];
            var reflection = (LinearGradientBrush)background.Brush;
            Assert.That(reflection.GradientStops[0].Color, Is.EqualTo(ColorOf(header)));
            Assert.That(reflection.GradientStops[2].Color, Is.EqualTo(ColorOf(header)));
        }
        else
        {
            Solid("PanelSurface", surface);
            if (kind == "glass")
            {
                var brush = (LinearGradientBrush)Brush("SectionHeaderSurface");
                Assert.That(brush.IsFrozen, Is.True);
                Assert.That(brush.StartPoint, Is.EqualTo(new Point(0, 0)));
                Assert.That(brush.EndPoint, Is.EqualTo(new Point(1, 0)));
                Assert.That(brush.GradientStops.Select(stop => stop.Offset), Is.EqualTo(new[] { 0d, .5, 1d }));
                Assert.That(brush.GradientStops.Select(stop => stop.Color.A), Is.All.EqualTo(byte.MaxValue));
                Assert.That(brush.GradientStops[0].Color, Is.EqualTo(ColorOf(header)));
                Assert.That(brush.GradientStops[2].Color, Is.EqualTo(ColorOf(header)));
            }
            else Solid("SectionHeaderSurface", header);
        }
    }

    private static IEnumerable<TestCaseData> PaletteCases()
    {
        foreach (var theme in new[] { "Dark", "Light", "Copper", "FrostedPorcelain" })
            foreach (var palette in ChartPalettes.Names) yield return new TestCaseData(theme, palette);
    }

    [TestCaseSource(nameof(PaletteCases))]
    public void EveryPaletteWiresDistinctReadoutAndChartColorsInResourceOrder(string theme, string palette)
    {
        Apply(theme, palette);
        Assert.That(_window.OverallGraph.UseSeriesPatterns, Is.EqualTo(palette == "Colorblind"));
        if (SystemParameters.HighContrast) { AssertHighContrast(); return; }
        var finish = ThemeFinishes.IsFinish(theme) ? ThemeFinishes.For(theme) : null;
        var light = theme == "Light" || finish?.Light == true;
        var readouts = ChartPalettes.ColorsForTheme(palette, light, finish);
        var charts = ChartPalettes.ChartColorsForTheme(palette, light, finish);
        for (var i = 0; i < AccentKeys.Length; i++)
        {
            Solid(AccentKeys[i], readouts[i]);
            Solid(AccentKeys[i].Replace("Accent", "ChartAccent", StringComparison.Ordinal), charts[i]);
        }
    }

    [TestCase("Colorblind", true), TestCase("colorblind", false), TestCase(" Colorblind ", false)]
    [TestCase("Classic", false), TestCase("unknown", false)]
    public void SeriesPatternsUseTheStoredExactIdentifierWhileColorsMayNormalizeIt(string palette, bool patterned)
    {
        Apply("Dark", palette);
        Assert.That(_window.OverallGraph.UseSeriesPatterns, Is.EqualTo(patterned));
    }

    [TestCase(GaugeStyle.Bowl), TestCase(GaugeStyle.Needle), TestCase(GaugeStyle.SegmentedArc)]
    [TestCase(GaugeStyle.LeftBar), TestCase(GaugeStyle.BottomBar), TestCase(GaugeStyle.SegmentedBar), TestCase(GaugeStyle.None)]
    public void ThemeApplicationForwardsTheGaugeStyleToFullAndCompactReadouts(GaugeStyle style)
    {
        Apply("Dark", style: style);
        foreach (var metric in new[] { _window.CpuMetric, _window.MemoryMetric, _window.DiskMetric,
                     _window.NetworkMetric, _window.GpuMetric, _window.SecondaryDiskMetric })
            Assert.That(metric.GaugeStyle, Is.EqualTo(style));
        foreach (var tile in _window.CompactTiles) Assert.That(tile.Readout.GaugeStyle, Is.EqualTo(style));
    }

    [Test]
    public void SwitchingFromBrushedToPlainReplacesEveryOldRoleWithoutChangingPreferences()
    {
        Apply("Copper", "Colorblind");
        var unrelated = new object();
        Application.Current.Resources["theme-characterization-unrelated"] = unrelated;
        var preferences = _window.Preferences with { Theme = "Light", ChartPalette = "Ocean" };
        _window.Preferences = preferences;
        _window.ApplyTheme();
        AssertPlain(true);
        Assert.That(_window.Preferences, Is.SameAs(preferences));
        Assert.That(Application.Current.Resources["theme-characterization-unrelated"], Is.SameAs(unrelated));
        Assert.That(_window.OverallGraph.UseSeriesPatterns, Is.False);
        var oldSurface = Brush("Surface");
        _window.ApplyTheme();
        if (!SystemParameters.HighContrast) Assert.That(Brush("Surface"), Is.Not.SameAs(oldSurface));
        AssertPlain(true);
    }

    [TestCase("Dark", "Ocean"), TestCase("Light", "Colorblind")]
    [TestCase("Copper", "Colorblind"), TestCase("Crystal", "Ocean")]
    public void ObservedHighContrastUsesSystemBrushIdentitiesAndRetainsIndependentGaugeAndPatternChoices(string theme, string palette)
    {
        _window.Preferences = _window.Preferences with { Theme = theme, ChartPalette = palette, GaugeStyle = GaugeStyle.LeftBar };
        _window.ApplyTheme(true, false);
        AssertHighContrast();
        Assert.That(_window.OverallGraph.UseSeriesPatterns, Is.EqualTo(palette == "Colorblind"));
        Assert.That(_window.CpuMetric.GaugeStyle, Is.EqualTo(GaugeStyle.LeftBar));
        Assert.That(_window.CompactTiles[0].Readout.GaugeStyle, Is.EqualTo(GaugeStyle.LeftBar));
        Assert.That(_window.BackdropActive, Is.False);
        Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
    }

    [TestCase("FrostedPorcelain", "#F2F4F6", "#EDF2F8", "#F1F5FA", 226, 212, 230, false)]
    [TestCase("SmokedSapphire", "#10213C", "#19395F", "#1A3658", 228, 208, 224, false)]
    [TestCase("Crystal", "#1C2938", "#537399", "#435E7D", 240, 215, 229, true)]
    [TestCase("Slate", "#2B3A48", "#526878", "#4D6374", 210, 190, 205, false)]
    [TestCase("Prism", "#1A2031", "#332F5C", "#303A63", 238, 216, 224, true)]
    public void ObservedBackdropChangesOnlyTheIntendedPanelChromeAndHeaderAlpha(string theme, string surface,
        string chrome, string header, int panelAlpha, int chromeAlpha, int headerAlpha, bool sheen)
    {
        _window.Preferences = _window.Preferences with { Theme = theme };
        foreach (var active in new[] { true, false })
        {
            _window.ApplyTheme(false, active);
            Solid("Surface", surface);
            var panel = (SolidColorBrush)Brush("PanelSurface");
            var title = (SolidColorBrush)Brush("TitleBarSurface");
            var panelColor = ColorOf(surface);
            var titleColor = ColorOf(chrome);
            Assert.That(panel.Color, Is.EqualTo(Color.FromArgb((byte)(active ? panelAlpha : 255), panelColor.R, panelColor.G, panelColor.B)));
            Assert.That(title.Color, Is.EqualTo(Color.FromArgb((byte)(active ? chromeAlpha : 255), titleColor.R, titleColor.G, titleColor.B)));
            var headerColor = ColorOf(header);
            if (sheen)
            {
                var reflection = (LinearGradientBrush)Brush("SectionHeaderSurface");
                Assert.That(reflection.GradientStops.Select(stop => stop.Color.A), Is.All.EqualTo((byte)(active ? headerAlpha : 255)));
                Assert.That(reflection.GradientStops[0].Color,
                    Is.EqualTo(Color.FromArgb((byte)(active ? headerAlpha : 255), headerColor.R, headerColor.G, headerColor.B)));
            }
            else Assert.That(((SolidColorBrush)Brush("SectionHeaderSurface")).Color,
                Is.EqualTo(Color.FromArgb((byte)(active ? headerAlpha : 255), headerColor.R, headerColor.G, headerColor.B)));
            Assert.That(_window.BackdropActive, Is.EqualTo(active));
        }
        Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
    }

    [Test]
    public void ReturningFromObservedHighContrastOverwritesSystemBrushesWithTheSelectedNormalRoles()
    {
        _window.Preferences = _window.Preferences with { Theme = "Light", ChartPalette = "Ocean" };
        _window.ApplyTheme(true, false);
        AssertHighContrast();
        _window.ApplyTheme(false, false);
        AssertPlain(true, false);
    }

    [Test]
    public void ResourceNotificationsCanChangeTheLaterPaletteWithoutChangingTheAlreadyChosenPatternFlag()
    {
        var seed = new SolidColorBrush(Colors.Brown);
        Application.Current.Resources["Surface"] = seed;
        var label = _window.CpuMetric.ValueLabel;
        label.SetResourceReference(TextBlock.ForegroundProperty, "Surface");
        Assert.That(label.Foreground, Is.SameAs(seed));
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.ForegroundProperty, typeof(TextBlock))!;
        var notifications = 0;
        EventHandler changed = (_, _) =>
        {
            notifications++;
            _window.Preferences = _window.Preferences with { ChartPalette = "Colorblind" };
        };
        descriptor.AddValueChanged(label, changed);
        try
        {
            _window.Preferences = _window.Preferences with { Theme = "Dark", ChartPalette = "Ocean" };
            _window.ApplyTheme(false, false);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(_window.OverallGraph.UseSeriesPatterns, Is.False);
            Solid("CpuAccent", "#56B4E9");
            Solid("Accent", "#56B4E9");
        }
        finally { descriptor.RemoveValueChanged(label, changed); }
    }

    [Test]
    public void ResourceNotificationsKeepTheCapturedFinishButUseTheLaterStoredThemeForInactiveText()
    {
        Application.Current.Resources["Surface"] = new SolidColorBrush(Colors.Brown);
        var label = _window.CpuMetric.ValueLabel;
        label.SetResourceReference(TextBlock.ForegroundProperty, "Surface");
        var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.ForegroundProperty, typeof(TextBlock))!;
        var notifications = 0;
        EventHandler changed = (_, _) =>
        {
            notifications++;
            _window.Preferences = _window.Preferences with { Theme = "Copper" };
        };
        descriptor.AddValueChanged(label, changed);
        try
        {
            _window.Preferences = _window.Preferences with { Theme = "Crystal" };
            _window.ApplyTheme(false, false);
            Assert.That(notifications, Is.EqualTo(1));
            Solid("Text", "#EDF5FC");
            Solid("InactiveTabText", "#FFF9F4");
            Assert.That(Brush("SectionHeaderSurface"), Is.TypeOf<LinearGradientBrush>());
        }
        finally { descriptor.RemoveValueChanged(label, changed); }
    }

    [Test]
    public void AnExistingProcessRowIsRetainedWhileThemeInvalidationUsesTheCurrentView()
    {
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 20,
            [new ProcessUsage(int.MaxValue, 1, "theme fixture", 20)], 0)
            { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
        _window.Session.Record(sample, sample);
        _window.RefreshVisiblePresentation();
        var rows = _window.Processes.Rows.ToArray();
        Assert.That(rows, Is.Not.Empty);
        Apply("Dark");
        Assert.That(_window.Processes.Rows, Is.EqualTo(rows));
        Apply("Light");
        Assert.That(_window.Processes.Rows, Is.EqualTo(rows));
    }
}
