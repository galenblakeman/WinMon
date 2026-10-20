using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class HardwareInfoTextTests
{
    [Test]
    public void Format_IncludesProductSnapshotAndAllHardwareDetails()
    {
        var capturedAt = new DateTimeOffset(2026, 10, 19, 22, 30, 0, TimeSpan.FromHours(-5));
        WindowsHardwareInfo.Section[] sections = [
            new("CPU", "Model · 8 cores", [new WindowsHardwareInfo.Detail("Socket 0", "L3 cache: 32 MiB")], "CPU details"),
            new("Installed memory", "32 GiB", [new WindowsHardwareInfo.Detail("DIMM A2", "Part number: RAM-16")], "Module details"),
            new("Physical drives", "Disk 0", [new WindowsHardwareInfo.Detail("Example SSD", "Volumes: C:, D:")], "Drive details"),
            new("Network adapters", "Ethernet · 2.5 Gbps")];
        var text = HardwareInfoText.Format(sections, capturedAt);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith($"WinMon {AppInfo.Version}"));
            Assert.That(text, Does.Contain(AppInfo.Description).And.Contain(AppInfo.RepositoryUrl).And.Contain(AppInfo.Copyright));
            Assert.That(text, Does.Contain("Snapshot: 2026-10-19 22:30:00 -05:00"));
            Assert.That(text, Does.Contain("CPU details").And.Contain("L3 cache: 32 MiB").And.Contain("Model · 8 cores"));
            Assert.That(text, Does.Contain("Module details").And.Contain("RAM-16"));
            Assert.That(text, Does.Contain("Drive details").And.Contain("Volumes: C:, D:"));
            Assert.That(text, Does.Contain("Ethernet · 2.5 Gbps"));
        });
    }

    [Test]
    public void Format_PreservesUnavailableSectionsWithoutInventingDetails()
    {
        var text = HardwareInfoText.Format([new WindowsHardwareInfo.Section("CPU", "Additional specifications unavailable.")], DateTimeOffset.UnixEpoch);
        Assert.That(text, Does.Contain("Additional specifications unavailable."));
        Assert.That(text, Does.Not.Contain("CPU details"));
    }
}
