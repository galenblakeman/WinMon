using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed class MetricDetailsTests
{
    [Test]
    public void Row_OnlyNotifiesWhenDisplayedTextChanges()
    {
        var row = new DetailRow { Label = "Available", Value = "—" };
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        row.Label = "Available";
        row.Value = "—";
        Assert.That(changed, Is.Empty);

        row.Value = "10.5 GiB";
        Assert.That(changed, Is.EqualTo([nameof(DetailRow.Value)]));
    }

    [Test]
    public void Model_ReusesItsFixedRowsAcrossUpdates()
    {
        var model = new MetricDetails();
        var row = new DetailRow { Label = "Cached", Value = "—" };
        model.Rows.Add(row);
        row.Value = "7.2 GiB";
        Assert.Multiple(() =>
        {
            Assert.That(model.Rows, Has.Count.EqualTo(1));
            Assert.That(model.Rows[0], Is.SameAs(row));
            Assert.That(model.Rows[0].Value, Is.EqualTo("7.2 GiB"));
        });
    }

    [Test]
    public void BusiestLive_UsesFullCurrentProcessesEvenWhenHistoryWasCompacted()
    {
        var now = DateTimeOffset.UnixEpoch;
        var small = new ProcessUsage(1, 1, "small", 2, 100) { GpuPercent = 1 };
        var large = new ProcessUsage(2, 2, "large", 30, 5_000) { GpuPercent = 20 };
        var compacted = new MonitorSample(now, 40, [small], 0);
        var full = compacted with { Processes = [small, large] };
        foreach (var sort in new[] { ProcessSort.Cpu, ProcessSort.Memory, ProcessSort.Gpu })
            Assert.That(MainWindow.BusiestLive([compacted], full, sort)?.Name, Is.EqualTo("large"),
                $"The {sort} tooltip must use LatestFull, independent of the table view.");
        Assert.That(MainWindow.BusiestLive([compacted], null, ProcessSort.Cpu), Is.Null);
    }

    [Test]
    public void MachineCounts_UseSystemReadingsAndShowDashWhenUnavailable()
    {
        var system = new ResourceSample { ProcessCount = 1_247, ThreadCount = 18_903, HandleCount = 612_880 };
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 10,
            [new ProcessUsage(1, 1, "one readable process", 1)], 8) { Resources = system };
        Assert.That(sample.Processes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(MainWindow.ProcessThreadCounts(sample.Resources),
                Is.EqualTo(string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} · {1:N0}", 1247, 18903)));
            Assert.That(MainWindow.HandleCountText(sample.Resources),
                Is.EqualTo(string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0}", 612880)));
            Assert.That(MainWindow.ProcessThreadCounts(system with { ThreadCount = null }),
                Is.EqualTo(WinMon.Resources.Strings.MissingReading));
            Assert.That(MainWindow.HandleCountText(null), Is.EqualTo(WinMon.Resources.Strings.MissingReading));
        });
    }
}
