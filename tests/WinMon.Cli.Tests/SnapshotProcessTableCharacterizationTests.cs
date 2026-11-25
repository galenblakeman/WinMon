using System.Globalization;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Rendering;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class SnapshotProcessTableCharacterizationTests
{
    private static readonly SnapshotProcess Complete = new(42, "worker", 12.5, 1024, 9999, 1500, 7.5, 1234, 5678);

    [TestCase("cpu")]
    [TestCase("memory")]
    [TestCase("io")]
    [TestCase("unexpected")]
    [TestCase("GPU")]
    public void BaseAndUnknownSortsKeepExactlyFiveColumnsAndTheSuppliedRowOrder(string sort)
    {
        var report = Report(sort, Complete, Complete with { Pid = 7, Name = "second", CpuPercent = 99 });

        var table = report.BuildProcessTable();

        Assert.Multiple(() =>
        {
            Assert.That(table.Columns.Select(column => Render(column.Header)),
                Is.EqualTo(new[] { "PID", Strings.ProcessColumn, "CPU %", Strings.ProcessWorkingSetLabel, Strings.IO }));
            Assert.That(Cells(table, 0), Is.EqualTo(new[] { "42", "worker", "12.5%", "1.0 KiB", "1.5 KB/s" }));
            Assert.That(Cells(table, 1), Is.EqualTo(new[] { "7", "second", "99.0%", "1.0 KiB", "1.5 KB/s" }));
            Assert.That(report.Processes[0], Is.SameAs(Complete));
        });
    }

    [TestCase("gpu", false)]
    [TestCase("gpu", true)]
    [TestCase("threads", false)]
    [TestCase("threads", true)]
    [TestCase("handles", false)]
    [TestCase("handles", true)]
    public void OptionalColumnsUseTheirOwnMetricAndPreserveUnavailableValues(string sort, bool missing)
    {
        var process = missing ? Complete with { GpuPercent = null, Threads = null, Handles = null } : Complete;

        var table = Report(sort, process).BuildProcessTable();

        var header = sort == "gpu" ? Strings.GPU + " %" : sort == "threads" ? Strings.Threads : Strings.Handles;
        var value = missing ? Strings.Unavailable : sort == "gpu" ? "7.5%" : sort == "threads" ? "1234" : "5678";
        Assert.Multiple(() =>
        {
            Assert.That(table.Columns, Has.Count.EqualTo(6));
            Assert.That(Render(table.Columns[5].Header), Is.EqualTo(header));
            Assert.That(Cells(table, 0), Is.EqualTo(new[] { "42", "worker", "12.5%", "1.0 KiB", "1.5 KB/s", value }));
        });
    }

    [TestCase("cpu", 5)]
    [TestCase("gpu", 6)]
    [TestCase("threads", 6)]
    [TestCase("handles", 6)]
    public void EmptyReportsRetainTheColumnSchemaWithoutInventingRows(string sort, int columns)
    {
        var table = Report(sort).BuildProcessTable();
        Assert.That(table.Columns, Has.Count.EqualTo(columns));
        Assert.That(table.Rows, Is.Empty);
        Assert.That(Render(table), Does.Contain("PID").And.Not.Contain("worker"));
    }

    [Test]
    public void MissingBaseMetricsStayUnavailableWhileZerosRemainMeasurements()
    {
        var missing = Complete with { CpuPercent = null, WorkingSetBytes = null, IoBytesPerSecond = null };
        var zero = Complete with { CpuPercent = 0, WorkingSetBytes = 0, IoBytesPerSecond = 0, Threads = 0 };
        var table = Report("threads", missing, zero).BuildProcessTable();
        Assert.That(Cells(table, 0), Is.EqualTo(new[]
            { "42", "worker", Strings.Unavailable, Strings.Unavailable, Strings.Unavailable, "1234" }));
        Assert.That(Cells(table, 1), Is.EqualTo(new[] { "42", "worker", "0.0%", "0 B", "0 B/s", "0" }));
    }

    [Test]
    public void ProcessNamesRemoveTerminalControlsAndFormatCharactersButKeepMarkupLiteral()
    {
        var process = Complete with { Name = "na\u001bme\n\u202E[red]x[/]" };
        var report = Report("cpu", process);
        var table = report.BuildProcessTable();
        Assert.That(Render(table.Rows.Single()[1]), Is.EqualTo("name[red]x[/]"));
        var rendered = Render(table);
        Assert.That(rendered, Does.Contain("name[red]x[/]"));
        Assert.That(rendered.Any(character => character is '\u001b' or '\u202E'), Is.False);
        Assert.That(report.Processes[0].Name, Is.EqualTo(process.Name));
    }

    [Test]
    public void CurrentNumberCultureFormatsMetricsAndPidRetainsInvariantDigits()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var table = Report("gpu", Complete).BuildProcessTable();
        Assert.That(Cells(table, 0), Is.EqualTo(new[] { "42", "worker", "12,5%", "1,0 KiB", "1,5 KB/s", "7,5%" }));
    }

    [Test]
    public void RepeatedBuildsProduceIndependentTablesWithoutChangingTheInputList()
    {
        var rows = new[] { Complete };
        var report = Report("gpu", rows);
        var first = report.BuildProcessTable();
        first.AddRow("0", "changed", "0", "0", "0", "0");
        var second = report.BuildProcessTable();
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Rows, Has.Count.EqualTo(1));
        Assert.That(report.Processes, Is.SameAs(rows));
        Assert.That(rows, Is.EqualTo(new[] { Complete }));
        Assert.That(Cells(second, 0), Is.EqualTo(new[] { "42", "worker", "12.5%", "1.0 KiB", "1.5 KB/s", "7.5%" }));
    }

    private static SnapshotReport Report(string sort, params SnapshotProcess[] processes) =>
        SnapshotReport.Create(new CapturedFrame(new MonitorSample(DateTimeOffset.UnixEpoch, 0, [], 0)),
            "fixed", 0, ProcessSort.Cpu, "") with { Sort = sort, Processes = processes };

    private static string[] Cells(Table table, int row)
    {
        var cells = table.Rows.ElementAt(row);
        return Enumerable.Range(0, cells.Count).Select(column => Render(cells[column])).ToArray();
    }

    private static string Render(IRenderable content)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.No
        });
        console.Write(content);
        return output.ToString().TrimEnd('\r', '\n');
    }
}
