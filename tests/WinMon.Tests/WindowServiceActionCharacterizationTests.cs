using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static readonly ServiceObservation OwnedService =
        new("WinMon.Characterization.NeverInstalled", "Owned service display name", 4, 0, 1);

    private sealed class WindowActionContext(Dispatcher dispatcher) : SynchronizationContext
    {
        private int _operations;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<Exception> Errors { get; } = [];
        public override void OperationStarted() => Interlocked.Increment(ref _operations);
        public override void OperationCompleted()
        {
            if (Interlocked.Decrement(ref _operations) == 0) Completion.TrySetResult();
        }
        public override void Post(SendOrPostCallback callback, object? state) => dispatcher.BeginInvoke(() =>
        {
            try { callback(state); }
            catch (Exception error) { Errors.Add(error); }
        });
    }

    private static void InvokeServiceAction(UpdateUi ui, string action, Action<MessageDialog>? confirm = null,
        ProcessUsage? host = null, Action? whilePending = null, Action<IReadOnlyList<Exception>>? verifyErrors = null)
    {
        ui.Window.ApplyTheme(false, false);
        var invoke = (typeof(MainWindow).GetMethod("RunServiceAction", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing service action callback"))
            .CreateDelegate<Action<ServiceObservation, ProcessUsage?, string>>(ui.Window);
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var observed = new HashSet<MessageDialog>();
        var context = new WindowActionContext(ui.Window.Dispatcher);
        var previous = SynchronizationContext.Current;
        DispatcherOperation? watch = null;
        Exception? failure = null;
        Window[] OwnedDialogs() => Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
        void Watch()
        {
            foreach (var window in OwnedDialogs())
            {
                if (window is not MessageDialog dialog)
                {
                    failure ??= new AssertionException("Unexpected service-action window: " + window.Title);
                    window.Close();
                    continue;
                }
                if (!observed.Add(dialog)) continue;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        Assert.That(confirm, Is.Not.Null, "A guarded action must not prompt");
                        Assert.That(dialog.Owner, Is.Null, "The unshown fixture is not a modal owner");
                        Assert.That(dialog.Title, Is.EqualTo(Strings.Services));
                        var label = action == "stop" ? Strings.StopService : Strings.RestartService;
                        Assert.That(SettingsEntryFlow.Children((DependencyObject)dialog.Content).OfType<TextBlock>().Select(text => text.Text),
                            Is.EqualTo(new[] { Strings.Format(Strings.ConfirmServiceAction, label, OwnedService.DisplayName) }));
                        Assert.That(SettingsEntryFlow.Children((DependencyObject)dialog.Content).OfType<Button>().Select(button => button.Content),
                            Is.EqualTo(new object[] { label, Strings.Cancel }));
                        confirm!(dialog);
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
            failure ??= new AssertionException("Service-action modal exceeded its bound");
            foreach (var dialog in OwnedDialogs()) if (dialog.IsVisible) dialog.Close();
        };
        try
        {
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Watch));
            timeout.Start();
            SynchronizationContext.SetSynchronizationContext(context);
            invoke(OwnedService, host, action);
            SynchronizationContext.SetSynchronizationContext(previous);
            whilePending?.Invoke();
            FinishWindowCheck(context.Completion.Task);
            FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            if (failure is not null) throw new AssertionException(failure.ToString());
            if (verifyErrors is null) Assert.That(context.Errors, Is.Empty, "Unexpected async-void exception");
            else verifyErrors(context.Errors);
            Assert.That(observed.Count, Is.EqualTo(confirm is null ? 0 : 1));
            Assert.That(OwnedDialogs(), Is.Empty);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            timeout.Stop();
            watch?.Abort();
            foreach (var dialog in OwnedDialogs()) if (dialog.IsVisible) dialog.Close();
        }
    }

    private static void AcceptServiceConfirmation(MessageDialog dialog) =>
        SettingsEntryFlow.Children((DependencyObject)dialog.Content).OfType<Button>().First()
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [TestCase("busy")]
    [TestCase("closing")]
    [TestCase("historical")]
    [TestCase("diagnostic")]
    public void ServiceActionGuardsPreserveTheExistingNoticeWithoutPrompting(string guard)
    {
        using var ui = new UpdateUi(_directory, diagnostic: guard == "diagnostic");
        ui.Window.ServiceView.SetBusy(false, "Owned previous service notice");
        ui.Window.ServiceActionBusy = guard == "busy";
        ui.Window.IsClosing = guard == "closing";
        if (guard == "historical") ui.Window.SelectedSample = new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0);
        InvokeServiceAction(ui, "stop");
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo("Owned previous service notice"));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("stop")]
    [TestCase("restart")]
    [TestCase("unknown")]
    public void ServiceActionDeclinedConfirmationsPreserveTheNoticeAndDoNotExecute(string action)
    {
        using var ui = new UpdateUi(_directory);
        ui.Window.ServiceView.SetBusy(false, "Owned previous service notice");
        InvokeServiceAction(ui, action, dialog => SettingsEntryFlow.Children((DependencyObject)dialog.Content)
            .OfType<Button>().Last().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo("Owned previous service notice"));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("closing")]
    [TestCase("historical")]
    public void ServiceActionRechecksWindowStateAfterAcceptedConfirmation(string transition)
    {
        using var ui = new UpdateUi(_directory);
        ui.Window.ServiceView.SetBusy(false, "Owned previous service notice");
        InvokeServiceAction(ui, "restart", dialog =>
        {
            if (transition == "closing") ui.Window.IsClosing = true;
            else ui.Window.SelectedSample = new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0);
            AcceptServiceConfirmation(dialog);
        });
        Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo("Owned previous service notice"));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("restart")]
    public void ServiceActionPreCanceledLifetimeClearsBusyStateWithoutExecutingScm(string action)
    {
        using var ui = new UpdateUi(_directory);
        ui.Window.Lifetime.Cancel();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            ui.Window.ServiceView.SetBusy(false, "Owned previous service notice");
            InvokeServiceAction(ui, action, action == "start" ? null : AcceptServiceConfirmation);
            Assert.That(ui.Window.ServiceView.Notice.Text, Is.EqualTo(Strings.ServicesCollecting));
        }
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test]
    public void ServiceActionUnknownDiscriminatorReportsTheExistingArgumentErrorAndAllowsRetry()
    {
        using var ui = new UpdateUi(_directory);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            InvokeServiceAction(ui, "unknown", AcceptServiceConfirmation);
            Assert.That(ui.Window.ServiceView.Notice.Text,
                Is.EqualTo(Strings.Format(Strings.ServiceActionFailed, new ArgumentException("action").Message)));
            Assert.That(ui.Window.ServiceView.Notice.Visibility, Is.EqualTo(Visibility.Visible));
        }
        Assert.That(Directory.Exists(_directory), Is.False);
    }
}
