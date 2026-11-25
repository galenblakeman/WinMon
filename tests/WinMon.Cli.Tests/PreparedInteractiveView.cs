using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

public sealed record ViewCase(string Name, TerminalTab Tab = TerminalTab.Processes, int Width = 120, int Height = 36,
    bool Unicode = true, string Mode = "live", NetworkTrafficState Traffic = NetworkTrafficState.Active,
    int Status = 0, bool Workers = true, bool SaveFailed = false, bool Color = true, int Top = 3,
    int DetailOffset = 0, int HorizontalOffset = 0);

public sealed record ViewSnapshot(string[] Lines, string State)
{
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
}

/// <summary>Owned observations for rendering tests and the isolated before/after probe; never starts sampling.</summary>
public sealed class PreparedInteractiveView : IDisposable
{
    public static IReadOnlyList<ViewCase> Cases { get; } = CreateCases();
    public static readonly string[] RepresentativeNames =
        ["processes-120-unicode", "services-120-unicode", "connections-120-unicode", "usage-120-unicode", "hardware-120-unicode",
            "editing-filter", "details-0-3", "connections-Unavailable-save", "help-hardware-0-0", "small-help-40"];
    public ViewCase Case { get; }
    public InteractiveState State { get; }
    private readonly MonitorSession _session;
    private int _detailsRead, _cellsRead;

