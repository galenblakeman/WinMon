using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class TitlePinCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private CultureInfo? _resourceCulture;
    private CultureInfo? _defaultUiCulture;
    private CultureInfo _uiCulture = null!;
    private CultureInfo _formats = null!;

    [SetUp]
    public void SetUp()
    {
        _resourceCulture = Strings.Culture;
        _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _formats = CultureInfo.CurrentCulture;
        Strings.SetCulture(CultureInfo.GetCultureInfo("en"));
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("IdleSurface")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("IdleBorder")));
        _window.PinButton.Style = style;
        SetPalette(false);
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
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
        CultureInfo.CurrentCulture = _formats;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        Strings.SetCulture(_resourceCulture);
    }

    [TestCase("en", false), TestCase("en", true), TestCase("ja", false), TestCase("ja", true)]
    public void RoutedClickTogglesTopmostAndPublishesLocalizedFeedbackBeforeObservers(string language, bool initiallyPinned)
    {
        AppLanguages.Apply(language);
        _window.Topmost = initiallyPinned;
        var preferences = _window.Preferences;
        var contentBinding = _window.PinButton.GetBindingExpression(ContentControl.ContentProperty);
        var observed = new List<bool>();
        _window.PinButton.Click += (_, _) =>
        {
            observed.Add(_window.Topmost);
            AssertFeedback(_window.Topmost);
        };
        _window.PinButton.Background = Brushes.Red;
        _window.PinButton.BorderBrush = Brushes.Red;
        _window.PinButton.Foreground = Brushes.Red;

        Click();
        Assert.That(_window.Topmost, Is.EqualTo(!initiallyPinned));
        Click();

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.EqualTo(new[] { !initiallyPinned, initiallyPinned }));
            Assert.That(_window.Topmost, Is.EqualTo(initiallyPinned));
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(contentBinding, Is.Not.Null);
            Assert.That(_window.PinButton.GetBindingExpression(ContentControl.ContentProperty), Is.SameAs(contentBinding));
            Assert.That(_window.IsVisible, Is.False, "The fixture does not change native window stacking.");
        });
    }

    [TestCase(false), TestCase(true)]
    public void ThemeResourceReplacementKeepsPinnedColorsAndUnpinnedStyleInheritanceLive(bool pinned)
    {
        _window.Topmost = !pinned;
        Click();
        AssertFeedback(pinned);

        SetPalette(true);

        AssertFeedback(pinned);
        if (!pinned)
        {
            Assert.Multiple(() =>
            {
                Assert.That(_window.PinButton.ReadLocalValue(Control.BackgroundProperty), Is.SameAs(DependencyProperty.UnsetValue));
                Assert.That(_window.PinButton.ReadLocalValue(Control.BorderBrushProperty), Is.SameAs(DependencyProperty.UnsetValue));
            });
        }
        Click();
        AssertFeedback(!pinned);
    }

    [TestCase(false), TestCase(true)]
    public void ApplyingLanguageRefreshesPinLabelsWithoutTogglingOrReplacingACaptureNotice(bool pinned)
    {
        _window.Topmost = !pinned;
        Click();
        var oldName = AutomationProperties.GetName(_window.PinButton);
        _window.ShowUpdateStatus("owned capture notice", true);
        _window.Preferences = _window.Preferences with { Language = "ja" };

        _window.ApplyLanguage();

        Assert.Multiple(() =>
        {
            Assert.That(_window.Topmost, Is.EqualTo(pinned));
            Assert.That(AutomationProperties.GetName(_window.PinButton), Is.Not.EqualTo(oldName));
            Assert.That(_window.UpdateStatusText.Text, Is.EqualTo("owned capture notice"));
            Assert.That(_window.UpdateFooter.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(_window.UpdateNowButton.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(_window.IsVisible, Is.False);
        });
        AssertFeedback(pinned);
    }

    private void Click() => _window.PinButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private void SetPalette(bool replacement)
    {
        var colors = replacement
            ? new[] { Colors.DarkGreen, Colors.Yellow, Colors.MidnightBlue, Colors.Magenta, Colors.Wheat, Colors.Teal }
            : new[] { Colors.Black, Colors.White, Colors.DarkBlue, Colors.Orange, Colors.SlateGray, Colors.DimGray };
        var keys = new[] { "Text", "SelectedText", "SelectedSurface", "Accent", "IdleSurface", "IdleBorder" };
        for (var index = 0; index < keys.Length; index++)
            _window.PinButton.Resources[keys[index]] = new SolidColorBrush(colors[index]);
    }

    private void AssertFeedback(bool pinned)
    {
        var button = _window.PinButton;
        Color Expected(string key) => ((SolidColorBrush)button.Resources[key]).Color;
        Assert.Multiple(() =>
        {
            Assert.That(((SolidColorBrush)button.Foreground).Color, Is.EqualTo(Expected(pinned ? "SelectedText" : "Text")));
            Assert.That(((SolidColorBrush)button.Background).Color, Is.EqualTo(Expected(pinned ? "SelectedSurface" : "IdleSurface")));
            Assert.That(((SolidColorBrush)button.BorderBrush).Color, Is.EqualTo(Expected(pinned ? "Accent" : "IdleBorder")));
            Assert.That(button.ToolTip, Is.EqualTo(pinned ? Strings.KeptOnTopOfOtherWindowsClickToStop : Strings.KeepThisWindowOnTopOfOtherWindows));
            Assert.That(AutomationProperties.GetName(button), Is.EqualTo(pinned ? Strings.KeepOnTopOn : Strings.KeepOnTopOff));
        });
    }
}
