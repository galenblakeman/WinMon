using System.Text.Json;
using NUnit.Framework;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class CrashCommandTests
{
    [Test]
    public async Task JsonReportGroupsFiltersAndUsesInvariantCodes()
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, readCrashes: (_, _, _) => Result(
            Event("Example.exe", "2.0", "example.dll", 0xC0000005, 0x10),
            Event("example.exe", "1.0", "example.dll", 0xC0000005, 0x10),
            Event("Other.exe", "1.0", "other.dll", 0xE0434352, 0x20)));

        Assert.That(await command.Parse(["crashes", "--days", "7", "--filter", "EXAMPLE", "--format", "json"]).InvokeAsync(), Is.Zero);
        using var document = JsonDocument.Parse(output.ToString());
        var report = document.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(report.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(report.GetProperty("days").GetInt32(), Is.EqualTo(7));
            Assert.That(report.GetProperty("groups").GetArrayLength(), Is.EqualTo(1));
            Assert.That(report.GetProperty("groups")[0].GetProperty("count").GetInt32(), Is.EqualTo(2));
            Assert.That(report.GetProperty("groups")[0].GetProperty("exceptionCode").GetString(), Is.EqualTo("0xC0000005"));
            Assert.That(report.GetProperty("groups")[0].GetProperty("exceptionName").GetString(), Is.EqualTo("Access violation"));
            Assert.That(report.GetProperty("coverage").GetProperty("oldestRetainedApplicationEvent").GetDateTimeOffset().Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [TestCase("0")]
    [TestCase("91")]
    [TestCase("not-a-number")]
    public void InvalidDaysNeverRead(string days)
    {
        var command = CliCommand.Create(readCrashes: (_, _, _) => throw new AssertionException("Must not read"));
        Assert.That(command.Parse(["crashes", "--days", days]).Errors, Is.Not.Empty);
    }

    [Test]
    public async Task DefaultsToThirtyDaysAndCancellationWritesNoPartialReport()
    {
        using var output = new StringWriter();
        TimeSpan? range = null;
        var defaultCommand = CliCommand.Create(output: output, error: output, readCrashes: (start, end, _) =>
        {
            range = end - start;
            return Result();
        });
        Assert.That(await defaultCommand.Parse(["crashes", "--format", "json"]).InvokeAsync(), Is.Zero);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("days").GetInt32(), Is.EqualTo(30));
        Assert.That(range, Is.EqualTo(TimeSpan.FromDays(30)));

        output.GetStringBuilder().Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = CliCommand.Create(output: output, error: output, readCrashes: (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Result();
        });
        Assert.That(await cancelled.Parse(["crashes"]).InvokeAsync(cancellationToken: cancellation.Token), Is.EqualTo(130));
        Assert.That(output.ToString(), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(90)]
    public async Task AcceptsExactDayBoundsAndPassesTheRequestedRange(int days)
    {
        TimeSpan? range = null;
        var command = CliCommand.Create(readCrashes: (start, end, _) =>
        {
            range = end - start;
            return Result();
        });
        Assert.That(await command.Parse(["crashes", "--days", days.ToString(), "--format", "json"]).InvokeAsync(), Is.Zero);
        Assert.That(range, Is.EqualTo(TimeSpan.FromDays(days)));
    }

    [Test]
    public async Task FailedReaderExitsOneAndSanitizesItsError()
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, readCrashes: (_, _, _) => throw new InvalidOperationException("bad\u001b[31m"));
        Assert.That(await command.Parse(["crashes"]).InvokeAsync(), Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString().Contains('\u001b'), Is.False);
            Assert.That(output.ToString(), Does.Contain("bad"));
        });
    }

    [Test]
    public async Task TableEscapesControlCharactersAndReportsCoverage()
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, readCrashes: (_, _, _) => Result(
            Event("[red]app.exe", "1.0", "[blue]module.dll", 0xC0000005, 0x10)));

        Assert.That(await command.Parse(["crashes", "--no-color"]).InvokeAsync(), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString().Contains('\u001b'), Is.False);
            Assert.That(output.ToString(), Does.Contain("[red]app.exe"));
            Assert.That(output.ToString(), Does.Contain("[blue]module.dll"));
            Assert.That(output.ToString(), Does.Contain("0xC0000005"));
            Assert.That(output.ToString(), Does.Contain("Access violation"));
            Assert.That(output.ToString(), Does.Contain("Application log: Complete"));
        });
    }

    [Test, SetCulture("de-DE"), SetUICulture("de-DE")]
    public async Task TableLocalizesFailureNamesWhileJsonKeepsStableNames()
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, readCrashes: (_, _, _) => Result(
            Event("app.exe", "1.0", "app.dll", 0xC0000005, 0x10)));

        Assert.That(await command.Parse(["crashes", "--no-color"]).InvokeAsync(), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("Zugriffsverletzung"));
        output.GetStringBuilder().Clear();
        Assert.That(await command.Parse(["crashes", "--format", "json"]).InvokeAsync(), Is.Zero);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.That(json.RootElement.GetProperty("groups")[0].GetProperty("exceptionName").GetString(),
            Is.EqualTo("Access violation"));
    }

    private static CrashEventResult Result(params IncidentEvent[] events) => new(events,
        new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Complete), false,
        DateTimeOffset.Parse("2026-11-01T00:00:00Z"));

    private static IncidentEvent Event(string application, string version, string module, uint code, ulong offset) => new(
        DateTimeOffset.UtcNow, "Application Error", 1000, IncidentEventLevel.Error, "", CrashSignature: new(
            CrashKind.Crash, application, version, module, "1.0", code, offset));
}
