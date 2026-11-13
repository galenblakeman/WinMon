using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

internal static class ActivityCommands
{
    internal static Command Create(string name, Func<IFrameSource>? createSource, TextWriter output, TextWriter error)
    {
        var format = new Option<string>("--format") { Description = Strings.CliFormat, DefaultValueFactory = _ => "table" };
        format.AcceptOnlyFromAmong("table", "json");
        var filter = new Option<string>("--filter") { Description = Strings.CliFilter, DefaultValueFactory = _ => "" };
        var top = new Option<int>("--top") { Description = Strings.CliTop, DefaultValueFactory = _ => 20 };
        var duration = new Option<int>("--duration") { Description = Strings.CliCollectionSeconds, DefaultValueFactory = _ => 1 };
        var sort = new Option<string>("--sort") { Description = Strings.CliSort, DefaultValueFactory = _ => name == "usage" ? "cpu-time" : name == "connections" ? "pid" : "cpu" };
        var sorts = name switch
        {
            "usage" => new[] { "cpu-time", "io", "gpu-time", "processes", "last-active" },
            "services" => ["cpu", "memory", "io", "name"],
            _ => ["pid", "name"]
        };
        sort.AcceptOnlyFromAmong(sorts);
        var noColor = new Option<bool>("--no-color") { Description = Strings.CliNoColor };
        var command = new Command(name, name switch { "services" => Strings.Services, "usage" => Strings.UsageTab, _ => Strings.ConnectionsTab })
            { format, filter, top, duration, sort, noColor };
        top.Validators.Add(result =>
        {
            if (int.TryParse(result.Tokens.LastOrDefault()?.Value, out var value) && value is < 0 or > 1000)
                result.AddError(Strings.CliCollectionRange);
        });
        duration.Validators.Add(result =>
        {
            if (int.TryParse(result.Tokens.LastOrDefault()?.Value, out var value) && value is < 1 or > 60)
                result.AddError(Strings.CliCollectionRange);
        });
        command.SetAction(async (result, token) =>
        {
            try
            {
                using var source = (createSource ?? (() => new WindowsFrameSource()))();
                var baseline = source.Capture(new SamplingOptions());
                var since = baseline.Full.Timestamp;
                var ledger = new UsageLedger(since);
                var frames = new List<CapturedFrame>();
                for (var i = 0; i < result.GetValue(duration); i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                    var frame = source.Capture(new SamplingOptions());
                    frames.Add(frame);
                    ledger.Add(frame.Full);
                }
                token.ThrowIfCancellationRequested();
                var report = ActivityReport.Create(name, frames, ledger, Array.IndexOf(sorts, result.GetValue(sort)),
                    result.GetValue(filter)!, result.GetValue(top));
                if (result.GetValue(format) == "json")
                    await output.WriteLineAsync(JsonSerializer.Serialize(report.Data, SnapshotReport.JsonOptions));
                else Render(report.Table, output, result.GetValue(noColor));
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

    internal static void Render(TerminalTable report, TextWriter output, bool noColor)
    {
        var redirected = Console.IsOutputRedirected || !ReferenceEquals(output, Console.Out);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output), Ansi = redirected ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = noColor || Environment.GetEnvironmentVariable("NO_COLOR") is not null ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Interactive = InteractionSupport.No
        });
        var table = new Table();
        foreach (var column in report.Columns) table.AddColumn(Markup.Escape(SnapshotReport.SafeText(column)));
        foreach (var row in report.Rows) table.AddRow(row.Cells.Select(cell => Markup.Escape(SnapshotReport.SafeText(cell))).ToArray());
        console.Write(table);
        console.WriteLine(SnapshotReport.SafeText(report.Note));
    }
}
