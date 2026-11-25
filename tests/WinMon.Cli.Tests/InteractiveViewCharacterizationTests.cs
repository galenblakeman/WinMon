using System.Text.Json;
using System.Text.RegularExpressions;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
[NonParallelizable]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class InteractiveViewCharacterizationTests
{
    private Dictionary<string, string> _fingerprints = null!;
    private Dictionary<string, ViewSnapshot> _representatives = null!;
    private static IEnumerable<string> Names => PreparedInteractiveView.Cases.Select(item => item.Name);
    private static IEnumerable<string> SmallNames => PreparedInteractiveView.Cases
        .Where(item => item.Width < 80 || item.Height < 20).Select(item => item.Name);
    private static IEnumerable<string> NormalNames => PreparedInteractiveView.Cases.Take(20).Select(item => item.Name);

    [OneTimeSetUp]
    public void LoadOriginalRenderingContracts()
    {
        var root = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "InteractiveView");
        _fingerprints = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "Fingerprints.json")))!;
        _representatives = JsonSerializer.Deserialize<Dictionary<string, ViewSnapshot>>(File.ReadAllText(Path.Combine(root, "Representatives.json")))!;
        Assert.That(_fingerprints.Keys.Order(), Is.EqualTo(Names.Order()));
        Assert.That(_representatives.Keys.Order(), Is.EqualTo(PreparedInteractiveView.RepresentativeNames.Order()));
    }

    [TestCaseSource(nameof(Names))]
    public void RenderedViewsPreserveOriginalTextStylesAndState(string name)
    {
        using var view = Create(name);
        var actual = view.Capture();
        if (_representatives.TryGetValue(name, out var expected))
        {
            Assert.That(actual.Lines, Is.EqualTo(expected.Lines));
            Assert.That(actual.State, Is.EqualTo(expected.State));
        }
        Assert.That(actual.Fingerprint(), Is.EqualTo(_fingerprints[name]),
            JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
    }

    [TestCaseSource(nameof(SmallNames))]
    public void SmallTerminalReturnsBeforeTopScrollSelectionOrDetailsChanges(string name)
    {
        using var view = Create(name);
        var before = view.StateFingerprint();
        _ = view.Capture();
        Assert.That(view.StateFingerprint(), Is.EqualTo(before));
    }

    [TestCaseSource(nameof(NormalNames))]
    public void NormalViewsApplyTopAndClampSelectionAndVisibleColumns(string name)
    {
        using var view = Create(name);
        _ = view.Capture();
        var active = view.State.Active;
        Assert.Multiple(() =>
        {
            Assert.That(active.Top, Is.EqualTo(3));
            Assert.That(active.Rows, Has.Count.InRange(1, 3));
            Assert.That(active.SelectedKey, Is.EqualTo(active.Rows[0].Key));
            Assert.That(active.Offset, Is.Zero);
            Assert.That(active.ColumnOffset, Is.InRange(0, 6));
            Assert.That(active.RevealSort, Is.False);
        });
    }

    [TestCase(-9, 0)]
    [TestCase(0, 0)]
    [TestCase(999, 11)]
    public void DetailsClampVerticalOffsetWithoutApplyingNormalViewSettings(int offset, int expected)
    {
        using var view = Create("details-" + offset + "-3");
        var snapshot = view.Capture();
        using var state = JsonDocument.Parse(snapshot.State);
        Assert.That(view.State.Active.DetailOffset, Is.EqualTo(expected));
        Assert.That(view.State.Active.HorizontalOffset, Is.EqualTo(3));
        Assert.That(view.State.Active.Top, Is.EqualTo(7));
        Assert.That(state.RootElement.GetProperty("DetailsRead").GetInt32(), Is.EqualTo(1));
        Assert.That(state.RootElement.GetProperty("CellsRead").GetInt32(), Is.Zero);
    }

    [Test]
    public void DetailScrollingKeepsRuneBoundariesLiteralMarkupAndSanitizedControls()
    {
        using var view = Create("details-0-3");
        var snapshot = view.Capture();
        Assert.That(PreparedInteractiveView.Plain(snapshot.Lines[2]), Is.EqualTo("st  [red]literal[/][31m"));
        Assert.That(PreparedInteractiveView.Plain(snapshot.Lines[3]), Is.EqualTo("econd"));
        Assert.That(string.Join('\n', snapshot.Lines), Does.Not.Contain("\uFFFD"));
        var first = snapshot.State;
        _ = view.Capture();
        Assert.That(view.StateFingerprint(), Is.EqualTo(first), "The selected row's details stay cached.");
    }

    [Test]
    public void MissingRecordedUsageKeepsTheEmptyTableAndPeriodWarning()
    {
        using var view = Create("usage-unavailable");
        var text = PreparedInteractiveView.Plain(view.Render());
        Assert.That(view.State.Paused, Is.True);
        Assert.That(view.State.Observations.UsageAvailable, Is.False);
        Assert.That(InteractiveMonitor.Query(view.State).Columns, Is.Empty);
        Assert.That(text, Does.Contain(Strings.UsagePeriodUnavailable).And.Contain("Rows 0-0 of 0"));
    }

    [Test]
    public void HelpTakesPrecedenceOverSelectedDetailsWithoutReadingTheRow()
    {
        using var view = Create("help-details");
        var snapshot = view.Capture();
        using var state = JsonDocument.Parse(snapshot.State);
        Assert.That(view.State.Help, Is.True);
        Assert.That(view.State.Active.Details, Is.True);
        Assert.That(state.RootElement.GetProperty("DetailsRead").GetInt32(), Is.Zero);
        Assert.That(state.RootElement.GetProperty("CellsRead").GetInt32(), Is.Zero);
        Assert.That(string.Join('\n', snapshot.Lines), Does.Not.Contain("Detail line"));
    }

    private static IEnumerable<TestCaseData> HealthCases
    {
        get
        {
            (NetworkTrafficState State, string Color, string Key)[] cases =
            [
                (NetworkTrafficState.NotStarted, "210;153;34", "NetworkProcessMonitoringWarmingUp"),
                (NetworkTrafficState.Connecting, "210;153;34", "NetworkProcessMonitoringWarmingUp"),
                (NetworkTrafficState.WarmingUp, "210;153;34", "NetworkProcessMonitoringWarmingUp"),
                (NetworkTrafficState.Active, "63;185;80", "NetworkProcessMonitoringActive"),
                (NetworkTrafficState.Incomplete, "210;153;34", "NetworkProcessMonitoringIncomplete"),
                (NetworkTrafficState.Unavailable, "255;123;114", "CliNetworkSetupHint"),
                (NetworkTrafficState.UpdateRequired, "255;123;114", "CliNetworkSetupHint")
            ];
            foreach (var (state, color, key) in cases)
                foreach (var failed in new[] { false, true })
                    yield return new TestCaseData(state, failed, failed ? "255;123;114" : color,
                        failed ? "CliSettingsSaveFailed" : key);
        }
    }

    [TestCaseSource(nameof(HealthCases))]
    public void ConnectionWarningKeepsHealthColorAndSettingsFailurePrecedence(NetworkTrafficState traffic,
        bool failed, string color, string resource)
    {
        using var view = Create("connections-" + traffic + (failed ? "-save" : ""));
        var rendered = view.Render();
        var prefix = Strings.ResourceManager.GetString(resource, System.Globalization.CultureInfo.CurrentUICulture)!;
        Assert.That(rendered, Does.Match("\u001b\\[[0-9;]*38;2;" + Regex.Escape(color) + "m" + Regex.Escape(prefix)));
    }

    [TestCase("error", "ReadingsUnavailableRetryingEachSecond", "255;123;114")]
    [TestCase("gap", "CliWarmingUp", "210;153;34")]
    [TestCase("no-matches", "CliNoMatches", "210;153;34")]
    [TestCase("settings-failure", "CliSettingsSaveFailed", "255;123;114")]
    public void ProcessWarningsKeepErrorGapEmptyAndSaveFailureSemantics(string name, string resource, string color)
    {
        using var view = Create(name);
        var rendered = view.Render();
        var prefix = Strings.ResourceManager.GetString(resource, System.Globalization.CultureInfo.CurrentUICulture)!;
        Assert.That(rendered, Does.Match("\u001b\\[[0-9;]*38;2;" + Regex.Escape(color) + "m" + Regex.Escape(prefix)));
    }

    [TestCase(false, "[x]", " | ")]
    [TestCase(true, "[✓]", " · ")]
    public void ControlsKeepAsciiAndUnicodeCheckedMarkers(bool unicode, string mark, string separator)
    {
        var item = PreparedInteractiveView.Cases.Single(candidate => candidate.Name == "pins") with { Unicode = unicode };
        using var view = new PreparedInteractiveView(item);
        var text = PreparedInteractiveView.Plain(view.Render());
        Assert.That(text, Does.Contain(mark).And.Contain(separator));
        Assert.That(view.State.Active.PinnedOnly, Is.True);
        Assert.That(view.State.Active.Grouped, Is.True);
    }

    private static PreparedInteractiveView Create(string name) =>
        new(PreparedInteractiveView.Cases.Single(candidate => candidate.Name == name));
}
