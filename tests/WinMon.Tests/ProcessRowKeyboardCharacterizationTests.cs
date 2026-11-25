using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessRowKeyboardCharacterizationTests
{
    private static readonly ProcessUsage First = new(8123, 100, "first.exe", 10);
    private static readonly ProcessUsage Second = new(8123, 200, "second.exe", 20);

    private static ProcessRow Row(ProcessUsage? process = null, bool grouped = false)
    {
        var row = new ProcessRow();
        row.EnableProcessActions(_ => false, _ => Assert.Fail("No termination may run"));
        if (process is not null) Update(row, process, grouped);
        return row;
    }

    private static void Update(ProcessRow row, ProcessUsage process, bool grouped = false,
        PinnedProcessState state = PinnedProcessState.Present) =>
        row.Update(process, [new MonitorSample(DateTimeOffset.UnixEpoch, 20, [process], 0)],
            120, false, null, grouped, state);

    private static KeyEventArgs Input(Key key) => new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key)
        { RoutedEvent = Keyboard.KeyDownEvent };

    [TestCase(Key.Enter, false)]
    [TestCase(Key.Enter, true)]
    [TestCase(Key.Space, false)]
    [TestCase(Key.Space, true)]
    public void AcceptedKeysSelectTheCurrentObservationBeforeHandlingTheEvent(Key key, bool grouped)
    {
        var row = Row(First, grouped);
        var input = Input(key);
        var calls = new List<(ProcessUsage Process, bool Grouped)>();
        row.ProcessSelected += (process, mode) =>
        {
            Assert.That(input.Handled, Is.False);
            calls.Add((process, mode));
        };
        row.RaiseEvent(input);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(calls[0].Process, Is.SameAs(First));
            Assert.That(calls[0].Grouped, Is.EqualTo(grouped));
            Assert.That(input.Handled, Is.True);
        });
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void EmptyRowsLeaveSelectionKeysUnhandledWithoutNotifying(Key key)
    {
        var row = Row();
        var calls = 0;
        row.ProcessSelected += (_, _) => calls++;
        var input = Input(key);
        row.RaiseEvent(input);
        Assert.That(calls, Is.Zero);
        Assert.That(input.Handled, Is.False);
    }

    [TestCase(Key.None)]
    [TestCase(Key.Escape)]
    [TestCase(Key.Right)]
    [TestCase(Key.Tab)]
    public void OtherKeysDoNotSelectOrConsumeTheEvent(Key key)
    {
        var row = Row(First);
        var calls = 0;
        row.ProcessSelected += (_, _) => calls++;
        var input = Input(key);
        row.RaiseEvent(input);
        Assert.That(calls, Is.Zero);
        Assert.That(input.Handled, Is.False);
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void AcceptedKeysAreHandledEvenWithoutSubscribers(Key key)
    {
        var row = Row(First);
        var input = Input(key);
        row.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
        ProcessUsage? selected = null;
        row.ProcessSelected += (process, _) => selected = process;
        row.RaiseEvent(Input(key));
        Assert.That(selected, Is.SameAs(First));
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void AReusedRowSelectsTheNewLifetimeAndGrouping(Key key)
    {
        var row = Row(First);
        Update(row, Second, true);
        var calls = new List<(ProcessUsage Process, bool Grouped)>();
        row.ProcessSelected += (process, grouped) => calls.Add((process, grouped));
        row.RaiseEvent(Input(key));
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].Process, Is.SameAs(Second));
        Assert.That(calls[0].Grouped, Is.True);
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void RepeatedSelectionKeysNotifyAgainWithoutReplacingTheObservation(Key key)
    {
        var row = Row(First);
        var calls = new List<ProcessUsage>();
        row.ProcessSelected += (process, _) => calls.Add(process);
        var first = Input(key);
        var second = Input(key);
        row.RaiseEvent(first);
        row.RaiseEvent(second);
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[0], Is.SameAs(First));
        Assert.That(calls[1], Is.SameAs(First));
        Assert.That(first.Handled && second.Handled, Is.True);
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void SubscriberFailureEscapesBeforeHandlingAndStopsLaterSubscribers(Key key)
    {
        var row = Row(First);
        var failure = new InvalidOperationException("selection failure");
        var laterCalls = 0;
        void Fail(ProcessUsage process, bool grouped)
        {
            Assert.That(process, Is.SameAs(First));
            Assert.That(grouped, Is.False);
            throw failure;
        }
        row.ProcessSelected += Fail;
        row.ProcessSelected += (_, _) => laterCalls++;
        var input = Input(key);
        Assert.That(Assert.Throws<InvalidOperationException>(() => row.RaiseEvent(input)), Is.SameAs(failure));
        Assert.That(input.Handled, Is.False);
        Assert.That(laterCalls, Is.Zero);
        row.ProcessSelected -= Fail;
        var next = Input(key);
        row.RaiseEvent(next);
        Assert.That(laterCalls, Is.EqualTo(1));
        Assert.That(next.Handled, Is.True);
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void ASubscriberRefreshAffectsOnlyTheNextKeyDispatch(Key key)
    {
        var row = Row(First);
        var calls = new List<(ProcessUsage Process, bool Grouped)>();
        row.ProcessSelected += (process, grouped) =>
        {
            calls.Add((process, grouped));
            Update(row, Second, true);
        };
        var first = Input(key);
        var second = Input(key);
        row.RaiseEvent(first);
        row.RaiseEvent(second);
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(calls[0].Process, Is.SameAs(First));
            Assert.That(calls[0].Grouped, Is.False);
            Assert.That(calls[1].Process, Is.SameAs(Second));
            Assert.That(calls[1].Grouped, Is.True);
            Assert.That(first.Handled && second.Handled, Is.True);
        });
    }

    [TestCase(Key.Enter)]
    [TestCase(Key.Space)]
    public void AlreadyHandledRoutingDoesNotNotifyButTheNextKeyStillSelects(Key key)
    {
        var row = Row(First);
        var calls = 0;
        row.ProcessSelected += (_, _) => calls++;
        var ignored = Input(key);
        ignored.Handled = true;
        row.RaiseEvent(ignored);
        Assert.That(calls, Is.Zero);
        Assert.That(ignored.Handled, Is.True);
        row.RaiseEvent(Input(key));
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase(Key.Enter, 0)]
    [TestCase(Key.Space, 0)]
    [TestCase(Key.Escape, 1)]
    public void OnlyUnconsumedKeysReachTheOrdinaryParentHandler(Key key, int expected)
    {
        var row = Row(First);
        var parent = new Grid();
        parent.Children.Add(row);
        var calls = 0;
        parent.KeyDown += (_, _) => calls++;
        var input = Input(key);
        row.RaiseEvent(input);
        Assert.That(calls, Is.EqualTo(expected));
        Assert.That(input.Handled, Is.EqualTo(expected == 0));
    }

    [TestCase(false, PinnedProcessState.Exited)]
    [TestCase(true, PinnedProcessState.Exited)]
    [TestCase(false, PinnedProcessState.NotRecorded)]
    [TestCase(true, PinnedProcessState.Unavailable)]
    public void UnavailableAndGroupedObservationsRemainSelectable(bool grouped, PinnedProcessState state)
    {
        var row = Row();
        Update(row, First, grouped, state);
        var calls = new List<(ProcessUsage Process, bool Grouped)>();
        row.ProcessSelected += (process, mode) => calls.Add((process, mode));
        var input = Input(Key.Enter);
        row.RaiseEvent(input);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].Process, Is.SameAs(First));
        Assert.That(calls[0].Grouped, Is.EqualTo(grouped));
        Assert.That(input.Handled, Is.True);
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
