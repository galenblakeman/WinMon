using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class SettingsClearHistoryCharacterizationTests
{
    private SettingsWindow _window = null!;
    private readonly List<object> _addedResources = [];
    private readonly List<TaskCompletionSource> _pending = [];
    private readonly Queue<int> _answers = new();
    private readonly HashSet<Window> _existing = [];
    private readonly List<(string Title, string Text, string[] Labels, bool DismissalDefault, bool ConfirmDefault)> _dialogs = [];
    private readonly List<bool> _enabled = [];
    private DispatcherOperation? _watch;
    private DispatcherTimer _timeout = null!;
    private bool _watching;
    private Exception? _failure;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, value) in new[] { ("Surface", Brushes.Black), ("Text", Brushes.White) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = value; _addedResources.Add(key); }
        _existing.UnionWith(app.Windows.Cast<Window>());
        _window = new SettingsWindow(new MonitorPreferences(), () => [],
            startupAvailability: StartupAvailability.Available, loadNetworkInterfaces: () => []);
        _window.ClearResourceTrendsButton.IsEnabledChanged += (_, e) => _enabled.Add((bool)e.NewValue);
        _timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
        _timeout.Tick += (_, _) =>
        {
            _failure ??= new TimeoutException("Owned clear-history dialog did not complete");
            foreach (var dialog in OwnedDialogs()) dialog.Close();
            _timeout.Stop();
        };
        _watching = true;
        WatchDialogs();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var pending in _pending) pending.TrySetResult();
        Drain();
        _watching = false;
        _watch?.Abort();
        _timeout.Stop();
        foreach (var dialog in OwnedDialogs()) dialog.Close();
        _window.Close();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
        _pending.Clear();
        _existing.Clear();
        _dialogs.Clear();
        _enabled.Clear();
        _answers.Clear();
        _failure = null;
    }

    private MessageDialog[] OwnedDialogs() => Application.Current.Windows.OfType<MessageDialog>()
        .Where(dialog => !_existing.Contains(dialog) && dialog.IsVisible).ToArray();

    private void WatchDialogs()
    {
        if (!_watching) return;
        foreach (var dialog in OwnedDialogs())
        {
            try
            {
                var buttons = Descendants<Button>(dialog.Content).ToArray();
                var text = Descendants<TextBlock>(dialog.Content).Single().Text;
                _dialogs.Add((dialog.Title, text, buttons.Select(button => (string)button.Content).ToArray(),
                    buttons[^1].IsDefault && buttons[^1].IsCancel, buttons[0].IsDefault && buttons.Length > 1));
                if (text == Strings.ClearResourceTrendsConfirm)
                {
                    if (_answers.Count == 0) throw new InvalidOperationException("Unexpected confirmation");
                    var answer = _answers.Dequeue();
                    if (answer < 0) dialog.Close();
                    else buttons[answer == 1 ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else
                {
                    if (text != Strings.ResourceTrendsClearError || buttons.Length != 1)
                        throw new InvalidOperationException("Unexpected owned notice: " + text);
                    buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
            catch (Exception error)
            {
                _failure ??= error;
                dialog.Close();
            }
        }
        _watch = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WatchDialogs));
    }

    private void Click(int answer = 1)
    {
        _answers.Enqueue(answer);
        _timeout.Start();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(_window.Dispatcher));
        try { _window.ClearResourceTrendsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.That(_failure, Is.Null);
        Assert.That(_answers, Is.Empty);
    }

    private void Drain() => _window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private void Until(Func<bool> complete)
    {
        var frame = new DispatcherFrame();
        DispatcherOperation? check = null;
        void Check()
        {
            if (_failure is not null || complete()) frame.Continue = false;
            else check = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Check));
        }
        check = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Check));
        try { Dispatcher.PushFrame(frame); }
        finally { check?.Abort(); }
        Assert.That(_failure, Is.Null);
        Assert.That(complete(), Is.True);
    }

    private TaskCompletionSource Gate()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add(gate);
        return gate;
    }

    private void AssertDialogs(int confirmations, int notices = 0)
    {
        Assert.That(_failure, Is.Null);
        Assert.That(_dialogs.Count(dialog => dialog.Text == Strings.ClearResourceTrendsConfirm), Is.EqualTo(confirmations));
        Assert.That(_dialogs.Count(dialog => dialog.Text == Strings.ResourceTrendsClearError), Is.EqualTo(notices));
        foreach (var dialog in _dialogs)
        {
            Assert.That(dialog.Title, Is.EqualTo(Strings.ResourceTrends));
            Assert.That(dialog.DismissalDefault, Is.True);
            Assert.That(dialog.ConfirmDefault, Is.False);
            Assert.That(dialog.Labels, Is.EqualTo(dialog.Text == Strings.ClearResourceTrendsConfirm
                ? new[] { Strings.ClearResourceTrends, Strings.Cancel } : new[] { Strings.Close }));
        }
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void DismissingClearConfirmationLeavesTheButtonAndSubscribersUntouched(int answer)
    {
        var calls = 0;
        _window.ClearResourceTrendsRequested += () => { calls++; return Task.CompletedTask; };
        Click(answer);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Zero);
            Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.True);
            Assert.That(_enabled, Is.Empty);
            AssertDialogs(1);
        });
    }

    [Test]
    public void ConfirmedClearWithoutSubscribersStillRestoresTheButton()
    {
        Click();
        Assert.That(_enabled, Is.EqualTo(new[] { false, true }));
        Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.True);
        AssertDialogs(1);
    }

    [Test]
    public void ClearUsesAnInvocationSnapshotAndSubscriberChangesApplyOnTheNextClick()
    {
        var calls = new List<int>();
        var disabled = new List<bool>();
        Func<Task> second = () => { calls.Add(2); disabled.Add(!_window.ClearResourceTrendsButton.IsEnabled); return Task.CompletedTask; };
        Func<Task> third = () => { calls.Add(3); disabled.Add(!_window.ClearResourceTrendsButton.IsEnabled); return Task.CompletedTask; };
        _window.ClearResourceTrendsRequested += () =>
        {
            calls.Add(1);
            disabled.Add(!_window.ClearResourceTrendsButton.IsEnabled);
            _window.ClearResourceTrendsRequested -= second;
            _window.ClearResourceTrendsRequested += third;
            return Task.CompletedTask;
        };
        _window.ClearResourceTrendsRequested += second;
        Click();
        Assert.That(calls, Is.EqualTo(new[] { 1, 2 }));
        Click();
        Assert.That(calls, Is.EqualTo(new[] { 1, 2, 1, 3 }));
        Assert.That(disabled, Is.All.True);
        Assert.That(_enabled, Is.EqualTo(new[] { false, true, false, true }));
        AssertDialogs(2);
    }

    [Test]
    public void ClearAwaitsEachSubscriberOnTheUiThreadAndStaysDisabledUntilTheLastCompletion()
    {
        var first = Gate();
        var second = Gate();
        var calls = new List<int>();
        var threads = new List<int>();
        var uiThread = Environment.CurrentManagedThreadId;
        _window.ClearResourceTrendsRequested += () => { calls.Add(1); threads.Add(Environment.CurrentManagedThreadId); return first.Task; };
        _window.ClearResourceTrendsRequested += () => { calls.Add(2); threads.Add(Environment.CurrentManagedThreadId); return second.Task; };
        Click();
        Assert.That(calls, Is.EqualTo(new[] { 1 }));
        Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.False);
        first.SetResult();
        Until(() => calls.Count == 2);
        Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.False);
        second.SetResult();
        Until(() => _window.ClearResourceTrendsButton.IsEnabled);
        Assert.That(calls, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(threads, Is.All.EqualTo(uiThread));
        Assert.That(_enabled, Is.EqualTo(new[] { false, true }));
        AssertDialogs(1);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosingSettingsDuringClearDoesNotReenableItsButtonWhenTheHandlerCompletes(bool fails)
    {
        var gate = Gate();
        _window.ClearResourceTrendsRequested += () => gate.Task;
        Click();
        Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.False);
        _window.Close();
        if (fails) gate.SetException(new IOException("private archive path"));
        else gate.SetResult();
        Drain();
        if (fails) Until(() => _dialogs.Count == 2);
        Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.False);
        Assert.That(_enabled, Is.EqualTo(new[] { false }));
        AssertDialogs(1, fails ? 1 : 0);
    }

    [TestCase("synchronous")]
    [TestCase("faulted")]
    [TestCase("canceled")]
    [TestCase("deferred")]
    public void ClearFailuresStopLaterSubscribersShowOnlyTheGenericNoticeAndRestoreTheButton(string kind)
    {
        var gate = Gate();
        var calls = new List<int>();
        _window.ClearResourceTrendsRequested += () =>
        {
            calls.Add(1);
            return kind switch
            {
                "synchronous" => throw new IOException("private archive path"),
                "faulted" => Task.FromException(new InvalidOperationException("private archive path")),
                "canceled" => Task.FromCanceled(new CancellationToken(true)),
                _ => gate.Task
            };
        };
        _window.ClearResourceTrendsRequested += () => { calls.Add(2); return Task.CompletedTask; };
        Click();
        if (kind == "deferred")
        {
            Assert.That(_window.ClearResourceTrendsButton.IsEnabled, Is.False);
            gate.SetException(new IOException("private archive path"));
        }
        Until(() => _window.ClearResourceTrendsButton.IsEnabled);
        Assert.That(calls, Is.EqualTo(new[] { 1 }));
        Assert.That(_enabled, Is.EqualTo(new[] { false, true }));
        AssertDialogs(1, 1);
        Assert.That(_dialogs.Select(dialog => dialog.Text), Has.None.Contains("private archive path"));
    }

    private static IEnumerable<T> Descendants<T>(object root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        if (root is not DependencyObject parent) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        foreach (var result in Descendants<T>(child)) yield return result;
    }
}
