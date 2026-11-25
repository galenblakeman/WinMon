using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class ResourceTrendKeyboardCharacterizationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static ResourceTrendBucket[] Buckets(int count) => Enumerable.Range(0, count)
        .Select(index => new ResourceTrendBucket(Start.AddMinutes(index),
            new Dictionary<ResourceTrendMetric, ResourceTrendValue>
                { [ResourceTrendMetric.Cpu] = new(index * 60, 60, index, index, "", ResourceTrendBreak.None) }))
        .ToArray();

    private static ResourceTrendGraph Graph(ResourceTrendBucket[] buckets, int selected = -1)
    {
        var graph = new ResourceTrendGraph();
        graph.SetData(buckets, ResourceTrendMetric.Cpu, Start, Start.AddMinutes(Math.Max(1, buckets.Length)),
            selected < 0 ? null : buckets[selected]);
        return graph;
    }

    private static KeyEventArgs Input(Key key) =>
        new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent };

    [TestCase(1, -1, Key.Left, 0)]
    [TestCase(1, -1, Key.Right, 0)]
    [TestCase(1, -1, Key.Home, 0)]
    [TestCase(1, -1, Key.End, 0)]
    [TestCase(1, 0, Key.Left, 0)]
    [TestCase(1, 0, Key.Right, 0)]
    [TestCase(5, -1, Key.Left, 4)]
    [TestCase(5, -1, Key.Right, 0)]
    [TestCase(5, -1, Key.Home, 0)]
    [TestCase(5, -1, Key.End, 4)]
    [TestCase(5, 0, Key.Left, 0)]
    [TestCase(5, 0, Key.Right, 1)]
    [TestCase(5, 2, Key.Left, 1)]
    [TestCase(5, 2, Key.Right, 3)]
    [TestCase(5, 2, Key.Home, 0)]
    [TestCase(5, 2, Key.End, 4)]
    [TestCase(5, 4, Key.Left, 3)]
    [TestCase(5, 4, Key.Right, 4)]
    public void NavigationSelectsTheOriginalBucketAtEachBoundary(int count, int initial, Key key, int expected)
    {
        var buckets = Buckets(count);
        var graph = Graph(buckets, initial);
        var selected = new List<(object? Sender, ResourceTrendBucket Bucket)>();
        var input = Input(key);
        graph.BucketSelected += (sender, bucket) =>
        {
            Assert.That(input.Handled, Is.False);
            selected.Add((sender, bucket));
        };
        graph.RaiseEvent(input);
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(selected[0].Sender, Is.SameAs(graph));
            Assert.That(selected[0].Bucket, Is.SameAs(buckets[expected]));
            Assert.That(input.Handled, Is.True);
        });
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    [TestCase(Key.Home)]
    [TestCase(Key.End)]
    public void EmptyGraphsLeaveNavigationUnhandledWithoutNotifying(Key key)
    {
        var graph = Graph([]);
        var notifications = 0;
        graph.BucketSelected += (_, _) => notifications++;
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.Multiple(() =>
        {
            Assert.That(notifications, Is.Zero);
            Assert.That(input.Handled, Is.False);
        });
    }

    [TestCase(Key.Up)]
    [TestCase(Key.Down)]
    [TestCase(Key.Enter)]
    [TestCase(Key.Escape)]
    public void UnsupportedKeysPreserveSelectionForTheNextNavigation(Key key)
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets, 2);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => selected.Add(bucket);
        var ignored = Input(key);
        graph.RaiseEvent(ignored);
        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.Empty);
            Assert.That(ignored.Handled, Is.False);
        });
        var next = Input(Key.Right);
        graph.RaiseEvent(next);
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(selected[0], Is.SameAs(buckets[3]));
        Assert.That(next.Handled, Is.True);
    }

    [TestCase(Key.Left, 4)]
    [TestCase(Key.Right, 1)]
    [TestCase(Key.Home, 1)]
    [TestCase(Key.End, 4)]
    public void NavigationWithoutSubscribersStillStoresSelectionAndHandlesTheKey(Key key, int nextExpected)
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets);
        var first = Input(key);
        graph.RaiseEvent(first);
        Assert.That(first.Handled, Is.True);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => selected.Add(bucket);
        var next = Input(Key.Right);
        graph.RaiseEvent(next);
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(selected[0], Is.SameAs(buckets[nextExpected]));
        Assert.That(next.Handled, Is.True);
    }

    [TestCase(Key.Left, 0)]
    [TestCase(Key.Right, 4)]
    [TestCase(Key.Home, 0)]
    [TestCase(Key.End, 4)]
    public void RepeatedBoundaryKeysNotifyAgainWithoutWrapping(Key key, int boundary)
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets, boundary);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => selected.Add(bucket);
        for (var repeat = 0; repeat < 3; repeat++)
        {
            var input = Input(key);
            graph.RaiseEvent(input);
            Assert.That(input.Handled, Is.True);
        }
        Assert.That(selected, Has.Count.EqualTo(3));
        Assert.That(selected.All(bucket => ReferenceEquals(bucket, buckets[boundary])), Is.True);
    }

    [Test]
    public void SubscriberFailureKeepsAdvancedSelectionAndPrecedesTheHandledAssignment()
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets, 2);
        var input = Input(Key.Right);
        var failure = new InvalidOperationException("Owned trend subscriber failure");
        EventHandler<ResourceTrendBucket> handler = (_, bucket) =>
        {
            Assert.That(bucket, Is.SameAs(buckets[3]));
            Assert.That(input.Handled, Is.False);
            throw failure;
        };
        graph.BucketSelected += handler;
        var actual = Assert.Throws<InvalidOperationException>(() => graph.RaiseEvent(input));
        Assert.That(actual, Is.SameAs(failure));
        Assert.That(input.Handled, Is.False);
        graph.BucketSelected -= handler;
        var recovered = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => recovered.Add(bucket);
        var next = Input(Key.Right);
        graph.RaiseEvent(next);
        Assert.That(recovered, Has.Count.EqualTo(1));
        Assert.That(recovered[0], Is.SameAs(buckets[4]));
        Assert.That(next.Handled, Is.True);
    }

    [Test]
    public void SubscriberCanClearDataBeforeTheSuccessfulKeyIsMarkedHandled()
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets);
        var input = Input(Key.Home);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) =>
        {
            Assert.That(input.Handled, Is.False);
            selected.Add(bucket);
            graph.SetData([], ResourceTrendMetric.Cpu, Start, Start.AddMinutes(1), null);
        };
        graph.RaiseEvent(input);
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(selected[0], Is.SameAs(buckets[0]));
        Assert.That(input.Handled, Is.True);
        var next = Input(Key.Right);
        graph.RaiseEvent(next);
        Assert.That(next.Handled, Is.False);
        Assert.That(selected, Has.Count.EqualTo(1));
    }

    [Test]
    public void DataRefreshRecognizesAnEqualSelectionAndDiscardsAStaleOne()
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => selected.Add(bucket);
        graph.SetData(buckets, ResourceTrendMetric.Cpu, Start, Start.AddMinutes(5), buckets[2] with { });
        Assert.That(selected, Is.Empty);
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(selected[0], Is.SameAs(buckets[3]));
        graph.SetData(buckets, ResourceTrendMetric.Cpu, Start, Start.AddMinutes(5),
            buckets[0] with { StartUtc = Start.AddMinutes(-1) });
        Assert.That(selected, Has.Count.EqualTo(1));
        graph.RaiseEvent(Input(Key.Left));
        Assert.That(selected, Has.Count.EqualTo(2));
        Assert.That(selected[1], Is.SameAs(buckets[4]));
    }

    [Test]
    public void AnAlreadyHandledKeyDoesNotNotifyOrAdvanceSelection()
    {
        var buckets = Buckets(5);
        var graph = Graph(buckets, 2);
        var selected = new List<ResourceTrendBucket>();
        graph.BucketSelected += (_, bucket) => selected.Add(bucket);
        var ignored = Input(Key.Right);
        ignored.Handled = true;
        graph.RaiseEvent(ignored);
        Assert.That(selected, Is.Empty);
        Assert.That(ignored.Handled, Is.True);
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(selected, Has.Count.EqualTo(1));
        Assert.That(selected[0], Is.SameAs(buckets[3]));
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
