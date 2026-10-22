using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class PinPersistenceTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReopeningSettings_RestoresBothPinListsInOrderAndPinnedOnly(bool portable)
    {
        var application = Path.Combine(_directory, "app");
        var localData = Path.Combine(_directory, "local");
        Directory.CreateDirectory(application);
        if (portable) await File.WriteAllTextAsync(Path.Combine(application, "portable.flag"), "");
        var first = new ProcessPins();
        var individual = Process(20, 100, "editor");
        var group = Process(30, 200, "browser") with { ImagePath = @"C:\Apps\browser.exe" };
        first.Toggle(individual, false);
        first.Toggle(group, true);
        first.Toggle(Process(40, 300, "worker"), false);
        await new PreferenceStore(application, localData).SaveAsync(new MonitorPreferences
        {
            PinnedProcesses = first.Snapshot(), PinnedOnly = true
        });

        var loaded = await new PreferenceStore(application, localData).LoadAsync();
        var reopened = new ProcessPins();
        reopened.Restore(loaded.PinnedProcesses);
        Assert.Multiple(() =>
        {
            Assert.That(loaded.PinnedOnly, Is.True);
            Assert.That(reopened.Snapshot(), Is.EqualTo(first.Snapshot()));
            Assert.That(reopened.Count(false), Is.EqualTo(2));
            Assert.That(reopened.Count(true), Is.EqualTo(1));
            Assert.That(reopened.RetentionSnapshot()!(individual), Is.True);
            Assert.That(reopened.Contains(group with { ProcessId = 300, CreationTicks = 999, ImagePath = @"c:\apps\BROWSER.exe" }, true), Is.True);
        });
        reopened.Toggle(individual, false);
        await new PreferenceStore(application, localData).SaveAsync(loaded with { PinnedProcesses = reopened.Snapshot() });
        Assert.That((await new PreferenceStore(application, localData).LoadAsync()).PinnedProcesses.Select(p => p.ProcessId),
            Is.EqualTo(new[] { 30, 40 }));
    }

    [Test]
    public void RestoredIndividual_DoesNotFollowReusedPidOrRestoreStaleReadings()
    {
        var original = Process(20, 100, "editor") with { CpuPercent = 99, GpuPercent = 88, WorkingSetBytes = 12345, ThreadCount = 10 };
        var first = new ProcessPins();
        first.Toggle(original, false);
        var reopened = new ProcessPins();
        reopened.Restore(first.Snapshot());
        var reused = original with { CreationTicks = 200 };
        var sample = new CpuSample(DateTimeOffset.UtcNow, 10, [reused], 0);
        var missing = reopened.Resolve(sample, [reused], false, false).Single();
        Assert.Multiple(() =>
        {
            Assert.That(reopened.Contains(reused, false), Is.False);
            Assert.That(reopened.RetentionSnapshot()!(reused), Is.False);
            Assert.That(missing.State, Is.EqualTo(PinnedProcessState.Exited));
            Assert.That(missing.Usage.CpuPercent, Is.Zero);
            Assert.That(missing.Usage.GpuPercent, Is.Null);
            Assert.That(missing.Usage.WorkingSetBytes, Is.Null);
            Assert.That(missing.Usage.ThreadCount, Is.Null);
            Assert.That(reopened.Resolve(sample, [reused], false, true).Single().State, Is.EqualTo(PinnedProcessState.NotRecorded));
        });
    }

    [Test]
    public async Task InvalidAndDuplicateSavedPins_AreIgnoredWithoutDiscardingOtherSettings()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"Theme":"Light","PinnedOnly":true,"PinnedProcesses":[null,{},
              {"ProcessId":-1,"CreationTicks":1,"Name":"invalid"},
              {"ProcessId":20,"CreationTicks":100,"Name":"editor","ImagePath":null,"AppName":null},
              {"ProcessId":20,"CreationTicks":100,"Name":"duplicate"}]}
            """);
        var loaded = await new PreferenceStore(path).LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.Theme, Is.EqualTo("Light"));
            Assert.That(loaded.PinnedOnly, Is.True);
            Assert.That(loaded.PinnedProcesses, Has.Length.EqualTo(1));
            Assert.That(loaded.PinnedProcesses[0].Name, Is.EqualTo("editor"));
            Assert.That(loaded.PinnedProcesses[0].ImagePath, Is.Empty);
        });
    }

    [Test]
    public async Task OlderSettings_DefaultToNoPinsAndNormalRanking()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(path, """{"Theme":"Light","TopProcessCount":20}""");
        var loaded = await new PreferenceStore(path).LoadAsync();
        Assert.Multiple(() =>
        {
            Assert.That(loaded.PinnedProcesses, Is.Empty);
            Assert.That(loaded.PinnedOnly, Is.False);
            Assert.That(loaded.TopProcessCount, Is.EqualTo(20));
        });
    }

    private static ProcessUsage Process(int pid, long creation, string name) => new(pid, creation, name, 1)
    {
        AppName = name, HasAppWindow = true
    };
}
