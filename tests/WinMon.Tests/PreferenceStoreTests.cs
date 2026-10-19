using NUnit.Framework;
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
