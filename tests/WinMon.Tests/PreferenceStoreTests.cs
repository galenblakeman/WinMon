using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class PreferenceStoreTests
{
    [TestCaseSource(typeof(ThemeFinishes), nameof(ThemeFinishes.Names))]
    public async Task AppearanceFinish_RoundTripsWithoutChangingGaugeOrPalette(string theme)
    {
        var store = new PreferenceStore(settingsPath);
        await store.SaveAsync(new MonitorPreferences
        {
            Theme = theme, GaugeStyle = GaugeStyle.SegmentedBar, ChartPalette = "Ocean"
        });

        var loaded = await store.LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo(theme));
            Assert.That(loaded.GaugeStyle, Is.EqualTo(GaugeStyle.SegmentedBar));
            Assert.That(loaded.ChartPalette, Is.EqualTo("Ocean"));
        });
    }

    [Test]
    public async Task ProcessActionsHidden_DefaultsForLegacySettings_AndRoundTripsBothStates()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, "{\"TopProcessCount\":10}");
        Assert.That((await new PreferenceStore(settingsPath).LoadAsync()).ProcessActionsHidden, Is.False);
        foreach (var hidden in new[] { true, false })
        {
            await new PreferenceStore(settingsPath).SaveAsync(new MonitorPreferences { ProcessActionsHidden = hidden });
            Assert.That((await new PreferenceStore(settingsPath).LoadAsync()).ProcessActionsHidden, Is.EqualTo(hidden));
        }
    }

    [Test]
    public async Task AppsOnly_DefaultsToAllProcesses_AndRoundTrips()
    {
        var store = new PreferenceStore(settingsPath);
        Assert.That((await store.LoadAsync()).AppsOnly, Is.False);
        await store.SaveAsync(new MonitorPreferences { AppsOnly = true });
        Assert.That((await store.LoadAsync()).AppsOnly, Is.True);
    }

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
    [TestCase(false)]
    [TestCase(true)]
    public async Task StorageMode_LoadsAndSavesOnlySelectedProfile(bool portable)
    {
        string applicationDirectory = Path.Combine(testDirectory, "app");
        string localDirectory = Path.Combine(testDirectory, "local");
        string portablePath = Path.Combine(applicationDirectory, "settings.json");
        string installedPath = Path.Combine(localDirectory, "WinMon", "settings.json");
        Directory.CreateDirectory(applicationDirectory);
        await new PreferenceStore(portablePath).SaveAsync(new MonitorPreferences { Theme = "Light" });
        await new PreferenceStore(installedPath).SaveAsync(new MonitorPreferences { Theme = "Dark" });
        if (portable)
        {
            await File.WriteAllTextAsync(Path.Combine(applicationDirectory, "portable.flag"), "");
        }

        var store = new PreferenceStore(applicationDirectory, localDirectory);
        Assert.That((await store.LoadAsync()).Theme, Is.EqualTo(portable ? "Light" : "Dark"));
        await store.SaveAsync(new MonitorPreferences { Theme = "System" });

        var saved = await new PreferenceStore(portable ? portablePath : installedPath).LoadAsync();
        var untouched = await new PreferenceStore(portable ? installedPath : portablePath).LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Theme, Is.EqualTo("System"));
            Assert.That(untouched.Theme, Is.EqualTo(portable ? "Dark" : "Light"));
        });
    }

    [Test]
    public async Task PortableMode_MissingSettingsUsesDefaults_AndKeepsPathUntilRestart()
    {
        string applicationDirectory = Path.Combine(testDirectory, "app");
        string localDirectory = Path.Combine(testDirectory, "local");
        string installedPath = Path.Combine(localDirectory, "WinMon", "settings.json");
        Directory.CreateDirectory(applicationDirectory);
        string markerPath = Path.Combine(applicationDirectory, "portable.flag");
        await File.WriteAllTextAsync(markerPath, "");
        await new PreferenceStore(installedPath).SaveAsync(new MonitorPreferences { Theme = "Light" });
        var store = new PreferenceStore(applicationDirectory, localDirectory);

        Assert.That((await store.LoadAsync()).Theme, Is.EqualTo("Dark"));
        File.Delete(markerPath);
        await store.SaveAsync(new MonitorPreferences { Theme = "System" });

        Assert.That((await new PreferenceStore(Path.Combine(applicationDirectory, "settings.json")).LoadAsync()).Theme, Is.EqualTo("System"));
        Assert.That((await new PreferenceStore(applicationDirectory, localDirectory).LoadAsync()).Theme, Is.EqualTo("Light"));
    }

    [Test]
    [TestCase("{")]
    [TestCase("null")]
    public async Task LoadAsync_CorruptOrNullConfiguration_ReturnsDefaults(string content)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, content);
        PreferenceStore store = new(settingsPath);

        MonitorPreferences preferences = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(preferences.Width, Is.EqualTo(600));
            Assert.That(preferences.Height, Is.EqualTo(840));
            Assert.That(preferences.Theme, Is.EqualTo("Dark"));
            Assert.That(preferences.RetentionSeconds, Is.EqualTo(600));
        });
    }

    [Test]
    public async Task LoadAsync_OutOfRangeConfiguration_NormalizesValues()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, """
            { "Width": -1, "Height": 9000, "Theme": "unsupported", "RetentionSeconds": 9999,
              "TopUsageCount": 7, "UsageSortColumn": 9 }
            """);
        PreferenceStore store = new(settingsPath);

        MonitorPreferences preferences = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(preferences.Width, Is.EqualTo(600));
            Assert.That(preferences.Height, Is.EqualTo(1500));
            Assert.That(preferences.Theme, Is.EqualTo("Dark"));
            Assert.That(preferences.RetentionSeconds, Is.EqualTo(3600));
            Assert.That(preferences.TopUsageCount, Is.EqualTo(20));
            Assert.That(preferences.UsageSortColumn, Is.Zero);
            Assert.That(preferences.UsageAppsOnly, Is.False);
        });
    }

    [Test]
    public async Task SaveAsync_RoundTripsValidatedPreferencesWithoutTemporaryFiles()
    {
        PreferenceStore store = new(settingsPath);
        MonitorPreferences supplied = new()
        {
            Width = 100,
            Height = 100,
            Theme = "system",
            RetentionSeconds = 1,
            MinimizeToTray = true,
        };

        await store.SaveAsync(supplied);
        MonitorPreferences loaded = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(600));
            Assert.That(loaded.Height, Is.EqualTo(160));
            Assert.That(loaded.Theme, Is.EqualTo("System"));
            Assert.That(loaded.RetentionSeconds, Is.EqualTo(600));
            Assert.That(loaded.MinimizeToTray, Is.True);
            Assert.That(Directory.GetFiles(testDirectory, "*.tmp"), Is.Empty);
        });
    }

    [Test]
    public async Task GpuVisibility_OldPreferencesEnableGpu_AndHiddenStateRoundTrips()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, "{\"VisibleMetrics\":5}");
        var store = new PreferenceStore(settingsPath);
        var old = await store.LoadAsync();
        Assert.That(old.SeriesMask, Is.EqualTo(21));
        await store.SaveAsync(old with { ShowGpu = false });
        var loaded = await store.LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.ShowGpu, Is.False);
            Assert.That(loaded.SeriesMask, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ProcessGpuRanking_DefaultsToCpu_AndRoundTrips()
    {
        var store = new PreferenceStore(settingsPath);
        Assert.That((await store.LoadAsync()).ProcessSort, Is.EqualTo(ProcessSort.Cpu));
        await store.SaveAsync(new MonitorPreferences { ProcessSort = ProcessSort.Gpu });
        Assert.That((await store.LoadAsync()).ProcessSort, Is.EqualTo(ProcessSort.Gpu));
    }

    [TestCase(600, 0, new[] { 120, 600 }, 120)]
    [TestCase(3600, 3600, new[] { 120, 600, 1800, 3600 }, 3600)]
    [TestCase(3600, 1800, new[] { 120, 600, 1800, 3600 }, 1800)]
    [TestCase(1800, 600, new[] { 120, 600, 1800 }, 600)]
    [TestCase(600, 1800, new[] { 120, 600 }, 120)]
    [TestCase(1800, 300, new[] { 120, 600, 1800 }, 120)]
    public void ChartRanges_AreFixedChoicesUpToRetention(int retention, int chosen, int[] ranges, int effective)
    {
        var preferences = new MonitorPreferences { RetentionSeconds = retention, ChartRangeSeconds = chosen };
        Assert.Multiple(() =>
        {
            Assert.That(preferences.ChartRanges, Is.EqualTo(ranges));
            Assert.That(preferences.EffectiveChartRange, Is.EqualTo(effective));
        });
    }

    [Test]
    public async Task GroupProcesses_DefaultsToIndividual_AndRoundTrips()
    {
        var store = new PreferenceStore(settingsPath);
        Assert.That((await store.LoadAsync()).GroupProcesses, Is.False);
        await store.SaveAsync(new MonitorPreferences { GroupProcesses = true });
        Assert.That((await store.LoadAsync()).GroupProcesses, Is.True);
    }

    [Test]
    public async Task ChartRange_RoundTrips_AndLegacyExpandedSettingStillLoads()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, "{\"Expanded\":true,\"RetentionSeconds\":1800,\"TopProcessCount\":10}");
        var store = new PreferenceStore(settingsPath);
        var legacy = await store.LoadAsync();
        await store.SaveAsync(legacy with { ChartRangeSeconds = 1800 });
        var loaded = await store.LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(legacy.TopProcessCount, Is.EqualTo(10));
            Assert.That(legacy.EffectiveChartRange, Is.EqualTo(120));
            Assert.That(loaded.EffectiveChartRange, Is.EqualTo(1800));
        });
    }

    [Test]
    public async Task ResourceChartChoice_RoundTrips_AndRejectsInvalidValues()
    {
        var store = new PreferenceStore(settingsPath);
        await store.SaveAsync(new MonitorPreferences
        {
            ResourceChartType = ResourceChartType.Focus,
            FocusMetric = 5,
            SecondaryDrive = "D:"
        });
        var loaded = await store.LoadAsync();
        var saved = await File.ReadAllTextAsync(settingsPath);
        var invalid = PreferenceStore.Validate(new MonitorPreferences
        {
            ResourceChartType = (ResourceChartType)99,
            FocusMetric = 99,
            SecondaryDrive = "D:"
        });
        Assert.Multiple(() =>
        {
            Assert.That(loaded.ResourceChartType, Is.EqualTo(ResourceChartType.Focus));
            Assert.That(saved, Does.Contain("\"ResourceChartType\": \"Focus\""));
            Assert.That(loaded.FocusMetric, Is.EqualTo(5));
            Assert.That(invalid.ResourceChartType, Is.EqualTo(ResourceChartType.Overlay));
            Assert.That(invalid.FocusMetric, Is.EqualTo(5));
            Assert.That(PreferenceStore.Validate(new MonitorPreferences { FocusMetric = 5 }).FocusMetric,
                Is.EqualTo(0));
        });
    }

    [TestCase("{}", GaugeStyle.Bowl)]
    [TestCase("{\"GaugeStyle\":\"Needle\"}", GaugeStyle.Needle)]
    [TestCase("{\"GaugeStyle\":\"None\"}", GaugeStyle.None)]
    [TestCase("{\"GaugeStyle\":42}", GaugeStyle.Bowl)]
    public async Task GaugeStyle_DefaultsToTheBowl_AndSavesByName(string json, GaugeStyle expected)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, json);
        var store = new PreferenceStore(settingsPath);
        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);
        var saved = await File.ReadAllTextAsync(settingsPath);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.GaugeStyle, Is.EqualTo(expected));
            Assert.That(saved, Does.Contain($"\"GaugeStyle\": \"{expected}\""));
        });
    }

    [TestCase("{\"SortByGpu\":true}", ProcessSort.Gpu)]
    [TestCase("{\"SortByGpu\":false}", ProcessSort.Cpu)]
    [TestCase("{\"ProcessSort\":\"Memory\"}", ProcessSort.Memory)]
    [TestCase("{\"ProcessSort\":\"Threads\"}", ProcessSort.Threads)]
    [TestCase("{\"ProcessSort\":\"Handles\"}", ProcessSort.Handles)]
    [TestCase("{\"ProcessSort\":\"Io\",\"SortByGpu\":true}", ProcessSort.Io)]
    [TestCase("{\"ProcessSort\":42}", ProcessSort.Cpu)]
    public async Task ProcessSort_MigratesLegacyGpuFlag_AndRejectsUnknownValues(string json, ProcessSort expected)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, json);
        var store = new PreferenceStore(settingsPath);
        var loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);
        var saved = await File.ReadAllTextAsync(settingsPath);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.ProcessSort, Is.EqualTo(expected));
            Assert.That(saved, Does.Contain($"\"ProcessSort\": \"{expected}\""));
            Assert.That(saved, Does.Not.Contain("SortByGpu"));
        });
    }

    [Test]
    public void SaveAsync_CanceledBeforeWriting_ThrowsWithoutCreatingPreferences()
    {
        PreferenceStore store = new(settingsPath);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        Assert.That(async () => await store.SaveAsync(new MonitorPreferences(), cancellationSource.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(File.Exists(settingsPath), Is.False);
    }

    [TestCase(5)]
    [TestCase(10)]
    [TestCase(15)]
    [TestCase(20)]
    [TestCase(50)]
    [TestCase(0)]
    public async Task TopProcessCount_RoundTrips(int count)
    {
        PreferenceStore store = new(settingsPath);
        await store.SaveAsync(new MonitorPreferences { TopProcessCount = count });
        Assert.That((await store.LoadAsync()).TopProcessCount, Is.EqualTo(count));
    }

    [TestCase("{}")]
    [TestCase("{\"TopProcessCount\":7}")]
    [TestCase("{\"TopProcessCount\":1000000}")]
    public async Task TopProcessCount_MissingOrUnsupported_UsesFive(string json)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, json);
        Assert.That((await new PreferenceStore(settingsPath).LoadAsync()).TopProcessCount, Is.EqualTo(5));
    }

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(10)]
    [TestCase(15)]
    [TestCase(20)]
    [TestCase(50)]
    public async Task ServiceAndProcessCountsRoundTripIndependently(int serviceCount)
    {
        var store = new PreferenceStore(settingsPath);
        await store.SaveAsync(new MonitorPreferences { TopProcessCount = 0, TopServiceCount = serviceCount });
        var loaded = await new PreferenceStore(settingsPath).LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.TopProcessCount, Is.Zero);
            Assert.That(loaded.TopServiceCount, Is.EqualTo(serviceCount));
        });
    }

    [TestCase("{}")]
    [TestCase("{\"TopServiceCount\":7}")]
    [TestCase("{\"TopServiceCount\":-1}")]
    public async Task ServiceCountMissingOrInvalidDefaultsToTwenty(string json)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, json);
        Assert.That((await new PreferenceStore(settingsPath).LoadAsync()).TopServiceCount, Is.EqualTo(20));
    }

    [TestCase("C:\\Program Files\\WinMon\\WinMon.exe", true)]
    [TestCase("C:\\Program Files\\dotnet\\dotnet.exe", false)]
    [TestCase("C:\\Program Files\\dotnet\\testhost.exe", false)]
    [TestCase(null, false)]
    public void CanRegisterExecutablePath_DevelopmentHostsAreRejected(string? executablePath, bool expected)
    {
        Assert.That(StartupRegistration.CanRegisterExecutablePath(executablePath), Is.EqualTo(expected));
    }

    [Test]
    public void ApplySettings_TakesOnlyDialogSettings_AndKeepsChoicesMadeInTheWindowMeanwhile()
    {
        var opened = new MonitorPreferences { Theme = "Dark", RetentionSeconds = 600, GroupProcesses = false, CompactMode = false };
        var confirmed = opened with
        {
            Theme = "Light", RetentionSeconds = 1800, MinimizeToTray = !opened.MinimizeToTray,
            GaugeStyle = GaugeStyle.SegmentedBar
        };
        // While the confirmed settings were being saved, the user grouped processes and pinned one.
        var current = opened with
        {
            GroupProcesses = true, CompactMode = true,
            PinnedProcesses = [new SavedProcessPin(10, 100, "pinned", "", "", null, false)]
        };

        var applied = SettingsWindow.ApplySettings(current, confirmed);

        Assert.Multiple(() =>
        {
            Assert.That(applied.Theme, Is.EqualTo("Light"));
            Assert.That(applied.RetentionSeconds, Is.EqualTo(1800));
            Assert.That(applied.MinimizeToTray, Is.EqualTo(confirmed.MinimizeToTray));
            Assert.That(applied.GaugeStyle, Is.EqualTo(GaugeStyle.SegmentedBar));
            Assert.That(applied.GroupProcesses, Is.True);
            Assert.That(applied.CompactMode, Is.True);
            Assert.That(applied.PinnedProcesses.Single().Name, Is.EqualTo("pinned"));
        });
    }
}
