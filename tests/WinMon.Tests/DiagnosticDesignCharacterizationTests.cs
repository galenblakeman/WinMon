using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void DesignDiagnosticPreservesChartsReportsSelectionsAndOriginalWindowLifetime()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        InstallDesignActionStyles();
        try
        {
            scope.Run(WindowDiagnostics.CaptureDesignFixtureAsync);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "snapshot.json")));
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("Corners").GetInt32(), Is.EqualTo(12));
                foreach (var flag in new[] { "ResizedWhileSelected", "ShortChart", "LightAndDark", "Expiry",
                             "ReturnToLive", "CenteredChartInvestigationActions", "LatestSampleAction", "SnapshotCopy" })
                    Assert.That(report.RootElement.GetProperty(flag).GetBoolean(), Is.True, flag);
                Assert.That(report.RootElement.GetProperty("RenderScales").EnumerateArray().Select(value => value.GetDouble()),
                    Is.EqualTo(new[] { 1d, 1.5, 2d }));
                Assert.That(File.ReadAllText(Path.Combine(_directory, "clipboard.txt")), Does.Contain("process-01"));
                Assert.That(scope.Window!.Session.History.Samples, Has.Count.EqualTo(1));
                Assert.That(scope.Window.SelectedSample, Is.Null);
                Assert.That(scope.Window.SnapshotLayer.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(scope.Window.SnapshotCopyButton.IsEnabled, Is.False);
            });
            AssertDesignFixtureLifetime(scope);
            var names = Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName).ToArray();
            foreach (var chart in Enum.GetValues<ResourceChartType>().Where(type => type != ResourceChartType.Temperature))
                Assert.That(names, Does.Contain($"chart-design-{chart.ToString().ToLowerInvariant()}.png"));
#if WINMON_STORE
            Assert.That(names, Does.Not.Contain("chart-design-temperature.png"));
#else
            Assert.That(names, Does.Contain("chart-design-temperature.png"));
#endif
            foreach (var name in new[] { "apps-only-grouped-history.png", "chart-design-focus-second-disk.png",
                         "disks-settings-dark.png", "disks-settings-light.png", "gpu-process-sort.png",
                         "memory-process-sort.png", "grouped.png", "snapshot-short.png", "glacier-light.png" })
                Assert.That(names, Does.Contain(name));
            foreach (var width in new[] { 905, 720, 600 })
            {
                Assert.That(names, Does.Contain($"chart-mode-live-{width}.png"));
                Assert.That(names, Does.Contain($"chart-mode-snapshot-{width}.png"));
                foreach (var corner in new[] { "top-left", "top-right", "bottom-left", "bottom-right" })
                    Assert.That(names, Does.Contain($"snapshot-{width}-{corner}.png"));
            }
            foreach (var path in Directory.GetFiles(_directory, "*.png"))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(frame.PixelWidth, Is.GreaterThan(0));
                Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally { PreserveDesignArtifacts("complete"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void DesignDiagnosticPreCancellationPreservesSeededDataAndOriginalWindowLifetime()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        InstallDesignActionStyles();
        scope.Cancel();
        try
        {
            var exception = Assert.Catch<OperationCanceledException>(() => scope.Run(WindowDiagnostics.CaptureDesignFixtureAsync));
            Assert.That(exception!.CancellationToken, Is.EqualTo(CancellationToken.None));
            Assert.That(scope.Token.IsCancellationRequested, Is.True);
            AssertDesignFixtureFirstCaptureState(scope);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        }
        finally { PreserveDesignArtifacts("pre-cancel"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void DesignDiagnosticLockedFirstCapturePreservesSentinelAndOriginalWindowLifetime()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        InstallDesignActionStyles();
        var path = Path.Combine(_directory, "chart-design-overlay.png");
        File.WriteAllText(path, "owned design sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.Throws<IOException>(() => scope.Run(WindowDiagnostics.CaptureDesignFixtureAsync));
            AssertDesignFixtureFirstCaptureState(scope);
            Assert.That(File.ReadAllText(path), Is.EqualTo("owned design sentinel"));
            Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName), Is.EqualTo(new[] { "chart-design-overlay.png" }));
        }
        finally { PreserveDesignArtifacts("locked-first-capture"); }
    }

    private static void AssertDesignFixtureFirstCaptureState(AutoCaptureDiagnosticScope scope)
    {
        AssertDesignFixtureLifetime(scope);
        var window = scope.Window!;
        Assert.Multiple(() =>
        {
            Assert.That(window.Session.History.Samples, Has.Count.EqualTo(120));
            Assert.That(window.Session.History.Samples.All(sample => sample.Processes.Count == 7), Is.True);
            Assert.That(window.Session.History.Samples.All(sample => sample.LogicalProcessorPercents?.Count == 16), Is.True);
            Assert.That(window.Preferences.ResourceChartType, Is.EqualTo(ResourceChartType.Overlay));
            Assert.That(window.SelectedSample, Is.Null);
            Assert.That(window.Processes.Rows.Any(row => row.Visibility == Visibility.Visible), Is.True);
        });
    }

    private static void InstallDesignActionStyles()
    {
        var document = XDocument.Load(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Fixtures", "DiagnosticPins", "App.xaml.xml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var styles = document.Root!.Element(presentation + "Application.Resources")!.Elements(presentation + "Style");
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "controls", "clr-namespace:WinMon.Controls;assembly=WinMon"),
            styles.Where(style => (string?)style.Attribute(xaml + "Key") is "ActionBarButton" or "ActionBarLastButton" ||
                                  (style.Attribute(xaml + "Key") is null && (string?)style.Attribute("TargetType") == "Button"))
                .Select(style => new XElement(style)));
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        foreach (var key in new object[] { typeof(Button), "ActionBarButton", "ActionBarLastButton" })
            Application.Current.Resources[key] = resources[key];
    }

    private static void AssertDesignFixtureLifetime(AutoCaptureDiagnosticScope scope)
    {
        Assert.Multiple(() =>
        {
            Assert.That(scope.Window, Is.Not.Null);
            Assert.That(scope.Window!.IsVisible, Is.False);
            Assert.That(scope.Window.SamplingTask, Is.Null);
            // Characterize the source lifetime; the test scope closes its owned windows afterwards.
            Assert.That(Application.Current.Windows.Cast<Window>(), Does.Contain(scope.Window));
            Assert.That(scope.DispatcherError, Is.Null);
            Assert.That(scope.TimedOut, Is.False);
        });
    }

    private void PreserveDesignArtifacts(string scenario)
    {
        if (TestContext.Parameters.Get("DesignArtifactDirectory") is not { Length: > 0 } artifacts) return;
        artifacts = Path.Combine(artifacts, scenario);
        Directory.CreateDirectory(artifacts);
        foreach (var path in Directory.GetFiles(_directory))
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
    }
}
