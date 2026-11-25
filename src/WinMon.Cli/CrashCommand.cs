using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using Spectre.Console;

namespace WinMon.Cli;

internal static class CrashCommand
{
    internal static Command Create(TextWriter output, TextWriter error,
        Func<DateTimeOffset, DateTimeOffset, CancellationToken, CrashEventResult>? readCrashes)
    {
        var days = new Option<int>("--days") { Description = Strings.CliCrashDays, DefaultValueFactory = _ => 30 };
        var filter = new Option<string>("--filter") { Description = Strings.CliFilter, DefaultValueFactory = _ => "" };
        var format = new Option<string>("--format") { Description = Strings.CliFormat, DefaultValueFactory = _ => "table" };
        format.AcceptOnlyFromAmong("table", "json");
        var noColor = new Option<bool>("--no-color") { Description = Strings.CliNoColor };
        days.Validators.Add(result =>
        {
            if (int.TryParse(result.Tokens.LastOrDefault()?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value is < 1 or > 90)
                result.AddError(Strings.CliCrashDaysRange);
        });
        var command = new Command("crashes", Strings.CliCrashes)
            { days, filter, format, noColor };
        command.SetAction(async (result, cancellationToken) =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var crashes = await Task.Run(() => (readCrashes ?? new WindowsHistoryEventReader().ReadCrashes)(
                    now.AddDays(-result.GetValue(days)), now, cancellationToken), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var report = CrashReport.Create(result.GetValue(days), result.GetValue(filter)!, crashes);
                if (result.GetValue(format) == "json")
                    await output.WriteLineAsync(JsonSerializer.Serialize(report, SnapshotReport.JsonOptions));
                else
                    CrashReport.Render(output, report, result.GetValue(noColor));
                return 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return 130; }
            catch (Exception ex)
            {
                await error.WriteLineAsync(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, SnapshotReport.SafeText(ex.Message)));
                return 1;
            }
        });
        return command;
    }
}

internal sealed record CrashCoverage(string ApplicationLogStatus, bool LimitReached, DateTimeOffset? OldestRetainedApplicationEvent);
internal sealed record CrashVersion(string ApplicationVersion, string? ModuleVersion, int Count);
internal sealed record CrashEntry(string Kind, string Application, string? Module, string? ExceptionCode,
    string? ExceptionName, string? Offset, int Count, DateTimeOffset FirstUtc, DateTimeOffset LastUtc,
    IReadOnlyList<CrashVersion> Versions);
internal sealed record CrashReport(int SchemaVersion, int Days, CrashCoverage Coverage, IReadOnlyList<CrashEntry> Groups)
{
    internal static CrashReport Create(int days, string filter, CrashEventResult result)
    {
        var groups = CrashGroups.Build(result.Events).Where(group => group.Key.Application.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(Project).ToArray();
        return new(1, days, new(result.ApplicationCoverage.Status.ToString(), result.LimitReached,
            result.OldestRetainedApplicationEvent?.ToUniversalTime()), groups);
    }

    internal static CrashEntry Project(CrashGroup group) => new(group.Key.Kind.ToString(), SnapshotReport.SafeText(group.Key.Application),
        group.Key.Module is null ? null : SnapshotReport.SafeText(group.Key.Module), Hex(group.Key.ExceptionCode),
        CrashGroups.ExceptionName(group.Key.ExceptionCode), Hex(group.Key.Offset), group.Count, group.FirstTime.ToUniversalTime(),
        group.LastTime.ToUniversalTime(), group.Versions.Select(version => new CrashVersion(SnapshotReport.SafeText(version.ApplicationVersion),
            version.ModuleVersion is null ? null : SnapshotReport.SafeText(version.ModuleVersion), version.Count)).ToArray());

    internal static void Render(TextWriter output, CrashReport report, bool noColor)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors, Interactive = InteractionSupport.No
        });
        var table = new Table().Border(TableBorder.Simple).AddColumn(Strings.CliCrashApplication).AddColumn(Strings.CliCrashFailure)
            .AddColumn(Strings.CliCrashCount).AddColumn(Strings.CliCrashLatestUtc);
        foreach (var group in report.Groups)
        {
            var exceptionName = group.ExceptionCode switch
            {
                "0xC0000005" => Strings.FlightRecorderCrashAccessViolation,
                "0xC0000409" => Strings.FlightRecorderCrashStackBufferOverrun,
                "0xE0434352" => Strings.FlightRecorderCrashUnhandledDotNetException,
                _ => null
            };
            var exception = string.Join(" ", new[] { group.ExceptionCode, exceptionName }.Where(value => !string.IsNullOrWhiteSpace(value)));
            var failure = group.Kind == "Hang" ? Strings.CliCrashNotResponding : string.Join(" ", new[] { group.Module, exception }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            table.AddRow(Markup.Escape(group.Application), Markup.Escape(failure), group.Count.ToString(CultureInfo.InvariantCulture),
                group.LastUtc.ToString("u", CultureInfo.InvariantCulture));
        }
        console.Write(table);
        console.WriteLine(Strings.Format(Strings.CliCrashCoverage, report.Coverage.ApplicationLogStatus, report.Coverage.LimitReached,
            report.Coverage.OldestRetainedApplicationEvent?.ToString("u", CultureInfo.InvariantCulture) ?? Strings.CliCrashUnknown));
    }

    internal static string? Hex(uint? value) => value is { } code ? "0x" + code.ToString("X8", CultureInfo.InvariantCulture) : null;
    internal static string? Hex(ulong? value) => value is { } offset ? "0x" + offset.ToString("X", CultureInfo.InvariantCulture) : null;
}
