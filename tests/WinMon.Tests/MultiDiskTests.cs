using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class MultiDiskTests
{
    private string testDirectory = null!;
    private string settingsPath = null!;

    [SetUp]
    public void SetUp()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));
        settingsPath = Path.Combine(testDirectory, "settings.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Test]
    public void Defaults_UseSystemDrive_AndDoNotConfigureASecondDrive()
    {
        var preferences = new MonitorPreferences();

        Assert.Multiple(() =>
        {
            Assert.That(preferences.PrimaryDrive, Is.EqualTo(Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\')));
            Assert.That(preferences.SecondaryDrive, Is.Null);
            Assert.That(preferences.ShowSecondaryDisk, Is.True);
            Assert.That(preferences.SeriesMask, Is.EqualTo(31));
        });
    }

    [Test]
    public void Validate_NormalizesDriveLetters_AndRejectsInvalidOrDuplicateDrives()
    {
        var normalized = PreferenceStore.Validate(new MonitorPreferences { PrimaryDrive = " c:\\ ", SecondaryDrive = " e:\\ " });
        var duplicate = PreferenceStore.Validate(new MonitorPreferences { PrimaryDrive = "c:", SecondaryDrive = " C:\\" });
        var invalid = PreferenceStore.Validate(new MonitorPreferences { PrimaryDrive = "disk-one", SecondaryDrive = "\\\\server\\share" });

        Assert.Multiple(() =>
        {
            Assert.That(normalized.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(normalized.SecondaryDrive, Is.EqualTo("E:"));
            Assert.That(duplicate.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(duplicate.SecondaryDrive, Is.Null);
            Assert.That(invalid.PrimaryDrive, Is.EqualTo(new MonitorPreferences().PrimaryDrive));
            Assert.That(invalid.SecondaryDrive, Is.Null);
        });
    }

    [TestCase(null, true, 31)]
    [TestCase("E:", true, 63)]
    [TestCase("E:", false, 31)]
    public void SeriesMask_IncludesSecondaryTraceOnlyWhenConfiguredAndVisible(string? secondaryDrive, bool showSecondary, int expected)
    {
        var preferences = PreferenceStore.Validate(new MonitorPreferences { SecondaryDrive = secondaryDrive, ShowSecondaryDisk = showSecondary });

        Assert.That(preferences.SeriesMask, Is.EqualTo(expected));
    }

    [Test]
    public async Task PreferenceStore_LegacySettingsKeepLegacySeries_AndRoundTripConfiguredDrives()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, "{\"VisibleMetrics\":15}");
        var store = new PreferenceStore(settingsPath);

        var legacy = await store.LoadAsync();
        await store.SaveAsync(legacy with { PrimaryDrive = "c:\\", SecondaryDrive = "e:\\", ShowSecondaryDisk = true });
        var loaded = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(legacy.SecondaryDrive, Is.Null);
            Assert.That(legacy.SeriesMask, Is.EqualTo(31));
            Assert.That(loaded.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(loaded.SecondaryDrive, Is.EqualTo("E:"));
            Assert.That(loaded.SeriesMask, Is.EqualTo(63));
        });
    }

    [Test]
    public void SecondaryDiskMeasurements_DefaultToUnavailable_AndAreRetainedWithoutMutation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new ResourceSample().SecondaryDiskName, Is.Empty);
            Assert.That(new ResourceSample().SecondaryDiskActivePercent, Is.Null);
            Assert.That(new ResourceSample().SecondaryDiskBytesPerSecond, Is.Null);
        });

        var resources = new ResourceSample { SecondaryDiskName = "E:", SecondaryDiskActivePercent = 61.5, SecondaryDiskBytesPerSecond = 9_250_000 };
        var sample = new CpuSample(DateTimeOffset.UnixEpoch, 10, [], 0) { Resources = resources };
        var history = new CpuHistory();
        history.Add(sample);
        var before = history.Samples;
        var changed = resources with { SecondaryDiskActivePercent = 3 };
        history.Add(sample with { Timestamp = sample.Timestamp.AddSeconds(1), Resources = changed });

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo([sample]));
            Assert.That(history.Latest!.Resources.SecondaryDiskActivePercent, Is.EqualTo(3));
            Assert.That(history.Samples[0].Resources, Is.SameAs(resources));
            Assert.That(history.Samples[0].Resources.SecondaryDiskActivePercent, Is.EqualTo(61.5));
        });
    }

    [Test]
    public void CpuGraph_MapsAndDescribesConfiguredSecondaryDisk()
    {
        var sample = new CpuSample(DateTimeOffset.UnixEpoch, 10, [], 0)
        {
            Resources = new ResourceSample
            {
                DiskName = "C:",
                SecondaryDiskName = "E:",
                SecondaryDiskActivePercent = 61.5,
                SecondaryDiskBytesPerSecond = 9_250_000
            }
        };

        Assert.Multiple(() =>
        {
            Assert.That(CpuGraph.MetricValue(sample, 5), Is.EqualTo(61.5));
            Assert.That(CpuGraph.DescribeSample(sample), Does.Contain("Disk E: 61.5% · 9.25 MB/s"));
        });
    }
}
