using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessIconProviderTests
{
    [Test]
    public async Task GetAsync_CurrentProcess_LoadsFrozenIconAndReusesCache()
    {
        using var process = Process.GetCurrentProcess();
        var usage = new ProcessUsage(process.Id, process.StartTime.ToFileTimeUtc(), process.ProcessName, 1);

        var icon = await ProcessIconProvider.GetAsync(usage);

        Assert.Multiple(() =>
        {
            Assert.That(icon, Is.Not.SameAs(ProcessIconProvider.Fallback));
            Assert.That(icon.IsFrozen, Is.True);
            Assert.That(icon.PixelWidth, Is.GreaterThan(0));
        });
        Assert.That(await ProcessIconProvider.GetAsync(usage), Is.SameAs(icon));
    }

    [Test]
    public async Task GetAsync_ReusedPidOrExitedProcess_ReturnsFallback()
    {
        using var process = Process.GetCurrentProcess();
        var wrongLifetime = new ProcessUsage(process.Id, process.StartTime.ToFileTimeUtc() - 1, process.ProcessName, 1);
        var missing = new ProcessUsage(int.MaxValue, 1, "exited", 1);

        Assert.That(await ProcessIconProvider.GetAsync(wrongLifetime), Is.SameAs(ProcessIconProvider.Fallback));
        Assert.That(await ProcessIconProvider.GetAsync(missing), Is.SameAs(ProcessIconProvider.Fallback));
    }
}
