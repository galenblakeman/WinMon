using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using MonCore.Monitoring;

namespace WinMon.Cli;

internal static class UsageArchiveCommand
{
    internal static Command Create(TextWriter output, TextWriter error)
    {
        var file = new Option<string>("--file") { Description = Strings.CliArchiveFile,
            DefaultValueFactory = _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", "usage-history.json") };
        var days = new Option<int>("--days") { Description = Strings.CliArchiveDays, DefaultValueFactory = _ => 1 };
        days.Validators.Add(result => { if (int.TryParse(result.Tokens.LastOrDefault()?.Value, out var n) && n is not (1 or 7)) result.AddError(Strings.CliArchiveDays); });
        var ending = new Option<string>("--ending") { Description = Strings.CliArchiveEnding,
            DefaultValueFactory = _ => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        ending.Validators.Add(result => { if (result.Tokens.LastOrDefault()?.Value is { } value &&
            !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) result.AddError(Strings.CliArchiveEnding); });
        var format = new Option<string>("--format") { Description = Strings.CliFormat, DefaultValueFactory = _ => "table" };
        format.AcceptOnlyFromAmong("table", "json");
        var noColor = new Option<bool>("--no-color") { Description = Strings.CliNoColor };
        var command = new Command("usage-history", Strings.CliUsageHistory) { file, days, ending, format, noColor };
        command.SetAction(async (result, token) =>
        {
            try
            {
                var date = DateOnly.ParseExact(result.GetValue(ending)!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (date.DayNumber < result.GetValue(days) || date == DateOnly.MaxValue) { await error.WriteLineAsync(Strings.CliArchiveEnding); return 1; }
                var archive = await UsageArchive.LoadAsync(result.GetValue(file)!, token);
                var (period, observedSeconds) = archive.Query(date.AddDays(1 - result.GetValue(days)), date);
                token.ThrowIfCancellationRequested();
                if (result.GetValue(format) == "json")
                    await output.WriteLineAsync(JsonSerializer.Serialize(new { schemaVersion = 1, timeZone = archive.TimeZoneId,
                        period.Since, period.Latest,
                        ObservedSeconds = observedSeconds, period.MachineBusyTicks, period.NotAttributedTicks,
                        complete = false, apps = period.Apps.Select(a => new { a.Name, a.AppName, a.ImagePath,
                            a.CpuTicks, a.IoBytes, a.GpuSeconds, dailyLifetimeObservations = a.Lifetimes, running = (int?)null,
                            lastActiveUtc = a.LastActive?.Timestamp.ToUniversalTime() }) }, SnapshotReport.JsonOptions));
                else ActivityCommands.Render(UsageTab.Build(period.Apps, period.Since, period.MachineBusyTicks,
                    period.Latest, 0, "", false, 0, true) with { Note = Strings.Format(Strings.UsageArchiveObserved, observedSeconds) }, output, result.GetValue(noColor));
                return 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return 130; }
            catch (Exception ex)
            {
                await error.WriteLineAsync(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, SnapshotReport.SafeText(ex.Message)));
                return 1;
            }
        });
        return command;
    }
}
