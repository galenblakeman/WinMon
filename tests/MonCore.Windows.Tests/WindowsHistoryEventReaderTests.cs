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
