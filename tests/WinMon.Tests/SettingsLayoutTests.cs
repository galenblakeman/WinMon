using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SettingsLayoutTests
{
    [Test]
    public void Sections_OpenOnGeneralAndFollowTheSidebar()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            var opened = window.Section;
            SettingsSection[] expected = UpdatePolicy.GitHubUpdatesEnabled
                ? [SettingsSection.General, SettingsSection.Appearance, SettingsSection.Monitoring,
                    SettingsSection.AutomaticCaptures,
                    SettingsSection.Updates]
                : [SettingsSection.General, SettingsSection.Appearance, SettingsSection.Monitoring,
                    SettingsSection.AutomaticCaptures];
            var shown = expected.Select(section =>
            {
                window.ShowSection(section);
                return window.Section;
            }).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.EqualTo(SettingsSection.General));
                Assert.That(shown, Is.EqualTo(expected));
                Assert.That(window.Height, Is.LessThanOrEqualTo(600), "Every section fits without growing the dialog.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void Appearance_ContainsThemePaletteAndGaugeWhileGeneralKeepsLanguage()
    {
        var window = Settings(new MonitorPreferences
            { Theme = "Porcelain", ChartPalette = "Ocean", GaugeStyle = GaugeStyle.Needle });
        try
        {
            var general = Descendants(window).ToArray();
            window.ShowSection(SettingsSection.Appearance);
            var appearance = Descendants(window).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(general, Does.Contain(window.LanguageSelector));
                Assert.That(general, Does.Not.Contain(window.ThemeSelector));
                Assert.That(general, Does.Not.Contain(window.PaletteSelector));
                Assert.That(general, Does.Not.Contain(window.GaugeSelector));
                Assert.That(appearance, Does.Not.Contain(window.LanguageSelector));
                Assert.That(appearance, Does.Contain(window.ThemeSelector));
                Assert.That(appearance, Does.Contain(window.PaletteSelector));
                Assert.That(appearance, Does.Contain(window.GaugeSelector));
                Assert.That(window.ThemeSelector.SelectedValue, Is.EqualTo("Porcelain"));
                Assert.That(window.PaletteSelector.SelectedValue, Is.EqualTo("Ocean"));
                Assert.That(window.GaugeSelector.SelectedValue, Is.EqualTo(GaugeStyle.Needle));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ThemeSelector_ListsBuiltInModesBeforeAlphabeticalLocalizedFinishes()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            var choices = window.ThemeSelector.Items.Cast<object>().Select(choice =>
            {
                var type = choice.GetType();
                return (Name: (string)type.GetProperty("Name")!.GetValue(choice)!,
                    Label: (string)type.GetProperty("Label")!.GetValue(choice)!);
            }).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(choices.Take(3).Select(choice => choice.Name),
                    Is.EqualTo(new[] { "System", "Light", "Dark" }));
                Assert.That(choices.Skip(3).Select(choice => choice.Name),
                    Is.EquivalentTo(ThemeFinishes.Names));
                Assert.That(choices.Skip(3).Select(choice => choice.Label),
                    Is.EqualTo(choices.Skip(3).Select(choice => choice.Label)
                        .OrderBy(label => label, StringComparer.CurrentCultureIgnoreCase)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void GaugeSelector_OpensOnTheSavedStyleAndPreviewsEachChoice()
    {
        var window = Settings(new MonitorPreferences { GaugeStyle = GaugeStyle.SegmentedArc });
        try
        {
            var opened = (window.GaugeSelector.SelectedValue, window.GaugePreview.GaugeStyle);
            var choices = window.GaugeSelector.Items.Count;
            window.GaugeSelector.SelectedValue = GaugeStyle.None;

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.EqualTo(((object)GaugeStyle.SegmentedArc, GaugeStyle.SegmentedArc)));
                Assert.That(window.GaugeSelector.Items[0].ToString(), Is.EqualTo(Strings.GaugeBowl),
                    "The default comes first.");
                Assert.That(choices, Is.EqualTo(Enum.GetValues<GaugeStyle>().Length));
                Assert.That(window.GaugePreview.GaugeStyle, Is.EqualTo(GaugeStyle.None));
                Assert.That(window.GaugePreview.IsHitTestVisible, Is.False);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void AppearancePreview_CombinesUnsavedThemePaletteAndGaugeChoices()
    {
        var window = Settings(new MonitorPreferences
            { Theme = "Porcelain", ChartPalette = "Ocean", GaugeStyle = GaugeStyle.Needle });
        try
        {
            var preview = window.CombinedPreview;
            var appMemoryBrush = Application.Current?.Resources["MemoryAccent"];
            Assert.That(preview.GaugePreview.GaugeStyle, Is.EqualTo(GaugeStyle.Needle));
            Assert.That(preview.ChartPreview.ShowGrid, Is.True);
            if (!SystemParameters.HighContrast)
            {
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("PlotSurface")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString("#FFFEFA")));
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("MemoryAccent")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString("#6745A5")));
            }

            window.ThemeSelector.SelectedValue = "Gunmetal";
            window.PaletteSelector.SelectedValue = "Classic";
            window.GaugeSelector.SelectedValue = GaugeStyle.BottomBar;

            Assert.Multiple(() =>
            {
                Assert.That(preview.GaugePreview.GaugeStyle, Is.EqualTo(GaugeStyle.BottomBar));
                if (Application.Current is not null)
                    Assert.That(Application.Current.Resources["MemoryAccent"], Is.SameAs(appMemoryBrush),
                        "The unsaved preview must not recolor the main window.");
                if (!SystemParameters.HighContrast)
                {
                    Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("PlotSurface")!).Color,
                        Is.EqualTo((Color)ColorConverter.ConvertFromString("#151D26")));
                    Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("MemoryAccent")!).Color,
                        Is.EqualTo((Color)ColorConverter.ConvertFromString("#4285FF")));
                }
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("Porcelain", "#FFFEFA", "#365F85")]
    [TestCase("MidnightInk", "#0D1724", "#477DAE")]
    [TestCase("EvergreenInstrument", "#111E1B", "#87B782")]
    [TestCase("FrostedPorcelain", "#FFFFFF", "#38698E")]
    [TestCase("SmokedSapphire", "#0B1930", "#5C9BE0")]
    [TestCase("Crystal", "#121B27", "#A5D0F2")]
    [TestCase("Slate", "#111A23", "#B6CAD7")]
    [TestCase("Prism", "#101624", "#8DCAFF")]
    [TestCase("Candy", "#FFFCF9", "#BF155D")]
    [TestCase("Wildberry", "#1D1424", "#E36B9D")]
    public void NewThemePreview_UsesFlatSurfaceAndIndependentUiAccent(string theme, string plot, string accent)
    {
        var window = Settings(new MonitorPreferences { Theme = theme, ChartPalette = "Ocean" });
        try
        {
            var preview = window.CombinedPreview;
            Assert.That(window.ThemeSelector.SelectedValue, Is.EqualTo(theme));
            if (SystemParameters.HighContrast) return;
            Assert.Multiple(() =>
            {
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("PlotSurface")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString(plot)));
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("Accent")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString(accent)));
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("MemoryAccent")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString(theme is "Porcelain" or "FrostedPorcelain" or "Candy"
                        ? "#6745A5" : "#B0A0FF")));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("FrostedPorcelain", 226)]
    [TestCase("SmokedSapphire", 228)]
    [TestCase("Crystal", 240)]
    [TestCase("Slate", 210)]
    [TestCase("Prism", 238)]
    public void TranslucentThemePreview_KeepsThePlotOpaque(string theme, byte panelOpacity)
    {
        var window = Settings(new MonitorPreferences { Theme = theme });
        try
        {
            if (SystemParameters.HighContrast) return;
            var preview = window.CombinedPreview;
            Assert.Multiple(() =>
            {
                Assert.That(((SolidColorBrush)preview.TryFindResource("PanelSurface")!).Color.A,
                    Is.EqualTo(panelOpacity));
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("PlotSurface")!).Color.A,
                    Is.EqualTo(byte.MaxValue));
                Assert.That(((SolidColorBrush)preview.ChartPreview.TryFindResource("MemoryAccent")!).Color,
                    Is.EqualTo((Color)ColorConverter.ConvertFromString(theme == "FrostedPorcelain"
                        ? "#1469B5" : "#4285FF")));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase("Crystal", true)]
    [TestCase("Slate", false)]
    [TestCase("Prism", true)]
    public void GlassVariantPreview_UsesTheSelectedHeaderTreatment(string theme, bool sheen)
    {
        var window = Settings(new MonitorPreferences { Theme = theme });
        try
        {
            if (SystemParameters.HighContrast) return;
            var header = window.CombinedPreview.TryFindResource("SectionHeaderSurface");
            Assert.That(header, sheen ? Is.TypeOf<LinearGradientBrush>() : Is.TypeOf<SolidColorBrush>());
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CaptureSwitch_ShowsItsStateAndIsWhatSaveReads()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            var section = window.AutoCapture;
            var off = (section.EnabledSwitch.Content, section.Read(out _, out _)!.Enabled);
            section.EnabledSwitch.IsChecked = true;
            var on = (section.EnabledSwitch.Content, section.Read(out _, out _)!.Enabled);

            Assert.Multiple(() =>
            {
                Assert.That(off, Is.EqualTo((Strings.SettingsOff, false)));
                Assert.That(on, Is.EqualTo((Strings.SettingsOn, true)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CaptureRead_NamesTheFieldToFix()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            window.ShowSection(SettingsSection.AutomaticCaptures);
            var box = Descendants(window.AutoCapture).OfType<TextBox>().First(b => b.Text == "90");
            box.Text = "250";

            var result = window.AutoCapture.Read(out var error, out var invalid);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(error, Is.Not.Null);
                Assert.That(invalid, Is.SameAs(box));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SecondDiskRule_IsUnavailableWithoutASecondDrive()
    {
        var window = Settings(new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = null });
        try
        {
            var rows = Descendants(window.AutoCapture).OfType<CheckBox>()
                .Where(box => box.Content is StackPanel).ToArray();
            var second = rows[CaptureRuleSetting.Defaults.ToList().FindIndex(r => r.Metric == CaptureMetric.SecondaryDisk)];
            var disabled = second.IsEnabled;
            window.SecondaryDriveSelector.ItemsSource = new[] { new SettingsWindow.DriveChoice("D:", "D:") };
            window.SecondaryDriveSelector.SelectedIndex = 0;

            Assert.Multiple(() =>
            {
                Assert.That(disabled, Is.False);
                Assert.That(second.IsEnabled, Is.True, "Choosing a second drive enables its rule.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static SettingsWindow Settings(MonitorPreferences preferences)
    {
        return new SettingsWindow(preferences, () => [], checkForUpdates: () => Task.FromResult("checked"),
            captureFolder: Path.Combine(Path.GetTempPath(), "WinMon.SettingsLayoutTests"));
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent)
                     .OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
