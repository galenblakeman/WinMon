using NUnit.Framework;
using System.Globalization;
using WinMon.Cli;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ServicesTabTests
{
    private static readonly ServiceObservation Running = new("Alpha", "Alpha service", 4, 10, 0);
    private static readonly ServiceObservation Stopped = new("Beta", "Beta service", 1, 0, 0);

    [Test]
    public void UsesDesktopFiveSecondAveragesForCpuAndIoAndCurrentPrivateMemory()
    {
        var history = Enumerable.Range(0, 6).Select(second => Frame(second,
            second == 0 ? 1_000 : second == 5 ? 90 : 0, second == 0 ? 10_000 : second == 5 ? 500 : 0,
            30, 200)).ToArray();

        var table = ServicesTab.Build(history, 0, "", 0, false, 0);

        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Select(row => row.Key), Is.EqualTo(["host:20:20", "host:10:10"]));
            Assert.That(table.Rows[1].Cells[2], Is.EqualTo("18.0%"));
            Assert.That(table.Rows[1].Cells[4], Is.EqualTo("100 B/s"));
            Assert.That(table.Rows[1].Cells[3], Is.EqualTo(SnapshotReport.Memory(6_000_000)),
                "Private memory is from the latest frame, not the averaging window.");
            Assert.That(table.Note, Does.Contain(Strings.ServiceAverageSummary));
        });
    }

    [Test]
    public void MirrorsDesktopFilteringStatusAndNameSortRestriction()
    {
        var frame = Frame(1, 90, 0, 10, 0, includeStopped: true);

        var running = ServicesTab.Build([frame], 0, "alpha", 1, false, 0);
        var stopped = ServicesTab.Build([frame], 0, "", 2, false, 0);
        var nameAll = ServicesTab.Build([frame], 3, "", 0, false, 0);
        var nameTop = ServicesTab.Build([frame], 3, "", 0, false, 1);

        Assert.Multiple(() =>
        {
            Assert.That(running.Rows.Select(row => row.Key), Is.EqualTo(["host:10:10"]));
            Assert.That(stopped.Rows.Select(row => row.Key), Is.EqualTo(["service:Beta"]));
            Assert.That(nameAll.Rows.Select(row => row.Key), Is.EqualTo(["host:10:10", "service:Beta", "host:20:20"]));
            Assert.That(nameTop.Rows.Select(row => row.Key), Is.EqualTo(["host:10:10"]),
                "Name ranking is desktop-only when Top is All; finite Top falls back to CPU.");
        });
    }

    [Test]
    public void IncludesSharedHostWorkersAndServiceStatesInDetails()
    {
        var host = new ProcessUsage(42, 42, "svchost.exe", 5, 1_000, 100) { PrivateBytes = 2_000 };
        var worker = new ProcessUsage(43, 43, "worker.exe", 7, 2_000, 200) { PrivateBytes = 3_000 };
        var services = new[]
        {
            new ServiceObservation("One", "One service", 4, 42, 0),
            new ServiceObservation("Two", "Two service", 7, 42, 0)
        };
        var frame = new ServiceFrame(Sample(1, [host, worker]),
            [new ServiceGroup("host:42:42", services, host, [host, worker])]);

        var table = ServicesTab.Build([frame], 0, "", 0, true, 0);

        Assert.Multiple(() =>
        {
            Assert.That(table.Rows[0].Cells[0], Does.Contain("42"));
            Assert.That(table.Rows[0].Cells[2], Is.EqualTo("12.0%"));
            Assert.That(table.Rows[0].Details, Does.Contain("One"));
            Assert.That(table.Rows[0].Details, Does.Contain(Strings.ServicePaused));
            Assert.That(table.Rows[0].Details, Does.Contain(string.Format(CultureInfo.CurrentCulture,
                Strings.ServiceHostWorkers, 1)));
            Assert.That(table.Rows[0].Details, Does.Contain($"{Strings.ServiceHostOnly}: svchost.exe · " +
                $"{string.Format(CultureInfo.CurrentCulture, Strings.ServiceProcessPid, 42)} · {Strings.CPU}: 5.0%"));
            Assert.That(table.Rows[0].Details, Does.Contain($"{Strings.ServiceWorkers}: worker.exe · " +
                $"{string.Format(CultureInfo.CurrentCulture, Strings.ServiceProcessPid, 43)} · {Strings.CPU}: 7.0%"));
            Assert.That(table.Rows[0].Details, Does.Contain("\n" + Strings.ServiceWorkers + ": worker.exe"));
            Assert.That(table.Rows[0].Details, Does.Contain($"{Strings.ProcessPrivateMemoryLabel}: " +
                $"{SnapshotReport.Memory(3_000)} · {Strings.ReadWriteIO}: {SnapshotReport.Rate(200)}"));
            Assert.That(table.Note, Does.Contain(Strings.ServiceWorkersIncluded));
        });
    }

    [Test]
    public void ReportsCollectingAndUnavailableMetadataWithoutShowingStaleRows()
    {
        var unavailable = new ServiceFrame(Sample(1, []), [], false);

        Assert.Multiple(() =>
        {
            Assert.That(ServicesTab.Build([], 0, "", 0, false, 0).Note, Is.EqualTo(Strings.ServicesCollecting));
            Assert.That(ServicesTab.Build([Frame(0, 5, 0, 1, 0), unavailable], 0, "", 0, false, 0).Rows,
                Is.Empty);
            Assert.That(ServicesTab.Build([unavailable], 0, "", 0, false, 0).Note,
                Is.EqualTo(Strings.ServicesUnavailable));
        });
    }

    private static ServiceFrame Frame(int second, double alphaCpu, double alphaIo, double gammaCpu, double gammaIo,
        bool includeStopped = false)
    {
        var alpha = new ProcessUsage(10, 10, "alpha-host.exe", alphaCpu, 1_000, alphaIo)
        {
            PrivateBytes = (ulong)(1_000_000 + second * 1_000_000)
        };
        var gamma = new ProcessUsage(20, 20, "gamma-host.exe", gammaCpu, 2_000, gammaIo)
        {
            PrivateBytes = 3_000_000
        };
        var groups = new List<ServiceGroup>
        {
            new("host:10:10", [Running], alpha, [alpha]),
            new("host:20:20", [new ServiceObservation("Gamma", "Gamma service", 4, 20, 0)], gamma, [gamma])
        };
        if (includeStopped) groups.Add(new ServiceGroup("service:Beta", [Stopped], null, []));
        return new ServiceFrame(Sample(second, [alpha, gamma]), groups);
    }

    private static MonitorSample Sample(int second, IReadOnlyList<ProcessUsage> processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, processes, 0) { IntervalSeconds = 1 };
}
