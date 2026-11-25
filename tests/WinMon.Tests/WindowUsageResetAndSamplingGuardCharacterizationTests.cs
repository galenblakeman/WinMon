using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static readonly DateTimeOffset ResetStart = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessUsage ResetProcess = new(0, 100, "owned-reset.exe", 10)
        { ImagePath = @"C:\Synthetic\owned-reset.exe", IoBytesPerSecond = 200 };

    [TestCase(true, 0), TestCase(true, 1), TestCase(true, 2), TestCase(false, 1), TestCase(false, 2)]
    public void UsageResetSnapshotAndArchiveGuardsPreserveTotalsWithoutPrompt(bool selected, int archive)
    {
        using var ui = new UpdateUi(_directory, true);
        SeedResetUsage(ui.Window, true);
        ui.Window.SelectedSample = selected ? ui.Window.Session.Latest : null;
        ui.Window.UsageView.ArchiveScope.SelectedIndex = archive;
        var before = ui.Window.Session.Usage.Export();
        var samples = ui.Window.Session.History.Samples.ToArray();

        InvokeOwnedUsageReset(ui, null);

        AssertResetUsageUnchanged(ui.Window.Session.Usage.Export(), before);
        Assert.That(ui.Window.Session.History.Samples, Is.EqualTo(samples));
        Assert.That(ui.Window.SelectedSample, Is.SameAs(selected ? samples[^1] : null));
        Assert.That(ui.Window.UsageView.ArchiveScope.SelectedIndex, Is.EqualTo(archive));
        Assert.That(ui.Window.SamplingTask, Is.Null);
    }

    [TestCase(false, "cancel"), TestCase(false, "close"), TestCase(false, "confirm")]
    [TestCase(true, "cancel"), TestCase(true, "close"), TestCase(true, "confirm")]
    public void UsageResetRequiresExplicitConfirmationAndPreservesRecordedHistory(bool populated, string choice)
    {
        using var ui = new UpdateUi(_directory, true);
        SeedResetUsage(ui.Window, populated);
        var before = ui.Window.Session.Usage.Export();
        var samples = ui.Window.Session.History.Samples.ToArray();

        InvokeOwnedUsageReset(ui, dialog =>
        {
            AssertResetUsageUnchanged(ui.Window.Session.Usage.Export(), before);
            Assert.That(ui.Window.UsageView.Rows.Items.Count, Is.EqualTo(populated ? 1 : 0));
            Assert.That(dialog.Title, Is.EqualTo(Strings.UsageTab));
            Assert.That(ResetDescendants<TextBlock>(dialog.Content).Single().Text, Is.EqualTo(Strings.UsageResetConfirm));
            var buttons = ResetDescendants<Button>(dialog.Content).ToArray();
            Assert.That(buttons.Select(button => button.Content), Is.EqualTo(new[] { Strings.UsageReset, Strings.Cancel }));
            Assert.That(buttons[1].IsDefault && buttons[1].IsCancel, Is.True);
            Assert.That(buttons[0].IsDefault, Is.False);
            if (populated && choice == "confirm") CaptureResetConfirmation(dialog);
            if (choice == "close") dialog.Close();
            else buttons[choice == "confirm" ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });

        Assert.That(ui.Window.Session.History.Samples, Is.EqualTo(samples), "Resetting totals must not clear recorded history.");
        Assert.That(ui.Window.SelectedSample, Is.Null);
        Assert.That(ui.Window.SamplingTask, Is.Null);
        Assert.That(ui.Window.Session.HasLiveSample, Is.False);
        if (choice != "confirm")
        {
            AssertResetUsageUnchanged(ui.Window.Session.Usage.Export(), before);
            Assert.That(ui.Window.UsageView.Rows.Items.Count, Is.EqualTo(populated ? 1 : 0));
            return;
        }

        var reset = ui.Window.Session.Usage.Export();
        Assert.That(reset.Since, Is.Not.EqualTo(before.Since));
        Assert.That(reset.Apps, Is.Empty);
        Assert.That(reset.Lifetimes, Is.Empty);
        Assert.That(reset.MachineBusyTicks, Is.Zero);
        Assert.That(reset.AttributedTicks, Is.Zero);
        Assert.That(reset.IdentityPolicyId, Is.EqualTo(before.IdentityPolicyId));
        Assert.That(ui.Window.UsageView.Rows.Items, Is.Empty);

        var next = ResetSample(reset.Since.AddSeconds(1));
        ui.Window.Session.Record(next, next);
        var continued = ui.Window.Session.Usage.Export();
        Assert.That(continued.Apps, Has.Count.EqualTo(1));
        Assert.That(continued.Apps[0].CpuTicks, Is.EqualTo(100));
        Assert.That(continued.Apps[0].Lifetimes, Is.EqualTo(1));
        Assert.That(continued.Lifetimes, Has.Count.EqualTo(1));
        Assert.That(continued.Lifetimes[0].ProcessId, Is.EqualTo(ResetProcess.ProcessId));
        Assert.That(continued.Since, Is.EqualTo(reset.Since));
        ui.Window.RefreshPresentation();
        Assert.That(ui.Window.UsageView.Rows.Items, Has.Count.EqualTo(1));
    }

    [TestCase(true, false, false), TestCase(true, true, true)]
    [TestCase(false, false, true), TestCase(false, true, false)]
    [SetCulture("en-US")]
    public void SamplingCallbackRestoredHistoryClearsStaleErrorWithoutSignalingUpdateReady(bool diagnostic, bool signaled, bool visible)
    {
        using var ui = new UpdateUi(_directory, diagnostic);
        SeedResetUsage(ui.Window, true);
        ui.Field("_updateReadySignaled").SetValue(ui.Window, signaled);
        ui.Window.ShownSamplingError = SamplingError.ReadingsUnavailable;
        if (visible) ShowResetFixture(ui);
        var before = ui.Window.Session.Usage.Export();
        var samples = ui.Window.Session.History.Samples.ToArray();
        Assert.That(ui.Window.Session.HasLiveSample, Is.False);
        Assert.That(ui.Window.Session.IsHealthy, Is.False, "Restored records must not prove live startup health.");

        typeof(MainWindow).GetMethod("OnSamplesRecorded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action>(ui.Window)();

        Assert.That(ui.Window.ShownSamplingError, Is.Null);
        Assert.That(ui.Field("_updateReadySignaled").GetValue(ui.Window), Is.EqualTo(signaled));
        Assert.That(ui.Window.Session.History.Samples, Is.EqualTo(samples));
        AssertResetUsageUnchanged(ui.Window.Session.Usage.Export(), before);
        Assert.That(ui.Window.SamplingTask, Is.Null);
        if (visible) Assert.That(((MetricReadout)ui.Window.FindName("CpuMetric")).ValueLabel.Text,
            Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 50)));
    }

    private static MonitorSample ResetSample(DateTimeOffset timestamp) =>
        new(timestamp, 50, [ResetProcess], 0) { IntervalSeconds = 1, IntervalCpuTicks = 1000 };

    private static void SeedResetUsage(MainWindow window, bool populated)
    {
        window.Session.Usage.Reset(ResetStart);
        if (!populated) return;
        foreach (var second in new[] { 1, 2 })
        {
            var sample = ResetSample(ResetStart.AddSeconds(second));
            window.Session.Record(sample, sample);
        }
        Assert.That(window.Session.Usage.Apps.Single().CpuTicks, Is.EqualTo(200));
    }

    private static void AssertResetUsageUnchanged(UsageLedgerState actual, UsageLedgerState expected)
    {
        Assert.That(actual.Since, Is.EqualTo(expected.Since));
        Assert.That(actual.MachineBusyTicks, Is.EqualTo(expected.MachineBusyTicks));
        Assert.That(actual.AttributedTicks, Is.EqualTo(expected.AttributedTicks));
        Assert.That(actual.Apps, Is.EqualTo(expected.Apps));
        Assert.That(actual.Lifetimes, Is.EqualTo(expected.Lifetimes));
        Assert.That(actual.IdentityPolicyId, Is.EqualTo(expected.IdentityPolicyId));
    }

    private static void ShowResetFixture(UpdateUi ui)
    {
        ui.Window.ApplyTheme(false, false);
        ui.Window.Title = "Owned usage reset fixture";
        ui.Window.ShowActivated = false;
        ui.Window.ShowInTaskbar = false;
        ui.Window.Show();
        Assert.That(ui.Window.SamplingTask, Is.Null);
    }

    private static void InvokeOwnedUsageReset(UpdateUi ui, Action<MessageDialog>? exercise)
    {
        if (exercise is not null)
        {
            ShowResetFixture(ui);
            ui.Window.UsageTab.IsChecked = true;
            ui.Window.RefreshPresentation();
            Assert.That(ui.Window.UsageView.IsVisible, Is.True);
        }
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var observed = new HashSet<MessageDialog>();
        DispatcherOperation? watch = null;
        Exception? failure = null;
        Window[] Owned() => Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
        void Watch()
        {
            foreach (var window in Owned())
            {
                if (window is not MessageDialog dialog)
                {
                    failure ??= new AssertionException("Unexpected reset window: " + window.Title);
                    window.Close();
                    continue;
                }
                if (!observed.Add(dialog)) continue;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        Assert.That(exercise, Is.Not.Null, "A snapshot/archive guard must not prompt.");
                        Assert.That(dialog.Owner, Is.SameAs(ui.Window));
                        exercise!(dialog);
                    }
                    catch (Exception error) { failure ??= error; }
                    finally { if (dialog.IsVisible) dialog.Close(); }
                };
            }
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Send, ui.Window.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) =>
        {
            failure ??= new AssertionException("Owned usage-reset modal exceeded its safety bound.");
            foreach (var window in Owned()) if (window.IsVisible) window.Close();
        };
        try
        {
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Watch));
            timeout.Start();
            typeof(MainWindow).GetMethod("ResetUsage", BindingFlags.Instance | BindingFlags.NonPublic)!
                .CreateDelegate<Action>(ui.Window)();
            FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.That(observed.Count, Is.EqualTo(exercise is null ? 0 : 1));
            Assert.That(Owned(), Is.Empty);
        }
        finally
        {
            timeout.Stop();
            watch?.Abort();
            foreach (var window in Owned()) if (window.IsVisible) window.Close();
        }
    }

    private static IEnumerable<T> ResetDescendants<T>(object root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        if (root is not DependencyObject parent) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        foreach (var descendant in ResetDescendants<T>(child)) yield return descendant;
    }

    private static void CaptureResetConfirmation(MessageDialog dialog)
    {
        var directory = TestContext.Parameters.Get("UsageResetArtifactDirectory", "");
        if (directory.Length == 0) return;
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        image.Render(dialog);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, "usage-reset-confirm.png"));
        encoder.Save(output);
    }
}
