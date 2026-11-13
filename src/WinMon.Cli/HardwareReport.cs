using Spectre.Console;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

/// <summary>Renders the shared privacy-projected inventory using the desktop's localized formatter.</summary>
internal static class HardwareReport
{
    internal static string Text(HardwareInventorySnapshot inventory) => Strings.Format(Strings.HardwareCollectionInterval,
        inventory.CollectionStartedUtc, inventory.CollectionCompletedUtc) + "\n\n" + string.Join("\n\n",
            WindowsHardwareInfo.Format(inventory).Select(section => section.Name + "\n" + section.Details +
                (section.Items is { Count: > 0 } items ? "\n\n" + section.DetailHeading + "\n" +
                    string.Join("\n\n", items.Select(item => item.Name + "\n" + item.Text)) : "")));

    internal static void Render(IAnsiConsole console, HardwareInventorySnapshot inventory) => console.Write(new Text(Safe(Text(inventory))));

    internal static TerminalTable Table(HardwareInventorySnapshot? inventory, bool loading)
    {
        if (inventory is null)
            return new TerminalTable([Strings.InfoHardware, Strings.Details], [],
                loading ? Strings.ReadingHardwareInformation : Strings.CliHardwareUnavailable);
        var rows = WindowsHardwareInfo.Format(inventory).Select(section =>
            new TerminalRow(Safe(section.Name), [Safe(section.Name), Safe(section.Details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? Strings.Unavailable)], Safe(Details(section)))).ToArray();
        return new TerminalTable([Strings.InfoHardware, Strings.Details], rows,
            Strings.Format(Strings.CliHardwareRefresh, inventory.CollectionCompletedUtc.ToLocalTime()));
    }

    private static string Details(WindowsHardwareInfo.Section section) => section.Name + "\n" + section.Details +
        (section.Items is { Count: > 0 } items ? "\n\n" + section.DetailHeading + "\n" +
            string.Join("\n\n", items.Select(item => item.Name + "\n" + item.Text)) : "");

    private static string Safe(string text) => string.Join('\n', text.Split('\n').Select(SnapshotReport.SafeText));
}
