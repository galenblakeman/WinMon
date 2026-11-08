using System.Diagnostics;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class DiagnosticLogTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "WinMon.LogTests." + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Test]
    public void Stop_DrainsEventsAndRemovesPrivateArgumentsAndExceptionMessages()
    {
        using (var log = new DiagnosticLog(_folder, "App"))
        {
            Exception failure;
            try
            {
                throw new InvalidOperationException("secret endpoint 192.0.2.1 user=private");
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            log.TraceEvent(null, "source", TraceEventType.Error, 0, "Capture.Failed path={0} error={1}",
                @"C:\Users\private\process.exe", failure);
            log.TraceEvent(null, "source", TraceEventType.Information, 0, "Startup.Ready durationMs={0}", 42);
        }

        var text = File.ReadAllText(Path.Combine(_folder, "winmon.log"));
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Capture.Failed path=[omitted] error=InvalidOperationException"));
            Assert.That(text, Does.Contain(nameof(Stop_DrainsEventsAndRemovesPrivateArgumentsAndExceptionMessages)));
            Assert.That(text, Does.Contain("Startup.Ready durationMs=42"));
            Assert.That(text, Does.Contain("session="));
            Assert.That(text, Does.Not.Contain("private").And.Not.Contain("192.0.2.1"));
        });
    }

    [Test]
    public void RepeatedFailures_AreSummarizedAndOtherEventsRemainVisible()
    {
        using (var log = new DiagnosticLog(_folder, "App"))
        {
            for (var index = 0; index < 100; index++)
                log.TraceEvent(null, "source", TraceEventType.Warning, 0, "Network.ConnectionFailed");
            log.TraceEvent(null, "source", TraceEventType.Information, 0, "Network.Reconnected");
        }

        var lines = File.ReadAllLines(Path.Combine(_folder, "winmon.log"));
        Assert.That(lines.Count(line => line.Contains("Network.ConnectionFailed") && !line.Contains("Repeated")),
            Is.EqualTo(1));
        Assert.That(lines.Any(line => line.Contains("Repeated count=99")), Is.True);
        Assert.That(lines.Any(line => line.Contains("Network.Reconnected")), Is.True);
    }

    [Test]
    public void Rotation_KeepsFiveSizeLimitedFilesAndRetainsTheNewestEntry()
    {
        using (var log = new DiagnosticLog(_folder, "App", 1024))
        {
            for (var index = 0; index < 100; index++)
                log.TraceEvent(null, "source", TraceEventType.Information, 0, "Startup.Milestone number={0}", index);
        }

        var files = Directory.GetFiles(_folder, "*.log").Select(path => new FileInfo(path)).ToArray();
        Assert.That(files, Has.Length.EqualTo(5));
        Assert.That(files.All(file => file.Length <= 1024), Is.True);
        Assert.That(files.Sum(file => file.Length), Is.LessThanOrEqualTo(5 * 1024));
        Assert.That(File.ReadAllText(Path.Combine(_folder, "winmon.log")), Does.Contain("number=99"));
    }

    [Test]
    public void ConcurrentProducers_WriteWholeLinesWithoutExceedingTheQueue()
    {
        using (var log = new DiagnosticLog(_folder, "App"))
        {
            Parallel.For(0, 10000, index =>
                log.TraceEvent(null, "source", TraceEventType.Information, 0, "Producer.Item number={0}", index));
        }

        var lines = File.ReadAllLines(Path.Combine(_folder, "winmon.log"));
        Assert.That(lines.Length, Is.GreaterThan(0));
        Assert.That(lines.All(line => line.Contains("session=") && !line.Contains('\r')), Is.True);
        Assert.That(lines.Any(line => line.Contains("Logging.Dropped")), Is.True);
    }

    [Test]
    public void FolderLease_PreventsCompetingWritersAndReleasesAtShutdown()
    {
        using (var first = new DiagnosticLog(_folder, "App"))
        {
            Assert.Throws<IOException>(() => new DiagnosticLog(_folder, "App"));
        }

        Assert.DoesNotThrow(() =>
        {
            using var next = new DiagnosticLog(_folder, "App");
        });
    }

    [Test]
    public void UnavailableSharedDirectory_UsesLocalFallbackAndBothFailuresDisableLogging()
    {
        var local = Path.Combine(_folder, "local");
        using (var log = DiagnosticLog.CreateApp(Path.Combine(_folder, "shared"), local,
                   () => throw new UnauthorizedAccessException()))
        {
            Assert.That(log, Is.Not.Null);
            Assert.That(log!.Folder, Is.EqualTo(local));
        }

        var occupied = Path.Combine(_folder, "occupied");
        File.WriteAllText(occupied, "keep");
        Assert.That(DiagnosticLog.CreateApp(occupied, occupied, () => { }), Is.Null);
        Assert.That(File.ReadAllText(occupied), Is.EqualTo("keep"));
    }

    [Test]
    public void AvailableSharedDirectory_IsPreferredOverFallback()
    {
        var shared = Path.Combine(_folder, "shared");
        var local = Path.Combine(_folder, "local");
        using var log = DiagnosticLog.CreateApp(shared, local, () => { });
        Assert.That(log!.Folder, Is.EqualTo(shared));
        Assert.That(Directory.Exists(local), Is.False);
    }

    [Test]
    public async Task RotationFailure_IsNonfatalAndReportsLossAfterRecovery()
    {
        using (var log = new DiagnosticLog(_folder, "App", 1024))
        {
            var blockedArchive = Path.Combine(_folder, "winmon.4.log");
            Directory.CreateDirectory(blockedArchive);
            for (var index = 0; index < 20; index++)
                log.TraceEvent(null, "source", TraceEventType.Information, 0, "Writer.Item number={0}", index);
            await WaitUntil(() => log.DroppedEntries > 0);
            Directory.Delete(blockedArchive);
            log.TraceEvent(null, "source", TraceEventType.Information, 0, "Writer.Recovered");
        }

        var text = string.Join("", Directory.GetFiles(_folder, "*.log").Select(File.ReadAllText));
        Assert.That(text, Does.Contain("Writer.Recovered"));
        Assert.That(text, Does.Contain("Logging.Dropped"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var started = Stopwatch.GetTimestamp();
        while (!condition())
        {
            Assert.That(Stopwatch.GetElapsedTime(started), Is.LessThan(TimeSpan.FromSeconds(5)),
                "The log writer did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    [Test]
    public void InvalidFolder_IsRejectedWithoutTouchingTheExistingFile()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "occupied");
        File.WriteAllText(path, "keep");
        Assert.Throws<IOException>(() => new DiagnosticLog(path, "App"));
        Assert.That(File.ReadAllText(path), Is.EqualTo("keep"));
    }
}
