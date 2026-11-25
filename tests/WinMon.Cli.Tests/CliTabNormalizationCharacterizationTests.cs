using System.Text.Json;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US")]
public sealed class CliTabNormalizationCharacterizationTests
{
    [TestCase(TerminalTab.Services, -1, 3, 0, 3)]
    [TestCase(TerminalTab.Services, 1001, 3, 0, 3)]
    [TestCase(TerminalTab.Services, 0, 3, 0, 3)]
    [TestCase(TerminalTab.Services, 1, 3, 1, 0)]
    [TestCase(TerminalTab.Services, 1000, 3, 1000, 0)]
    [TestCase(TerminalTab.Services, 0, 4, 0, 0)]
    [TestCase(TerminalTab.Usage, 1000, 4, 1000, 4)]
    [TestCase(TerminalTab.Usage, 1, 5, 1, 0)]
    [TestCase(TerminalTab.Processes, 0, 5, 0, 5)]
    [TestCase(TerminalTab.Connections, 1000, 5, 1000, 5)]
    [TestCase(TerminalTab.Processes, -1, -1, 0, 0)]
    [TestCase(TerminalTab.Connections, 1001, 6, 0, 0)]
    [TestCase(TerminalTab.Hardware, 1000, 5, 0, 0)]
    [TestCase(TerminalTab.Processes, int.MinValue, 5, 0, 5)]
    [TestCase(TerminalTab.Processes, int.MaxValue, int.MaxValue, 0, 0)]
    public void TopNormalizationPrecedesTheServiceTreeSortRuleAndPreservesTabSortBounds(
        TerminalTab tab, int top, int sort, int expectedTop, int expectedSort)
    {
        var value = new CliTabPreferences { Top = top, Sort = sort, Status = 2, TrafficSeconds = 60, Workers = false };
        value.Normalize(tab);
        Assert.That((value.Top, value.Sort, value.Status, value.TrafficSeconds, value.Workers),
            Is.EqualTo((expectedTop, expectedSort, 2, 60, false)));
    }

