using Win32Exception = System.ComponentModel.Win32Exception;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable, Apartment(ApartmentState.STA)]
public sealed class CpuControlNavigationCharacterizationTests
{
    private static readonly MonitorSample[] Samples = new[] { 0, 10, 20 }
        .Select(second => new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, [], 0)).ToArray();

    private static CpuGraph Graph(params ChartMarker[] markers)
    {
        var graph = new CpuGraph { ShowGrid = true, Markers = markers };
        graph.SetData(Samples, 30, Samples[1]);
        return graph;
    }

    private static ChartMarker Marker(int second, bool selected = false) =>
        new(CpuGraph.Moment(Samples[0]) + second, "Cpu", "fixture marker", selected, new object());

    private static KeyEventArgs Input(Key key) => new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key)
        { RoutedEvent = Keyboard.KeyDownEvent };

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    public void ControlArrowPublishesTheAdjacentMarkerBeforeHandlingWithoutSampleOrLiveEvents(Key key)
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control);
        var before = Marker(5);
        var after = Marker(15);
        var graph = Graph(before, after);
        var input = Input(key);
        var published = new List<ChartMarker>();
        graph.SampleSelected += (_, _) => Assert.Fail("Control navigation must not publish a sample.");
        graph.LiveRequested += (_, _) => Assert.Fail("Control navigation must not request live mode.");
        graph.MarkerSelected += (sender, marker) =>
        {
            Assert.That(sender, Is.SameAs(graph));
            Assert.That(input.Handled, Is.False);
            published.Add(marker);
        };
        graph.RaiseEvent(input);
        Assert.That(published, Is.EqualTo(new[] { key == Key.Left ? before : after }));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Left, false)]
    [TestCase(Key.Right, false)]
    [TestCase(Key.Left, true)]
    [TestCase(Key.Right, true)]
    public void ControlArrowWithoutAnAdjacentMarkerIsHandledWithoutPublishing(Key key, bool boundary)
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control);
        var graph = boundary ? Graph(Marker(key == Key.Left ? 5 : 15, true)) : Graph();
        graph.SampleSelected += (_, _) => Assert.Fail("No adjacent marker must not fall back to a sample.");
        graph.MarkerSelected += (_, _) => Assert.Fail("No adjacent marker exists.");
        graph.LiveRequested += (_, _) => Assert.Fail("No live mode request is expected.");
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(input.Handled, Is.True);
    }

    [Test]
    public void OtherModifierCombinationsKeepOrdinarySampleNavigation(
        [Values(Key.Left, Key.Right)] Key key,
        [Values(ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Control | ModifierKeys.Alt,
            ModifierKeys.Shift)] ModifierKeys modifiers)
    {
        using var state = new ThreadKeyboardState(modifiers);
        var graph = Graph(Marker(5), Marker(15));
        var samples = new List<MonitorSample>();
        graph.SampleSelected += (_, sample) => samples.Add(sample);
        graph.MarkerSelected += (_, _) => Assert.Fail("Only Control alone navigates markers.");
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(samples, Is.EqualTo(new[] { Samples[key == Key.Left ? 0 : 2] }));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    public void WindowsKeyInTheThreadTableDoesNotChangeWpfControlOnlyMarkerNavigation(Key key)
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control | ModifierKeys.Windows, ModifierKeys.Control);
        Assert.That(state.IsPressed(0x5B), Is.True);
        var before = Marker(5);
        var after = Marker(15);
        var graph = Graph(before, after);
        var markers = new List<ChartMarker>();
        graph.MarkerSelected += (_, marker) => markers.Add(marker);
        graph.SampleSelected += (_, _) => Assert.Fail("WPF reports Control alone for this thread table.");
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(markers, Is.EqualTo(new[] { key == Key.Left ? before : after }));
        Assert.That(input.Handled, Is.True);
    }

    [TestCase(Key.Left)]
    [TestCase(Key.Right)]
    public void MarkerSubscriberFailurePropagatesBeforeHandlingAndTheNextCommandCanRetry(Key key)
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control);
        var graph = Graph(Marker(5), Marker(15));
        var failure = new InvalidOperationException("marker subscriber failure");
        EventHandler<ChartMarker> subscriber = (_, _) => throw failure;
        graph.MarkerSelected += subscriber;
        var input = Input(key);
        Assert.That(Assert.Throws<InvalidOperationException>(() => graph.RaiseEvent(input)), Is.SameAs(failure));
        Assert.That(input.Handled, Is.False);
        graph.MarkerSelected -= subscriber;
        var retry = Input(key);
        graph.RaiseEvent(retry);
        Assert.That(retry.Handled, Is.True);
    }

    [Test]
    public void MarkerSelectionAdvancesOnlyWhenTheHostSuppliesUpdatedMarkers()
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control);
        var before = Marker(5);
        var after = Marker(15);
        var graph = Graph(before, after);
        var published = new List<ChartMarker>();
        graph.MarkerSelected += (_, marker) => published.Add(marker);
        graph.RaiseEvent(Input(Key.Left));
        graph.RaiseEvent(Input(Key.Left));
        Assert.That(published, Is.EqualTo(new[] { before, before }));
        graph.Markers = [before with { Selected = true }, after];
        graph.RaiseEvent(Input(Key.Left));
        Assert.That(published, Has.Count.EqualTo(2));
        graph.RaiseEvent(Input(Key.Right));
        Assert.That(published, Is.EqualTo(new[] { before, before, after }));
    }

    [TestCase(Key.Home)]
    [TestCase(Key.End)]
    public void ControlDoesNotChangeHomeOrEndContracts(Key key)
    {
        using var state = new ThreadKeyboardState(ModifierKeys.Control);
        var graph = Graph(Marker(5), Marker(15));
        var samples = new List<MonitorSample>();
        var live = 0;
        graph.SampleSelected += (_, sample) => samples.Add(sample);
        graph.LiveRequested += (_, _) => live++;
        graph.MarkerSelected += (_, _) => Assert.Fail("Home and End must not navigate markers.");
        var input = Input(key);
        graph.RaiseEvent(input);
        Assert.That(samples, Is.EqualTo(key == Key.Home ? new[] { Samples[0] } : Array.Empty<MonitorSample>()));
        Assert.That(live, Is.EqualTo(key == Key.End ? 1 : 0));
        Assert.That(input.Handled, Is.True);
    }

    private sealed class ThreadKeyboardState : IDisposable
    {
        private readonly byte[] _original = new byte[256];
        private readonly int _thread = Environment.CurrentManagedThreadId;

        internal ThreadKeyboardState(ModifierKeys modifiers, ModifierKeys? expectedWpfModifiers = null)
        {
            Check(GetKeyboardState(_original));
            var replacement = (byte[])_original.Clone();
            foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                replacement[key] = 0;
            if (modifiers.HasFlag(ModifierKeys.Control)) replacement[0x11] = replacement[0xA2] = 0x80;
            if (modifiers.HasFlag(ModifierKeys.Shift)) replacement[0x10] = replacement[0xA0] = 0x80;
            if (modifiers.HasFlag(ModifierKeys.Alt)) replacement[0x12] = replacement[0xA4] = 0x80;
            if (modifiers.HasFlag(ModifierKeys.Windows)) replacement[0x5B] = 0x80;
            Check(SetKeyboardState(replacement));
            try { Assert.That(Keyboard.Modifiers, Is.EqualTo(expectedWpfModifiers ?? modifiers)); }
            catch { Check(SetKeyboardState(_original)); throw; }
        }

        internal bool IsPressed(int key)
        {
            var current = new byte[256];
            Check(GetKeyboardState(current));
            return (current[key] & 0x80) != 0;
        }

        public void Dispose()
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(_thread));
            Check(SetKeyboardState(_original));
            var restored = new byte[256];
            Check(GetKeyboardState(restored));
            Assert.That(restored, Is.EqualTo(_original));
        }

        private static void Check(bool success)
        {
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // Same-thread state only: no SendInput, focus change, message pump or asynchronous work.
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetKeyboardState([Out] byte[] state);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetKeyboardState([In] byte[] state);
    }

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
