using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private const string MissingLiveServices = "Live SCM enumeration and process mapping did not produce service readings.";

    [TestCase("missing"), TestCase("unavailable"), TestCase("empty"), TestCase("unmapped")]
    public void ServicesWorkflowDiagnosticRejectsInvalidLiveReadings(string state)
    {
        Directory.CreateDirectory(_directory);
        using var ui = new UpdateUi(_directory, true);
        if (state != "missing") SeedServicesWorkflowCaller(ui.Window, state);
        var windows = Application.Current.Windows.Count;
        var task = new WindowDiagnostics(ui.Window).CaptureServicesDiagnosticsAsync(_directory, CancellationToken.None);
        var error = Assert.Throws<InvalidOperationException>(() =>
            FinishWindowCheck(task));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo(MissingLiveServices));
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
            Assert.That(ui.Window.SamplingTask, Is.Null);
            Assert.That(ui.Window.IsVisible, Is.False);
            Assert.That(Application.Current.Windows.Count, Is.EqualTo(windows));
        });
    }

    [Test]
    public void ServicesWorkflowDiagnosticPreCancellationPreservesCallerAndWritesNothing()
    {
        Directory.CreateDirectory(_directory);
        using var ui = new UpdateUi(_directory, true);
        var sample = SeedServicesWorkflowCaller(ui.Window);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var windows = Application.Current.Windows.Count;
        var tab = ui.Window.ServicesTab.IsChecked;
        var task = new WindowDiagnostics(ui.Window).CaptureServicesDiagnosticsAsync(_directory, cancellation.Token);
        var error = Assert.Catch<OperationCanceledException>(() =>
            FinishWindowCheck(task));
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
            Assert.That(ui.Window.Session.LatestFull, Is.SameAs(sample));
            Assert.That(ui.Window.Session.Services.Frames, Has.Count.EqualTo(1));
            Assert.That(ui.Window.ServicesTab.IsChecked, Is.EqualTo(tab));
            Assert.That(Application.Current.Windows.Count, Is.EqualTo(windows));
            Assert.That(ui.Window.SamplingTask, Is.Null);
        });
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ServicesWorkflowDiagnosticLockedNativeReportPreservesSentinelAndCaller()
    {
        Directory.CreateDirectory(_directory);
        using var ui = new UpdateUi(_directory, true);
        var sample = SeedServicesWorkflowCaller(ui.Window);
        var path = Path.Combine(_directory, "live-services.json");
        File.WriteAllText(path, "owned service report sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var tab = ui.Window.ServicesTab.IsChecked;
        var windows = Application.Current.Windows.Count;
        var task = new WindowDiagnostics(ui.Window).CaptureServicesDiagnosticsAsync(_directory, CancellationToken.None);
        try
        {
            Assert.Throws<IOException>(() => FinishWindowCheck(task));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned service report sentinel"));
                Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName), Is.EqualTo(new[] { "live-services.json" }));
                Assert.That(ui.Window.Session.LatestFull, Is.SameAs(sample));
                Assert.That(ui.Window.ServicesTab.IsChecked, Is.EqualTo(tab));
                Assert.That(Application.Current.Windows.Count, Is.EqualTo(windows));
                Assert.That(ui.Window.SamplingTask, Is.Null);
            });
        }
        finally { PreserveServicesWorkflowArtifacts("locked-native-report"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ServicesWorkflowDiagnosticPreservesReadingsPinsHistoryAndDeferredClose()
    {
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        InstallDesignActionStyles();
        InstallServicesWorkflowTitleStyles();
        var caller = Application.Current.Windows.OfType<MainWindow>().Single();
        SeedServicesWorkflowCaller(caller);
        caller.Left = SystemParameters.WorkArea.Left + 20;
        caller.Top = SystemParameters.WorkArea.Top + 20;
        caller.Show();
        try
        {
            scope.Run(new WindowDiagnostics(caller).CaptureServicesDiagnosticsAsync);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "live-services.json")));
            var fixture = scope.Window!;
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("Services").GetInt32(), Is.GreaterThan(0));
                Assert.That(report.RootElement.GetProperty("MappedHosts").GetInt32(), Is.EqualTo(1));
                Assert.That(report.RootElement.GetProperty("SharedHosts").GetInt32(), Is.Zero);
                Assert.That(report.RootElement.GetProperty("WorkerProcesses").GetInt32(), Is.Zero);
                Assert.That(report.RootElement.GetProperty("ProcessId").GetInt32(), Is.EqualTo(Environment.ProcessId));
                Assert.That(report.RootElement.GetProperty("ParentProcessId").GetInt32(), Is.GreaterThan(0));
                Assert.That(report.RootElement.GetProperty("CreationTicks").GetInt64(), Is.GreaterThan(0));
                Assert.That(report.RootElement.GetProperty("ServiceActionsExecuted").GetBoolean(), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(_directory, "services-copied-capture.txt")), Does.Contain("Build Agent"));
                Assert.That(fixture.Session.History.Samples, Has.Count.EqualTo(122));
                Assert.That(fixture.Session.Services.Frames, Has.Count.EqualTo(120));
                Assert.That(fixture.ServiceView.SavedPins, Is.EquivalentTo(new[] { "BuildAgent", "Spooler" }));
                Assert.That(fixture.Session.History.Samples[^1].Processes.Any(process => process.ProcessId == 9999), Is.True);
                Assert.That(fixture.IsClosing, Is.False);
                Assert.That(fixture.CloseAfterServiceAction, Is.False);
                Assert.That(fixture.IsVisible, Is.False);
                Assert.That(fixture.SamplingTask, Is.Null);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(fixture));
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
                Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo(uiCulture.Name));
                Assert.That(CultureInfo.DefaultThreadCurrentUICulture?.Name, Is.EqualTo("en"));
                Assert.That(Resources.Strings.Culture?.Name, Is.EqualTo("en"));
                Assert.That(caller.ServicesTab.IsChecked, Is.True);
                Assert.That(caller.SamplingTask, Is.Null);
            });
            var images = Directory.GetFiles(_directory, "*.png");
            Assert.That(images.Length, Is.GreaterThanOrEqualTo(22));
            foreach (var path in images)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(frame.PixelWidth, Is.GreaterThan(0));
                Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally
        {
            try { PreserveServicesWorkflowArtifacts("complete"); }
            finally
            {
                CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
                CultureInfo.CurrentUICulture = uiCulture;
            }
        }
    }

    private static MonitorSample SeedServicesWorkflowCaller(MainWindow window, string state = "mapped")
    {
        var host = new ProcessUsage(4242, 1, "owned-service-host", 5);
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 10, [host], 0, MonotonicTimestamp: Stopwatch.Frequency);
        ServiceObservation[] services = [new("OwnedService", "Owned Service", 4, state == "unmapped" ? 4243 : 4242, 1)];
        var frame = new ServiceTree().Capture(sample, new ServiceCatalog(long.MaxValue, services));
        if (state == "unavailable") frame = frame with { Available = false };
        if (state == "empty") frame = frame with { Groups = [] };
        window.Session.Record(sample, sample, services: frame);
        return sample;
    }

    private static void InstallServicesWorkflowTitleStyles()
    {
        var document = XDocument.Load(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Fixtures", "DiagnosticPins", "App.xaml.xml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var styles = document.Root!.Element(presentation + "Application.Resources")!.Elements(presentation + "Style");
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "controls", "clr-namespace:WinMon.Controls;assembly=WinMon"),
            styles.Where(style => (string?)style.Attribute(xaml + "Key") is "TitleActionButton" or "AlwaysOnTopButton" ||
                                  (style.Attribute(xaml + "Key") is null && (string?)style.Attribute("TargetType") == "Button"))
                .Select(style => new XElement(style)));
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        foreach (var key in new[] { "TitleActionButton", "AlwaysOnTopButton" })
            Application.Current.Resources[key] = resources[key];
    }

    private void PreserveServicesWorkflowArtifacts(string scenario)
    {
        if (TestContext.Parameters.Get("ServicesWorkflowArtifactDirectory") is not { Length: > 0 } artifacts) return;
        artifacts = Path.Combine(artifacts, scenario);
        Directory.CreateDirectory(artifacts);
        foreach (var path in Directory.GetFiles(_directory))
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
    }
}
