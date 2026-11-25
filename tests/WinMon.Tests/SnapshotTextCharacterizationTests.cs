using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class SnapshotTextCharacterizationTests
{
    private static readonly ProcessUsage Process = new(42, 1, "executable", 12.5, 1UL << 20, 1_234_567)
    {
        GpuPercent = 7.5, PrivateBytes = 2UL << 20, ThreadCount = 12, HandleCount = 34, ImagePath = "C:\\synthetic.exe"
    };

    private static SnapshotReport Report(params ProcessUsage[] processes) => new(
        new MonitorSample(DateTimeOffset.UnixEpoch, 25, [], 0), processes, false, "CPU", "", false, "", null);

    [TestCase(null, null, "executable")]
    [TestCase("", "", "executable")]
    [TestCase("  ", "Product", "Product")]
    [TestCase("App", "Product", "App")]
    [TestCase("", " ", "executable")]
    [TestCase(" App ", null, " App ")]
    public void DisplayNamesPreserveAppProductExecutablePrecedenceAndWhitespace(string? app, string? product, string expected)
    {
        var row = Rows(Report(Process with { AppName = app!, ProductName = product! })).Single();
        Assert.That(row, Is.EqualTo(new[] { expected, "42", "12.5", "7.5", "1.0", "1.23", "12", "34", "C:\\synthetic.exe" }));
    }

    [TestCase(false, 0, false, "1.0", "42")]
    [TestCase(false, 1, true, "1.0", "42, 7")]
    [TestCase(false, 2, true, "1.0", "42, 7")]
    [TestCase(false, 3, true, "1.0", "42, 7")]
    [TestCase(true, 1, false, "2.0", "42")]
    [TestCase(true, 2, false, "2.0", "42")]
    [TestCase(true, 3, true, "2.0", "42, 7")]
    public void GroupMemoryMemberNamesAndMemberIdsKeepTheirIndependentRules(bool grouped, int members, bool ids, string memory, string pids)
    {
        var report = Report(Process with { MemberCount = members, MemberIds = ids ? [42, 7] : [] }) with { Grouped = grouped };
        var text = SnapshotText.Format(report);
        var row = Rows(text).Single();
        Assert.Multiple(() =>
        {
            Assert.That(row[0], Is.EqualTo(members > 1 ? $"executable ×{members}" : "executable"));
            Assert.That(row[1], Is.EqualTo(pids));
            Assert.That(row[4], Is.EqualTo(memory));
            Assert.That(Header(text)[4], Is.EqualTo((grouped ? Strings.Private : Strings.Memory) + " (MiB)"));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void StateListsPreserveShortListFallbackMaskingAndIgnoredExtraEntries(int kind)
    {
        var processes = new[] { Process with { MemberCount = 3, MemberIds = [42, 7] }, Process with { ProcessId = 99, Name = "second" } };
        IReadOnlyList<string>? states = kind switch { 0 => null, 1 => [], 2 => ["gone"], 3 => ["gone", "", "ignored"], _ => ["gone", ""] };
        var rows = Rows(Report(processes) with { ProcessStates = states, Grouped = true });
        Assert.That(rows, Has.Length.EqualTo(2));
        if (kind >= 2)
        {
            Assert.That(rows[0], Is.EqualTo(new[] { "executable ×3 · gone", "42", Strings.MissingReading, Strings.MissingReading,
                Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, "C:\\synthetic.exe" }));
        }
        else Assert.That(rows[0], Is.EqualTo(new[] { "executable ×3", "42, 7", "12.5", "7.5", "2.0", "1.23", "12", "34", "C:\\synthetic.exe" }));
        Assert.That(rows[1], Is.EqualTo(new[] { "second", "99", "12.5", "7.5", "2.0", "1.23", "12", "34", "C:\\synthetic.exe" }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingMetricsAndPathsStayDistinctFromMeasuredZero(bool grouped)
    {
        var missing = Process with { WorkingSetBytes = null, PrivateBytes = null, IoBytesPerSecond = null,
            GpuPercent = null, ThreadCount = null, HandleCount = null, ImagePath = "" };
        var zero = Process with { CpuPercent = 0, GpuPercent = 0, WorkingSetBytes = 0, PrivateBytes = 0,
            IoBytesPerSecond = 0, ThreadCount = 0, HandleCount = 0 };
        var rows = Rows(Report(missing, zero) with { Grouped = grouped });
        Assert.That(rows[0], Is.EqualTo(new[] { "executable", "42", "12.5", Strings.MissingReading, Strings.MissingReading,
            Strings.MissingReading, Strings.MissingReading, Strings.MissingReading, Strings.ExecutablePathUnavailable }));
        Assert.That(rows[1], Is.EqualTo(new[] { "executable", "42", "0.0", "0.0", "0.0", "0.00", "0", "0", "C:\\synthetic.exe" }));
        var state = Rows(Report(missing) with { ProcessStates = ["not recorded"] }).Single();
        Assert.That(state[0], Is.EqualTo("executable · not recorded"));
        Assert.That(state[8], Is.EqualTo(Strings.ExecutablePathUnavailable));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContextNoticesKeepTheirMetadataOrderAndUseThePublishedRowCount(bool flags)
    {
        var report = Report(Process) with { Grouped = flags, AppsOnly = flags, Filter = flags ? "needle\tline\r\nnext" : "",
            Coverage = flags ? "coverage line\ncontinuation" : "", TriggerIncludedOutsideTop = flags, TopCount = 1 };
        var text = SnapshotText.Format(report);
        var lines = text.Split(Environment.NewLine);
        Assert.That(text, Does.Contain(Strings.Format(Strings.CaptureReportProcessCount, "1", 1)));
        Assert.That(text.Contains(Strings.CaptureReportExtraTrigger, StringComparison.Ordinal), Is.EqualTo(flags));
        Assert.That(text.Contains(Strings.IdleGroupMembersMayBeOmitted, StringComparison.Ordinal), Is.EqualTo(flags));
        if (flags)
        {
            var trigger = Array.IndexOf(lines, Strings.CaptureReportExtraTrigger);
            Assert.That(lines[trigger + 1], Is.EqualTo(Strings.IdleGroupMembersMayBeOmitted));
            Assert.That(lines[trigger + 2], Is.EqualTo(Strings.SearchNameOrPID + "\tneedle line  next"));
            Assert.That(lines[trigger + 3], Is.EqualTo(Strings.ProcessKindApp));
            Assert.That(text.Contains("coverage line\ncontinuation", StringComparison.Ordinal), Is.True);
        }
    }

    [TestCase(-1, "-1")]
    [TestCase(0, null)]
    [TestCase(1, "1")]
    [TestCase(3, "3")]
    public void TopCountIsDescriptiveAndNeverRelimitsOrReranksThePublishedRows(int top, string? label)
    {
        var report = Report(Process with { Name = "first", CpuPercent = 1 }, Process with { Name = "second", CpuPercent = 99 })
            with { TopCount = top };
        var text = SnapshotText.Format(report);
        Assert.That(text, Does.Contain(Strings.Format(Strings.CaptureReportProcessCount, label ?? Strings.AllEntries, 2)));
        Assert.That(Rows(text).Select(row => row[0]), Is.EqualTo(new[] { "first", "second" }));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RemaindersKeepTheirOwnCountAndMissingMetricColumns(bool grouped, bool io)
    {
        var row = Rows(Report(Process) with { Grouped = grouped, Remainder = new SnapshotRemainder(4, 3.3, io ? 1_500_000 : null) }).Last();
        Assert.That(row, Is.EqualTo(new[] { Strings.Format(grouped ? Strings.OtherExecutables : Strings.OtherProcesses, 4), "", "3.3",
            Strings.MissingReading, Strings.MissingReading, io ? "1.50" : Strings.MissingReading,
            Strings.MissingReading, Strings.MissingReading, Strings.MissingReading }));
    }

    [Test]
    public void EmptyReportsKeepTheTableHeaderWithoutInventingAProcessRow()
    {
        var report = Report();
        var text = SnapshotText.Format(report);
        Assert.That(Header(text), Has.Length.EqualTo(9));
        Assert.That(Rows(text), Is.Empty);
        Assert.That(text.EndsWith(Environment.NewLine, StringComparison.Ordinal), Is.False);
    }

    [TestCase(null, null, "—")]
    [TestCase(null, 4294967296UL, "—")]
    [TestCase(2147483648UL, null, "—")]
    [TestCase(0UL, 0UL, "0.0 / 0 GiB (—)")]
    [TestCase(2147483648UL, 4294967296UL, "2.0 / 4 GiB (50.0%)")]
    public void MemorySummaryPreservesIncompleteAndZeroCapacityReadings(ulong? used, ulong? total, string expected)
    {
        var report = Report();
        report = report with { Sample = report.Sample with { Resources = new ResourceSample { UsedMemoryBytes = used, TotalMemoryBytes = total } } };
        Assert.That(SnapshotText.Format(report).Split(Environment.NewLine).Single(line => line.StartsWith(Strings.Memory + "\t", StringComparison.Ordinal)),
            Is.EqualTo(Strings.Memory + "\t" + expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiskAndNetworkSummariesPreserveUnitsFallbackLabelsAndOptionalSecondDisk(bool secondary)
    {
        var report = Report();
        report = report with { Sample = report.Sample with { Resources = new ResourceSample
        {
            DiskName = "", DiskActivePercent = 25, DiskBytesPerSecond = 1_500_000,
            SecondaryDiskName = secondary ? "D:" : "", SecondaryDiskActivePercent = 0, SecondaryDiskBytesPerSecond = null,
            NetworkName = "Adapter\t\r\nA", ReceiveBytesPerSecond = 125_000, SendBytesPerSecond = 0
        } } };
        var text = SnapshotText.Format(report);
        Assert.That(text, Does.Contain(Strings.Disk + "\t25.0% · " + Strings.Format(Strings.MBS, 1.5)));
        Assert.That(text.Contains(Strings.Format(Strings.DiskLabel, "D:") + "\t0.0% · " + Strings.MissingReading, StringComparison.Ordinal), Is.EqualTo(secondary));
        Assert.That(text, Does.Contain(Strings.Network + "\tAdapter   A · ↓ 125 KB/s · ↑ 0 KB/s"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CleaningAndFinalTrimmingPreserveInteriorCellsAndOnlyTrimTheWholeReportEnd(bool remainder)
    {
        var process = Process with { Name = "=name\tline\r\nnext", ImagePath = "C:\\path\tline  " };
        var report = Report(process, process) with { Remainder = remainder ? new SnapshotRemainder(1, 0, null) : null };
        var text = SnapshotText.Format(report);
        var rows = Rows(text);
        Assert.That(rows[0][0], Is.EqualTo("=name line  next"));
        Assert.That(rows[0][8], Is.EqualTo("C:\\path line  "));
        Assert.That(rows[1][8], Is.EqualTo(remainder ? "C:\\path line  " : "C:\\path line"));
        Assert.That(rows.All(row => row.Length == 9), Is.True);
        Assert.That(text.EndsWith(Environment.NewLine, StringComparison.Ordinal), Is.False);
    }

    [Test, SetCulture("de-DE")]
    public void ProcessNumbersFollowTheCurrentCultureWithUnchangedColumnOrder()
    {
        Assert.That(Rows(Report(Process)).Single(), Is.EqualTo(new[] { "executable", "42", "12,5", "7,5", "1,0", "1,23", "12", "34", "C:\\synthetic.exe" }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void MalformedNullInputsRetainTheirExistingNullReferenceException(int kind)
    {
        SnapshotReport report = kind switch
        {
            0 => null!, 1 => Report() with { Sample = null! }, 2 => Report() with { Processes = null! },
            3 => Report(Process) with { ProcessStates = [null!] },
            _ => Report() with { Sample = Report().Sample with { Resources = null! } }
        };
        Assert.Throws<NullReferenceException>(() => SnapshotText.Format(report));
    }

    [Test]
    public void CpuAvailabilityAloneDoesNotMaskTheExistingRawCpuValue()
    {
        var report = Report(Process with { CpuAvailable = false, CpuPercent = 77.7 });
        Assert.That(Rows(report).Single()[2], Is.EqualTo("77.7"));
        Assert.That(Rows(report with { ProcessStates = ["not observed"] }).Single()[2], Is.EqualTo(Strings.MissingReading));
    }

    [Test]
    public void RepeatedFormattingDoesNotMutateInputsOrRetainAnotherReportsGrouping()
    {
        var rows = new[] { Process };
        var report = Report(rows);
        var text = SnapshotText.Format(report);
        _ = SnapshotText.Format(report with { Grouped = true, Remainder = new SnapshotRemainder(5, 10, 0) });
        Assert.That(SnapshotText.Format(report), Is.EqualTo(text));
        Assert.That(report.Processes, Is.SameAs(rows));
        Assert.That(rows, Is.EqualTo(new[] { Process }));
        Assert.That(report.Remainder, Is.Null);
    }

    private static string[][] Rows(SnapshotReport report) => Rows(SnapshotText.Format(report));
    private static string[][] Rows(string text)
    {
        var lines = text.Split(Environment.NewLine);
        var header = Array.FindIndex(lines, line => line.StartsWith(Strings.ProcessColumn + "\tPID\t", StringComparison.Ordinal));
        Assert.That(header, Is.GreaterThanOrEqualTo(0));
        return lines.Skip(header + 1).Select(line => line.Split('\t')).ToArray();
    }
    private static string[] Header(string text) => text.Split(Environment.NewLine)
        .Single(line => line.StartsWith(Strings.ProcessColumn + "\tPID\t", StringComparison.Ordinal)).Split('\t');
}
