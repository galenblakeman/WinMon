using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;
using Forms = System.Windows.Forms;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class TrayStatusCharacterizationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(1);
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;
    private Forms.NotifyIcon _tray = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); _addedResources.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); _addedResources.Add(typeof(Button)); }
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _tray = new Forms.NotifyIcon { Visible = false };
        _window.Tray = _tray;
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            _window.Tray = null;
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        _tray?.Dispose();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static MonitorSample Sample(double? cpu, int second = 0) => new(Start.AddSeconds(second), cpu, [], 0);
    private string Text => _tray.Text;

    [Test]
    public void MissingTrayLeavesTheDetachedIconAloneAndRefreshStillWorksAfterReattachment()
    {
        _window.Tray = null;
        _window.Session.Record(Sample(12.5));
        Assert.DoesNotThrow(_window.RefreshPresentation);
        Assert.That(Text, Is.Empty);
        _window.Tray = _tray;
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, "12.5% CPU")));
        Assert.That(_tray.Visible, Is.False);
    }

    [TestCase(0, "0.0% CPU")]
    [TestCase(12.5, "12.5% CPU")]
    [TestCase(100, "100.0% CPU")]
    [TestCase(-1, "-1.0% CPU")]
    [TestCase(125, "125.0% CPU")]
    public void HealthyCpuValuesKeepOneDecimalAndTheExistingUnclampedPolicy(double cpu, string expected)
    {
        _window.Session.Record(Sample(cpu));
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, expected)));
        Assert.That(_tray.Visible, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingCpuUsesTheMissingReadingAndCpuSuffix(bool recorded)
    {
        if (recorded) _window.Session.Record(Sample(null));
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, Strings.MissingReading + Strings.CpuTraySuffix)));
    }

    [TestCase(SamplingError.CpuUnavailable)]
    [TestCase(SamplingError.ReadingsUnavailable)]
    [TestCase(SamplingError.CpuUnsupported)]
    [TestCase(SamplingError.ProcessesUnavailable)]
    public void EverySamplingErrorKeepsTheExistingUnavailableTextEvenWithACpuValue(SamplingError error)
    {
        _window.Session.Record(Sample(42.5));
        _window.ShownSamplingError = error;
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, Strings.Unavailable)));
    }

    [Test]
    public void ClearingTheSamplingErrorRestoresTheLatestNumericText()
    {
        _window.Session.Record(Sample(42.5));
        _window.ShownSamplingError = SamplingError.CpuUnavailable;
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, Strings.Unavailable)));
        _window.ShownSamplingError = null;
        _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, "42.5% CPU")));
    }

    [TestCase("fr-FR", "12,5% CPU")]
    [TestCase("de-DE", "12,5% CPU")]
    public void NumericFormattingUsesTheCurrentCultureWithTheExistingUiLanguage(string culture, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            _window.Session.Record(Sample(12.5));
            _window.RefreshPresentation();
            Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, expected)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TrayStatusStaysLiveWhileTheVisiblePresentationInspectsAnOlderSample(bool visibleRefresh)
    {
        var past = Sample(10);
        var latest = Sample(70, 1);
        _window.Session.Record(past);
        _window.Session.Record(latest);
        _window.SelectedSample = past;
        if (visibleRefresh) _window.RefreshVisiblePresentation();
        else _window.RefreshPresentation();
        Assert.That(Text, Is.EqualTo(Strings.Format(Strings.TrayStatus, "70.0% CPU")));
        Assert.That(_window.SelectedSample, Is.SameAs(past));
        Assert.That(_tray.Visible, Is.False);
        Assert.That(_window.IsVisible, Is.False);
    }
}
