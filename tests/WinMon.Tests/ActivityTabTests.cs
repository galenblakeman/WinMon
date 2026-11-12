using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class ActivityTabTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [Category("Integration")]
    public async Task SelectedTabRoundTripsWithCollapsedPanel(int selected)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            await new PreferenceStore(path).SaveAsync(new MonitorPreferences
                { SelectedActivityTab = selected, ActivityPanelCollapsed = true });
            var loaded = await new PreferenceStore(path).LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(loaded.SelectedActivityTab, Is.EqualTo(selected));
                Assert.That(loaded.ActivityPanelCollapsed, Is.True);
            });
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [TestCase(-1)]
    [TestCase(4)]
    public void InvalidTabFallsBackToProcesses(int selected)
    {
        Assert.That(
            PreferenceStore.Validate(new MonitorPreferences { SelectedActivityTab = selected }).SelectedActivityTab,
            Is.Zero);
    }
}