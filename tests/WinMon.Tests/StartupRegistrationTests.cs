using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;
#if WINMON_STORE
using Windows.ApplicationModel;
#endif

namespace WinMon.Tests;

[TestFixture]
public sealed class StartupRegistrationTests
{
    [Test]
    public void PackageManifest_DeclaresTheStartupTaskTheCodeUses()
    {
        var manifest = XDocument.Load(Path.Combine(Path.GetDirectoryName(SourcePath())!, "..", "..", "packaging", "msix",
            "AppxManifest.xml"));
        var task = manifest.Descendants().Single(e => e.Name.LocalName == "StartupTask");
        var extension = task.Parent!;
        Assert.Multiple(() =>
        {
            Assert.That((string?)task.Attribute("TaskId"), Is.EqualTo(StartupRegistration.StartupTaskId));
            Assert.That((string?)task.Attribute("Enabled"), Is.EqualTo("false"), "Off until the user opts in.");
            Assert.That((string?)extension.Attribute("Category"), Is.EqualTo("windows.startupTask"));
            Assert.That((string?)extension.Attribute("Executable"), Is.EqualTo("WinMon.exe"));
        });
    }

    [Test]
    public void Availability_MatchesBuildChannel()
    {
        Assert.Multiple(() =>
        {
#if WINMON_STORE
            // The test host has no package identity, so it has no startup task to offer or change.
            Assert.That(StartupRegistration.Availability, Is.EqualTo(StartupAvailability.Unavailable));
            Assert.That(StartupRegistration.IsEnabled, Is.False);
            Assert.DoesNotThrow(() => StartupRegistration.SetEnabled(true));
#else
            Assert.That(StartupRegistration.Availability, Is.EqualTo(StartupAvailability.Available));
#endif
        });
    }

#if WINMON_STORE
    // StartupAvailability is internal, so cases name it.
    [TestCase(StartupTaskState.Disabled, nameof(StartupAvailability.Available))]
    [TestCase(StartupTaskState.Enabled, nameof(StartupAvailability.Available))]
    [TestCase(StartupTaskState.DisabledByUser, nameof(StartupAvailability.TurnedOffInWindows))]
    [TestCase(StartupTaskState.DisabledByPolicy, nameof(StartupAvailability.ManagedByPolicy))]
    [TestCase(StartupTaskState.EnabledByPolicy, nameof(StartupAvailability.ManagedByPolicy))]
    public void TaskState_DecidesWhetherSettingsMayChangeIt(StartupTaskState state, string expected)
    {
        Assert.That(StartupRegistration.AvailabilityOf(state), Is.EqualTo(Enum.Parse<StartupAvailability>(expected)));
    }
#endif

    [TestCase(nameof(StartupAvailability.Available), true, true)]
    [TestCase(nameof(StartupAvailability.Unavailable), false, false)]
    [TestCase(nameof(StartupAvailability.TurnedOffInWindows), true, false)]
    [TestCase(nameof(StartupAvailability.ManagedByPolicy), true, false)]
    [Apartment(ApartmentState.STA)]
    public void Settings_OffersTheOptionOnlyWhenTheUserMayChangeIt(string name, bool shown, bool enabled)
    {
        var availability = Enum.Parse<StartupAvailability>(name);
        var window = new SettingsWindow(new MonitorPreferences(), () => [], startupAvailability: availability);
        try
        {
            var elements = Descendants(window).ToArray();
            var box = elements.OfType<CheckBox>().SingleOrDefault(c => Equals(c.Content, Strings.StartWinMonWhenISignIn));
            string[] reasons = [Strings.StartupTurnedOffInWindows, Strings.StartupManagedByPolicy];
            var shownReasons = elements.OfType<TextBlock>().Select(t => t.Text).Where(t => reasons.Contains(t)).ToArray();
            string[] expectedReasons = availability switch
            {
                StartupAvailability.TurnedOffInWindows => [Strings.StartupTurnedOffInWindows],
                StartupAvailability.ManagedByPolicy => [Strings.StartupManagedByPolicy],
                _ => []
            };
            Assert.Multiple(() =>
            {
                Assert.That(box is not null, Is.EqualTo(shown));
                if (box is not null) Assert.That(box.IsEnabled, Is.EqualTo(enabled));
                Assert.That(shownReasons, Is.EqualTo(expectedReasons));
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string SourcePath([CallerFilePath] string path = "") => path;
}
