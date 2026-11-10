using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Spectre.Console;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

public static class CliCommand
{
    public static RootCommand Create(Func<IFrameSource>? createSource = null,
        TextWriter? output = null, TextWriter? error = null,
        Func<string, CancellationToken, Task>? runNetwork = null, CliSettingsStore? settingsStore = null,
        Func<InteractiveState, bool, int>? runWatch = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        settingsStore ??= new CliSettingsStore();
        var top = CreateTopOption(10);
        var sort = new Option<string>("--sort") { Description = Strings.CliSort, DefaultValueFactory = _ => "cpu" };
        sort.AcceptOnlyFromAmong("cpu", "memory", "gpu", "io", "threads", "handles");
        var filter = new Option<string>("--filter") { Description = Strings.CliFilter };
        var format = new Option<string>("--format") { Description = Strings.CliFormat, DefaultValueFactory = _ => "table" };
        format.AcceptOnlyFromAmong("table", "json");
        var noColor = new Option<bool>("--no-color") { Description = Strings.CliNoColor };
        var snapshot = new Command("snapshot", Strings.CliSnapshot) { top, sort, filter, format, noColor };
        snapshot.SetAction(async (result, token) =>
        {
            try
            {
                using var source = (createSource ?? (() => new WindowsFrameSource()))();
                source.Capture(new SamplingOptions());
                await Task.Delay(TimeSpan.FromSeconds(1), token);
                var frame = source.Capture(new SamplingOptions());
                token.ThrowIfCancellationRequested();
                var report = SnapshotReport.Create(frame, Environment.MachineName, result.GetValue(top),
                    Enum.Parse<ProcessSort>(result.GetValue(sort)!, true), result.GetValue(filter) ?? "");
                if (result.GetValue(format) == "json")
                    await output.WriteLineAsync(JsonSerializer.Serialize(report, SnapshotReport.JsonOptions));
                else
                {
                    var redirected = Console.IsOutputRedirected || !ReferenceEquals(output, Console.Out);
                    var suppressColors = result.GetValue(noColor) || redirected ||
                                         Environment.GetEnvironmentVariable("NO_COLOR") is not null;
                    var console = AnsiConsole.Create(new AnsiConsoleSettings
                    {
                        Out = new AnsiConsoleOutput(output),
                        Ansi = redirected ? AnsiSupport.No : AnsiSupport.Detect,
                        ColorSystem = suppressColors ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
                        Enrichment = new() { UseDefaultEnrichers = !suppressColors },
                        Interactive = InteractionSupport.No
                    });
                    report.Render(console);
                }
                return 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return 130; }
            catch (Exception ex)
            {
                await error.WriteLineAsync(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed,
                    SnapshotReport.SafeText(ex.Message)));
                return 1;
            }
        });
        var watchTop = CreateTopOption(0);
        var watchSort = new Option<string>("--sort") { Description = Strings.CliSort, DefaultValueFactory = _ => "auto" };
        watchSort.AcceptOnlyFromAmong("auto", "cpu", "memory", "gpu", "io", "threads", "handles", "name", "cpu-time",
            "gpu-time", "processes", "last-active", "receive", "send", "tcp", "udp", "received", "sent");
        var watchFilter = new Option<string>("--filter") { Description = Strings.CliFilter };
        var watchNoColor = new Option<bool>("--no-color") { Description = Strings.CliNoColor };
        var grouped = new Option<bool>("--grouped") { Description = Strings.Grouped };
        var appsOnly = new Option<bool>("--apps-only") { Description = Strings.AppOnly };
        var network = new Option<bool>("--network") { Description = Strings.NetworkProcessMonitoringHeading };
        var noSave = new Option<bool>("--no-save") { Description = Strings.CliNoSave };
        var tab = new Option<string>("--tab") { Description = Strings.CliTabs, DefaultValueFactory = _ => "processes" };
        tab.AcceptOnlyFromAmong("processes", "services", "usage", "connections");
        var watch = new Command("watch", Strings.CliWatch) { watchTop, watchSort, watchFilter, watchNoColor, grouped, appsOnly, network, tab, noSave };
        watch.Validators.Add(result =>
        {
            string? tabName, sortName;
            int selectedTop;
            try
            {
                tabName = result.GetValue(tab);
                sortName = result.GetValue(watchSort);
                selectedTop = result.GetValue(watchTop);
            }
            catch (InvalidOperationException) { return; } // The parser reports invalid individual option values.
            if (result.GetResult(tab) is { Implicit: false } && tabName is not null && sortName is not null &&
                (ResolveSort(tabName, sortName) < 0 || tabName == "services" && sortName == "name" && selectedTop != 0))
                result.AddError(Strings.CliSort);
        });
        watch.SetAction(async (result, token) =>
        {
            // Return the task before entering the input loop so the invoker can install cancellation handling.
            await Task.Yield();
            var suppressColors = result.GetValue(watchNoColor) || Environment.GetEnvironmentVariable("NO_COLOR") is not null;
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(output),
                ColorSystem = suppressColors ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
                Enrichment = new() { UseDefaultEnrichers = !suppressColors }
            });
            if (runWatch is null && (Console.IsInputRedirected || Console.IsOutputRedirected || !ReferenceEquals(output, Console.Out) ||
                !console.Profile.Capabilities.Ansi))
            {
                error.WriteLine(Strings.CliTerminalRequired);
                return 1;
            }
            try
            {
                var saved = settingsStore.Load(() => error.WriteLine(Strings.CliSettingsLoadFailed));
                if (IsExplicit(result, tab)) saved.ActiveTab = Enum.Parse<TerminalTab>(result.GetValue(tab)!, true);
                var active = saved.Tabs[(int)saved.ActiveTab];
                if (IsExplicit(result, watchTop)) active.Top = result.GetValue(watchTop);
                if (IsExplicit(result, watchSort))
                {
                    active.Sort = ResolveSort(saved.ActiveTab.ToString().ToLowerInvariant(), result.GetValue(watchSort)!);
                    if (active.Sort < 0) throw new ArgumentException(Strings.CliSort);
                }
                if (saved.ActiveTab == TerminalTab.Services && active.Sort == 3 && active.Top != 0)
                {
                    if (IsExplicit(result, watchSort)) throw new ArgumentException(Strings.CliSort);
                    active.Sort = 0;
                }
                if (IsExplicit(result, grouped)) saved.Tabs[0].Grouped = result.GetValue(grouped);
                if (IsExplicit(result, appsOnly) && saved.ActiveTab != TerminalTab.Services) active.AppsOnly = result.GetValue(appsOnly);
                if (IsExplicit(result, network)) saved.Network = result.GetValue(network);
                var state = new InteractiveState(ProcessSort.Cpu, "");
                state.RestorePreferences(saved);
                state.Active.Filter = state.Active.Draft = result.GetValue(watchFilter) ?? "";
                if (runWatch is not null) return runWatch(state, result.GetValue(noSave));
                var exit = InteractiveMonitor.Run(console, createSource ?? (() => new WindowsFrameSource()),
                    state.Active.Top, state.Sort, state.Filter, token,
                    initialState: state, settingsStore: settingsStore, noSave: result.GetValue(noSave));
                if (state.SettingsSaveFailed) error.WriteLine(Strings.CliSettingsSaveFailed);
                return exit;
            }
            catch (Exception ex)
            {
                error.WriteLine(string.Format(CultureInfo.CurrentCulture, Strings.CliFailed, SnapshotReport.SafeText(ex.Message)));
                return 1;
            }
        });
        return new RootCommand(Strings.CliDescription)
        { snapshot, watch, NetworkCommands.Create(output, error, runNetwork), SettingsCommands.Create(settingsStore, output, error) };
    }

    private static bool IsExplicit(ParseResult result, Option option) => result.GetResult(option) is { Implicit: false };

    private static Option<int> CreateTopOption(int defaultValue)
    {
        var option = new Option<int>("--top") { Description = Strings.CliTop, DefaultValueFactory = _ => defaultValue };
        option.Validators.Add(result =>
        {
            if (int.TryParse(result.Tokens.LastOrDefault()?.Value, out var count) && count is < 0 or > 1000)
                result.AddError(Strings.CliTopRange);
        });
        return option;
    }

    private static int ResolveSort(string tab, string sort)
    {
        if (sort == "auto") return 0;
        var values = tab switch
        {
            "services" => new[] { "cpu", "memory", "io", "name" },
            "usage" => ["cpu-time", "io", "gpu-time", "processes", "last-active"],
            "connections" => ["receive", "send", "tcp", "udp", "received", "sent"],
            _ => ["cpu", "gpu", "memory", "io", "threads", "handles"]
        };
        return Array.IndexOf(values, sort);
    }
}
