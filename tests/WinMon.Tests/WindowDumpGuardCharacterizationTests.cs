using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(false), TestCase(true), Category("Integration")]
    public void DumpDialogUnavailableAndBusyGuardsPreserveControlsAndNeverStartAWrite(bool busy)
    {
        using var ui = new UpdateUi(_directory, true);
        using var writes = new OwnedDumpTasks();
        var calls = 0;
        using var dialog = new OwnedDumpDialog(ui.Window, () =>
        {
            calls++;
            if (busy) throw new AssertionException("Busy state must short circuit eligibility.");
            return false;
        });
        dialog.Full.IsChecked = true;
        dialog.SetWriting(busy);
        if (busy) writes.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var before = dialog.Controls();
        var pending = writes.Set.ToArray();

        dialog.ClickCreate();

        Assert.That(calls, Is.EqualTo(busy ? 0 : 1));
        Assert.That(dialog.Controls(), Is.EqualTo(before));
        Assert.That(dialog.Status.Text, Is.EqualTo(Strings.ProcessDumpUnavailable));
        Assert.That(writes.Set, Is.EquivalentTo(pending));
        Assert.That(dialog.Writing, Is.EqualTo(busy));
    }

    [Test, Category("Integration")]
    public void DumpDialogClosingIsCancelledOnlyWhileWritingAndIdleCloseUsesTheOriginalButton()
    {
        using var ui = new UpdateUi(_directory, true);
        using var writes = new OwnedDumpTasks();
        using var dialog = new OwnedDumpDialog(ui.Window, () => false);
        writes.Add(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var cancelled = new List<bool>();
        var closed = 0;
        dialog.Window.Closing += (_, args) => cancelled.Add(args.Cancel);
        dialog.Window.Closed += (_, _) => closed++;
        dialog.SetWriting(true);

        dialog.Window.Close();

        Assert.That(cancelled, Is.EqualTo(new[] { true }));
        Assert.That(closed, Is.Zero);
        dialog.SetWriting(false);
        dialog.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(cancelled, Is.EqualTo(new[] { true, false }));
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test, Category("Integration")]
    public void DumpDialogEligibilityFailureRetainsItsOriginalAsyncVoidExceptionBeforeTheWriteTryBlock()
    {
        using var ui = new UpdateUi(_directory, true);
        using var writes = new OwnedDumpTasks();
        var original = new IOException("Owned eligibility failure");
        using var dialog = new OwnedDumpDialog(ui.Window, () => throw original);
        var before = dialog.Controls();
        var status = dialog.Status.Text;
        var seen = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherUnhandledExceptionEventHandler observe = (_, args) =>
        {
            if (!ReferenceEquals(args.Exception, original)) return;
            args.Handled = true;
            seen.TrySetResult(args.Exception);
        };
        ui.Window.Dispatcher.UnhandledException += observe;
        try
        {
            dialog.ClickCreate();
            FinishWindowCheck(seen.Task);
            Assert.That(seen.Task.Result, Is.SameAs(original));
            Assert.That(dialog.Controls(), Is.EqualTo(before));
            Assert.That(dialog.Status.Text, Is.EqualTo(status));
            Assert.That(dialog.Writing, Is.False);
            Assert.That(writes.Set, Is.Empty);
        }
        finally { ui.Window.Dispatcher.UnhandledException -= observe; }
    }

    [TestCase("success"), TestCase("fault"), TestCase("cancel"), Category("Integration")]
    public void DumpDialogCompletedWritesDoNotBlockOrGetPrunedByTheWaiter(string outcome)
    {
        using var ui = new UpdateUi(_directory, true);
        using var writes = new OwnedDumpTasks();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (outcome == "success") completed.SetResult();
        else if (outcome == "fault") completed.SetException(new IOException("Owned completed failure"));
        else completed.SetCanceled();
        writes.Add(completed);

        var wait = ProcessDumpDialog.WhenWritesFinishAsync();

        Assert.That(wait.IsCompletedSuccessfully, Is.True);
        Assert.That(writes.Set, Is.EquivalentTo(new[] { completed.Task }), "Each dialog removes its own write.");
    }

    [Test, Category("Integration")]
    public void DumpDialogWriteWaitSuppressesFailuresAndResnapshotsALatePendingWrite()
    {
        using var ui = new UpdateUi(_directory, true);
        using var writes = new OwnedDumpTasks();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        writes.Add(first);
        var wait = ProcessDumpDialog.WhenWritesFinishAsync();
        Assert.That(wait.IsCompleted, Is.False);
        writes.Add(late);
        first.SetException(new IOException("Owned first write failure"));
        var observedPending = false;
        var turnRan = false;
        // The original await continuation posts at Normal; ApplicationIdle observes its next wait.
        var turn = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            turnRan = true;
            observedPending = !wait.IsCompleted;
            late.TrySetCanceled();
        }));
        try
        {
            FinishWindowCheck(wait);
            Assert.That(turnRan, Is.True);
            Assert.That(observedPending, Is.True, "Late write must remain owned by the waiter after the first fails.");
            Assert.That(wait.IsCompletedSuccessfully, Is.True);
            Assert.That(writes.Set, Is.EquivalentTo(new[] { first.Task, late.Task }));
        }
        finally
        {
            if (turn.Status == DispatcherOperationStatus.Pending) turn.Abort();
            first.TrySetResult();
            late.TrySetResult();
        }
    }

    private sealed class OwnedDumpTasks : IDisposable
    {
        private readonly List<TaskCompletionSource> _owned = [];
        internal HashSet<Task> Set { get; } = (HashSet<Task>)typeof(ProcessDumpDialog)
            .GetField("Writes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        internal OwnedDumpTasks() => Assert.That(Set, Is.Empty, "Filtered host must not have a real pending dump.");
        internal void Add(TaskCompletionSource source) { _owned.Add(source); Set.Add(source.Task); }
        public void Dispose()
        {
            foreach (var source in _owned)
            {
                source.TrySetResult();
                _ = source.Task.Exception;
                Set.Remove(source.Task);
            }
            Assert.That(Set, Is.Empty);
            Assert.That(ProcessDumpDialog.WhenWritesFinishAsync().IsCompletedSuccessfully, Is.True);
        }
    }

    private sealed class OwnedDumpDialog : IDisposable
    {
        private static readonly FieldInfo WritingField = typeof(ProcessDumpDialog)
            .GetField("_writing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        internal ProcessDumpDialog Window { get; }
        internal Button Create { get; }
        internal Button Close { get; }
        internal CheckBox Full { get; }
        internal TextBlock Status { get; }
        internal bool Writing => (bool)WritingField.GetValue(Window)!;
        internal OwnedDumpDialog(System.Windows.Window owner, Func<bool> canDump)
        {
            Window = new ProcessDumpDialog(new ProcessUsage(42, 100, "owned dump target", 0), canDump);
            Assert.That(Window.Dispatcher, Is.SameAs(owner.Dispatcher));
            var layout = (StackPanel)Window.Content;
            var buttons = layout.Children.OfType<StackPanel>().Single().Children.OfType<Button>().ToArray();
            Create = buttons.Single(button => Equals(button.Content, Strings.CreateDumpFile));
            Close = buttons.Single(button => Equals(button.Content, Strings.Close));
            Full = layout.Children.OfType<CheckBox>().Single();
            Status = layout.Children.OfType<TextBlock>().Last();
        }
        internal void SetWriting(bool writing)
        {
            WritingField.SetValue(Window, writing);
            Create.IsEnabled = Close.IsEnabled = Full.IsEnabled = !writing;
            if (writing) Status.Text = Strings.ProcessDumpWriting;
        }
        internal (bool Create, bool Close, bool Full, bool? Checked) Controls() =>
            (Create.IsEnabled, Close.IsEnabled, Full.IsEnabled, Full.IsChecked);
        internal void ClickCreate() => Create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        public void Dispose() { SetWriting(false); Window.Close(); }
    }
}
