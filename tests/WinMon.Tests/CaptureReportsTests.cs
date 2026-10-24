using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
public sealed class CaptureReportsTests
{
    private static readonly DateTimeOffset Fired = new(2026, 10, 24, 14, 3, 12, TimeSpan.Zero);
    private string _folder = "";

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "WinMon.CaptureReportsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void DeleteFolder()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_folder, true);
    }

    [TestCase("en")]
    [TestCase("ja")]
    [TestCase("ar-SA")]
    public void FileName_IsTheSameInEveryCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var local = Fired.ToLocalTime();
            var time = local.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            var offset = local.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
            Assert.That(CaptureReports.FileName(Fired, CaptureMetric.SecondaryDisk, 3),
                Is.EqualTo($"WinMon-capture_{time}_o{offset}_disk2_3.txt"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void Write_ReportsInTheSameSecondGetNumberedNames()
    {
        var first = CaptureReports.Write(_folder, Fired, CaptureMetric.Cpu, Report());
        var second = CaptureReports.Write(_folder, Fired, CaptureMetric.Cpu, Report());

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(second), Is.EqualTo(CaptureReports.FileName(Fired, CaptureMetric.Cpu, 2)));
            Assert.That(CaptureReports.Find(_folder), Is.EqualTo(new[] { second, first }), "Newest first.");
        });
    }

    [Test]
    public void Write_DoesNotOverwriteAnUnrelatedFileWithTheFirstAvailableName()
    {
        var collision = Save(CaptureReports.FileName(Fired, CaptureMetric.Cpu), "Personal notes\n");

        var report = CaptureReports.Write(_folder, Fired, CaptureMetric.Cpu, Report());

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(collision), Is.EqualTo("Personal notes\n"));
            Assert.That(Path.GetFileName(report), Is.EqualTo(CaptureReports.FileName(Fired, CaptureMetric.Cpu, 2)));
            Assert.That(CaptureReports.Find(_folder), Is.EqualTo(new[] { report }),
                "A matching name alone does not make the unrelated file a report.");
        });
    }

    [Test]
    public void Prune_DeletesOnlyGenuineReportsBeyondTheLimit()
    {
        var reports = Enumerable.Range(0, 5)
            .Select(i => CaptureReports.Write(_folder, Fired.AddMinutes(i), CaptureMetric.Gpu, Report())).ToArray();
        // File times must not decide the order: make the newest report look oldest.
        File.SetLastWriteTimeUtc(reports[^1], new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var survivors = new List<string>
        {
            Save("notes.txt", Report()),
            Save(CaptureReports.FileName(Fired.AddDays(-1), CaptureMetric.Cpu), "Not a WinMon report\n"),
            Save("WinMon-capture_2026-10-24_cpu.txt", Report()),
            Save("renamed-report.txt", Report()),
            Save(CaptureReports.FileName(Fired.AddDays(-2), CaptureMetric.Cpu), Report(), FileAttributes.ReadOnly),
            Save(CaptureReports.FileName(Fired.AddDays(-3), CaptureMetric.Cpu), Report(), FileAttributes.Hidden),
            Save(CaptureReports.FileName(Fired.AddDays(-4), CaptureMetric.Cpu), Report(), FileAttributes.System)
        };
        var nested = Path.Combine(_folder, "older");
        Directory.CreateDirectory(nested);
        var deep = Path.Combine(nested, CaptureReports.FileName(Fired.AddDays(-5), CaptureMetric.Cpu));
        File.WriteAllText(deep, Report());
        survivors.Add(deep);

        CaptureReports.Prune(_folder, 2);

        Assert.Multiple(() =>
        {
            Assert.That(reports.Where(File.Exists), Is.EqualTo(reports[^2..]), "The two newest by name survive.");
            Assert.That(survivors.Where(path => !File.Exists(path)), Is.Empty);
            Assert.That(CaptureReports.Find(_folder), Has.Count.EqualTo(2), "Only genuine reports are counted.");
        });
    }

    [Test]
    public void Find_IgnoresUnicodeDigitsInCopyNumber()
    {
        var genuine = CaptureReports.Write(_folder, Fired, CaptureMetric.Cpu, Report());
        var lookalike = Save("WinMon-capture_2026-10-24_10-00-00_cpu_1٢.txt", Report());
        var invalidOffset = Save("WinMon-capture_2026-10-24_10-00-00_o+9999_cpu.txt", Report());
        var outOfRange = Save("WinMon-capture_0001-01-01_00-00-00_o+1400_cpu.txt", Report());

        Assert.That(CaptureReports.Find(_folder), Is.EqualTo(new[] { genuine }));
        CaptureReports.Prune(_folder, 0);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(lookalike), Is.True);
            Assert.That(File.Exists(invalidOffset), Is.True);
            Assert.That(File.Exists(outOfRange), Is.True);
        });
    }

    [Test]
    public void Find_OrdersRepeatedLocalHourByOffset()
    {
        var firstHour = Save("WinMon-capture_2026-11-01_01-50-00_o-0500_cpu.txt", Report());
        var secondHour = Save("WinMon-capture_2026-11-01_01-10-00_o-0600_cpu.txt", Report());

        Assert.That(CaptureReports.Find(_folder), Is.EqualTo(new[] { secondHour, firstHour }));
        CaptureReports.Prune(_folder, 1);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(secondHour), Is.True);
            Assert.That(File.Exists(firstHour), Is.False);
        });
    }

    [Test]
    public void Find_StillRecognizesOlderNamesWithoutAnOffset()
    {
        var olderTime = Fired.AddDays(-1).ToLocalTime().ToString("yyyy-MM-dd_HH-mm-ss",
            CultureInfo.InvariantCulture);
        var older = Save($"WinMon-capture_{olderTime}_cpu.txt", Report());
        var newer = CaptureReports.Write(_folder, Fired, CaptureMetric.Cpu, Report());

        Assert.That(CaptureReports.Find(_folder), Is.EqualTo(new[] { newer, older }));
    }

    [Test]
    public void Report_StartsWithTheFixedLineAndDisclaimsDiskAttribution()
    {
        var id = Guid.NewGuid();
        var history = Enumerable.Range(0, 10).Select(i => Sample(i, 40, 99)).ToArray();
        var fired = new CaptureFired(new CaptureRuleSetting(CaptureMetric.PrimaryDisk, true, 95, 3), history[^1],
            history[^2], 99, [97, 99, 98]);

        var text = CaptureReportText.Report(fired, id, history);

        Assert.Multiple(() =>
        {
            Assert.That(text.Split('\n')[0].TrimEnd('\r'), Is.EqualTo(CaptureReports.FirstLine(id)));
            Assert.That(text, Does.Contain(Strings.AutoCaptureIoDisclaimer));
            Assert.That(text, Does.Contain(Strings.AutoCaptureTotalIo));
            Assert.That(CaptureReportText.Notification(fired, history), Does.Not.Contain("busy"),
                "Disk notifications name no process.");
        });
    }

    [Test]
    public void Report_PutsTheTriggeringProcessFirst()
    {
        var history = Enumerable.Range(0, 5).Select(i => Sample(i, 40, 10)).ToArray();
        var trigger = history[^1].Processes.Single(p => p.Name == "quiet");
        var fired = new CaptureFired(new CaptureRuleSetting(CaptureMetric.Process, true, 50, 2), history[^1],
            history[^1], 80, [80, 80], trigger);

        var report = CaptureReportText.Snapshot(fired, history);

        Assert.That(report.Processes[0].Name, Is.EqualTo("quiet"));
    }

    [Test]
    public void Preferences_MissingOrInvalidRulesFallBackToDefaults()
    {
        var normalized = new AutoCapturePreferences
        {
            Rules = [new CaptureRuleSetting(CaptureMetric.Gpu, true, 400, 0)], CooldownMinutes = 0, Folder = "  "
        }.Normalized();

        Assert.Multiple(() =>
        {
            Assert.That(normalized.Rules.Select(r => r.Metric), Is.EqualTo(Enum.GetValues<CaptureMetric>()));
            Assert.That(normalized.Rules.Single(r => r.Metric == CaptureMetric.Gpu),
                Is.EqualTo(new CaptureRuleSetting(CaptureMetric.Gpu, true, 100, 1)));
            Assert.That(normalized.CooldownMinutes, Is.EqualTo(1));
            Assert.That(normalized.Folder, Is.Null);
            Assert.That(new MonitorPreferences().AutoCapture.Enabled, Is.False, "Off unless turned on.");
        });
    }

    private string Save(string name, string text, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        File.SetAttributes(path, attributes);
        return path;
    }

    private static string Report()
    {
        return CaptureReports.FirstLine(Guid.NewGuid()) + "\nAutomatic capture: CPU above 90% for 10 s\n";
    }

    private static MonitorSample Sample(int second, double cpu, double disk)
    {
        return new MonitorSample(Fired.AddSeconds(second), cpu,
        [
            new ProcessUsage(1, 1, "busy", 30, 100_000_000, 9_000_000),
            new ProcessUsage(2, 2, "quiet", 1, 50_000_000, 0)
        ], 0, false, Stopwatch.Frequency * (second + 1))
        {
            Resources = new ResourceSample { DiskName = "C:", DiskActivePercent = disk }
        };
    }
}
