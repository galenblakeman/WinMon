using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.Cli.Tests;

public sealed record InputKey(string Name, ConsoleKey Key, char Character);
public sealed record InputGroup(TerminalTab Tab, string Mode, ConsoleModifiers Modifiers)
{
    public string Name => Tab + "-" + Mode + "-" + (int)Modifiers;
}

/// <summary>Fresh public controller state for original/current key-transition comparisons.</summary>
public sealed class PreparedInteractiveInput
{
    public static readonly string[] Modes = ["normal", "edit", "help", "details", "paused"];
    public static IReadOnlyList<InputKey> Keys { get; } = CreateKeys();
    public static IReadOnlyList<InputGroup> Groups { get; } =
        (from tab in Enum.GetValues<TerminalTab>() from mode in Modes from modifiers in Enumerable.Range(0, 8)
            select new InputGroup(tab, mode, (ConsoleModifiers)modifiers)).ToArray();
    public InteractiveState State { get; }
    private int _cellsRead, _detailsRead;

    public PreparedInteractiveInput(TerminalTab tab = TerminalTab.Processes, string mode = "normal")
    {
        State = new InteractiveState(ProcessSort.Memory, "saved-filter");
        State.Configure(7, false, false, tab);
        var active = State.Active;
        active.Sort = 2; active.Offset = 2; active.SelectedKey = "two"; active.RevealSort = false;
        active.ColumnOffset = 3; active.DetailOffset = 5; active.HorizontalOffset = 16;
        active.Draft = "draft😀";
        active.Rows = Enumerable.Range(1, 3).Select(index => new TerminalRow(index == 1 ? "one" : index == 2 ? "two" : "three",
            () => { _cellsRead++; return ["Cell " + index]; }, () => { _detailsRead++; return "Detail " + index; })
        { PinIdentity = "app|" + index, PinName = "Pin " + index }).ToArray();
        var samples = Enumerable.Range(0, 3).Select(Sample).ToArray();
        State.Observe(samples[^1], SamplingError.ProcessesUnavailable, samples);
        State.NetworkStatus = NetworkTrafficState.Incomplete;
        if (mode == "edit") active.Editing = true;
        if (mode == "details") active.Details = true;
        if (mode == "help")
        {
            State.Handle(new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false), 3);
            if (!State.Help) throw new InvalidOperationException("The help fixture must start with help open.");
        }
        if (mode == "paused")
        {
            State.Handle(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), 3);
            if (!State.Paused) throw new InvalidOperationException("The paused fixture must start frozen.");
            State.Observe(Sample(3), SamplingError.ReadingsUnavailable, [.. samples, Sample(3)]);
            State.NetworkStatus = NetworkTrafficState.Active;
        }
    }

    public static MonitorSample Sample(int second) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 25,
        [new ProcessUsage(42 + second, second + 1, "Owned " + second, 10)], 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    public static ConsoleKeyInfo KeyInfo(InputKey key, ConsoleModifiers modifiers)
    {
        var character = modifiers.HasFlag(ConsoleModifiers.Shift) && key.Character is >= 'a' and <= 'z'
            ? char.ToUpperInvariant(key.Character) : key.Character;
        return new ConsoleKeyInfo(character, key.Key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));
    }

    public string Snapshot(Exception? exception = null) => JsonSerializer.Serialize(new
    {
        State.Tab, State.Help, State.Quit, State.Paused, State.NetworkEnabled, State.NetworkStatus, State.LiveNetworkStatus,
        State.UsagePeriodSeconds, State.HardwareLoading, State.HardwareRefreshRequested, State.Error,
        Frame = State.DisplayedFrame?.Full.Timestamp,
        Samples = State.DisplayedSamples.Select(sample => sample.Timestamp).ToArray(),
        State.Observations.UsageAvailable, State.Observations.UsageComplete, State.Observations.PeriodUsage,
        Active = new { State.Active.Sort, State.Active.Filter, State.Active.Draft, State.Active.Editing,
            State.Active.AppsOnly, State.Active.Grouped, State.Active.PinnedOnly, State.Active.Workers, State.Active.Status,
            State.Active.TrafficSeconds, State.Active.Top, State.Active.Offset, State.Active.SelectedKey,
            State.Active.Details, State.Active.DetailOffset, State.Active.HorizontalOffset, State.Active.ColumnOffset, State.Active.RevealSort,
            Rows = State.Active.Rows.Select(row => row.Key).ToArray(),
            Pins = State.Active.Pins.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value.PinIdentity, pair.Value.PinName }).ToArray(),
            Saved = State.Active.SavedPins.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value.Name }).ToArray() },
        Preferences = State.CapturePreferences(), CellsRead = _cellsRead, DetailsRead = _detailsRead,
        Exception = exception is null ? null : new { Type = exception.GetType().FullName, exception.HResult,
            Parameter = (exception as ArgumentException)?.ParamName }
    });

    public static string Fingerprint(InputGroup group)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var key in Keys)
        {
            var prepared = new PreparedInteractiveInput(group.Tab, group.Mode);
            hash.AppendData(Encoding.UTF8.GetBytes(key.Name + "\n" + prepared.Snapshot() + "\n"));
            Exception? error = null;
            try { prepared.State.Handle(KeyInfo(key, group.Modifiers), 3); }
            catch (Exception exception) { error = exception; }
            hash.AppendData(Encoding.UTF8.GetBytes(prepared.Snapshot(error) + "\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static InputKey[] CreateKeys()
    {
        ConsoleKey[] keys = [ConsoleKey.F1, ConsoleKey.F2, ConsoleKey.F3, ConsoleKey.F4, ConsoleKey.F5, ConsoleKey.F6,
            ConsoleKey.Tab, ConsoleKey.Q, ConsoleKey.B, ConsoleKey.Escape, ConsoleKey.LeftArrow, ConsoleKey.RightArrow,
            ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.PageUp, ConsoleKey.PageDown, ConsoleKey.Home, ConsoleKey.End,
            ConsoleKey.Enter, ConsoleKey.Backspace, ConsoleKey.Spacebar, ConsoleKey.P, ConsoleKey.N, ConsoleKey.G,
            ConsoleKey.A, ConsoleKey.W, ConsoleKey.S, ConsoleKey.R, ConsoleKey.T, ConsoleKey.U,
            ConsoleKey.D1, ConsoleKey.D2, ConsoleKey.D3, ConsoleKey.D4, ConsoleKey.D5, ConsoleKey.D6,
            ConsoleKey.Delete, ConsoleKey.F12, ConsoleKey.Oem2, ConsoleKey.Oem4, ConsoleKey.Oem6];
        var result = keys.Select(key => new InputKey(key.ToString(), key, key switch
        {
            >= ConsoleKey.A and <= ConsoleKey.Z => (char)('a' + key - ConsoleKey.A),
            >= ConsoleKey.D1 and <= ConsoleKey.D6 => (char)('1' + key - ConsoleKey.D1),
            ConsoleKey.Tab => '\t', ConsoleKey.Escape => '\u001b', ConsoleKey.Enter => '\r', ConsoleKey.Backspace => '\b',
            ConsoleKey.Spacebar => ' ', ConsoleKey.Oem2 => '/', ConsoleKey.Oem4 => '[', ConsoleKey.Oem6 => ']', _ => '\0'
        })).ToList();
        result.AddRange([new("Question", ConsoleKey.Oem2, '?'), new("FunctionQuestion", ConsoleKey.F2, '?'),
            new("TabSlash", ConsoleKey.Tab, '/'), new("QuitBracket", ConsoleKey.Q, '['),
            new("SpaceDigit", ConsoleKey.Spacebar, '1'), new("RefreshDigit", ConsoleKey.R, '3'),
            new("WorkersSlash", ConsoleKey.W, '/'), new("DigitQuestion", ConsoleKey.D4, '?'),
            new("LetterControl", ConsoleKey.A, '\u0001')]);
        return result.ToArray();
    }
}
