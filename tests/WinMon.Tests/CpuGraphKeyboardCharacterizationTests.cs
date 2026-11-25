using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class CpuGraphKeyboardCharacterizationTests
{
    private static MonitorSample[] Samples() => new[] { 0, 9, 10, 11, 20 }
        .Select(second => new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, [], 0))
        .ToArray();

    private static CpuGraph Graph(MonitorSample[] samples, MonitorSample? selected = null)
    {
        var graph = new CpuGraph { ShowGrid = true };
        graph.SetData(samples, 10, selected);
        return graph;
    }

    private static KeyEventArgs Input(Key key) =>
        new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent };

    [TestCase(-1, Key.Left, 3)]
    [TestCase(-1, Key.Right, 4)]
    [TestCase(-1, Key.Home, 2)]
    [TestCase(2, Key.Left, 2)]
    [TestCase(2, Key.Right, 3)]
    [TestCase(3, Key.Left, 2)]
    [TestCase(3, Key.Right, 4)]
    [TestCase(3, Key.Home, 2)]
    [TestCase(4, Key.Left, 3)]
    [TestCase(4, Key.Right, 4)]
    [TestCase(0, Key.Left, 2)]
    [TestCase(0, Key.Right, 2)]
    [TestCase(0, Key.Home, 2)]
    public void NavigationUsesTheInclusiveVisibleWindowAndClampsWithoutWrapping(int initial, Key key, int expected)
    {
        var samples = Samples();
        var graph = Graph(samples, initial < 0 ? null : samples[initial]);
        var input = Input(key);
        var selected = new List<MonitorSample>();
        graph.SampleSelected += (sender, sample) =>
        {
            Assert.That(sender, Is.SameAs(graph));
            Assert.That(input.Handled, Is.False);
            selected.Add(sample);
        };
        graph.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected.Single(), Is.SameAs(samples[expected]));
            Assert.That(input.Handled, Is.True);
        });
    }

    [TestCase(false, false, Key.Left)]
    [TestCase(false, false, Key.Right)]
    [TestCase(false, false, Key.Home)]
    [TestCase(false, false, Key.End)]
    [TestCase(false, true, Key.Left)]
    [TestCase(false, true, Key.Right)]
    [TestCase(false, true, Key.Home)]
    [TestCase(false, true, Key.End)]
    [TestCase(true, true, Key.Left)]
    [TestCase(true, true, Key.Right)]
    [TestCase(true, true, Key.Home)]
    [TestCase(true, true, Key.End)]
    public void SparklinesAndEmptyGraphsLeaveNavigationUnhandledWithoutPublishing(bool grid, bool empty, Key key)
    {
        var graph = Graph(empty ? [] : Samples());
        graph.ShowGrid = grid;
        var notifications = 0;
        graph.SampleSelected += (_, _) => notifications++;
        graph.LiveRequested += (_, _) => notifications++;
        graph.MarkerSelected += (_, _) => notifications++;
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(notifications, Is.Zero);
            Assert.That(input.Handled, Is.False);
        });
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Home)]
    public void EqualValuedSelectionOutsideTheSeriesUsesReferenceIdentity(Key key)
    {
        var samples = Samples();
        var graph = Graph(samples, samples[3] with { });
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(selected, Is.SameAs(samples[2]));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Home)]
    public void ARecordedGapIsStillTheOriginalNavigableSample(Key key)
    {
        var gap = Samples()[0] with { IsGap = true, TotalCpuPercent = null };
        var graph = Graph([gap]);
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(selected, Is.SameAs(gap));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Up)]
    [TestCase(Key.Down)]
    [TestCase(Key.Enter)]
    [TestCase(Key.Escape)]
    public void OtherKeysDoNotPublishOrChangeTheNextNavigation(Key key)
    {
        var samples = Samples();
        var graph = Graph(samples, samples[3]);
        var selected = new List<MonitorSample>();
        var live = 0;
        graph.SampleSelected += (_, sample) => selected.Add(sample);
        graph.LiveRequested += (_, _) => live++;
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(input.Handled, Is.False);
        Assert.That(selected, Is.Empty);
        Assert.That(live, Is.Zero);
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Is.EqualTo(new[] { samples[4] }));
    }

    [Test]
    public void EndRequestsLiveWithoutPublishingASampleAndHandlesAfterTheCallback()
    {
        var samples = Samples();
        var graph = Graph(samples, samples[2]);
        var input = Input(Key.End);
        var calls = 0;
        graph.SampleSelected += (_, _) => Assert.Fail("End must request live rather than a sample.");
        graph.LiveRequested += (sender, args) =>
        {
            Assert.That(sender, Is.SameAs(graph));
            Assert.That(args, Is.SameAs(EventArgs.Empty));
            Assert.That(input.Handled, Is.False);
            calls++;
        };
        graph.RaiseEvent(input);
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Home)]
    [TestCase(Key.End)]
    public void NavigationWithoutSubscribersIsHandledWithoutAdvancingStoredSelection(Key key)
    {
        var samples = Samples();
        var graph = Graph(samples, samples[3]);
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Is.SameAs(samples[4]));
    }

    [Test]
    public void OnlyHostFeedbackAdvancesRepeatedNavigation()
    {
        var samples = Samples();
        var graph = Graph(samples);
        var selected = new List<MonitorSample>();
        EventHandler<MonitorSample> observe = (_, sample) => selected.Add(sample);
        graph.SampleSelected += observe;
        graph.RaiseEvent(Input(Key.Left));
        graph.RaiseEvent(Input(Key.Left));
        Assert.That(selected, Is.EqualTo(new[] { samples[3], samples[3] }));
        graph.SampleSelected += (_, sample) => graph.SetData(samples, 10, sample);
        graph.RaiseEvent(Input(Key.Left));
        graph.RaiseEvent(Input(Key.Left));
        Assert.That(selected, Is.EqualTo(new[] { samples[3], samples[3], samples[3], samples[2] }));
    }

    [TestCase(Key.Home)]
    [TestCase(Key.End)]
    public void SubscriberExceptionsPropagateBeforeTheHandledAssignment(Key key)
    {
        var samples = Samples();
        var graph = Graph(samples, samples[3]);
        var expected = new InvalidOperationException("subscriber");
        EventHandler<MonitorSample> sampleFailure = (_, _) => throw expected;
        EventHandler liveFailure = (_, _) => throw expected;
        graph.SampleSelected += sampleFailure;
        graph.LiveRequested += liveFailure;
        var input = Input(key);
        Assert.That(Assert.Throws<InvalidOperationException>(() => graph.RaiseEvent(input)), Is.SameAs(expected));
        Assert.That(input.Handled, Is.False);
        graph.SampleSelected -= sampleFailure;
        graph.LiveRequested -= liveFailure;
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Is.SameAs(samples[4]));
    }

    [Test]
    public void ReentrantReplacementAppliesToTheNextKeyWithoutChangingThePublishedSample()
    {
        var samples = Samples();
        var replacement = Samples().Select(sample => sample with { TotalCpuPercent = 99 }).ToArray();
        var graph = Graph(samples, samples[3]);
        var selected = new List<MonitorSample>();
        graph.SampleSelected += (_, sample) =>
        {
            selected.Add(sample);
            if (selected.Count == 1) graph.SetData(replacement, 10, replacement[2]);
        };
        var input = Input(Key.Left);
        graph.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Has.Count.EqualTo(2));
        Assert.That(selected[0], Is.SameAs(samples[2]));
        Assert.That(selected[1], Is.SameAs(replacement[3]));
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
