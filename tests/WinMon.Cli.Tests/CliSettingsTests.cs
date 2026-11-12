using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class CliSettingsTests
{
    private string _directory = null!;
    private CliSettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cli-settings-" + Guid.NewGuid().ToString("N"));
        _store = new CliSettingsStore(Path.Combine(_directory, "cli-settings.json"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private static void Key(InteractiveState state, ConsoleKey key, char character = '\0') =>
        state.Handle(new ConsoleKeyInfo(character, key, false, false, false), 5);

    private static InteractiveState State(CliPreferences preferences)
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.RestorePreferences(preferences);
        return state;
    }

    [Test]
    [Category("Integration")]
    public void NetworkShortcutTogglesAndSavesFromEveryTabButNotWhileTypingFilter()
    {
        var state = State(new());
        var session = new CliSettingsSession(_store, state, () => Assert.Fail("Unexpected save failure"));
        foreach (var tabKey in new[] { ConsoleKey.F1, ConsoleKey.F2, ConsoleKey.F3, ConsoleKey.F4 })
        {
            Key(state, tabKey);
            Key(state, ConsoleKey.B, 'b');
            Assert.That(state.NetworkEnabled, Is.False);
            Key(state, ConsoleKey.B, 'b');
            Assert.That(state.NetworkEnabled, Is.True);
        }
        Key(state, ConsoleKey.Oem2, '/');
        Key(state, ConsoleKey.B, 'b');
        Assert.That(state.Draft, Is.EqualTo("b"));
        Assert.That(state.NetworkEnabled, Is.True);
        Key(state, ConsoleKey.Escape);
        Key(state, ConsoleKey.B, 'b');
        session.Observe(); session.Flush(force: true);
        Assert.That(_store.Load().Network, Is.False);
    }

    [Test]
    [Category("Integration")]
    public void MissingFileUsesDefaultsWithoutCreatingAnything()
    {
        Assert.That(_store.Load().Tabs, Has.Length.EqualTo(4));
        Assert.That(_store.Load().Network, Is.True);
        Assert.That(_store.Load().Tabs.Select(tab => tab.Top), Is.EqualTo(new[] { 10, 10, 10, 10 }));
        Assert.That(File.Exists(_store.Path), Is.False);
    }

    [TestCase(null, new[] { 10, 10, 10, 10 })]
    [TestCase("{\"tabs\":[{\"top\":0},{\"top\":5},{\"top\":20},{\"top\":50}]}", new[] { 0, 5, 20, 50 })]
    [Category("Integration")]
    public async Task WatchUsesTopTenOnEveryTabUnlessSaved(string? json, int[] expected)
    {
        if (json is not null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_store.Path, json);
        }
        using var output = new StringWriter();
        var invoked = false;
        var command = CliCommand.Create(output: output, error: output, settingsStore: _store, runWatch: (state, _) =>
        {
            invoked = true;
            Assert.That(state.CapturePreferences().Tabs.Select(tab => tab.Top), Is.EqualTo(expected));
            return 0;
        });
        Assert.That(await command.Parse(["watch"]).InvokeAsync(), Is.Zero, output.ToString());
        Assert.That(invoked, Is.True);
    }

    [TestCase(null, true)]
    [TestCase("{}", true)]
    [TestCase("{\"network\":false}", false)]
    [Category("Integration")]
    public async Task WatchUsesDefaultNetworkPreferenceAndPreservesSavedOptOut(string? json, bool expected)
    {
        if (json is not null)
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(_store.Path, json);
        }
        using var output = new StringWriter();
        var invoked = false;
        var command = CliCommand.Create(output: output, error: output, settingsStore: _store, runWatch: (state, _) =>
        {
            invoked = true;
            Assert.That(state.NetworkEnabled, Is.EqualTo(expected));
            return 0;
        });
        Assert.That(await command.Parse(["watch"]).InvokeAsync(), Is.Zero, output.ToString());
        Assert.That(invoked, Is.True);
        Assert.That(File.Exists(_store.Path), Is.EqualTo(json is not null));
        if (json is not null) Assert.That(File.ReadAllText(_store.Path), Is.EqualTo(json));
    }

    [Test]
    [Category("Integration")]
    public void RestoresPerTabControlsButNotFilterPauseSelectionOrDetails()
    {
        var state = State(new());
        var session = new CliSettingsSession(_store, state, () => Assert.Fail("Unexpected save failure"));
        Key(state, ConsoleKey.G); Key(state, ConsoleKey.A); Key(state, ConsoleKey.T);
        session.Observe();
        Key(state, ConsoleKey.F2); Key(state, ConsoleKey.W); Key(state, ConsoleKey.S);
        state.Active.Filter = state.Active.Draft = "temporary";
        Key(state, ConsoleKey.Spacebar);
        Assert.That(state.Paused, Is.True);
        state.Active.Details = true;
        state.Active.SelectedKey = "temporary";
        session.Observe();
        session.Flush(force: true);
        var restored = State(_store.Load());
        Assert.Multiple(() =>
        {
            Assert.That(restored.Tab, Is.EqualTo(TerminalTab.Services));
            Assert.That(restored.Active.Status, Is.EqualTo(1));
            Assert.That(restored.Active.Workers, Is.False);
            Assert.That(restored.Filter, Is.Empty);
            Assert.That(restored.Paused, Is.False);
            Assert.That(restored.Active.Details, Is.False);
            Assert.That(restored.Active.SelectedKey, Is.Null);
            Assert.That(restored.CapturePreferences().Tabs[0].Grouped, Is.True);
            Assert.That(restored.CapturePreferences().Tabs[0].AppsOnly, Is.True);
            Assert.That(restored.CapturePreferences().Tabs[0].Top, Is.EqualTo(15));
        });
    }

    [Test]
    [Category("Integration")]
    public async Task ArgumentsOverrideSavedSettingsWithoutSavingThemOrOtherOverriddenFields()
    {
        _store.Update(settings =>
        {
            settings.ActiveTab = TerminalTab.Connections; settings.Network = true;
            settings.Tabs[0].Grouped = true; settings.Tabs[2].AppsOnly = true; settings.Tabs[2].Top = 20;
        });
        using var output = new StringWriter();
        using var error = new StringWriter();
        var invoked = false;
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store, runWatch: (state, noSave) =>
        {
            invoked = true;
            Assert.Multiple(() =>
            {
                Assert.That(state.Tab, Is.EqualTo(TerminalTab.Connections));
                Assert.That(state.Active.Top, Is.EqualTo(10));
                Assert.That(state.Active.Sort, Is.EqualTo(1));
                Assert.That(state.Active.AppsOnly, Is.False);
                Assert.That(state.NetworkEnabled, Is.False);
                Assert.That(state.CapturePreferences().Tabs[0].Grouped, Is.False);
                Assert.That(noSave, Is.False);
            });
            var session = new CliSettingsSession(_store, state, () => Assert.Fail("Unexpected save failure"));
            session.Flush(force: true);
            Key(state, ConsoleKey.R);
            session.Observe(); session.Flush(force: true);
            return 0;
        });
        var exit = await command.Parse(["watch", "--top", "10", "--sort", "send", "--grouped=false", "--apps-only=false", "--network=false"])
            .InvokeAsync();
        var saved = _store.Load();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero, error.ToString());
            Assert.That(invoked, Is.True);
            Assert.That(saved.Network, Is.True);
            Assert.That(saved.Tabs[0].Grouped, Is.True);
            Assert.That(saved.Tabs[2].Top, Is.EqualTo(20));
            Assert.That(saved.Tabs[2].Sort, Is.Zero);
            Assert.That(saved.Tabs[2].AppsOnly, Is.True);
            Assert.That(saved.Tabs[2].TrafficSeconds, Is.EqualTo(1800));
        });
    }

    [Test]
    [Category("Integration")]
    public async Task ExplicitTabUsesThatTabsSavedSettingsAndNoSaveIsPassedThrough()
    {
        _store.Update(settings => { settings.Tabs[1].Top = 0; settings.Tabs[1].Sort = 3; });
        var invoked = false;
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, settingsStore: _store, runWatch: (state, noSave) =>
        {
            invoked = true;
            Assert.That(state.Active.Sort, Is.EqualTo(3));
            Assert.That(noSave, Is.True);
            return 0;
        });
        Assert.That(await command.Parse(["watch", "--tab", "services", "--no-save"]).InvokeAsync(), Is.Zero);
        Assert.That(invoked, Is.True);
        Assert.That(_store.Load().ActiveTab, Is.EqualTo(TerminalTab.Processes));
    }

    [Test]
    [Category("Integration")]
    public async Task SavedTabIsUsedToValidateSortAndNameRequiresAll()
    {
        _store.Update(settings => { settings.ActiveTab = TerminalTab.Services; settings.Tabs[1].Top = 10; });
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, settingsStore: _store,
            runWatch: (_, _) => throw new AssertionException("Invalid saved/argument combination must not run"));
        Assert.That(await command.Parse(["watch", "--sort", "name"]).InvokeAsync(), Is.EqualTo(1));
    }

    [Test]
    [Category("Integration")]
    public async Task ExplicitFiniteTopTakesPrecedenceOverSavedServiceNameSort()
    {
        _store.Update(saved => { saved.ActiveTab = TerminalTab.Services; saved.Tabs[1].Top = 0; saved.Tabs[1].Sort = 3; });
        using var output = new StringWriter();
        var invoked = false;
        var command = CliCommand.Create(output: output, error: output, settingsStore: _store, runWatch: (state, _) =>
        {
            invoked = true;
            Assert.That(state.Active.Sort, Is.Zero);
            Assert.That(state.Active.Top, Is.EqualTo(10));
            return 0;
        });
        Assert.That(await command.Parse(["watch", "--top", "10"]).InvokeAsync(), Is.Zero);
        Assert.That(invoked, Is.True);
        Assert.That(_store.Load().Tabs[1].Sort, Is.EqualTo(3));
    }

    [Test]
    [Category("Integration")]
    public void ConcurrentSessionsMergeOnlyChangedFieldsAndCombinePins()
    {
        var first = State(_store.Load());
        var second = State(_store.Load());
        var firstSession = new CliSettingsSession(_store, first, () => Assert.Fail("Unexpected failure"));
        var secondSession = new CliSettingsSession(_store, second, () => Assert.Fail("Unexpected failure"));
        first.Active.SavedPins["C:\\first.exe"] = new("C:\\first.exe", "first");
        Key(first, ConsoleKey.G); firstSession.Observe();
        second.Active.SavedPins["C:\\second.exe"] = new("C:\\second.exe", "second");
        Key(second, ConsoleKey.A); secondSession.Observe();
        firstSession.Flush(force: true); secondSession.Flush(force: true);
        var saved = _store.Load();
        Assert.That(saved.Tabs[0].Grouped, Is.True);
        Assert.That(saved.Tabs[0].AppsOnly, Is.True);
        Assert.That(saved.Tabs[0].Pins, Has.Length.EqualTo(2));
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    [Category("Integration")]
    public async Task RetriesTransientLockWhenReplacingSettings()
    {
        _store.Update(saved => saved.Network = false);
        var locked = new FileStream(_store.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(() =>
        {
            Thread.Sleep(75);
            locked.Dispose();
        });
        try { _store.Update(saved => saved.Network = true); }
        finally { await release; }
        Assert.That(_store.Load().Network, Is.True);
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    [Category("Integration")]
    public void PersistentReplacementLockPreservesSettingsAndCleansUp()
    {
        _store.Update(saved => saved.Network = false);
        using (var locked = new FileStream(_store.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Throws<UnauthorizedAccessException>(() => _store.Update(saved => saved.Network = true));
        }
        Assert.That(_store.Load().Network, Is.False);
        Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
    }

    [Test]
    [Category("Integration")]
    public void CorruptJsonIsBackedUpBeforeSavingAndUnknownVersionsAreNotOverwritten()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.Path, "{bad json");
        var warnings = 0;
        _store.Load(() => warnings++);
        Assert.That(warnings, Is.EqualTo(1));
        _store.Update(saved => saved.Network = true);
        Assert.That(_store.Load().Network, Is.True);
        Assert.That(File.ReadAllText(Directory.GetFiles(_directory, "*.invalid-*").Single()), Is.EqualTo("{bad json"));
        File.WriteAllText(_store.Path, "{\"version\":2}");
        Assert.Throws<InvalidDataException>(() => _store.Update(saved => saved.Network = false));
        Assert.That(File.ReadAllText(_store.Path), Is.EqualTo("{\"version\":2}"));
    }

    [Test]
    [Category("Integration")]
    public void InvalidStoredControlsAndNullPinsAreNormalized()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_store.Path, "{\"activeTab\":99,\"tabs\":[{\"top\":-1,\"sort\":99,\"pins\":[null,{\"identity\":\"process:42\",\"name\":\"old PID\"}]},null]}");
        var saved = _store.Load();
        Assert.That(saved.Tabs, Has.Length.EqualTo(4));
        Assert.That(saved.Tabs[0].Top, Is.Zero);
        Assert.That(saved.Tabs[0].Sort, Is.Zero);
        Assert.That(saved.Tabs[0].Pins, Is.Empty);
        Assert.That(saved.ActiveTab, Is.EqualTo(TerminalTab.Processes));
    }

    [Test]
    [Category("Integration")]
    public void EarlierExecutablePinPrefixIsNormalizedToThePath()
    {
        _store.Update(saved => saved.Tabs[0].Pins = [new CliPin("exe:C:\\first.exe", "first")]);
        Assert.That(_store.Load().Tabs[0].Pins.Single().Identity, Is.EqualTo("C:\\first.exe"));
    }

    [Test]
    [Category("Integration")]
    public void SaveFailureIsReportedWithoutChangingTheStateOrThrowing()
    {
        Directory.CreateDirectory(_directory);
        Directory.CreateDirectory(_store.Path);
        var state = State(new());
        var warnings = 0;
        var session = new CliSettingsSession(_store, state, () => warnings++);
        Key(state, ConsoleKey.G); session.Observe(); session.Flush(force: true); session.Flush(force: true);
        Assert.That(session.Failed, Is.True);
        Assert.That(warnings, Is.EqualTo(1));
        Assert.That(state.Active.Grouped, Is.True);
    }

    [Test]
    [Category("Integration")]
    public async Task SettingsCommandsShowSetAndResetWithoutSamplingOrInstalling()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = CliCommand.Create(() => throw new AssertionException("Must not sample"), output, error,
            (_, _) => throw new AssertionException("Must not install"), _store);
        Assert.That(await command.Parse(["settings", "set", "--network=true"]).InvokeAsync(), Is.Zero);
        Assert.That(_store.Load().Network, Is.True);
        Assert.That(await command.Parse(["settings", "set", "--network=false"]).InvokeAsync(), Is.Zero);
        Assert.That(_store.Load().Network, Is.False);
        output.GetStringBuilder().Clear();
        Assert.That(await command.Parse(["settings", "show"]).InvokeAsync(), Is.Zero);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.That(json.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(1));
        _store.Update(saved => saved.Tabs[0].Grouped = true);
        Assert.That(await command.Parse(["settings", "reset"]).InvokeAsync(), Is.Zero, error.ToString());
        Assert.That(_store.Load().Tabs[0].Grouped, Is.False);
        Assert.That(_store.Load().Tabs.Select(tab => tab.Top), Is.EqualTo(new[] { 10, 10, 10, 10 }));
        Assert.That(command.Parse(["settings", "set"]).Errors, Is.Not.Empty);
        Assert.That(error.ToString(), Is.Empty);
    }

    [Test]
    [Category("Integration")]
    public void SavedExecutablePinFollowsRestartAndDoesNotFollowPidReuseByAnotherExecutable()
    {
        var first = new ProcessUsage(42, 1, "first", 10, 1024) { ImagePath = "C:\\first.exe" };
        var state = State(new());
        state.Observe(new MonitorSample(DateTimeOffset.UnixEpoch, 10, [first], 0), null);
        state.Present(InteractiveMonitor.Query(state), 5);
        Key(state, ConsoleKey.P);
        // Save identity-only preferences, then construct a fresh frontend with new lifetimes.
        _store.Update(saved => saved.Tabs[0].Pins = state.CapturePreferences().Tabs[0].Pins);
        var restored = State(_store.Load());
        restored.Active.Top = 5;
        var replacement = first with { ProcessId = 90, CreationTicks = 2, CpuPercent = 0 };
        var reused = first with { ImagePath = "C:\\other.exe", CreationTicks = 3 };
        restored.Observe(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 10, [replacement, reused], 0), null);
        var table = restored.Present(InteractiveMonitor.Query(restored), 5);
        Assert.That(restored.Active.Pins.Values.Single().Process!.ProcessId, Is.EqualTo(90));
        Assert.That(table.Rows[0].Process!.ProcessId, Is.EqualTo(90));
        Assert.That(File.ReadAllText(_store.Path), Does.Not.Contain("processId").And.Not.Contain("creationTicks"));
    }

    [Test]
    public void MissingExecutablePinRemainsVisibleWhenGroupingChanges()
    {
        var saved = new CliPreferences();
        saved.Tabs[0].Pins = [new CliPin("C:\\first.exe", "first")];
        var state = State(saved);
        var empty = new TerminalTable(["Process", "CPU"], []);
        Assert.That(state.Present(empty, 5).Rows, Has.Count.EqualTo(1));
        Key(state, ConsoleKey.G);
        var row = state.Present(empty, 5).Rows.Single();
        Assert.That(row.Cells[0], Is.EqualTo("first"));
        Assert.That(row.Cells[1], Is.EqualTo(WinMon.Resources.Strings.Unavailable));
    }

    [TestCase(TerminalTab.Services, "service:[\"Demo\"]")]
    [TestCase(TerminalTab.Connections, "C:\\first.exe")]
    [TestCase(TerminalTab.Usage, "C:\\first.exe")]
    public void OtherTabsRestorePinsByStableIdentityInsteadOfOldRowKey(TerminalTab tab, string identity)
    {
        var saved = new CliPreferences { ActiveTab = tab };
        saved.Tabs[(int)tab].Pins = [new CliPin(identity, "first")];
        var state = State(saved);
        var first = new TerminalRow("old-runtime-key", ["first"], "") { PinIdentity = identity };
        state.Present(new TerminalTable(["Process"], [first]), 5);
        var next = first with { Key = "new-runtime-key" };
        var table = state.Present(new TerminalTable(["Process"], [next]), 5);
        Assert.That(state.Active.Pins.Keys, Is.EqualTo(new[] { "new-runtime-key" }));
        Assert.That(table.Rows, Has.Count.EqualTo(1));
        Key(state, ConsoleKey.P);
        Assert.That(state.CapturePreferences().Tabs[(int)tab].Pins, Is.Empty);
    }

    [Test]
    public void SavedServicePinDoesNotTransferWhenTheSameHostNowRepresentsDifferentServices()
    {
        var saved = new CliPreferences { ActiveTab = TerminalTab.Services };
        saved.Tabs[1].Pins = [new CliPin("service:[\"Demo\"]", "Demo")];
        var state = State(saved);
        var first = new TerminalRow("host:42:1", ["Demo"], "") { PinIdentity = "service:[\"Demo\"]" };
        state.Present(new TerminalTable(["Process"], [first]), 5);
        var other = first with { Cells = ["Other"], PinIdentity = "service:[\"Other\"]" };
        var table = state.Present(new TerminalTable(["Process"], [other]), 5);
        Assert.That(state.Active.Pins.ContainsKey(other.Key), Is.False);
        Assert.That(table.Rows[0].Cells[0], Is.EqualTo("Demo"));
        Assert.That(table.Rows[0].Details, Does.Contain(WinMon.Resources.Strings.Unavailable));
    }

    [Test]
    [Category("Integration")]
    public void WritesAreDebouncedAndUnchangedStartupDoesNotWrite()
    {
        var clock = new ManualClock();
        var state = State(new());
        var session = new CliSettingsSession(_store, state, () => Assert.Fail("Unexpected failure")) { Clock = clock };
        session.Observe(); session.Flush(force: true);
        Assert.That(File.Exists(_store.Path), Is.False);
        Key(state, ConsoleKey.G); session.Observe(); session.Flush();
        Assert.That(File.Exists(_store.Path), Is.False);
        clock.Milliseconds = 499;
        session.Flush();
        Assert.That(File.Exists(_store.Path), Is.False);
        clock.Milliseconds = 500;
        session.Flush();
        Assert.That(_store.Load().Tabs[0].Grouped, Is.True);
    }

    [Test, Category("Integration")]
    public void LaterChangesRestartDebounceAndForceFlushPreservesAllChanges()
    {
        var clock = new ManualClock();
        var state = State(new());
        var session = new CliSettingsSession(_store, state, () => Assert.Fail("Unexpected failure")) { Clock = clock };
        Key(state, ConsoleKey.G); session.Observe();
        clock.Milliseconds = 499;
        Key(state, ConsoleKey.A); session.Observe();
        clock.Milliseconds = 500;
        session.Flush();
        Assert.That(File.Exists(_store.Path), Is.False, "The most recent change restarts the debounce.");
        clock.Milliseconds = 998;
        session.Flush();
        Assert.That(File.Exists(_store.Path), Is.False);
        session.Flush(force: true);
        var saved = _store.Load();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Tabs[0].Grouped, Is.True);
            Assert.That(saved.Tabs[0].AppsOnly, Is.True);
        });
    }

    private sealed class ManualClock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    [Test]
    [Category("Integration")]
    public async Task SnapshotsIgnoreInteractivePreferencesAndDoNotModifyThem()
    {
        _store.Update(saved => { saved.ActiveTab = TerminalTab.Usage; saved.Network = true; saved.Tabs[0].PinnedOnly = true; });
        var before = File.ReadAllText(_store.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = CliCommand.Create(() => new SnapshotSource(), output, error, settingsStore: _store);
        Assert.That(await command.Parse(["snapshot", "--format", "json"]).InvokeAsync(), Is.Zero);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.That(json.RootElement.GetProperty("sort").GetString(), Is.EqualTo("cpu"));
        Assert.That(json.RootElement.GetProperty("processes").GetArrayLength(), Is.EqualTo(1));
        Assert.That(File.ReadAllText(_store.Path), Is.EqualTo(before));
        Assert.That(error.ToString(), Is.Empty);
    }

    private sealed class SnapshotSource : IFrameSource
    {
        public CapturedFrame Capture(SamplingOptions options) => new(new MonitorSample(DateTimeOffset.UnixEpoch,
            10, [new ProcessUsage(42, 1, "first", 10, 1024)], 0));
        public void Reset() { }
        public void Dispose() { }
    }
}
