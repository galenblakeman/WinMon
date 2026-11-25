using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using Forms = System.Windows.Forms;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class CaptureNotificationGuardCharacterizationTests
{
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;
    private Forms.NotifyIcon _tray = null!;
    private Action<MainWindow.CaptureMarker, string> _notify = null!;
    private Func<MainWindow.CaptureMarker, bool> _openReport = null!;
    private string _root = null!;

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
        _notify = Method("NotifyCapture").CreateDelegate<Action<MainWindow.CaptureMarker, string>>(_window);
        _openReport = Method("OpenCaptureReport").CreateDelegate<Func<MainWindow.CaptureMarker, bool>>(_window);
        _root = Path.Combine(Path.GetTempPath(), "WinMon.CaptureNotification." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "marker.txt"), "owned fixture baseline");
    }

    [TearDown]
    public void TearDown()
    {
        _window.Tray = null;
        foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        _window.CloseForFixture();
        _tray.Dispose();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
        if (_root is not null)
        {
            Assert.That(Path.GetFullPath(_root), Does.StartWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinMon.CaptureNotification."))));
            Directory.Delete(_root, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingTrayDoesNotConsumeOrReplaceANotification(bool futureTimestamp)
    {
        _window.Tray = null;
        var previous = Marker(0);
        var timestamp = futureTimestamp ? DateTime.MaxValue : DateTime.MinValue;
        Set("_notifiedMarker", previous);
        Set("_lastCaptureNotification", timestamp);
        Assert.DoesNotThrow(() => _notify(Marker(1), ""));
        Assert.That(Get<MainWindow.CaptureMarker>("_notifiedMarker"), Is.SameAs(previous));
        Assert.That(Get<DateTime>("_lastCaptureNotification"), Is.EqualTo(timestamp));
        Assert.That(Get<bool>("_balloonHooked"), Is.False);
        AssertHidden();
    }

    [Test]
    public void FutureTimestampSuppressesNotificationWithoutReplacingThePreviousTarget()
    {
        var previous = Marker(0);
        Set("_notifiedMarker", previous);
        Set("_lastCaptureNotification", DateTime.MaxValue);
        Assert.DoesNotThrow(() => _notify(Marker(1), ""));
        Assert.That(Get<MainWindow.CaptureMarker>("_notifiedMarker"), Is.SameAs(previous));
        Assert.That(Get<DateTime>("_lastCaptureNotification"), Is.EqualTo(DateTime.MaxValue));
        Assert.That(Get<bool>("_balloonHooked"), Is.False);
        AssertHidden();
    }

    [TestCase("plain")]
    [TestCase("multiline")]
    public void HiddenTrayAcceptsValidNotificationsAndRetainsTheirTarget(string partition)
    {
        var marker = Marker(1);
        _notify(marker, partition == "plain" ? "fixture notification" : "first line\nsecond line");
        Assert.That(Get<MainWindow.CaptureMarker>("_notifiedMarker"), Is.SameAs(marker));
        Assert.That(Get<DateTime>("_lastCaptureNotification"), Is.GreaterThan(DateTime.MinValue));
        Assert.That(Get<bool>("_balloonHooked"), Is.True);
        AssertHidden();
    }

    [Test]
    public void LaterEligibleNotificationReplacesTheClickTargetWithTheHookAlreadyInstalled()
    {
        var first = Marker(1);
        var next = Marker(2);
        _notify(first, "first notification");
        Assert.That(Get<bool>("_balloonHooked"), Is.True);
        Set("_lastCaptureNotification", DateTime.MinValue);
        _notify(next, "next notification");
        Assert.That(Get<MainWindow.CaptureMarker>("_notifiedMarker"), Is.SameAs(next));
        Assert.That(Get<DateTime>("_lastCaptureNotification"), Is.GreaterThan(DateTime.MinValue));
        Assert.That(Get<bool>("_balloonHooked"), Is.True);
        AssertHidden();
    }

    [TestCase(null)]
    [TestCase("")]
    public void InvalidBodyPropagatesTheExistingExceptionAfterRecordingTheAttempt(string? body)
    {
        var marker = Marker(1);
        var error = Assert.Throws<ArgumentException>(() => _notify(marker, body!));
        Assert.That(error!.ParamName, Is.Null);
        Assert.That(Get<MainWindow.CaptureMarker>("_notifiedMarker"), Is.SameAs(marker));
        Assert.That(Get<DateTime>("_lastCaptureNotification"), Is.GreaterThan(DateTime.MinValue));
        Assert.That(Get<bool>("_balloonHooked"), Is.True);
        AssertHidden();
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("whitespace")]
    [TestCase("missing")]
    [TestCase("directory")]
    [TestCase("invalid")]
    public void UnavailableReportPathsReturnFalseWithoutChangingTheCaptureOrOwnedFiles(string partition)
    {
        var marker = Marker(1);
        marker.ReportPath = partition switch
        {
            "null" => null,
            "empty" => "",
            "whitespace" => " ",
            "missing" => Path.Combine(_root, "missing-report.html"),
            "directory" => _root,
            "invalid" => Path.Combine(_root, "invalid\0.html"),
            _ => throw new ArgumentOutOfRangeException(nameof(partition))
        };
        var path = marker.ReportPath;
        Assert.That(_openReport(marker), Is.False);
        Assert.That(marker.ReportPath, Is.EqualTo(path));
        Assert.That(marker.WriteFailed, Is.False);
        Assert.That(Directory.GetFileSystemEntries(_root).Select(Path.GetFileName), Is.EqualTo(new[] { "marker.txt" }));
        Assert.That(File.ReadAllText(Path.Combine(_root, "marker.txt")), Is.EqualTo("owned fixture baseline"));
        AssertHidden();
    }

    private void AssertHidden()
    {
        Assert.That(_tray.Visible, Is.False);
        Assert.That(_window.IsVisible, Is.False);
        Assert.That(_window.SamplingTask, Is.Null);
    }

    private static MainWindow.CaptureMarker Marker(int second)
    {
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 97, [], 0);
        return new(new CaptureFired(new CaptureRuleSetting(CaptureMetric.Cpu, true, 90, 1), sample, sample, 97, [97]),
            new Guid(second, 0, 0, new byte[8]), "fixture rule " + second);
    }

    private static MethodInfo Method(string name) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private T Get<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_window)!;
    private void Set<T>(string name, T value) => typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_window, value);
}
