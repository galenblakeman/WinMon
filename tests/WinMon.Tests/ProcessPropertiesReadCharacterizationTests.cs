using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessPropertiesReadCharacterizationTests
{
    private static readonly ProcessDetailsSnapshot Snapshot = new(
        new ProcessUsage(0, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc(), "owned-properties.exe", 10),
        false, new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero), false, true, "owned state");
    private static readonly ProcessInspection Available = new(false, "owned-properties.exe --inspected",
        new ProcessOwner("fixture-account", 7, true), new ProcessSignature(ProcessSignatureStatus.Verified, "Owned signer"));
    private static readonly ProcessInspection Unavailable = new(false, null, null,
        new ProcessSignature(ProcessSignatureStatus.Unavailable, null));

    [TestCase("grouped"), TestCase("historical"), TestCase("unavailable"), TestCase("inspected")]
    [TestCase("zero-start"), TestCase("negative-start")]
    public void ViewsWithoutFreshInspectionNeverInvokeReaderAndCopyTheirCapturedReport(string view)
    {
        var snapshot = view switch
        {
            "grouped" => Snapshot with { Grouped = true },
            "historical" => Snapshot with { Historical = true },
            "unavailable" => Snapshot with { Available = false },
            "inspected" => Snapshot with { Inspection = Available },
            "zero-start" => Snapshot with { Process = Snapshot.Process with { CreationTicks = 0 } },
            "negative-start" => Snapshot with { Process = Snapshot.Process with { CreationTicks = -1 } },
            _ => throw new AssertionException("Unknown view")
        };
        var reads = 0;
        using var probe = new PropertiesProbe(snapshot, _ => { reads++; return Task.FromResult(Available); });
        Assert.That(probe.Copy.IsEnabled, Is.True);
        Assert.That(probe.Details.Text, Is.EqualTo(snapshot.Format()));

        probe.Show();
        probe.ClickCopy();

        Assert.That(reads, Is.Zero);
        Assert.That(probe.Copies, Is.EqualTo(new[] { snapshot.Format() }));
        Assert.That(AutomationProperties.GetName(probe.Details), Is.EqualTo(Strings.ProcessProperties));
        Assert.That(probe.Details.IsReadOnly, Is.True);
        Assert.That(probe.Close.IsDefault && probe.Close.IsCancel, Is.True);
        probe.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(probe.Window.IsVisible, Is.False);
    }

    [TestCase(null, "owned-properties.exe"), TestCase("", "owned-properties.exe")]
    [TestCase("   ", "owned-properties.exe"), TestCase("Owned application", "Owned application")]
    public void HeadingUsesApplicationNameOnlyWhenItContainsText(string? application, string expected)
    {
        var snapshot = Snapshot with { Process = Snapshot.Process with { AppName = application! } };
        using var probe = new PropertiesProbe(snapshot, _ => Task.FromResult(Available));
        Assert.That(probe.Heading.Text, Is.EqualTo(expected));
        Assert.That(probe.Window.Title, Is.EqualTo(Strings.ProcessProperties + " — " + snapshot.Process.Name));
        probe.Show();
        Pump(probe.Ready.Task);
        probe.ClickCopy();
        Assert.That(probe.Copies.Single(), Does.Contain(snapshot.Process.Name));
    }

    [TestCase(true, "available"), TestCase(false, "available")]
    [TestCase(false, "unavailable"), TestCase(false, "exited")]
    public void CompletedInspectionUpdatesDetailsAndCopyFromTheCapturedSnapshot(bool immediate, string state)
    {
        var inspection = state switch
        {
            "available" => Available,
            "unavailable" => Unavailable,
            "exited" => Available with { Exited = true, CommandLine = "must not appear" },
            _ => throw new AssertionException("Unknown inspection")
        };
        using var controlled = new ControlledReader();
        var reads = 0;
        ProcessDetailsSnapshot? requested = null;
        using var probe = new PropertiesProbe(Snapshot, snapshot =>
        {
            reads++;
            requested = snapshot;
            return immediate ? Task.FromResult(inspection) : controlled.Read(snapshot);
        });
        Assert.That(reads, Is.Zero);
        Assert.That(probe.Copy.IsEnabled, Is.False);
        Assert.That(probe.Details.Text, Does.Contain(Strings.ProcessReadingDetails));

        probe.Show();
        Assert.That(reads, Is.EqualTo(1));
        Assert.That(requested, Is.SameAs(Snapshot));
        if (!immediate)
        {
            Pump(controlled.Started.Task);
            Assert.That(probe.Copy.IsEnabled, Is.False);
            if (state == "available") probe.Capture("pending");
            Pump(Task.Run(() => controlled.Result.TrySetResult(inspection)));
        }
        Pump(probe.Ready.Task);
        var expected = (Snapshot with { Inspection = inspection }).Format();
        Assert.That(probe.Details.Text, Is.EqualTo(expected));
        Assert.That(probe.Details.Text, Does.Not.Contain(Strings.ProcessReadingDetails));
        if (state == "available")
        {
            Assert.That(probe.Details.Text, Does.Contain("--inspected").And.Contain("fixture-account").And.Contain("Owned signer"));
            if (!immediate) probe.Capture("ready");
        }
        if (state == "exited") Assert.That(probe.Details.Text, Does.Contain(Strings.ProcessExited).And.Not.Contain("must not appear"));
        probe.ClickCopy();
        probe.ClickCopy();
        Assert.That(probe.Copies, Is.EqualTo(new[] { expected, expected }));
        Assert.That(reads, Is.EqualTo(1));
    }

    [TestCase(false), TestCase(true)]
    public void ClosingDuringInspectionDiscardsLateDetailsAndNeverEnablesCopy(bool completeInClosedEvent)
    {
        using var controlled = new ControlledReader();
        using var probe = new PropertiesProbe(Snapshot, controlled.Read);
        probe.Show();
        Pump(controlled.Started.Task);
        var pending = probe.Details.Text;
        if (completeInClosedEvent)
            probe.Window.Closed += (_, _) => controlled.Result.TrySetResult(Available);

        probe.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!completeInClosedEvent) controlled.Result.TrySetResult(Available);
        Pump(controlled.Delivered.Task);

        Assert.That(probe.Window.IsVisible, Is.False);
        Assert.That(probe.Details.Text, Is.EqualTo(pending));
        Assert.That(probe.Copy.IsEnabled, Is.False);
        Assert.That(probe.Copies, Is.Empty);
        Assert.That(controlled.Calls, Is.EqualTo(1));
    }

    [Test]
    public void ConcurrentWindowsKeepTheirReadersTargetsAndCopiedReportsIndependent()
    {
        using var firstReader = new ControlledReader();
        using var secondReader = new ControlledReader();
        var secondSnapshot = Snapshot with { Process = Snapshot.Process with { Name = "second-owned.exe" } };
        using var first = new PropertiesProbe(Snapshot, firstReader.Read);
        using var second = new PropertiesProbe(secondSnapshot, secondReader.Read);
        first.Show();
        second.Show();
        Pump(firstReader.Started.Task);
        Pump(secondReader.Started.Task);
        var secondInspection = Available with { CommandLine = "second-owned.exe --second" };

        secondReader.Result.SetResult(secondInspection);
        Pump(second.Ready.Task);
        Assert.That(first.Copy.IsEnabled, Is.False);
        Assert.That(first.Details.Text, Does.Contain(Strings.ProcessReadingDetails));
        second.ClickCopy();
        firstReader.Result.SetResult(Available);
        Pump(first.Ready.Task);
        first.ClickCopy();

        Assert.That(firstReader.Request, Is.SameAs(Snapshot));
        Assert.That(secondReader.Request, Is.SameAs(secondSnapshot));
        Assert.That(first.Copies.Single(), Does.Contain("--inspected").And.Not.Contain("--second"));
        Assert.That(second.Copies.Single(), Does.Contain("--second").And.Not.Contain("--inspected"));
        Assert.That((firstReader.Calls, secondReader.Calls), Is.EqualTo((1, 1)));
    }

    [Test]
    public void CopyFailurePropagatesItsOriginalExceptionWithoutChangingTheLoadedReport()
    {
        var original = new InvalidOperationException("Owned copy failure");
        using var probe = new PropertiesProbe(Snapshot, _ => Task.FromResult(Available), _ => throw original);
        probe.Show();
        Pump(probe.Ready.Task);
        var report = probe.Details.Text;

        Assert.That(Assert.Throws<InvalidOperationException>(probe.ClickCopy), Is.SameAs(original));

        Assert.That(probe.Details.Text, Is.EqualTo(report));
        Assert.That(probe.Copy.IsEnabled, Is.True);
        Assert.That(probe.Copies, Is.EqualTo(new[] { report }));
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void DefaultAndNullTaskReadersFinishWithUnavailableInspectionForPidZero(bool nullTask)
    {
        var calls = 0;
        Func<ProcessDetailsSnapshot, Task<ProcessInspection>>? read = nullTask ? _ => { calls++; return null!; } : null;
        using var probe = new PropertiesProbe(Snapshot, read);
        Assert.That(Snapshot.Process.ProcessId, Is.Zero);
        probe.Show();
        Pump(probe.Ready.Task);

        Assert.That(calls, Is.EqualTo(nullTask ? 1 : 0));
        Assert.That(probe.Details.Text, Is.EqualTo((Snapshot with { Inspection = Unavailable }).Format()));
        Assert.That(probe.Details.Text, Does.Not.Contain(Strings.ProcessReadingDetails));
        probe.ClickCopy();
        Assert.That(probe.Copies.Single(), Is.EqualTo(probe.Details.Text));
    }

    private sealed class ControlledReader : IDisposable
    {
        internal TaskCompletionSource<ProcessInspection> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Delivered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal ProcessDetailsSnapshot? Request { get; private set; }
        internal async Task<ProcessInspection> Read(ProcessDetailsSnapshot snapshot)
        {
            Calls++;
            Request = snapshot;
            var dispatcher = Dispatcher.CurrentDispatcher;
            Started.TrySetResult();
            var result = await Result.Task;
            dispatcher.VerifyAccess();
            // The provider returns on the UI thread. Its caller resumes inline or at Normal priority;
            // this idle signal runs afterward even when the closed-window guard produces no UI change.
            _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => Delivered.TrySetResult()));
            return result;
        }
        public void Dispose()
        {
            Result.TrySetResult(Unavailable);
            if (Calls > 0) Pump(Delivered.Task);
        }
    }

    private sealed class PropertiesProbe : IDisposable
    {
        private readonly SynchronizationContext? _previous = SynchronizationContext.Current;
        internal ProcessPropertiesWindow Window { get; }
        internal TextBox Details { get; }
        internal TextBlock Heading { get; }
        internal Button Copy { get; }
        internal Button Close { get; }
        internal List<string> Copies { get; } = [];
        internal TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource Loaded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource Rendered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal PropertiesProbe(ProcessDetailsSnapshot snapshot, Func<ProcessDetailsSnapshot, Task<ProcessInspection>>? read,
            Action<string>? copy = null)
        {
            _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            Window = new ProcessPropertiesWindow(snapshot, text => { Copies.Add(text); copy?.Invoke(text); }, read)
                { ShowActivated = false };
            Window.Resources["Surface"] = Brushes.Black;
            Window.Resources["Text"] = Brushes.White;
            Window.Resources["Accent"] = Brushes.LightGreen;
            var layout = (DockPanel)Window.Content;
            Details = layout.Children.OfType<TextBox>().Single();
            Heading = layout.Children.OfType<TextBlock>().Single();
            var buttons = layout.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            Copy = buttons.Single(button => Equals(button.Content, Strings.CopyDetails));
            Close = buttons.Single(button => Equals(button.Content, Strings.Close));
            Copy.IsEnabledChanged += (_, _) => { if (Copy.IsEnabled) Ready.TrySetResult(); };
            if (Copy.IsEnabled) Ready.TrySetResult();
            Window.Loaded += (_, _) => Loaded.TrySetResult();
            Window.ContentRendered += (_, _) => Rendered.TrySetResult();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Window.Dispatcher));
        }
        internal void Show() { Window.Show(); Pump(Loaded.Task); }
        internal void ClickCopy() => Copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        internal void Capture(string state)
        {
            var directory = TestContext.Parameters.Get("PropertiesReadArtifactDirectory", "");
            if (directory.Length == 0) return;
            Pump(Rendered.Task);
            Window.UpdateLayout();
            Directory.CreateDirectory(directory);
            var image = new RenderTargetBitmap((int)Math.Ceiling(Window.ActualWidth), (int)Math.Ceiling(Window.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            image.Render(Window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var output = File.Create(Path.Combine(directory, state + ".png"));
            encoder.Save(output);
        }
        public void Dispose() { Window.Close(); SynchronizationContext.SetSynchronizationContext(_previous); }
    }

    private static void Pump(Task task)
    {
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(10));
        if (!bounded.IsCompleted)
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = bounded.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        bounded.GetAwaiter().GetResult();
    }
}
