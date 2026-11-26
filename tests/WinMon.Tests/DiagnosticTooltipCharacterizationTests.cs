using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false), TestCase(true), Category("Integration"), SetCulture("en-US")]
    public void TooltipDiagnosticCapturesTheLatestSampleAndRestoresTheOriginalTooltip(bool originalPresent)
    {
        using var fixture = new TooltipDiagnostic(_directory, originalPresent);
        var path = Path.Combine(_directory, "tooltip.png");

        FinishWindowCheck(fixture.Start(path));

        fixture.AssertRestored();
        var tooltip = fixture.Created!;
        Assert.That(fixture.Opened.Count, Is.EqualTo(1));
        Assert.That(tooltip.Content, Is.TypeOf<string>());
        Assert.That((string)tooltip.Content, Does.Contain("CPU 42.0%").And.Not.Contain("CPU 7.0%"));
        Assert.That(tooltip.PlacementTarget, Is.SameAs(fixture.Window.OverallGraph));
        Assert.That(tooltip.Placement, Is.EqualTo(PlacementMode.Center));
        Assert.That(((SolidColorBrush)tooltip.Foreground).Color, Is.EqualTo(fixture.Text.Color));
        Assert.That(((SolidColorBrush)tooltip.Background).Color, Is.EqualTo(fixture.Raised.Color));
        Assert.That(tooltip.Foreground, Is.Not.SameAs(fixture.Text), "Matching colors do not require the same brush instance.");
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var png = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = png.Frames.Single();
        Assert.That(frame.PixelWidth, Is.GreaterThan(0));
        Assert.That(frame.PixelHeight, Is.GreaterThan(0));
        Assert.That(frame.DpiX, Is.EqualTo(96).Within(.1));
        Assert.That(frame.DpiY, Is.EqualTo(96).Within(.1));
        Assert.That(frame.PixelWidth, Is.EqualTo((int)Math.Ceiling(tooltip.ActualWidth)));
        Assert.That(frame.PixelHeight, Is.EqualTo((int)Math.Ceiling(tooltip.ActualHeight)));
        var images = TestContext.Parameters.Get("TooltipArtifactDirectory", "");
        if (images.Length > 0)
        {
            Directory.CreateDirectory(images);
            var destination = Path.Combine(images, $"tooltip-{originalPresent}.png");
            using var copy = File.Create(destination);
            stream.Position = 0;
            stream.CopyTo(copy);
        }
    }

    [TestCase("foreground-kind"), TestCase("foreground-color"), TestCase("background-kind"), TestCase("background-color")]
    [Category("Integration")]
    public void TooltipDiagnosticRejectsEveryThemeMismatchAndClosesTheTemporaryPopup(string mismatch)
    {
        using var fixture = new TooltipDiagnostic(_directory, true, mismatch);
        var path = Path.Combine(_directory, "unexpected-tooltip.png");

        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Start(path)));

        Assert.That(error!.Message, Is.EqualTo("Tooltip did not use the active theme's foreground and background."));
        Assert.That(File.Exists(path), Is.False);
        fixture.AssertRestored();
    }

    [TestCase("Text"), TestCase("Raised"), Category("Integration")]
    public void TooltipDiagnosticPreservesResourceCastFailuresAndRestoresTheExistingTooltip(string key)
    {
        using var fixture = new TooltipDiagnostic(_directory, true);
        fixture.Window.Resources[key] = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
        var path = Path.Combine(_directory, "unexpected-tooltip.png");

        Assert.Throws<InvalidCastException>(() => FinishWindowCheck(fixture.Start(path)));

        Assert.That(File.Exists(path), Is.False);
        fixture.AssertRestored();
    }

    [Test, Category("Integration")]
    public void TooltipDiagnosticDispatcherCancellationRetainsItsOriginalTokenlessFailureAndRestoresTheTooltip()
    {
        using var fixture = new TooltipDiagnostic(_directory, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(_directory, "cancelled-tooltip.png");

        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start(path, cancellation.Token)));

        Assert.That(error, Is.TypeOf<TaskCanceledException>());
        Assert.That(error!.CancellationToken, Is.EqualTo(CancellationToken.None),
            "The existing WPF DispatcherOperation task does not retain the supplied cancellation token.");
        Assert.That(File.Exists(path), Is.False);
        fixture.AssertRestored();
    }

    [Test, Category("Integration")]
    public void TooltipDiagnosticEmptyHistoryFailsBeforeReplacingTheExistingTooltip()
    {
        using var fixture = new TooltipDiagnostic(_directory, true, seed: false);
        var path = Path.Combine(_directory, "empty-tooltip.png");

        Assert.Throws<ArgumentOutOfRangeException>(() => FinishWindowCheck(fixture.Start(path)));

        Assert.That(fixture.Created, Is.Null);
        Assert.That(fixture.Opened, Is.Empty);
        Assert.That(File.Exists(path), Is.False);
        fixture.AssertRestored();
    }

    [TestCase("missing-folder"), TestCase("locked-file"), Category("Integration")]
    public void TooltipDiagnosticWriteFailuresRestoreThePopupAndPreserveExistingData(string failure)
    {
        using var fixture = new TooltipDiagnostic(_directory, true);
        var path = failure == "missing-folder" ? Path.Combine(_directory, "missing", "tooltip.png")
            : Path.Combine(_directory, "locked-tooltip.png");
        if (failure == "locked-file") File.WriteAllText(path, "owned image sentinel");
        using var locked = failure == "locked-file" ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;

        var error = Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Start(path)));

        Assert.That(error, failure == "missing-folder" ? Is.TypeOf<DirectoryNotFoundException>() : Is.TypeOf<IOException>());
        if (failure == "locked-file") Assert.That(File.ReadAllText(path), Is.EqualTo("owned image sentinel"));
        else Assert.That(File.Exists(path), Is.False);
        fixture.AssertRestored();
    }

    private sealed class TooltipDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly Func<string, CancellationToken, Task> _capture;
        private readonly object? _original;
        internal MainWindow Window => _ui.Window;
        internal SolidColorBrush Text { get; }
        internal SolidColorBrush Raised { get; }
        internal List<ToolTip> Opened { get; } = [];
        internal ToolTip? Created { get; private set; }

        internal TooltipDiagnostic(string directory, bool originalPresent, string? mismatch = null, bool seed = true)
        {
            _ui = new UpdateUi(directory, true);
            Directory.CreateDirectory(directory);
            Window.ApplyTheme(false, false);
            Text = (SolidColorBrush)Window.FindResource("Text");
            Raised = (SolidColorBrush)Window.FindResource("Raised");
            Brush foreground = mismatch switch
            {
                "foreground-kind" => new LinearGradientBrush(Colors.Red, Colors.Blue, 0),
                "foreground-color" => new SolidColorBrush(Color.FromRgb(1, 2, 3)),
                _ => new SolidColorBrush(Text.Color)
            };
            Brush background = mismatch switch
            {
                "background-kind" => new LinearGradientBrush(Colors.Red, Colors.Blue, 0),
                "background-color" => new SolidColorBrush(Color.FromRgb(4, 5, 6)),
                _ => new SolidColorBrush(Raised.Color)
            };
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.ForegroundProperty, foreground));
            style.Setters.Add(new Setter(Control.BackgroundProperty, background));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new EventSetter(ToolTip.OpenedEvent, new RoutedEventHandler((sender, _) => Opened.Add((ToolTip)sender))));
            Application.Current.Resources[typeof(ToolTip)] = style;
            _original = originalPresent ? new object() : null;
            // The diagnostic opens its own tooltip. Keep the physical pointer from replacing it.
            Window.OverallGraph.IsHitTestVisible = false;
            Window.OverallGraph.ToolTip = _original;
            if (seed)
            {
                Window.Session.Record(new(DateTimeOffset.UnixEpoch, 7, [], 0));
                Window.Session.Record(new(DateTimeOffset.UnixEpoch.AddSeconds(1), 42, [], 0));
            }
            Window.ShowActivated = false;
            Window.ShowInTaskbar = false;
            Window.Title = "Owned tooltip diagnostic fixture";
            Window.Width = 600;
            Window.Height = 500;
            Window.Show();
            FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            _capture = typeof(WindowDiagnostics).GetMethod("CaptureToolTipAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(Window));
        }

        internal Task Start(string path, CancellationToken token = default)
        {
            var task = _capture(path, token);
            Created = Window.OverallGraph.ToolTip as ToolTip ?? Opened.LastOrDefault();
            return task;
        }

        internal void AssertRestored()
        {
            FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            Created ??= Opened.LastOrDefault();
            Assert.That(Window.OverallGraph.ToolTip, Is.SameAs(_original));
            if (Created is not null) Assert.That(Created.IsOpen, Is.False);
            Assert.That(Opened.All(tooltip => !tooltip.IsOpen), Is.True);
            Assert.That(Window.Session.HasLiveSample, Is.False, "The fixture must never start native sampling.");
        }

        public void Dispose()
        {
            foreach (var tooltip in Opened) tooltip.IsOpen = false;
            Created?.IsOpen = false;
            _ui.Dispose();
        }
    }
}
