using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void PinsFixtureDiagnosticPreservesPinFilteringGroupingSnapshotDividerAndRowUnpinContracts()
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        FinishWindowCheck(WindowDiagnostics.CapturePinsFixtureAsync(_directory, default));
        var window = fixture.CreatedWindow();
        Assert.That(window.IsVisible, Is.False);
        Assert.That(window.Session.History.Samples, Has.Count.EqualTo(120));
        Assert.That(window.Session.History.Samples.All(sample => sample.Processes.Count == 6), Is.True);
        Assert.That(window.Session.HasLiveSample, Is.False);
        Assert.That(window.SamplingTask, Is.Null);
        Assert.That(window.SnapshotCopiedTimer.IsEnabled, Is.False);
        Assert.That(window.Processes.Pins.Count(false), Is.EqualTo(2));
        Assert.That(window.Processes.Pins.Count(true), Is.EqualTo(1));
        Assert.That(window.Processes.PinnedRows.Count(row => row.Visibility == Visibility.Visible), Is.EqualTo(2));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "pins.json")));
        foreach (var name in new[] { "StableOrder", "Deduplicated", "Exited", "PinnedOnly", "Search", "AppsOnly",
                     "GroupedPins", "SnapshotCopy", "RowButtonUnpin" })
            Assert.That(report.RootElement.GetProperty(name).GetBoolean(), Is.True, name);
        Assert.That(report.RootElement.GetProperty("Themes").EnumerateArray().Select(value => value.GetString()),
            Is.EqualTo(new[] { "Dark", "Light" }));
        var images = Directory.GetFiles(_directory, "*.png");
        Assert.That(images, Has.Length.EqualTo(7));
        foreach (var path in images)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            if (TestContext.Parameters.Get("PinsDiagnosticArtifactDirectory") is { Length: > 0 } artifacts)
            {
                Directory.CreateDirectory(artifacts);
                stream.Position = 0;
                using var copy = File.Create(Path.Combine(artifacts, Path.GetFileName(path)));
                stream.CopyTo(copy);
            }
        }
    }

    [Test, Category("Integration")]
    public void PinsFixtureDiagnosticPreCancellationLeavesItsOriginalUnshownFixtureAndNoOutput()
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(WindowDiagnostics.CapturePinsFixtureAsync(_directory, cancellation.Token)));
        var window = fixture.CreatedWindow();
        Assert.That(window.IsVisible, Is.False);
        Assert.That(window.Session.History.Samples, Has.Count.EqualTo(120));
        Assert.That(window.Processes.Pins.Count(false), Is.EqualTo(3));
        Assert.That(window.SnapshotCopiedTimer.IsEnabled, Is.False);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test, Category("Integration")]
    public void PinsFixtureDiagnosticCancellationAfterSnapshotCopyPreservesReturnToLiveAndTheUnshownFixture()
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        using var cancellation = new CancellationTokenSource();
        var capture = WindowDiagnostics.CapturePinsFixtureAsync(_directory, cancellation.Token);
        var window = fixture.CreatedWindow();
        var signalled = false;
        DispatcherHookEventHandler posted = (_, args) =>
        {
            if (!signalled && args.Operation.Priority == DispatcherPriority.Render && window.Preferences.Theme == "Light")
            { signalled = true; cancellation.Cancel(); }
        };
        window.Dispatcher.Hooks.OperationPosted += posted;
        try
        {
            Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(capture));
            Assert.That(signalled, Is.True);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.SnapshotCopiedTimer.IsEnabled, Is.False);
            Assert.That(window.SelectedSample, Is.Null);
            Assert.That(window.SnapshotReport, Is.Null);
            Assert.That(window.Session.HasLiveSample, Is.False);
            Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(3));
            Assert.That(File.Exists(Path.Combine(_directory, "pins.json")), Is.False);
        }
        finally
        {
            window.Dispatcher.Hooks.OperationPosted -= posted;
            cancellation.Cancel();
            if (!capture.IsCompleted) FinishWindowCheck(capture.ContinueWith(_ => { }, TaskScheduler.Default));
            _ = capture.Exception;
        }
    }

    [Test, Category("Integration")]
    public void PinsFixtureDiagnosticNullOutputRetainsArgumentFailureAndTheUnshownFixture()
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        var error = Assert.Throws<ArgumentNullException>(() => FinishWindowCheck(WindowDiagnostics.CapturePinsFixtureAsync(null!, default)));
        Assert.That(error!.ParamName, Is.EqualTo("path1"));
        Assert.That(fixture.CreatedWindow().IsVisible, Is.False);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase("pins-dark.png"), TestCase("pins-dark-150.png"), TestCase("pins-only.png")]
    [TestCase("pins-light.png"), TestCase("pins-minimum.png")]
    [TestCase("divider-chart-minimum.png"), TestCase("divider-process-minimum.png"), TestCase("pins.json")]
    [Category("Integration")]
    public void PinsFixtureDiagnosticWriteFailurePreservesTheSentinelAndOriginalFixtureLifetime(string name)
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "owned pin diagnostic sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FinishWindowCheck(WindowDiagnostics.CapturePinsFixtureAsync(_directory, default)));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned pin diagnostic sentinel"));
        Assert.That(fixture.CreatedWindow().IsVisible, Is.False);
        Assert.That(fixture.CreatedWindow().Session.HasLiveSample, Is.False);
        if (name == "pins.json") Assert.That(fixture.CreatedWindow().SnapshotCopiedTimer.IsEnabled, Is.False);
    }

    [TestCase("empty"), TestCase("no-fill"), Category("Integration")]
    public void PinsFixtureDiagnosticPreservesMissingOrUnfilledPinTemplateFailures(string mode)
    {
        using var fixture = new PinsDiagnosticScope(_directory, mode);
        var error = Assert.Catch(() => FinishWindowCheck(WindowDiagnostics.CapturePinsFixtureAsync(_directory, default)));
        if (mode == "empty") Assert.That(error, Is.TypeOf<NullReferenceException>());
        else
        {
            Assert.That(error, Is.TypeOf<InvalidOperationException>());
            Assert.That(error!.Message, Does.StartWith("UI presentation failed:"));
        }
        Assert.That(fixture.CreatedWindow().IsVisible, Is.False);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    private sealed class PinsDiagnosticScope : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly Window[] _existing;
        internal PinsDiagnosticScope(string directory, string captionMode = "normal")
        {
            _ui = new UpdateUi(directory, true);
            Directory.CreateDirectory(directory);
            var document = XDocument.Load(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "Fixtures", "DiagnosticPins", "App.xaml.xml"));
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            var caption = document.Root!.Element(presentation + "Application.Resources")!.Elements(presentation + "Style")
                .Single(style => (string?)style.Attribute(xaml + "Key") == "CaptionButton");
            var dictionary = new XElement(presentation + "ResourceDictionary",
                new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "controls", "clr-namespace:WinMon.Controls;assembly=WinMon"),
                new XElement(caption));
            if (captionMode == "no-fill")
                foreach (var setter in dictionary.Descendants(presentation + "Setter")
                             .Where(setter => (string?)setter.Attribute("Property") == "Fill").ToArray()) setter.Remove();
            var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            Application.Current.Resources["CaptionButton"] = captionMode == "empty"
                ? new Style(typeof(System.Windows.Controls.Button)) : resources["CaptionButton"];
            _existing = Application.Current.Windows.Cast<Window>().ToArray();
        }
        internal MainWindow CreatedWindow() => Application.Current.Windows.OfType<MainWindow>().Single(window => !_existing.Contains(window));
        public void Dispose()
        {
            foreach (var window in Application.Current.Windows.OfType<MainWindow>().Where(window => !_existing.Contains(window)).ToArray())
            {
                window.SnapshotCopiedTimer.Stop();
                foreach (var view in new FrameworkElement[] { window.UsageView, window.Processes, window.ServiceView, window.ConnectionView })
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                window.CloseForFixture();
                window.Lifetime.Dispose();
            }
            _ui.Dispose();
        }
    }
}
