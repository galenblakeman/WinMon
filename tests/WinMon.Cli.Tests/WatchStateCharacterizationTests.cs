using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, Category("Integration"), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class WatchStateCharacterizationTests
{
    private string _directory = null!;
    private CliSettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "watch-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new(Path.Combine(_directory, "settings.json"));
    }

    [TearDown]
    public void TearDown()
    {
        var root = Path.GetFullPath(TestContext.CurrentContext.WorkDirectory) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(_directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected fixture path");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    private static CliPreferences Saved(TerminalTab tab = TerminalTab.Usage)
    {
        var preferences = new CliPreferences { ActiveTab = tab, Network = true };
        for (var i = 0; i < preferences.Tabs.Length; i++)
        {
            preferences.Tabs[i].Top = 17 + i;
            preferences.Tabs[i].AppsOnly = true;
            preferences.Tabs[i].PinnedOnly = true;
            preferences.Tabs[i].TrafficSeconds = 1800;
        }
        preferences.Normalize();
        return preferences;
    }

    private string Write(CliPreferences saved)
    {
        var text = JsonSerializer.Serialize(saved, CliSettingsStore.JsonOptions);
        File.WriteAllText(_store.Path, text);
        return text;
    }

    private void AssertUnchanged(string original)
    {
        Assert.That(File.ReadAllText(_store.Path), Is.EqualTo(original));
        Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName), Is.EqualTo(new[] { "settings.json" }));
    }

    [TestCase(TerminalTab.Processes, false, false)]
    [TestCase(TerminalTab.Processes, true, true)]
    [TestCase(TerminalTab.Services, false, false)]
    [TestCase(TerminalTab.Services, true, false)]
    [TestCase(TerminalTab.Connections, false, false)]
    [TestCase(TerminalTab.Connections, true, true)]
    [TestCase(TerminalTab.Usage, false, false)]
    [TestCase(TerminalTab.Usage, true, true)]
    [TestCase(TerminalTab.Hardware, false, false)]
    [TestCase(TerminalTab.Hardware, true, false)]
    public async Task ExplicitWatchFlagsKeepTheirTabScopeAndDoNotRewriteSavedSettings(
        TerminalTab tab, bool flag, bool expectedAppsOnly)
    {
        var saved = Saved();
        saved.Network = !flag;
        saved.Tabs[0].Grouped = !flag;
        var original = Write(saved);
        using var output = new StringWriter();
        using var error = new StringWriter();
        InteractiveState? observed = null;
        var calls = 0;
        var observedNoSave = false;
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store, runWatch: (state, noSave) =>
        { observed = state; observedNoSave = noSave; calls++; return 17; });

        var exit = await command.Parse(["watch", "--tab", tab.ToString().ToLowerInvariant(),
            "--grouped=" + flag, "--apps-only=" + flag, "--network=" + flag,
            "--filter", " [literal] ", "--no-save"]).InvokeAsync();

        saved.ActiveTab = tab;
        saved.Network = flag;
        saved.Tabs[0].Grouped = flag;
        saved.Tabs[(int)tab].AppsOnly = expectedAppsOnly;
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(17));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(observedNoSave, Is.True);
            Assert.That(observed, Is.Not.Null);
            Assert.That(JsonSerializer.Serialize(observed!.CapturePreferences(), CliSettingsStore.JsonOptions),
                Is.EqualTo(JsonSerializer.Serialize(saved, CliSettingsStore.JsonOptions)));
            Assert.That(observed.Filter, Is.EqualTo(" [literal] "));
            Assert.That(observed.Draft, Is.EqualTo(" [literal] "));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.Empty);
            AssertUnchanged(original);
        });
        for (var i = 0; i < 5; i++)
        {
            observed!.Handle(new ConsoleKeyInfo('\0', (ConsoleKey)((int)ConsoleKey.F1 + i), false, false, false), 5);
            Assert.That(observed.Filter, Is.EqualTo(i == (int)tab ? " [literal] " : ""));
            Assert.That(observed.Draft, Is.EqualTo(i == (int)tab ? " [literal] " : ""));
        }
    }

    [TestCase(TerminalTab.Processes)]
    [TestCase(TerminalTab.Services)]
    [TestCase(TerminalTab.Connections)]
    [TestCase(TerminalTab.Usage)]
    [TestCase(TerminalTab.Hardware)]
    public async Task OmittedWatchOptionsRestoreTheSavedTabAndCreateIndependentStates(TerminalTab tab)
    {
        var saved = Saved(tab);
        var original = Write(saved);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var states = new List<InteractiveState>();
        var noSaves = new List<bool>();
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store, runWatch: (state, noSave) =>
        { states.Add(state); noSaves.Add(noSave); return 23; });

        Assert.That(await command.Parse(["watch"]).InvokeAsync(), Is.EqualTo(23));
        Assert.That(await command.Parse(["watch"]).InvokeAsync(), Is.EqualTo(23));

        Assert.Multiple(() =>
        {
            Assert.That(states, Has.Count.EqualTo(2));
            Assert.That(states[0], Is.Not.SameAs(states[1]));
            Assert.That(noSaves, Is.EqualTo(new[] { false, false }));
            foreach (var state in states)
            {
                Assert.That(JsonSerializer.Serialize(state.CapturePreferences(), CliSettingsStore.JsonOptions), Is.EqualTo(original));
                Assert.That(state.Filter, Is.Empty);
                Assert.That(state.Draft, Is.Empty);
            }
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.Empty);
            AssertUnchanged(original);
        });
    }

    [TestCase(0, null, 3, 0)]
    [TestCase(1, null, 0, 0)]
    [TestCase(1000, null, 0, 0)]
    [TestCase(0, "name", 3, 0)]
    [TestCase(1, "name", 0, 1)]
    [TestCase(0, "cpu", 0, 0)]
    [TestCase(0, "gpu", 0, 1)]
    public async Task ServiceTreeSortKeepsExplicitSortAndTopPrecedence(int top, string? sort, int expectedSort, int expectedExit)
    {
        var saved = Saved(TerminalTab.Services);
        saved.Tabs[1].Top = 0;
        saved.Tabs[1].Sort = 3;
        var original = Write(saved);
        using var output = new StringWriter();
        using var error = new StringWriter();
        InteractiveState? observed = null;
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store,
            runWatch: (state, _) => { observed = state; return 0; });
        var args = new List<string> { "watch", "--top", top.ToString(CultureInfo.InvariantCulture) };
        if (sort is not null) args.AddRange(["--sort", sort]);

        var exit = await command.Parse(args.ToArray()).InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(expectedExit));
            if (expectedExit == 0)
            {
                Assert.That(observed, Is.Not.Null);
                Assert.That(observed!.Active.Top, Is.EqualTo(top));
                Assert.That(observed.Active.Sort, Is.EqualTo(expectedSort));
                Assert.That(error.ToString(), Is.Empty);
            }
            else
            {
                Assert.That(observed, Is.Null);
                Assert.That(error.ToString().TrimEnd('\r', '\n'), Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, Strings.CliSort)));
            }
            Assert.That(output.ToString(), Is.Empty);
            AssertUnchanged(original);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WatchRunnerFailuresKeepTheExistingExitAndSanitizedErrorContract(bool canceled)
    {
        var original = Write(Saved());
        using var output = new StringWriter();
        using var error = new StringWriter();
        var calls = 0;
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store, runWatch: (_, _) =>
        {
            calls++;
            if (canceled) throw new OperationCanceledException("runner\u001b\nfailed");
            throw new InvalidOperationException("runner\u001b\nfailed");
        });
        var exit = await command.Parse(["watch"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(error.ToString().TrimEnd('\r', '\n'), Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, "runnerfailed")));
            Assert.That(output.ToString(), Is.Empty);
            AssertUnchanged(original);
        });
    }

    [Test]
    public async Task MalformedSettingsWarnBeforeRunningWithDefaultsAndExplicitOverridesWithoutRepair()
    {
        const string original = "{not JSON";
        File.WriteAllText(_store.Path, original);
        using var output = new StringWriter();
        using var error = new StringWriter();
        InteractiveState? observed = null;
        string? warningAtRun = null;
        var command = CliCommand.Create(output: output, error: error, settingsStore: _store,
            runWatch: (state, _) => { observed = state; warningAtRun = error.ToString(); return 29; });
        var exit = await command.Parse(["watch", "--tab", "connections", "--top", "0", "--network=false"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(29));
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Tab, Is.EqualTo(TerminalTab.Connections));
            Assert.That(observed.Active.Top, Is.Zero);
            Assert.That(observed.NetworkEnabled, Is.False);
            Assert.That(observed.CapturePreferences().Tabs.Select(tab => tab.Top), Is.EqualTo(new[] { 10, 10, 0, 10, 0 }));
            Assert.That(error.ToString().TrimEnd('\r', '\n'), Is.EqualTo(Strings.CliSettingsLoadFailed));
            Assert.That(warningAtRun!.TrimEnd('\r', '\n'), Is.EqualTo(Strings.CliSettingsLoadFailed));
            Assert.That(output.ToString(), Is.Empty);
            AssertUnchanged(original);
        });
    }
}
