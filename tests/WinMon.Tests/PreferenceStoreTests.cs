using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class PreferenceStoreTests
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
            Assert.That(preferences.CompactHistorySeconds, Is.EqualTo(120));
            Assert.That(preferences.RetentionSeconds, Is.EqualTo(600));
        });
    }

    [Test]
    public async Task LoadAsync_OutOfRangeConfiguration_NormalizesValues()
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, """
            { "Width": -1, "Height": 9000, "Theme": "unsupported", "CompactHistorySeconds": 200, "RetentionSeconds": 9999 }
            """);
        PreferenceStore store = new(settingsPath);

        MonitorPreferences preferences = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(preferences.Width, Is.EqualTo(600));
            Assert.That(preferences.Height, Is.EqualTo(1500));
            Assert.That(preferences.Theme, Is.EqualTo("Dark"));
            Assert.That(preferences.CompactHistorySeconds, Is.EqualTo(120));
            Assert.That(preferences.RetentionSeconds, Is.EqualTo(3600));
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
            CompactHistorySeconds = 500,
            RetentionSeconds = 1,
            CloseToTray = true,
        };

        await store.SaveAsync(supplied);
        MonitorPreferences loaded = await store.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(600));
            Assert.That(loaded.Height, Is.EqualTo(800));
            Assert.That(loaded.Theme, Is.EqualTo("System"));
            Assert.That(loaded.CompactHistorySeconds, Is.EqualTo(300));
            Assert.That(loaded.RetentionSeconds, Is.EqualTo(600));
            Assert.That(loaded.CloseToTray, Is.True);
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

    [TestCase(120, 600, 0, new[] { 120, 600 }, 120)]
    [TestCase(60, 3600, 3600, new[] { 60, 600, 3600 }, 3600)]
    [TestCase(300, 1800, 600, new[] { 300, 600, 1800 }, 600)]
    [TestCase(120, 600, 1800, new[] { 120, 600 }, 120)]
    public void ChartRanges_FollowShortRangeAndRetention(int shortRange, int retention, int chosen, int[] ranges, int effective)
    {
        var preferences = new MonitorPreferences { CompactHistorySeconds = shortRange, RetentionSeconds = retention, ChartRangeSeconds = chosen };
        Assert.Multiple(() =>
        {
            Assert.That(preferences.ChartRanges, Is.EqualTo(ranges));
            Assert.That(preferences.EffectiveChartRange, Is.EqualTo(effective));
        });
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

    [TestCase("{\"SortByGpu\":true}", ProcessSort.Gpu)]
    [TestCase("{\"SortByGpu\":false}", ProcessSort.Cpu)]
    [TestCase("{\"ProcessSort\":\"Memory\"}", ProcessSort.Memory)]
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
    public async Task TopProcessCount_RoundTrips(int count)
    {
        PreferenceStore store = new(settingsPath);
        await store.SaveAsync(new MonitorPreferences { TopProcessCount = count });
        Assert.That((await store.LoadAsync()).TopProcessCount, Is.EqualTo(count));
    }

    [TestCase("{}")]
    [TestCase("{\"TopProcessCount\":0}")]
    [TestCase("{\"TopProcessCount\":7}")]
    [TestCase("{\"TopProcessCount\":1000000}")]
    public async Task TopProcessCount_MissingOrUnsupported_UsesFive(string json)
    {
        Directory.CreateDirectory(testDirectory);
        await File.WriteAllTextAsync(settingsPath, json);
        Assert.That((await new PreferenceStore(settingsPath).LoadAsync()).TopProcessCount, Is.EqualTo(5));
    }

    [TestCase("C:\\Program Files\\WinMon\\WinMon.exe", true)]
    [TestCase("C:\\Program Files\\dotnet\\dotnet.exe", false)]
    [TestCase("C:\\Program Files\\dotnet\\testhost.exe", false)]
    [TestCase(null, false)]
    public void CanRegisterExecutablePath_DevelopmentHostsAreRejected(string? executablePath, bool expected)
    {
        Assert.That(StartupRegistration.CanRegisterExecutablePath(executablePath), Is.EqualTo(expected));
    }
}
