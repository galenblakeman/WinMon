using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Controls;
using MonCore.Monitoring;
using Path = System.Windows.Shapes.Path;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

internal sealed class PreparedProcessMenu
{
    internal static readonly string[] Cases = ["minimal", "full", "control", "owned", "observed", "unknown-suspension",
        "suspend-denied", "efficiency-on", "efficiency-mixed", "efficiency-unavailable", "grouped", "unavailable",
        "historical", "ineligible", "pinned", "navigation-unbound"];

    internal static PreparedProcessMenu Create(string name)
    {
        if (!Cases.Contains(name)) throw new ArgumentException(name);
        var fixture = new PreparedProcessMenu(name == "grouped", name == "unavailable");
        if (name == "minimal") return fixture;
        if (name == "full") { fixture.Inspect(); fixture.Pin(); fixture.Priority(); }
        if (name == "historical") { fixture.Historical = true; fixture.Inspect(false); }
        if (name == "pinned") { fixture.Pinned = true; fixture.Pin(); }
        if (name == "navigation-unbound") fixture.Inspect(true, false);
        if (name == "grouped") fixture.Refresh(Original with { MemberIds = [8123, 9000], MemberCount = 2 }, true);
        fixture.Owned = name == "owned";
        fixture.Observed = name == "unknown-suspension" ? null : name == "observed";
        fixture.CanSuspend = name != "suspend-denied";
        fixture.CanControl = name != "ineligible";
        fixture.Efficiency = name switch
        {
            "efficiency-on" => EfficiencyModeState.On,
            "efficiency-mixed" => EfficiencyModeState.Mixed,
            "efficiency-unavailable" => EfficiencyModeState.Unavailable,
            _ => EfficiencyModeState.Off
        };
        fixture.Controls();
        return fixture;
    }

    internal static readonly ProcessUsage Original = new(8123, 100, "example.exe", 10);
    internal static readonly MonitorSample Sample = new(DateTimeOffset.UnixEpoch.AddDays(1), 20, [Original], 0);
    internal ProcessRow Row { get; } = new();
    internal List<string> Calls { get; } = [];
    internal List<(string Action, ProcessDetailsSnapshot Snapshot)> Actions { get; } = [];
    internal List<(string Action, ProcessUsage Process)> Targets { get; } = [];
    internal bool Historical, Owned, Pinned;
    internal bool CanControl = true, CanSuspend = true, CanEnd = true, CanDump = true;
    internal bool? Observed = false;
    internal EfficiencyModeState Efficiency = EfficiencyModeState.Off;
    internal string? ThrowAt = null;

    internal PreparedProcessMenu(bool grouped = false, bool unavailable = false)
    {
        Row.EnableProcessActions(p => Read("end", CanEnd), p => Targets.Add(("end", p)));
        Refresh(Original, grouped, unavailable);
    }

    internal void Refresh(ProcessUsage process, bool grouped = false, bool unavailable = false) =>
        Row.Update(process, [Sample], 120, false, null, grouped,
            unavailable ? PinnedProcessState.Exited : PinnedProcessState.Present);

    internal void Inspect(bool navigation = true, bool navigationPredicate = true)
    {
        Row.EnableInspectionActions(s => Actions.Add(("properties", s)), s => Actions.Add(("copy", s)),
            p => Targets.Add(("location", p)), p => Targets.Add(("dump", p)),
            p => Read("dump", CanDump), () => Read("history", Historical),
            navigation ? (action, snapshot) => Actions.Add((action, snapshot)) : null,
            navigationPredicate ? (action, _) => Read(action, action == "usage") : null);
    }

    internal void Pin() => Row.EnablePinning((_, _) => Read("pin", Pinned),
        (p, grouped) => Targets.Add((grouped ? "pin-group" : "pin-process", p)));

    internal void Priority() => Row.EnablePriorityActions(p => Read("priority", true),
        p => Read("read-priority", ProcessPriorityClass.Normal),
        (p, value) => Targets.Add(("priority:" + value, p)));

    internal void Controls() => Row.EnableControlActions(p => Read("control", CanControl),
        p => Read("can-suspend", CanSuspend), p => Read("owned", Owned),
        p => Read("observed", Observed), p => Read("efficiency", Efficiency),
        (action, snapshot) => Actions.Add((action, snapshot)));

    private T Read<T>(string name, T value)
    {
        Calls.Add(name);
        if (ThrowAt == name) throw new InvalidOperationException(name);
        return value;
    }

    internal bool Prepare()
    {
        Calls.Clear();
        return Row.PrepareProcessMenu();
    }

    internal MenuItem Item(string header) => Row.ContextMenu!.Items.OfType<MenuItem>()
        .Single(item => Equals(item.Header, header));

    internal string Snapshot() => JsonSerializer.Serialize(new
    {
        Items = Row.ContextMenu!.Items.Cast<object>().Select(Describe).ToArray(),
        Calls = Calls.ToArray(),
        Actions = Actions.Select(a => new { a.Action, a.Snapshot }).ToArray(),
        Targets = Targets.Select(t => new { t.Action, t.Process }).ToArray()
    });

    private static object Describe(object item) => item is not MenuItem menu ? new { Separator = true } : (object)new
    {
        Header = menu.Header?.ToString(), menu.IsEnabled, menu.IsCheckable, menu.IsChecked,
        ToolTip = menu.ToolTip?.ToString(),
        Icon = menu.Icon is Path path ? path.Data.ToString(CultureInfo.InvariantCulture) : null,
        Children = menu.Items.Cast<object>().Select(Describe).ToArray()
    };
}
