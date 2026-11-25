using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessActionUpdateCharacterizationTests
{
    private static readonly ProcessUsage Process = new(0, 1, "synthetic.exe", 1) { MemberCount = 3 };
    private static ProcessDetailsSnapshot Snapshot => new(Process, false, DateTimeOffset.UnixEpoch, false, true, "");

    [OneTimeSetUp]
    public void WarmFallbackIcons()
    {
        foreach (var ticks in new[] { 1L, 2L })
            Assert.That(ProcessIconProvider.GetAsync(Process with { CreationTicks = ticks }).GetAwaiter().GetResult(),
                Is.SameAs(ProcessIconProvider.Fallback));
    }

    private static IEnumerable<TestCaseData> SelectionCases()
    {
        foreach (var grouped in new[] { false, true })
        foreach (var historical in new[] { false, true })
        foreach (var available in new[] { false, true })
        foreach (var canEnd in new[] { false, true })
            yield return new TestCaseData(grouped, historical, available, canEnd)
                .SetName($"IdentityAndEndEligibility_{grouped}_{historical}_{available}_{canEnd}");
    }

    [TestCaseSource(nameof(SelectionCases))]
    public void IdentityAndEndEligibilityRespectTheSelectedSnapshot(bool grouped, bool historical, bool available, bool canEnd)
    {
        var bar = new ProcessActionBar();
        var snapshot = Snapshot with { Grouped = grouped, Historical = historical, Available = available };

        bar.Update(snapshot, canEnd, true);

        var detail = grouped ? Strings.Format(Strings.Processes, 3) : Strings.Format(Strings.PID, 0);
        Assert.Multiple(() =>
        {
            Assert.That(bar.Title, Is.EqualTo(Process.Name));
            Assert.That(bar.Detail, Is.EqualTo(detail));
            Assert.That(Text(bar, bar.Title).ToolTip, Is.EqualTo(bar.Title));
            Assert.That(Text(bar, detail).ToolTip, Is.EqualTo(detail));
            Assert.That(Status(bar).Text, Is.EqualTo(historical ? Strings.ProcessHistoricalView : Strings.LiveMode));
            Assert.That(((Border)Status(bar).Parent).Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Descendants(bar).OfType<Image>().Single().Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Descendants(bar).OfType<Image>().Single().Source, Is.SameAs(ProcessIconProvider.Fallback));
            Assert.That(Button(bar, grouped ? Strings.EndProcessGroup : Strings.EndProcess).IsEnabled,
                Is.EqualTo(available && !historical && canEnd));
            Assert.That(Button(bar, Strings.Properties).IsEnabled, Is.True);
            Assert.That(Button(bar, Strings.CopyDetails).IsEnabled, Is.True);
            Assert.That(Button(bar, Strings.MoreActions + " ▾").IsEnabled, Is.True);
            Assert.That(Button(bar, grouped ? Strings.EndProcessGroup : Strings.EndProcess).ToolTip,
                Is.EqualTo(Strings.AvailableForLiveProcesses));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("Example app")]
    public void OptionalAppNamesOnlyAppendNonWhitespaceText(string? appName)
    {
        var bar = new ProcessActionBar();
        bar.Update(Snapshot with { Process = Process with { AppName = appName! } }, true, true);
        var expected = Strings.Format(Strings.PID, 0) + (appName == "Example app" ? " · Example app" : "");
        Assert.That(bar.Detail, Is.EqualTo(expected));
        Assert.That(Text(bar, expected).ToolTip, Is.EqualTo(expected));
    }

    [Test]
    public void ClearingSelectionRestoresEmptyIdentityAndDisablesAllProcessActions()
    {
        var bar = new ProcessActionBar();
        bar.Resources["Line"] = Brushes.Gray;
        bar.Resources["Accent"] = Brushes.Red;
        bar.Update(Snapshot, true, true, true, true);
        var accent = Descendants(bar).OfType<Border>().Single(border => border.CornerRadius == new CornerRadius(3, 0, 0, 3));
        Assert.That(accent.Background, Is.SameAs(Brushes.Red));
        var requests = 0;
        bar.ActionRequested += (_, _) => requests++;

        bar.Update(null, true, true, true, true);
        Button(bar, Strings.CopyDetails).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(bar.Title, Is.EqualTo(Strings.SelectProcess));
            Assert.That(bar.Detail, Is.Empty);
            Assert.That(Status(bar).Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(((Border)Status(bar).Parent).Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Descendants(bar).OfType<Image>().Single().Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(accent.Background, Is.SameAs(Brushes.Gray));
            Assert.That(new[] { Strings.Properties, Strings.CopyDetails, Strings.MoreActions + " ▾", Strings.EndProcess }
                .All(label => !Button(bar, label).IsEnabled), Is.True);
            Assert.That(bar.CreateMoreMenu(), Is.Null);
            Assert.That(requests, Is.Zero);
        });
    }

    [TestCase(false, false, 1)]
    [TestCase(false, true, 0)]
    [TestCase(true, false, 0)]
    [TestCase(true, true, 0)]
    public void OwnershipIsReadOnlyForLiveIndividualSelectionsEvenWithoutAnObservation(bool grouped, bool historical, int calls)
    {
        var bar = new ProcessActionBar();
        var reads = new List<ProcessUsage>();
        bar.EnableControlActions(_ => throw new AssertionException("Update must not read control eligibility"),
            _ => throw new AssertionException("Update must not read suspend eligibility"),
            process => { reads.Add(process); return true; },
            _ => throw new AssertionException("Update must not read thread suspension"),
            _ => throw new AssertionException("Update must not read efficiency mode"));
        bar.Update(Snapshot with { Grouped = grouped, Historical = historical, State = "captured state" }, true, true);
        Assert.That(reads, Has.Count.EqualTo(calls));
        if (calls != 0) Assert.That(reads[0], Is.SameAs(Process));
        Assert.That(Status(bar).Text, Is.EqualTo("captured state"));
    }

    [TestCase(null, false)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(null, true)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void OnlyConfirmedOwnedSuspensionForTheCurrentLifetimeOverridesTheCapturedState(bool? observed, bool owned)
    {
        var bar = new ProcessActionBar();
        var snapshot = Snapshot with { State = "captured state" };
        bar.EnableControlActions(_ => true, _ => true, _ => owned, _ => observed, _ => EfficiencyModeState.Off);
        bar.Update(snapshot, true, true);
        bar.ObserveSuspension(Process, observed);
        bar.Update(snapshot, true, true);
        Assert.That(Status(bar).Text, Is.EqualTo(owned && observed == true ? Strings.ProcessSuspendedByWinMon : "captured state"));

        var reused = snapshot with { Process = Process with { CreationTicks = 2 } };
        bar.Update(reused, true, true);
        Assert.That(Status(bar).Text, Is.EqualTo("captured state"));
        bar.ObserveSuspension(Process, true);
        Assert.That(Status(bar).Text, Is.EqualTo("captured state"));
    }

    [Test]
    public void AnOwnershipReaderFailureRetainsTheExistingPartialUpdateOrder()
    {
        var bar = new ProcessActionBar();
        bar.Update(Snapshot with { State = "old state" }, false, false);
        var failure = new InvalidOperationException("ownership read failed");
        bar.EnableControlActions(_ => true, _ => true, _ => throw failure, _ => false, _ => EfficiencyModeState.Off);
        var next = Snapshot with { Process = Process with { Name = "next.exe", AppName = "Next app" }, State = "new state" };

        Assert.That(Assert.Throws<InvalidOperationException>(() => bar.Update(next, true, true)), Is.SameAs(failure));

        ProcessDetailsSnapshot? requested = null;
        bar.ActionRequested += (_, snapshot) => requested = snapshot;
        Button(bar, Strings.CopyDetails).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(bar.Title, Is.EqualTo("next.exe"));
            Assert.That(bar.Detail, Is.EqualTo(Strings.Format(Strings.PID, 0) + " · Next app"));
            Assert.That(Status(bar).Text, Is.EqualTo("old state"));
            Assert.That(Button(bar, Strings.EndProcess).IsEnabled, Is.False);
            Assert.That(requested, Is.SameAs(next));
        });
    }

    private static Button Button(ProcessActionBar bar, string label) => Descendants(bar).OfType<Button>()
        .Single(button => Equals(button.Content, label));
    private static TextBlock Text(ProcessActionBar bar, string value) => Descendants(bar).OfType<TextBlock>()
        .Single(text => text.Text == value);
    private static TextBlock Status(ProcessActionBar bar) => Descendants(bar).OfType<TextBlock>()
        .Single(text => text.Parent is Border);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
