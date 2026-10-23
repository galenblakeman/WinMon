using System.Diagnostics;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal static class ProcessPriorityMenu
{
    internal static MenuItem Create(ProcessUsage process, bool eligible,
        Func<ProcessUsage, ProcessPriorityClass?> read,
        Action<ProcessUsage, ProcessPriorityClass> set)
    {
        var menu = new MenuItem { Header = Strings.SetPriority };
        var current = eligible ? read(process) : null;
        menu.IsEnabled = current is not null;
        if (current is null) menu.ToolTip = Strings.ProcessPriorityUnavailable;
        foreach (var priority in ProcessPriorityChanger.Choices)
        {
            var choice = new MenuItem
            {
                Header = priority switch
                {
                    ProcessPriorityClass.Idle => Strings.PriorityLow,
                    ProcessPriorityClass.BelowNormal => Strings.PriorityBelowNormal,
                    ProcessPriorityClass.Normal => Strings.PriorityNormal,
                    _ => Strings.PriorityAboveNormal
                },
                IsCheckable = true,
                IsChecked = current == priority,
                IsEnabled = current != priority
            };
            choice.Click += (_, _) => set(process, priority);
            menu.Items.Add(choice);
        }

        return menu;
    }
}
