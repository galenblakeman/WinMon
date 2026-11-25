using MonCore.Monitoring;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class InteractiveLoopCharacterizationTests
{
    private string _directory = null!;
    [SetUp] public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "WinMon.InteractiveLoop." + Guid.NewGuid().ToString("N"));
    [TearDown] public void TearDown()
    {
        Assert.That(Path.GetFullPath(_directory), Does.StartWith(Path.GetFullPath(Path.GetTempPath())).IgnoreCase);
        Assert.That(Path.GetFileName(_directory), Does.StartWith("WinMon.InteractiveLoop."));
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class Source : IFrameSource
    {
        internal bool Disposed;
        public CapturedFrame Capture(SamplingOptions options) => new(new MonitorSample(
            new DateTimeOffset(2026, 11, 24, 0, 0, 0, TimeSpan.Zero), 25,
            [new ProcessUsage(42, 123, "owned-worker", 25, 100)], 0)
            { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 });
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }

    // Preserve Spectre's renderer/cursor/pipeline; only terminal input belongs to the script.
    private sealed class ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) : IAnsiConsole
    {
        public Profile Profile => inner.Profile;
        public IAnsiConsoleCursor Cursor => inner.Cursor;
        public IAnsiConsoleInput Input => input;
        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;
        public RenderPipeline Pipeline => inner.Pipeline;
        public void Clear(bool home) => inner.Clear(home);
        public void Write(IRenderable renderable) => inner.Write(renderable);
        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);
    }

    private sealed class Input(Func<bool> available, Func<ConsoleKeyInfo?> read) : IAnsiConsoleInput
    {
        public bool IsKeyAvailable() => available();
        public ConsoleKeyInfo? ReadKey(bool intercept) { Assert.That(intercept, Is.True); return read(); }
        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) =>
            throw new AssertionException("The synchronous loop must not start a second asynchronous reader");
    }

    private sealed class Loop : IDisposable
    {
        internal readonly StringWriter Output = new();
        internal readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(10));
        internal readonly Source Source = new();
        internal readonly InteractiveState State = new(ProcessSort.Cpu, "") { HardwareReadAttempted = true };
        internal readonly IAnsiConsole Console;
        internal int Reads;
        private bool _renderedFrame;
        private Func<ConsoleKeyInfo?>[] _steps = [];

        internal Loop(TerminalTab tab = TerminalTab.Processes)
        {
            State.Configure(10, false, false, tab);
            var real = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(Output),
                Ansi = AnsiSupport.Yes, Interactive = InteractionSupport.Yes
            });
            Console = new ScriptedConsole(real, new Input(() =>
            {
                Assert.That(State.NetworkEnabled, Is.False, "The script must not start native network capture");
                if (Stop.IsCancellationRequested || State.DisplayedFrame is null) return false;
                if (!_renderedFrame) { _renderedFrame = true; return false; }
                return true;
            }, () =>
            {
                if (Reads >= _steps.Length) throw new AssertionException("Input script ended without quit or cancellation");
                return _steps[Reads++]();
            }));
            Console.Profile.Width = 90;
            Console.Profile.Height = 28;
        }

        internal int Run(Func<ConsoleKeyInfo?>[] steps, CliSettingsStore? store = null, bool noSave = false)
        {
            _steps = steps;
            return InteractiveMonitor.Run(Console, () => Source, 10, ProcessSort.Cpu, "", Stop.Token,
                initialState: State, settingsStore: store, noSave: noSave);
        }

        internal void AssertRestored()
        {
            Assert.That(Source.Disposed, Is.True);
            Assert.That(Output.ToString(), Does.Contain("\u001b[?1049l").And.Contain("\u001b[?25h"));
        }
        public void Dispose() { Stop.Cancel(); Stop.Dispose(); Output.Dispose(); }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0') => new(character, key, false, false, false);
    private static ConsoleKeyInfo Quit() => Key(ConsoleKey.Q, 'q');

    [TestCase(TerminalTab.Processes), TestCase(TerminalTab.Services), TestCase(TerminalTab.Usage)]
    [TestCase(TerminalTab.Connections), TestCase(TerminalTab.Hardware)]
    public void InteractiveLoopPublishesAnOwnedFrameAndQuitsWithoutNativeDiscovery(TerminalTab tab)
    {
        using var loop = new Loop(tab);
        Assert.That(loop.Run([() => Quit()]), Is.Zero);
        Assert.That(loop.State.Tab, Is.EqualTo(tab));
        Assert.That(loop.State.DisplayedFrame?.Full.Processes.Single().Name, Is.EqualTo("owned-worker"));
        Assert.That(loop.State.Quit, Is.True);
        Assert.That(loop.State.HardwareLoading, Is.False);
        Assert.That(loop.Output.ToString(), Does.Contain("WinMon"));
        loop.AssertRestored();
    }

    [TestCase(false), TestCase(true)]
    public void InteractiveLoopHonorsNoSaveAndFlushesChangedPreferencesOnQuit(bool noSave)
    {
        using var loop = new Loop();
        var store = new CliSettingsStore(Path.Combine(_directory, "settings.json"));
        Assert.That(loop.Run([() => Key(ConsoleKey.G, 'g'), () => Quit()], store, noSave), Is.Zero);
        Assert.That(loop.State.Active.Grouped, Is.True);
        Assert.That(File.Exists(store.Path), Is.EqualTo(!noSave));
        if (!noSave) Assert.That(store.Load().Tabs[(int)TerminalTab.Processes].Grouped, Is.True);
        loop.AssertRestored();
    }

    [Test]
    public void InteractiveLoopPreservesFilterPauseResizeAndNullableInputTransitions()
    {
        using var loop = new Loop();
        Assert.That(loop.Run([
            () => null,
            () => Key(ConsoleKey.Oem2, '/'),
            () => Key(ConsoleKey.W, 'w'),
            () => Key(ConsoleKey.Enter, '\r'),
            () => { Assert.That(loop.State.Filter, Is.EqualTo("w")); return Key(ConsoleKey.Spacebar, ' '); },
            () => { Assert.That(loop.State.Paused, Is.True); loop.Console.Profile.Width = 70; return Key(ConsoleKey.Spacebar, ' '); },
            () => { Assert.That(loop.State.Paused, Is.False); return Quit(); }
        ]), Is.Zero);
        Assert.That(loop.Reads, Is.EqualTo(7));
        Assert.That(loop.State.Filter, Is.EqualTo("w"));
        loop.AssertRestored();
    }

    [Test]
    public void InteractiveLoopRendersBetweenBoundedInputBatchesWithoutDroppingKeys()
    {
        using var loop = new Loop();
        var outputBeforeInput = 0;
        var steps = new List<Func<ConsoleKeyInfo?>> { () => { outputBeforeInput = loop.Output.ToString().Length; return Key(ConsoleKey.Oem2, '/'); } };
        for (var i = 0; i < 40; i++) steps.Add(() =>
        {
            if (loop.Reads == 33) Assert.That(loop.Output.ToString().Length, Is.GreaterThan(outputBeforeInput));
            return Key(ConsoleKey.A, 'a');
        });
        steps.Add(() => Key(ConsoleKey.Enter, '\r'));
        steps.Add(() => Quit());
        Assert.That(loop.Run(steps.ToArray()), Is.Zero);
        Assert.That(loop.State.Filter, Is.EqualTo(new string('a', 40)));
        Assert.That(loop.Reads, Is.EqualTo(43));
        loop.AssertRestored();
    }

    [Test]
    public void InteractiveLoopCancellationAfterDeliveryReturns130AndDisposesTheCollector()
    {
        using var loop = new Loop();
        Assert.That(loop.Run([() => { loop.Stop.Cancel(); return null; }]), Is.EqualTo(130));
        Assert.That(loop.State.Quit, Is.False);
        loop.AssertRestored();
    }

    [Test]
    public void InteractiveLoopInputFailurePreservesTheExceptionAndRestoresTerminalAndCollector()
    {
        using var loop = new Loop();
        var failure = new IOException("Owned input failure");
        Assert.That(Assert.Throws<IOException>(() => loop.Run([() => throw failure])), Is.SameAs(failure));
        loop.AssertRestored();
    }

    [Test]
    public void InteractiveLoopSettingsFailureIsReportedWithoutLosingQuitOrCleanup()
    {
        using var loop = new Loop();
        var store = new CliSettingsStore(Path.Combine(_directory, "settings.json"));
        Directory.CreateDirectory(store.Path);
        Assert.That(loop.Run([() => Key(ConsoleKey.G, 'g'), () => Quit()], store), Is.Zero);
        Assert.That(loop.State.SettingsSaveFailed, Is.True);
        Assert.That(Directory.Exists(store.Path), Is.True);
        loop.AssertRestored();
    }
}
