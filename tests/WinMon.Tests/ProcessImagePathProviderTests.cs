using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed class ProcessImagePathProviderTests
{
    private static ProcessCpuTimes Process(long created = 100) => new(42, created, "updater", 0);

    [Test]
    public void Capture_ReadablePathIsImmediateAndCachedByLifetime()
    {
        var reads = 0;
        var provider = new ProcessImagePathProvider((_, _) => { reads++; return @"C:\Apps\app.exe"; },
            _ => throw new AssertionException("A readable path must not use the fallback."));
        List<ProcessCpuTimes> processes = [Process()];
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.EqualTo(@"C:\Apps\app.exe"));
        provider.Capture(processes);
        Assert.That(reads, Is.EqualTo(1));
        processes = [Process(200)];
        provider.Capture(processes);
        Assert.That(reads, Is.EqualTo(2));
    }

    [Test]
    public void Capture_FallbackWaitsForNextSnapshotOfSameLifetimeThenCaches()
    {
        var reads = 0;
        var provider = new ProcessImagePathProvider((_, _) => "", _ => { reads++; return @"C:\Dropbox\updater.exe"; });
        List<ProcessCpuTimes> first = [Process()];
        provider.Capture(first);
        Assert.That(first[0].ImagePath, Is.Empty, "Do not expose a PID-only result before verifying its lifetime.");
        List<ProcessCpuTimes> next = [Process()];
        provider.Capture(next);
        Assert.That(next[0].ImagePath, Is.EqualTo(@"C:\Dropbox\updater.exe"));
        provider.Capture(next);
        Assert.That(reads, Is.EqualTo(1));
        Assert.That(first[0].ImagePath, Is.Empty, "Earlier snapshots remain unchanged.");
    }

    [Test]
    public void Capture_ReusedPidNeverReceivesPendingOrCachedPathOfOldLifetime()
    {
        var candidate = @"C:\Old\updater.exe";
        var provider = new ProcessImagePathProvider((_, _) => "", _ => candidate);
        List<ProcessCpuTimes> processes = [Process()];
        provider.Capture(processes);
        candidate = @"C:\New\updater.exe";
        processes = [Process(200)];
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.Empty);
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.EqualTo(candidate));
        processes = [Process(300)];
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.Empty, "Cached paths must also be lifetime-scoped.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Capture_ProcessDisappearingDropsPendingAndCachedPaths(bool confirmed)
    {
        var candidate = @"C:\Old\updater.exe";
        var provider = new ProcessImagePathProvider((_, _) => "", _ => candidate);
        List<ProcessCpuTimes> processes = [Process()];
        provider.Capture(processes);
        if (confirmed) provider.Capture(processes);
        provider.Capture([]);
        candidate = @"C:\New\updater.exe";
        processes = [Process(200)];
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.Empty);
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.EqualTo(candidate));
    }

    [Test]
    public void Capture_FailedFallbackLeavesPathUnavailableWithoutRepeatedLookups()
    {
        var reads = 0;
        var provider = new ProcessImagePathProvider((_, _) => "", _ => { reads++; return ""; });
        List<ProcessCpuTimes> processes = [Process()];
        provider.Capture(processes);
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.Empty);
        Assert.That(reads, Is.EqualTo(1));
    }

    [TestCase(@"\Device\HarddiskVolume3\Apps\updater.exe", @"C:\Apps\updater.exe")]
    [TestCase(@"\device\harddiskvolume3\Apps\updater.exe", @"C:\Apps\updater.exe")]
    [TestCase(@"\Device\HarddiskVolume30\Apps\updater.exe", "")]
    [TestCase(@"\Device\Unknown\app.exe", "")]
    [TestCase(@"\??\C:\Apps\app.exe", @"C:\Apps\app.exe")]
    [TestCase(@"\??\relative.exe", "")]
    [TestCase("", "")]
    public void ToDosPath_MapsOnlyRecognizedAbsolutePaths(string nativePath, string expected)
    {
        Assert.That(ProcessImagePathProvider.ToDosPath(nativePath,
            drive => drive == "C:" ? @"\Device\HarddiskVolume3" : null), Is.EqualTo(expected));
    }

    [Test]
    public void ReadByProcessId_CurrentProcessMatchesExecutablePath()
    {
        Assert.That(new ProcessImagePathProvider().ReadByProcessId(Environment.ProcessId),
            Is.EqualTo(Environment.ProcessPath).IgnoreCase);
    }

    [Test]
    public void ReadByProcessId_NonexistentProcessFailsGracefully()
    {
        Assert.That(new ProcessImagePathProvider().ReadByProcessId(-1), Is.Empty);
    }

    [Test]
    public void Capture_NativeFallbackProvidesFriendlyNameAfterLifetimeConfirmation()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var provider = new ProcessImagePathProvider(read: (_, _) => "");
        List<ProcessCpuTimes> processes = [new(process.Id, process.StartTime.ToFileTimeUtc(), "test", 0)];
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.Empty);
        provider.Capture(processes);
        Assert.That(processes[0].ImagePath, Is.EqualTo(Environment.ProcessPath).IgnoreCase);
        var names = new ProcessAppNameProvider().Get(processes[0].ImagePath!);
        Assert.That(names.Description, Is.EqualTo(FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileDescription?.Trim()));
    }

    [Test]
    public void Capture_NativeCurrentProcessPathPreservesDirectLifetimeCheck()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        List<ProcessCpuTimes> processes = [new(process.Id, process.StartTime.ToFileTimeUtc(), "test", 0)];
        new ProcessImagePathProvider(fallback: _ => throw new AssertionException("Expected direct lookup.")).Capture(processes);
        Assert.That(processes[0].ImagePath, Is.EqualTo(Environment.ProcessPath).IgnoreCase);
    }
}
