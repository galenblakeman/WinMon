using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class SettingsLayoutTests
{
    [Test]
    public void Sections_OpenOnGeneralAndFollowTheSidebar()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            var opened = window.Section;
            SettingsSection[] expected = UpdatePolicy.GitHubUpdatesEnabled
                ? [SettingsSection.General, SettingsSection.Monitoring, SettingsSection.AutomaticCaptures,
                    SettingsSection.Updates]
                : [SettingsSection.General, SettingsSection.Monitoring, SettingsSection.AutomaticCaptures];
            var shown = expected.Select(section =>
            {
                window.ShowSection(section);
                return window.Section;
            }).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.EqualTo(SettingsSection.General));
                Assert.That(shown, Is.EqualTo(expected));
                Assert.That(window.Height, Is.LessThanOrEqualTo(600), "Every section fits without growing the dialog.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CaptureSwitch_ShowsItsStateAndIsWhatSaveReads()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            var section = window.AutoCapture;
            var off = (section.EnabledSwitch.Content, section.Read(out _, out _)!.Enabled);
            section.EnabledSwitch.IsChecked = true;
            var on = (section.EnabledSwitch.Content, section.Read(out _, out _)!.Enabled);

            Assert.Multiple(() =>
            {
                Assert.That(off, Is.EqualTo((Strings.SettingsOff, false)));
                Assert.That(on, Is.EqualTo((Strings.SettingsOn, true)));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void CaptureRead_NamesTheFieldToFix()
    {
        var window = Settings(new MonitorPreferences());
        try
        {
            window.ShowSection(SettingsSection.AutomaticCaptures);
            var box = Descendants(window.AutoCapture).OfType<TextBox>().First(b => b.Text == "90");
            box.Text = "250";

            var result = window.AutoCapture.Read(out var error, out var invalid);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.Null);
                Assert.That(error, Is.Not.Null);
                Assert.That(invalid, Is.SameAs(box));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void SecondDiskRule_IsUnavailableWithoutASecondDrive()
    {
        var window = Settings(new MonitorPreferences { PrimaryDrive = "C:", SecondaryDrive = null });
        try
        {
            var rows = Descendants(window.AutoCapture).OfType<CheckBox>()
                .Where(box => box.Content is StackPanel).ToArray();
            var second = rows[CaptureRuleSetting.Defaults.ToList().FindIndex(r => r.Metric == CaptureMetric.SecondaryDisk)];
            var disabled = second.IsEnabled;
            window.SecondaryDriveSelector.ItemsSource = new[] { new SettingsWindow.DriveChoice("D:", "D:") };
            window.SecondaryDriveSelector.SelectedIndex = 0;

            Assert.Multiple(() =>
            {
                Assert.That(disabled, Is.False);
                Assert.That(second.IsEnabled, Is.True, "Choosing a second drive enables its rule.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    private static SettingsWindow Settings(MonitorPreferences preferences)
    {
        return new SettingsWindow(preferences, () => [], checkForUpdates: () => Task.FromResult("checked"),
            captureFolder: Path.Combine(Path.GetTempPath(), "WinMon.SettingsLayoutTests"));
    }

    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent)
                     .OfType<System.Windows.DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