    public PreparedInteractiveView(ViewCase item)
    {
        Case = item;
        State = new InteractiveState(ProcessSort.Cpu, "");
        State.Configure(7, false, false, item.Tab);
        var settings = State.Active;
        settings.Offset = 2; settings.ColumnOffset = 99; settings.SelectedKey = "missing-selection";
        settings.DetailOffset = item.DetailOffset; settings.HorizontalOffset = item.HorizontalOffset;
        settings.Status = item.Status; settings.Workers = item.Workers;
        State.SettingsSaveFailed = item.SaveFailed;
        State.NetworkEnabled = item.Tab == TerminalTab.Connections && item.Mode != "network-disabled";
        State.NetworkStatus = item.Traffic;
        var local = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var anchor = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        _session = new MonitorSession(() => throw new InvalidOperationException("Prepared views must not collect native samples."),
            () => new SamplingOptions(), action => action(), retainUsageHistory: item.Mode != "usage-unavailable");
        _session.Usage.Reset(anchor);
        if (item.Mode is not ("no-frame" or "no-frame-error"))
        {
            for (var second = 0; second < 2; second++)
            {
                var processes = Enumerable.Range(0, 8).Select(index => new ProcessUsage(42 + index, index + 1,
                    "[red]owned " + index, 10 + index, (ulong)(index + 1) * 1_048_576, 1024)
                {
                    ImagePath = @"C:\owned\app" + index + ".exe", AppName = "Owned " + index,
                    HasAppWindow = index % 2 == 0, PrivateBytes = (ulong)(index + 1) * 2_097_152,
                    ThreadCount = index + 2, HandleCount = index + 3, GpuPercent = index
                }).ToArray();
                var sample = new MonitorSample(anchor.AddSeconds(second), 42, processes, 0,
                    IsGap: item.Mode == "gap" && second == 1, MonotonicTimestamp: (second + 1) * Stopwatch.Frequency)
                {
                    IntervalSeconds = 1, IntervalCpuTicks = 100_000_000,
                    Resources = new ResourceSample { TotalMemoryBytes = 32UL * 1_073_741_824, UsedMemoryBytes = 16UL * 1_073_741_824,
                        GpuPercent = 76, DiskActivePercent = 10, DiskName = "C:", NetworkName = "Owned adapter",
                        LinkBitsPerSecond = 1_000_000_000, ReceiveBytesPerSecond = 1_000_000, SendBytesPerSecond = 500_000 }
                };
                var services = new ServiceFrame(sample,
                    [new ServiceGroup("running", [new ServiceObservation("Owned.Service", "Owned [red]service", 4, 42, 1)], processes[0], processes[..2]),
                        new ServiceGroup("stopped", [new ServiceObservation("Stopped.Service", "Stopped service", 1, 0, 0)], null, [])]);
                var connections = new NetworkConnectionsFrame(processes.Select(process => new NetworkConnection(process.ProcessId,
                    process.CreationTicks, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 5000 + process.ProcessId),
                    new IPEndPoint(IPAddress.Parse("203.0.113.8"), 443), TcpState.Established)).ToArray(),
                    NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
                _session.Record(sample, sample, services, connections);
            }
            State.ObserveSession(_session);
        }
        if (item.Mode is "error" or "no-frame-error") State.Observe(State.DisplayedFrame?.Full,
            SamplingError.ProcessesUnavailable, State.DisplayedSamples);
        if (item.Mode is "paused" or "usage-unavailable") State.Handle(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), 5);
        if (item.Mode == "filter") settings.Filter = settings.Draft = "owned";
        if (item.Mode == "editing-filter") { settings.Editing = true; settings.Draft = "owned · 😀\u001b[31m"; }
        if (item.Mode == "no-matches") settings.Filter = settings.Draft = "no-matching-process";
        if (item.Mode == "columns") { settings.RevealSort = false; settings.ColumnOffset = 2; settings.Sort = (int)ProcessSort.Handles; }
        if (item.Mode == "pins")
        {
            var row = InteractiveMonitor.Query(State).Rows[0];
            settings.Pins[row.Key] = row; settings.SelectedKey = row.Key; settings.PinnedOnly = true; settings.Grouped = true;
        }
        if (item.Tab == TerminalTab.Hardware)
        {
            State.Hardware = new HardwareInventorySnapshot(1, anchor, anchor, "ok", false, false,
                new("ok", false, [], new HardwareCpu(["Owned hardware"], 8, 16, [])),
                new("unavailable", false, [], null), new("unavailable", false, [], null), new("unavailable", false, [], null),
                new("unavailable", false, [], null), new("unavailable", false, [], null), new("unavailable", false, [], null));
            if (item.Mode != "live" && item.Mode.StartsWith("hardware-", StringComparison.Ordinal)) State.Hardware = null;
            State.HardwareLoading = item.Mode is "hardware-loading" or "hardware-loading-failed";
            State.HardwareReadFailed = item.Mode is "hardware-error" or "hardware-loading-failed";
        }
        if (item.Mode is "details" or "help-details")
        {
            settings.Details = true;
            settings.Rows = [new TerminalRow("owned-details", () => { _cellsRead++; return ["Owned"]; },
                () => { _detailsRead++; return "First\t[red]literal[/]\u001b[31m\n😀 second\n" + string.Join('\n', Enumerable.Range(0, 40).Select(i => "Detail line " + i)); })];
            settings.SelectedKey = "owned-details";
        }
        if (item.Mode == "missing-details") { settings.Details = true; settings.Rows = []; }
        if (item.Mode is "help" or "help-details") State.Handle(new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false), 5);
    }

    public string StateFingerprint() => JsonSerializer.Serialize(new
    {
        State.Tab, State.Help, State.Paused, State.NetworkEnabled, State.NetworkStatus, State.SettingsSaveFailed,
        State.Active.Top, State.Active.Offset, State.Active.SelectedKey, State.Active.DetailOffset,
        State.Active.HorizontalOffset, State.Active.ColumnOffset, State.Active.RevealSort,
        Rows = State.Active.Rows.Select(row => row.Key).ToArray(), Pins = State.Active.Pins.Keys.Order(StringComparer.Ordinal).ToArray(),
        Preferences = State.CapturePreferences(), DetailsRead = _detailsRead, CellsRead = _cellsRead
    });

    public string Render()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.Yes,
            ColorSystem = Case.Color ? ColorSystemSupport.TrueColor : ColorSystemSupport.NoColors
        });
        console.Profile.Width = Case.Width;
        console.Write(InteractiveMonitor.BuildView(State, Case.Top, 5, Case.Width, Case.Height, Case.Unicode));
        return output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public ViewSnapshot Capture()
    {
        var lines = Render().Split('\n');
        var separator = Case.Unicode ? " · " : " | ";
        var prefix = TerminalText.Fit(TerminalChrome.ProductName + separator + SnapshotReport.SafeText(Environment.MachineName) + separator,
            Math.Max(0, Case.Width - 12));
        if (prefix.Length > 0)
        {
            if (!lines[0].Contains(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("The dynamic header prefix must stay intact before normalization.");
            lines[0] = lines[0].Replace(prefix, "<VERSION-AND-MACHINE>", StringComparison.Ordinal);
        }
        return new ViewSnapshot(lines, StateFingerprint());
    }

    public static string Plain(string value) => Regex.Replace(value, "\u001b\\[[0-9;]*m", "");
    public void Dispose() => _session.Close();

    private static ViewCase[] CreateCases()
    {
        var cases = new List<ViewCase>();
        foreach (var tab in Enum.GetValues<TerminalTab>())
            foreach (var size in new[] { (80, 20), (120, 36) })
                foreach (var unicode in new[] { false, true })
                    cases.Add(new ViewCase(tab.ToString().ToLowerInvariant() + "-" + size.Item1 + (unicode ? "-unicode" : "-ascii"), tab, size.Item1, size.Item2, unicode));
        foreach (var mode in new[] { "paused", "error", "gap", "filter", "editing-filter", "no-matches", "pins", "columns" })
            cases.Add(new ViewCase(mode, Mode: mode));
        cases.Add(new ViewCase("settings-failure", SaveFailed: true));
        cases.Add(new ViewCase("all-entries", Top: 0));
        cases.Add(new ViewCase("monochrome", Color: false));
        cases.Add(new ViewCase("usage-unavailable", TerminalTab.Usage, Mode: "usage-unavailable"));
        foreach (var size in new[] { (79, 20), (80, 19), (40, 10), (1, 1) })
            foreach (var mode in new[] { "live", "help", "details" })
                cases.Add(new ViewCase("small-" + mode + "-" + size.Item1, Width: size.Item1, Height: size.Item2, Mode: mode));
        foreach (var tab in new[] { TerminalTab.Processes, TerminalTab.Hardware })
            foreach (var offset in new[] { -9, 0, 999 })
                foreach (var horizontal in new[] { 0, 5 })
                    cases.Add(new ViewCase("help-" + tab.ToString().ToLowerInvariant() + "-" + offset + "-" + horizontal, tab,
                        Mode: "help", DetailOffset: offset, HorizontalOffset: horizontal));
        foreach (var offset in new[] { -9, 0, 999 })
            foreach (var horizontal in new[] { 0, 3, 999 })
                cases.Add(new ViewCase("details-" + offset + "-" + horizontal, Mode: "details", DetailOffset: offset, HorizontalOffset: horizontal));
        cases.Add(new ViewCase("missing-details", Mode: "missing-details"));
        cases.Add(new ViewCase("help-details", Mode: "help-details"));
        foreach (var error in new[] { false, true })
            foreach (var unicode in new[] { false, true })
                cases.Add(new ViewCase("no-frame" + (error ? "-error" : "") + (unicode ? "-unicode" : "-ascii"),
                    Unicode: unicode, Mode: error ? "no-frame-error" : "no-frame"));
        foreach (var status in new[] { 0, 1, 2 })
            foreach (var workers in new[] { false, true })
                cases.Add(new ViewCase("services-" + status + "-" + workers, TerminalTab.Services, Status: status, Workers: workers, Top: 0));
        foreach (var traffic in Enum.GetValues<NetworkTrafficState>())
            foreach (var failed in new[] { false, true })
                cases.Add(new ViewCase("connections-" + traffic + (failed ? "-save" : ""), TerminalTab.Connections,
                    Traffic: traffic, SaveFailed: failed));
        cases.Add(new ViewCase("connections-disabled", TerminalTab.Connections, Mode: "network-disabled"));
        cases.Add(new ViewCase("connections-error", TerminalTab.Connections, Mode: "error"));
        cases.Add(new ViewCase("connections-gap", TerminalTab.Connections, Mode: "gap"));
        foreach (var mode in new[] { "hardware-empty", "hardware-loading", "hardware-error", "hardware-loading-failed" })
            cases.Add(new ViewCase(mode, TerminalTab.Hardware, Mode: mode, SaveFailed: mode == "hardware-loading-failed"));
        return cases.ToArray();
    }
}