    [TestCase(-1, -1, 0, 300)]
    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 1, 1, 300)]
    [TestCase(2, 59, 2, 300)]
    [TestCase(3, 60, 0, 60)]
    [TestCase(1, 61, 1, 300)]
    [TestCase(1, 299, 1, 300)]
    [TestCase(1, 300, 1, 300)]
    [TestCase(1, 301, 1, 300)]
    [TestCase(1, 1799, 1, 300)]
    [TestCase(1, 1800, 1, 1800)]
    [TestCase(1, 1801, 1, 300)]
    public void StatusAndTrafficWindowsKeepTheirExactValidValuesAndDefaults(int status, int traffic, int expectedStatus, int expectedTraffic)
    {
        var value = new CliTabPreferences { Top = 42, Sort = 2, Status = status, TrafficSeconds = traffic };
        value.Normalize(TerminalTab.Connections);
        Assert.That((value.Status, value.TrafficSeconds, value.Top, value.Sort), Is.EqualTo((expectedStatus, expectedTraffic, 42, 2)));
    }

    [TestCase(TerminalTab.Processes, true, true, true, 15, 2, "C:\\valid.exe")]
    [TestCase(TerminalTab.Services, false, false, true, 15, 2, "service:valid")]
    [TestCase(TerminalTab.Connections, true, false, true, 15, 2, "C:\\valid.exe")]
    [TestCase(TerminalTab.Usage, true, false, true, 15, 2, "C:\\valid.exe")]
    [TestCase(TerminalTab.Hardware, false, false, false, 0, 0, null)]
    public void TabRestrictionsClearOnlyUnsupportedOptionsAndKeepOtherSettings(TerminalTab tab, bool apps, bool grouped,
        bool pinned, int top, int sort, string? identity)
    {
        var value = new CliTabPreferences
        {
            Top = 15, Sort = 2, AppsOnly = true, Grouped = true, PinnedOnly = true, Workers = false, Status = 2,
            TrafficSeconds = 60, Pins = [new("C:\\valid.exe", "executable"), new("service:valid", "service")]
        };
        value.Normalize(tab);
        Assert.That((value.AppsOnly, value.Grouped, value.PinnedOnly, value.Top, value.Sort, value.Workers, value.Status, value.TrafficSeconds),
            Is.EqualTo((apps, grouped, pinned, top, sort, false, 2, 60)));
        Assert.That(value.Pins.Select(pin => pin.Identity), Is.EqualTo(identity is null ? Array.Empty<string>() : new[] { identity }));
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void UndefinedTabsRetainTheExistingFallbackRules(int rawTab)
    {
        var value = new CliTabPreferences { Top = 1, Sort = 5, AppsOnly = true, Grouped = true, PinnedOnly = true,
            Pins = [new("C:\\valid.exe", "kept")] };
        value.Normalize((TerminalTab)rawTab);
        Assert.That((value.Top, value.Sort, value.AppsOnly, value.Grouped, value.PinnedOnly), Is.EqualTo((1, 5, true, false, true)));
        Assert.That(value.Pins.Single().Identity, Is.EqualTo("C:\\valid.exe"));
    }

    [TestCase(TerminalTab.Processes)]
    [TestCase(TerminalTab.Services)]
    [TestCase(TerminalTab.Connections)]
    [TestCase(TerminalTab.Usage)]
    [TestCase(TerminalTab.Hardware)]
    public void NullPinArraysNormalizeToAnEmptyArrayOnEveryTab(TerminalTab tab)
    {
        var value = new CliTabPreferences { Pins = null! };
        value.Normalize(tab);
        Assert.That(value.Pins, Is.Not.Null.And.Empty);
    }

    [TestCase(TerminalTab.Processes)]
    [TestCase(TerminalTab.Services)]
    public void PinValidationKeepsCaseSensitivePrefixesPathCategoriesAndFirstNames(TerminalTab tab)
    {
        CliPin[] pins =
        [
            null!, new(null!, "null identity"), new("", "empty"), new("C:\\bad-null.exe", null!), new("C:\\bad-empty.exe", ""),
            new("pid:123", "obsolete"), new("relative.exe", "relative"), new("\\root-relative", "root relative"), new("C:relative.exe", "drive relative"),
            new("service:svc", "service"), new("Service:bad", "wrong service case"), new("exe:service:legacy", "legacy service"),
            new("exe:C:\\ok.exe", "legacy executable"), new("EXE:C:\\bad.exe", "wrong exe case"), new("exe:", "empty legacy"),
            new("C:/forward.exe", "forward slash"), new("\\\\server\\share\\app.exe", "UNC"), new("C:\\ok.exe", "duplicate"), new("C:\\space.exe", " ")
        ];
        var original = pins.Select(pin => pin?.Identity).ToArray();
        var value = new CliTabPreferences { Pins = pins };
        value.Normalize(tab);
        var expected = tab == TerminalTab.Services
            ? new[] { new CliPin("service:svc", "service"), new CliPin("service:legacy", "legacy service") }
            : new[] { new CliPin("C:\\ok.exe", "legacy executable"), new CliPin("C:/forward.exe", "forward slash"),
                new CliPin("\\\\server\\share\\app.exe", "UNC"), new CliPin("C:\\space.exe", " ") };
        Assert.That(value.Pins, Is.EqualTo(expected));
        Assert.That(pins.Select(pin => pin?.Identity), Is.EqualTo(original));
    }

    [TestCase(3, false, true)]
    [TestCase(32768, false, true)]
    [TestCase(32769, false, false)]
    [TestCase(32768, true, true)]
    [TestCase(32769, true, false)]
    public void IdentityLengthIsValidatedBeforeLegacyPrefixRemoval(int length, bool legacy, bool accepted)
    {
        var path = "C:\\" + new string('x', length - (legacy ? 4 : 0) - 3);
        var raw = legacy ? "exe:" + path : path;
        var value = new CliTabPreferences { Pins = [new(raw, "name")] };
        value.Normalize(TerminalTab.Processes);
        Assert.That(value.Pins.Length, Is.EqualTo(accepted ? 1 : 0));
        if (accepted) Assert.That(value.Pins[0].Identity, Is.EqualTo(path));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(256, true)]
    [TestCase(257, false)]
    public void PinNamesKeepTheirLengthBoundariesWithoutTrimming(int length, bool accepted)
    {
        var name = new string(' ', length);
        var value = new CliTabPreferences { Pins = [new("C:\\valid.exe", name)] };
        value.Normalize(TerminalTab.Processes);
        Assert.That(value.Pins.Length, Is.EqualTo(accepted ? 1 : 0));
        if (accepted) Assert.That(value.Pins[0].Name, Is.EqualTo(name));
    }

    [Test]
    public void DeduplicationPrecedesTheLimitAndPreservesTheFirstNormalizedIdentity()
    {
        var first = new CliPin("exe:C:\\App\\p0.exe", "first");
        var pins = new[] { new CliPin("relative", "invalid"), first, new CliPin("c:\\app\\P0.EXE", "duplicate") }
            .Concat(Enumerable.Range(1, 256).Select(index => new CliPin($"C:\\App\\p{index}.exe", $"n{index}"))).ToArray();
        var value = new CliTabPreferences { Pins = pins };
        value.Normalize(TerminalTab.Processes);
        Assert.Multiple(() =>
        {
            Assert.That(value.Pins, Has.Length.EqualTo(256));
            Assert.That(value.Pins[0], Is.EqualTo(new CliPin("C:\\App\\p0.exe", "first")));
            Assert.That(value.Pins[^1], Is.EqualTo(new CliPin("C:\\App\\p255.exe", "n255")));
            Assert.That(value.Pins.Select(pin => pin.Identity).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(256));
            Assert.That(first.Identity, Is.EqualTo("exe:C:\\App\\p0.exe"));
            Assert.That(pins, Has.Length.EqualTo(259));
        });
    }

    [Test, SetCulture("tr-TR")]
    public void PinDeduplicationUsesOrdinalCaseRulesIndependentlyOfTheCurrentCulture()
    {
        var value = new CliTabPreferences { Pins = [new("C:\\I.EXE", "first"), new("c:\\i.exe", "second")] };
        value.Normalize(TerminalTab.Processes);
        Assert.That(value.Pins, Is.EqualTo(new[] { new CliPin("C:\\I.EXE", "first") }));
    }

    [Test]
    public void NormalizationIsIdempotentAndDoesNotRewriteTheInputPinRecords()
    {
        var original = new CliPin("exe:C:\\valid.exe", "kept");
        var value = new CliTabPreferences { Top = -1, Sort = 3, Grouped = true, AppsOnly = true, Pins = [original] };
        value.Normalize(TerminalTab.Processes);
        var first = JsonSerializer.Serialize(value);
        value.Normalize(TerminalTab.Processes);
        Assert.That(JsonSerializer.Serialize(value), Is.EqualTo(first));
        Assert.That(original.Identity, Is.EqualTo("exe:C:\\valid.exe"));
    }

    [Test]
    public void ParentNormalizationAndJsonRoundTripKeepIndependentTabPolicies()
    {
        var value = new CliPreferences
        {
            ActiveTab = TerminalTab.Hardware,
            Tabs = Enumerable.Range(0, 5).Select(_ => new CliTabPreferences { Top = 25, Sort = 3, AppsOnly = true, Grouped = true,
                Status = 3, TrafficSeconds = 59, Workers = false, PinnedOnly = true,
                Pins = [new("exe:C:\\valid.exe", "executable"), new("service:svc", "service")] }).ToArray()
        };
        value.Normalize();
        var json = CliPreferences.Serialize(value);
        var roundTrip = JsonSerializer.Deserialize<CliPreferences>(json, CliSettingsStore.JsonOptions)!;
        roundTrip.Normalize();
        Assert.Multiple(() =>
        {
            Assert.That(CliPreferences.Serialize(roundTrip), Is.EqualTo(json));
            Assert.That(roundTrip.ActiveTab, Is.EqualTo(TerminalTab.Hardware));
            Assert.That(roundTrip.Tabs.Select(tab => tab.Sort), Is.EqualTo(new[] { 3, 0, 3, 3, 0 }));
            Assert.That(roundTrip.Tabs.Select(tab => tab.Top), Is.EqualTo(new[] { 25, 25, 25, 25, 0 }));
            Assert.That(roundTrip.Tabs.Select(tab => tab.AppsOnly), Is.EqualTo(new[] { true, false, true, true, false }));
            Assert.That(roundTrip.Tabs.Select(tab => tab.Grouped), Is.EqualTo(new[] { true, false, false, false, false }));
            Assert.That(roundTrip.Tabs.Select(tab => tab.PinnedOnly), Is.EqualTo(new[] { true, true, true, true, false }));
            Assert.That(roundTrip.Tabs.All(tab => tab.Status == 0 && tab.TrafficSeconds == 300 && !tab.Workers), Is.True);
            Assert.That(roundTrip.Tabs[1].Pins, Is.EqualTo(new[] { new CliPin("service:svc", "service") }));
            Assert.That(roundTrip.Tabs[4].Pins, Is.Empty);
        });
    }
}
