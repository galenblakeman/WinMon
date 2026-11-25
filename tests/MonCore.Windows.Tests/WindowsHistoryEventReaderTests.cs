using NUnit.Framework;
using WinMon.Monitoring;
using System.Xml.Linq;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsHistoryEventReaderTests
{
    [Test]
    public void BuildQuery_UsesStructuredPerProviderSelectorsAndBroadSystem129()
    {
        var query = WindowsHistoryEventReader.BuildQuery("System", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        Assert.DoesNotThrow(() => XDocument.Parse(query));
        Assert.That(query, Does.StartWith("<QueryList><Query"));
        Assert.That(query, Does.Contain("Provider[@Name='disk']"));
        Assert.That(query, Does.Contain("Provider[@Name='Microsoft-Windows-WindowsUpdateClient']"));
        Assert.That(query, Does.Contain("EventID=129 and TimeCreated"));
        Assert.That(query, Does.Not.Contain("storport"));
    }

    [Test]
    public void BuildQuery_BoundsBothEndsOfTheRequestedRange()
    {
        var query = WindowsHistoryEventReader.BuildQuery("Application", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1));
        Assert.That(query, Does.Contain("1970-01-01T00:00:00.0000000Z"));
        Assert.That(query, Does.Contain("1970-01-01T00:01:00.0000000Z"));
        Assert.That(query.Split("<Select ").Length - 1, Is.EqualTo(3));
    }

    [Test]
    public void TryParseXml_UsesExactApplicationErrorLifetimeAndTruncatesTheFirstMessageLine()
    {
        const string xml = """
            <Event><System><Provider Name="Application Error"/><EventID>1000</EventID><Level>1</Level><TimeCreated SystemTime="2026-11-20T00:00:00.0000000Z"/></System><EventData><Data Name="ProcessId">42</Data><Data Name="ProcessCreationTime">0x01DC5A5B5C6D7E8F</Data></EventData></Event>
            """;
        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "first line\r\nsecond", out var item), Is.True);
        Assert.That(item.Level, Is.EqualTo(IncidentEventLevel.Error));
        Assert.That(item.ProcessId, Is.EqualTo(42));
        Assert.That(item.CreationTicks, Is.EqualTo(0x01DC5A5B5C6D7E8F));
        Assert.That(item.Message, Is.EqualTo("first line"));
    }

    [Test]
    public void TryParseXml_ReadsOnlyPathFreeStructuredApplicationErrorFields()
    {
        // Sanitized from a local Application Error 1000 event captured on 2026-11-25.
        const string xml = """
            <Event><System><Provider Name="Application Error"/><EventID>1000</EventID><Level>2</Level><TimeCreated SystemTime="2026-11-10T20:03:26.3281424Z"/></System><EventData><Data Name="AppName">WinMon.Performance.exe</Data><Data Name="AppVersion">4.0.6.0</Data><Data Name="ModuleName">KERNELBASE.dll</Data><Data Name="ModuleVersion">10.0.26100.9818</Data><Data Name="ExceptionCode">e0434352</Data><Data Name="FaultingOffset">00000000000c569a</Data><Data Name="AppPath">D:\\redacted\\WinMon.Performance.exe</Data><Data Name="ModulePath">C:\\WINDOWS\\System32\\KERNELBASE.dll</Data></EventData></Event>
            """;

        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "", out var item), Is.True);
        Assert.That(item.CrashSignature, Is.EqualTo(new CrashSignature(CrashKind.Crash, "WinMon.Performance.exe",
            "4.0.6.0", "KERNELBASE.dll", "10.0.26100.9818", 0xe0434352, 0xc569a)));
    }

    [TestCase("C:\\private\\app.exe", "1.0.0.0", "c0000005", "1")]
    [TestCase("app.exe", "malformed", "c0000005", "1")]
    [TestCase("app.exe", "1.0.0.0", "not-hex", "1")]
    [TestCase("app.exe", "1.0.0.0", "c0000005", "10000000000000000")]
    [TestCase("app.exe", "1.0.0.0", "100000000", "1")]
    [TestCase("app&#x9;.exe", "1.0.0.0", "c0000005", "1")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.0.0.0", "c0000005", "1")]
    public void TryParseXml_RejectsMalformedOrSensitiveCrashSignatureFields(string application, string version,
        string exceptionCode, string offset)
    {
        var xml = $"<Event><System><Provider Name=\"Application Error\"/><EventID>1000</EventID><TimeCreated SystemTime=\"2026-11-10T20:03:26Z\"/></System><EventData><Data Name=\"AppName\">{application}</Data><Data Name=\"AppVersion\">{version}</Data><Data Name=\"ModuleName\">module.dll</Data><Data Name=\"ModuleVersion\">1.0.0.0</Data><Data Name=\"ExceptionCode\">{exceptionCode}</Data><Data Name=\"FaultingOffset\">{offset}</Data></EventData></Event>";

        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "", out var item), Is.True);
        Assert.That(item.CrashSignature, Is.Null);
    }

    [Test]
    public void TryParseXml_LeavesMissingOrDuplicateNamedCrashFieldsUnsigned()
    {
        const string missing = """
            <Event><System><Provider Name="Application Error"/><EventID>1000</EventID><TimeCreated SystemTime="2026-11-10T20:03:26Z"/></System><EventData><Data Name="AppName">app.exe</Data><Data Name="AppVersion">1.0.0.0</Data><Data Name="ModuleName">module.dll</Data><Data Name="ModuleVersion">1.0.0.0</Data><Data Name="ExceptionCode">c0000005</Data></EventData></Event>
            """;
        const string duplicate = """
            <Event><System><Provider Name="Application Error"/><EventID>1000</EventID><TimeCreated SystemTime="2026-11-10T20:03:26Z"/></System><EventData><Data Name="AppName">app.exe</Data><Data Name="AppName">other.exe</Data><Data Name="AppVersion">1.0.0.0</Data><Data Name="ModuleName">module.dll</Data><Data Name="ModuleVersion">1.0.0.0</Data><Data Name="ExceptionCode">c0000005</Data><Data Name="FaultingOffset">1</Data></EventData></Event>
            """;

        Assert.That(WindowsHistoryEventReader.TryParseXml(missing, "", out var missingItem), Is.True);
        Assert.That(WindowsHistoryEventReader.TryParseXml(duplicate, "", out var duplicateItem), Is.True);
        Assert.That(missingItem.CrashSignature, Is.Null);
        Assert.That(duplicateItem.CrashSignature, Is.Null);
    }

    [Test]
    public void TryParseXml_ReadsTheFixtureCoveredUnnamedHangLayout()
    {
        // Sanitized from the ten-Data Application Hang 1002 XML layout in Microsoft Q&A 4210888.
        const string xml = """
            <Event><System><Provider Name="Application Hang"/><EventID>1002</EventID><TimeCreated SystemTime="2026-11-10T20:03:26Z"/></System><EventData><Data>Lightroom.exe</Data><Data>10.1.0.10</Data><Data>12c</Data><Data>01d6d2987e67eaa7</Data><Data>25</Data><Data>C:\\redacted\\Lightroom.exe</Data><Data>00000000-0000-0000-0000-000000000000</Data><Data></Data><Data></Data><Data>Unknown</Data></EventData></Event>
            """;

        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "", out var item), Is.True);
        Assert.That(item.CrashSignature, Is.EqualTo(new CrashSignature(CrashKind.Hang, "Lightroom.exe", "10.1.0.10")));
    }

    [Test]
    public void TryParseXml_ReadsNamedApplicationHangFields()
    {
        const string xml = """
            <Event><System><Provider Name="Application Hang"/><EventID>1002</EventID><TimeCreated SystemTime="2026-11-10T20:03:26Z"/></System><EventData><Data Name="AppName">SystemSettings.exe</Data><Data Name="AppVersion">10.0.26100.3037</Data><Data Name="ExeFileName">C:\\redacted\\SystemSettings.exe</Data></EventData></Event>
            """;

        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "", out var item), Is.True);
        Assert.That(item.CrashSignature, Is.EqualTo(new CrashSignature(CrashKind.Hang, "SystemSettings.exe", "10.0.26100.3037")));
    }

    [Test]
    public void TryParseXml_LeavesAnUnrecognizedUnnamedHangLayoutUnsigned()
    {
        const string xml = """
            <Event><System><Provider Name="Application Hang"/><EventID>1002</EventID><TimeCreated SystemTime="2026-11-10T20:03:26Z"/></System><EventData><Data>app.exe</Data><Data>1.0.0.0</Data></EventData></Event>
            """;

        Assert.That(WindowsHistoryEventReader.TryParseXml(xml, "", out var item), Is.True);
        Assert.That(item.CrashSignature, Is.Null);
    }

    [Test]
    public void BuildCrashQuery_SelectsOnlyApplicationErrorsAndHangsWithinTheRequestedRange()
    {
        var query = WindowsHistoryEventReader.BuildCrashQuery(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.DoesNotThrow(() => XDocument.Parse(query));
        Assert.That(query, Does.Contain("Application Error").And.Contain("Application Hang"));
        Assert.That(query, Does.Contain("1970-01-01T00:00:00.0000000Z").And.Contain("1970-01-02T00:00:00.0000000Z"));
        Assert.That(query, Does.Not.Contain(".NET Runtime"));
        var selector = XDocument.Parse(query).Descendants("Select").Single().Value;
        Assert.That(selector, Does.Contain("System[((Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Application Hang'] and EventID=1002)) and TimeCreated"));
    }

    [Test]
    public void ReadCrashesCore_PreservesOldestTimestampAtEventCap()
    {
        var oldestReads = 0;
        var result = WindowsHistoryEventReader.ReadCrashesCore(Read, ReadOldest, CancellationToken.None);

        Assert.That(result.Events, Has.Count.EqualTo(WindowsHistoryEventReader.MaximumCrashEvents));
        Assert.That(result.LimitReached, Is.True);
        Assert.That(result.ApplicationCoverage.Status, Is.EqualTo(IncidentEventCoverageStatus.Unknown));
        Assert.That(result.OldestRetainedApplicationEvent, Is.EqualTo(DateTimeOffset.UnixEpoch));
        Assert.That(oldestReads, Is.EqualTo(1));

        IEnumerable<IncidentEvent> Read(CancellationToken token)
        {
            for (var i = 0; i < WindowsHistoryEventReader.MaximumCrashEvents + 1; i++)
                yield return new IncidentEvent(DateTimeOffset.UnixEpoch, "Application Error", 1000,
                    IncidentEventLevel.Error, "");
        }
        DateTimeOffset? ReadOldest(CancellationToken token)
        {
            oldestReads++;
            return DateTimeOffset.UnixEpoch;
        }
    }

    [TestCase("timeout", IncidentEventCoverageStatus.TimedOut)]
    [TestCase("unreadable", IncidentEventCoverageStatus.Unreadable)]
    public void ReadCrashesCore_ReportsFailuresAndDoesNotReadOldest(string failure, IncidentEventCoverageStatus expected)
    {
        var oldestReads = 0;
        var result = WindowsHistoryEventReader.ReadCrashesCore(Read, ReadOldest, CancellationToken.None);

        Assert.That(result.ApplicationCoverage.Status, Is.EqualTo(expected));
        Assert.That(result.Events, Is.Empty);
        Assert.That(result.OldestRetainedApplicationEvent, Is.Null);
        Assert.That(oldestReads, Is.Zero);

        IEnumerable<IncidentEvent> Read(CancellationToken token) => failure == "timeout"
            ? throw new TimeoutException() : throw new System.ComponentModel.Win32Exception(5);
        DateTimeOffset? ReadOldest(CancellationToken token) { oldestReads++; return DateTimeOffset.UnixEpoch; }
    }

    [Test, Category("Integration")]
    public void ReadCrashes_ReportsApplicationCoverageForEmptyAndRetainedRanges()
    {
        var reader = new WindowsHistoryEventReader();
        var empty = reader.ReadCrashes(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), CancellationToken.None);
        var retained = reader.ReadCrashes(DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.That(empty.Events, Is.Empty);
        Assert.That(empty.ApplicationCoverage.Status, Is.EqualTo(IncidentEventCoverageStatus.Complete));
        Assert.That(retained.ApplicationCoverage.Status, Is.EqualTo(IncidentEventCoverageStatus.Complete));
        Assert.That(retained.Events, Has.Count.LessThanOrEqualTo(WindowsHistoryEventReader.MaximumCrashEvents));
        Assert.That(retained.OldestRetainedApplicationEvent, Is.Null.Or.LessThanOrEqualTo(DateTimeOffset.UtcNow));
    }

    [Test]
    public void ReadCore_GlobalCapLeavesTheUnqueriedLogUnknown()
    {
        var enumerated = 0;
        var disposed = false;
        var result = WindowsHistoryEventReader.ReadCore(Read, CancellationToken.None);
        Assert.That(result.Events, Has.Count.EqualTo(WindowsHistoryEventReader.MaximumEvents));
        Assert.That(result.LimitReached, Is.True);
        Assert.That(result.Coverage, Is.EqualTo([new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Unknown),
            new IncidentEventLogCoverage("System", IncidentEventCoverageStatus.Unknown)]));
        Assert.That(enumerated, Is.EqualTo(500));
        Assert.That(disposed, Is.True);

        IEnumerable<IncidentEvent> Read(string log, CancellationToken token)
        {
            Assert.That(log, Is.EqualTo("Application"));
            try
            {
                for (var i = 0; i < 501; i++)
                {
                    enumerated++;
                    yield return new IncidentEvent(DateTimeOffset.UnixEpoch, "Application Error", 1000, IncidentEventLevel.Error, "");
                }
            }
            finally { disposed = true; }
        }
    }

    [Test]
    public void ReadCore_SharesOneBudgetAcrossBothLogs()
    {
        var systemReads = 0;
        var result = WindowsHistoryEventReader.ReadCore(Read, CancellationToken.None);
        Assert.That(result.Events, Has.Count.EqualTo(500));
        Assert.That(systemReads, Is.EqualTo(200));
        Assert.That(result.Coverage.Select(item => item.Status),
            Is.EqualTo(new[] { IncidentEventCoverageStatus.Complete, IncidentEventCoverageStatus.Unknown }));

        IEnumerable<IncidentEvent> Read(string log, CancellationToken token)
        {
            for (var i = 0; i < 300; i++)
            {
                if (log == "System") systemReads++;
                yield return new IncidentEvent(DateTimeOffset.UnixEpoch, log, 1, IncidentEventLevel.Warning, "");
            }
        }
    }

    [Test]
    public void ReadCore_PreservesUnreadableAndTimeoutCoverageAndCancellation()
    {
        var result = WindowsHistoryEventReader.ReadCore(Statuses, CancellationToken.None);
        Assert.That(result.Coverage.Select(item => item.Status), Is.EqualTo([IncidentEventCoverageStatus.Unreadable, IncidentEventCoverageStatus.TimedOut]));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WindowsHistoryEventReader.ReadCore(Cancel, cancelled.Token));

        IEnumerable<IncidentEvent> Statuses(string log, CancellationToken token) =>
            log == "Application" ? throw new System.ComponentModel.Win32Exception(5) : throw new TimeoutException();
        IEnumerable<IncidentEvent> Cancel(string log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return [];
        }
    }

    [Test, Category("Integration")]
    public void Read_RecentSystemRangeReportsCoverageOutsideRestrictedSandbox()
    {
        var result = new WindowsHistoryEventReader().Read(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.That(result.Coverage.Single(item => item.Log == "System").Status, Is.EqualTo(IncidentEventCoverageStatus.Complete));
    }
}
