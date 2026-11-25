using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
[NonParallelizable]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class InteractiveInputCharacterizationTests
{
    private Dictionary<string, string> _fingerprints = null!;
    private static IEnumerable<TestCaseData> MatrixCases => PreparedInteractiveInput.Groups.Select(group =>
        new TestCaseData(group.Tab, group.Mode, group.Modifiers));

    [OneTimeSetUp]
    public void LoadOriginalTransitionContracts()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "InteractiveInput", "Fingerprints.json");
        _fingerprints = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        Assert.That(_fingerprints.Keys.Order(), Is.EqualTo(PreparedInteractiveInput.Groups.Select(group => group.Name).Order()));
    }

    [TestCaseSource(nameof(MatrixCases))]
    public void KeyMatrixPreservesOriginalPublicStateAndExceptionContracts(TerminalTab tab, string mode, ConsoleModifiers modifiers)
    {
        var group = new InputGroup(tab, mode, modifiers);
        Assert.That(PreparedInteractiveInput.Fingerprint(group), Is.EqualTo(_fingerprints[group.Name]));
    }

    [TestCase(ConsoleKey.F2, '\0', "draft😀")]
    [TestCase(ConsoleKey.Q, 'q', "draft😀q")]
    [TestCase(ConsoleKey.B, 'b', "draft😀b")]
    [TestCase(ConsoleKey.Tab, '\t', "draft😀")]
    [TestCase(ConsoleKey.Oem2, '?', "draft😀?")]
    [TestCase(ConsoleKey.Spacebar, ' ', "draft😀 ")]
    public void EditingSuppressesShortcutsButAlwaysResetsViewport(ConsoleKey key, char character, string expected)
    {
        var state = new PreparedInteractiveInput(mode: "edit").State;
        Press(state, key, character);
        Assert.That(state.Draft, Is.EqualTo(expected));
        Assert.That(state.Active.Offset, Is.Zero); Assert.That(state.Active.SelectedKey, Is.Null);
        Assert.That(state.Tab, Is.EqualTo(TerminalTab.Processes)); Assert.That(state.Quit, Is.False);
        Assert.That(state.NetworkEnabled, Is.False); Assert.That(state.Help, Is.False); Assert.That(state.Paused, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EditingCommitAndCancelKeepSeparateSavedAndDraftText(bool commit)
    {
        var state = new PreparedInteractiveInput(mode: "edit").State;
        Press(state, commit ? ConsoleKey.Enter : ConsoleKey.Escape);
        Assert.That(state.EditingFilter, Is.False);
        Assert.That(state.Filter, Is.EqualTo(commit ? "draft😀" : "saved-filter"));
        Assert.That(state.Draft, Is.EqualTo(commit ? "draft😀" : "saved-filter"));
        Assert.That(state.Offset, Is.Zero); Assert.That(state.Active.SelectedKey, Is.Null);
    }

    private static IEnumerable<TestCaseData> BackspaceCases
    {
        get
        {
            yield return new TestCaseData("", "").SetName(nameof(BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair) + "(empty)");
            yield return new TestCaseData("x", "").SetName(nameof(BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair) + "(single)");
            yield return new TestCaseData("x😀", "x").SetName(nameof(BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair) + "(pair)");
            yield return new TestCaseData("x\ud83d", "x").SetName(nameof(BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair) + "(high)");
            yield return new TestCaseData("x\ude00", "x").SetName(nameof(BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair) + "(low)");
        }
    }

    [TestCaseSource(nameof(BackspaceCases))]
    public void BackspaceRemovesOneUtf16CharacterOrOneCompleteTrailingPair(string draft, string expected)
    {
        var state = new PreparedInteractiveInput(mode: "edit").State; state.Active.Draft = draft;
        Press(state, ConsoleKey.Backspace);
        Assert.That(state.Draft.AsSpan().SequenceEqual(expected.AsSpan()), Is.True);
        Assert.That(state.Active.SelectedKey, Is.Null); Assert.That(state.Offset, Is.Zero);
    }

    [TestCase(127, 128)]
    [TestCase(128, 128)]
    [TestCase(129, 129)]
    public void EditingLengthLimitKeepsExistingUtf16UnitBoundary(int length, int expected)
    {
        var state = new PreparedInteractiveInput(mode: "edit").State; state.Active.Draft = new string('x', length);
        Press(state, ConsoleKey.A, 'a');
        Assert.That(state.Draft.Length, Is.EqualTo(expected));
        Assert.That(state.Draft, Is.EqualTo(new string('x', length) + (length < 128 ? "a" : "")));
    }

    [Test]
    public void FilterBoundaryRetainsTheExistingHalfSurrogateBehavior()
    {
        var state = new PreparedInteractiveInput(mode: "edit").State; state.Active.Draft = new string('x', 127);
        Press(state, ConsoleKey.A, '\ud83d'); Press(state, ConsoleKey.A, '\ude00');
        Assert.That(state.Draft.Length, Is.EqualTo(128));
        Assert.That(state.Draft[^1], Is.EqualTo('\ud83d'));
    }

    [TestCase(ConsoleKey.F1, TerminalTab.Processes)]
    [TestCase(ConsoleKey.F2, TerminalTab.Services)]
    [TestCase(ConsoleKey.F3, TerminalTab.Connections)]
    [TestCase(ConsoleKey.F4, TerminalTab.Usage)]
    [TestCase(ConsoleKey.F5, TerminalTab.Hardware)]
    public void FunctionTabsWinOverQuestionCharactersAndCloseHelp(ConsoleKey key, TerminalTab expected)
    {
        var state = new PreparedInteractiveInput(mode: "help").State;
        Press(state, key, '?');
        Assert.That(state.Tab, Is.EqualTo(expected)); Assert.That(state.Help, Is.False);
    }

    [TestCase(TerminalTab.Processes, true, TerminalTab.Hardware)]
    [TestCase(TerminalTab.Hardware, false, TerminalTab.Processes)]
    [TestCase(TerminalTab.Connections, true, TerminalTab.Services)]
    [TestCase(TerminalTab.Connections, false, TerminalTab.Usage)]
    public void TabCyclesRespectShiftAndWrapWithoutOpeningTheFilter(TerminalTab start, bool shift, TerminalTab expected)
    {
        var state = new PreparedInteractiveInput(start, "help").State;
        state.Handle(new ConsoleKeyInfo('/', ConsoleKey.Tab, shift, true, true), 3);
        Assert.That(state.Tab, Is.EqualTo(expected)); Assert.That(state.Help, Is.False); Assert.That(state.EditingFilter, Is.False);
    }

    [Test]
    public void EscapeClosesHelpThenDetailsBeforeClearingFilterAndOffset()
    {
        var state = new PreparedInteractiveInput(mode: "help").State; state.Active.Details = true;
        Press(state, ConsoleKey.Escape);
        Assert.That(state.Help, Is.False); Assert.That(state.Active.Details, Is.True); Assert.That(state.Filter, Is.EqualTo("saved-filter"));
        Press(state, ConsoleKey.Escape);
        Assert.That(state.Active.Details, Is.False); Assert.That(state.Filter, Is.EqualTo("saved-filter"));
        Press(state, ConsoleKey.Escape);
        Assert.That(state.Filter, Is.Empty); Assert.That(state.Draft, Is.Empty); Assert.That(state.Offset, Is.Zero);
        Assert.That(state.Active.SelectedKey, Is.EqualTo("two"));
    }

    [TestCase("help")]
    [TestCase("details")]
    public void QuitAndNetworkGlobalsStillPrecedeModalNavigation(string mode)
    {
        var state = new PreparedInteractiveInput(mode: mode).State;
        Press(state, ConsoleKey.B, 'b'); Press(state, ConsoleKey.Q, '[');
        Assert.That(state.NetworkEnabled, Is.True); Assert.That(state.Quit, Is.True);
        Assert.That(state.Paused, Is.False); Assert.That(state.Active.DetailOffset, Is.EqualTo(5));
    }

    [TestCase(ConsoleKey.UpArrow, 4, 16)]
    [TestCase(ConsoleKey.DownArrow, 6, 16)]
    [TestCase(ConsoleKey.PageUp, 2, 16)]
    [TestCase(ConsoleKey.PageDown, 8, 16)]
    [TestCase(ConsoleKey.Home, 0, 16)]
    [TestCase(ConsoleKey.End, 5, 16)]
    [TestCase(ConsoleKey.LeftArrow, 5, 0)]
    [TestCase(ConsoleKey.RightArrow, 5, 32)]
    public void ModalNavigationKeepsOffsetsAndSuppressesAltHistory(ConsoleKey key, int vertical, int horizontal)
    {
        foreach (var mode in new[] { "help", "details" })
        {
            var state = new PreparedInteractiveInput(mode: mode).State;
            state.Handle(new ConsoleKeyInfo('\0', key, false, true, false), 3);
            Assert.That(state.Active.DetailOffset, Is.EqualTo(vertical)); Assert.That(state.Active.HorizontalOffset, Is.EqualTo(horizontal));
            Assert.That(state.Paused, Is.False); Assert.That(state.DisplayedFrame!.Full.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(2)));
        }
    }

    [TestCase(ConsoleKey.LeftArrow, '\0', true, 0)]
    [TestCase(ConsoleKey.RightArrow, '\0', true, 2)]
    [TestCase(ConsoleKey.Oem4, '[', false, 1)]
    [TestCase(ConsoleKey.Oem6, ']', false, 2)]
    public void HistoryKeysKeepLargeAndSingleStepDirections(ConsoleKey key, char character, bool alt, int second)
    {
        var state = new PreparedInteractiveInput().State;
        state.Handle(new ConsoleKeyInfo(character, key, false, alt, false), 3);
        Assert.That(state.Paused, Is.True);
        Assert.That(state.DisplayedFrame!.Full.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(second)));
    }

    [Test]
    public void UsagePeriodCycleRequeriesTheFrozenPointAndPauseResumesLatest()
    {
        var state = new PreparedInteractiveInput(TerminalTab.Usage, "paused").State;
        foreach (var expected in new[] { 1800, 60, 300 })
        {
            Press(state, ConsoleKey.U, 'u');
            Assert.That(state.UsagePeriodSeconds, Is.EqualTo(expected)); Assert.That(state.Paused, Is.True);
            Assert.That(state.DisplayedFrame!.Full.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(2)));
        }
        Press(state, ConsoleKey.Spacebar, ' ');
        Assert.That(state.Paused, Is.False); Assert.That(state.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
        Assert.That(state.NetworkStatus, Is.EqualTo(NetworkTrafficState.Active));
        Assert.That(state.DisplayedFrame!.Full.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(3)));
    }

    [Test]
    public void PauseWithNoFramesStillTogglesWithoutInventingObservations()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        Press(state, ConsoleKey.Spacebar, ' ');
        Assert.That(state.Paused, Is.True); Assert.That(state.DisplayedFrame, Is.Null); Assert.That(state.DisplayedSamples, Is.Empty);
        Press(state, ConsoleKey.Spacebar, ' '); Assert.That(state.Paused, Is.False);
    }

    [Test]
    public void SavedPinRemovalIsCaseInsensitiveAndPreservesUnrelatedPins()
    {
        var state = new PreparedInteractiveInput().State; var active = state.Active;
        active.SavedPins["APP|2"] = new CliPin("APP|2", "Saved");
        active.Pins["old-a"] = new TerminalRow("old-a", ["A"], "") { PinIdentity = "APP|2" };
        active.Pins["old-b"] = new TerminalRow("old-b", ["B"], "") { PinIdentity = "app|2" };
        active.Pins["other"] = new TerminalRow("other", ["C"], "") { PinIdentity = "app|other" };
        Press(state, ConsoleKey.P, 'p');
        Assert.That(active.SavedPins, Is.Empty); Assert.That(active.Pins.Keys, Is.EqualTo(new[] { "other" }));
    }

    [Test]
    public void PinNamesAreSanitizedAndKeepTheExistingUtf16TruncationBoundary()
    {
        var state = new PreparedInteractiveInput().State;
        state.Active.Rows = [new TerminalRow("two", () => throw new AssertionException("Named pin must not read cells."),
            () => throw new AssertionException("Pin must not open details."))
        { PinIdentity = "app|2", PinName = "\u001b" + new string('x', 255) + "😀" }];
        Press(state, ConsoleKey.P, 'p');
        var name = state.Active.SavedPins["app|2"].Name;
        Assert.That(name.Length, Is.EqualTo(256)); Assert.That(name.Contains('\u001b'), Is.False);
        Assert.That(name[^1], Is.EqualTo('\ud83d')); Assert.That(state.Active.Pins.Keys, Is.EqualTo(new[] { "two" }));
    }

    [Test]
    public void SessionOnlyPinsToggleWithoutCreatingSavedIdentityAndNullSelectionDoesNothing()
    {
        var state = new PreparedInteractiveInput().State;
        state.Active.Rows = [new TerminalRow("two", ["Session"], "")];
        Press(state, ConsoleKey.P, 'p'); Assert.That(state.Active.Pins.Keys, Is.EqualTo(new[] { "two" }));
        Assert.That(state.Active.SavedPins, Is.Empty);
        Press(state, ConsoleKey.P, 'p'); Assert.That(state.Active.Pins, Is.Empty);
        state.Active.SelectedKey = null; Press(state, ConsoleKey.P, 'p'); Assert.That(state.Active.Pins, Is.Empty);
    }

    [Test]
    public void PinCellFailurePropagatesWithoutCreatingPartialSavedState()
    {
        var expected = new InvalidDataException("owned pin cell failure");
        var state = new PreparedInteractiveInput().State;
        state.Active.Rows = [new TerminalRow("two", () => throw expected, () => "") { PinIdentity = "app|2" }];
        Assert.That(Assert.Catch(() => Press(state, ConsoleKey.P, 'p')), Is.SameAs(expected));
        Assert.That(state.Active.Pins, Is.Empty); Assert.That(state.Active.SavedPins, Is.Empty);
    }

    [TestCase(0, 5)] [TestCase(5, 10)] [TestCase(10, 15)] [TestCase(15, 20)]
    [TestCase(20, 50)] [TestCase(50, 0)] [TestCase(7, 5)]
    public void TopCyclesKeepServiceNameSortConstraintAndResetOffset(int before, int after)
    {
        var state = new PreparedInteractiveInput(TerminalTab.Services).State; state.Active.Top = before; state.Active.Sort = 3;
        Press(state, ConsoleKey.T, 't');
        Assert.That(state.Active.Top, Is.EqualTo(after)); Assert.That(state.Offset, Is.Zero);
        Assert.That(state.Active.Sort, Is.EqualTo(after == 0 ? 3 : 0));
    }

    [Test]
    public void ConnectionTrafficPeriodsAndHardwareRefreshKeepTheirDistinctRules()
    {
        var state = new PreparedInteractiveInput(TerminalTab.Connections).State;
        foreach (var expected in new[] { 1800, 0, 60, 300 }) { Press(state, ConsoleKey.R, 'r'); Assert.That(state.Active.TrafficSeconds, Is.EqualTo(expected)); }
        var hardware = new PreparedInteractiveInput(TerminalTab.Hardware).State;
        hardware.HardwareLoading = true; Press(hardware, ConsoleKey.R, 'r'); Assert.That(hardware.TakeHardwareRefreshRequest(), Is.False);
        hardware.HardwareLoading = false; Press(hardware, ConsoleKey.R, 'r'); Assert.That(hardware.TakeHardwareRefreshRequest(), Is.True);
        Assert.That(hardware.TakeHardwareRefreshRequest(), Is.False);
    }

    private static IEnumerable<TestCaseData> SortCases => from tab in Enum.GetValues<TerminalTab>()
        from key in Enumerable.Range(1, 6) from all in new[] { false, true } select new TestCaseData(tab, key, all);

    [TestCaseSource(nameof(SortCases))]
    public void SortDigitsKeepTabSpecificEligibilityAndAlwaysResetViewport(TerminalTab tab, int digit, bool all)
    {
        var state = new PreparedInteractiveInput(tab).State; state.Active.Top = all ? 0 : 5; state.Active.Sort = 99;
        Press(state, ConsoleKey.D1, (char)('0' + digit));
        var expected = 99;
        if (tab == TerminalTab.Processes) expected = (int)new[] { ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Gpu, ProcessSort.Io, ProcessSort.Threads, ProcessSort.Handles }[digit - 1];
        if (tab == TerminalTab.Connections || tab == TerminalTab.Services && (digit <= 3 || digit == 4 && all) || tab == TerminalTab.Usage && digit <= 5) expected = digit - 1;
        Assert.That(state.Active.Sort, Is.EqualTo(expected)); Assert.That(state.Offset, Is.Zero);
        Assert.That(state.Active.SelectedKey, Is.Null); Assert.That(state.Active.RevealSort, Is.True);
    }

    [Test]
    public void GuardedShortcutsStillFallThroughToCharacterHandlingWhenNotEligible()
    {
        var state = new PreparedInteractiveInput().State;
        Press(state, ConsoleKey.R, '3'); Assert.That(state.Sort, Is.EqualTo(ProcessSort.Gpu));
        Press(state, ConsoleKey.W, '/'); Assert.That(state.EditingFilter, Is.True);
    }

    private static void Press(InteractiveState state, ConsoleKey key, char character = '\0') =>
        state.Handle(new ConsoleKeyInfo(character, key, false, false, false), 3);
}
