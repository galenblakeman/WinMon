using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private bool _resolvingSuspensions;

    private bool ResolveSuspendedProcessesBeforeClose()
    {
        if (_resolvingSuspensions) return false;
        _resolvingSuspensions = true;
        try
        {
            var resolved = ResolveSuspensionsBeforeClose(Processes.SuspendedProcesses, () =>
            {
                var dialog = new SuspendedProcessCloseDialog { Owner = this };
                _ = dialog.ShowDialog();
                return dialog.Choice;
            }, failures => MessageDialog.ShowNotice(this,
                Strings.Format(Strings.ResumeFailedProcesses, string.Join("\n", failures)),
                Strings.ResumeSuspendedProcesses));
            if (!resolved) AbortPendingUpdate();
            return resolved;
        }
        finally { _resolvingSuspensions = false; }
    }

    internal static bool ResolveSuspensionsBeforeClose(SuspendedProcessTracker tracker,
        Func<SuspendedCloseChoice> choose, Action<IReadOnlyList<string>> report,
        Func<int, IProcessControlTarget>? open = null)
    {
        tracker.RemoveExited(open);
        var pending = tracker.Pending;
        if (pending.Count == 0) return true;
        switch (choose())
        {
            case SuspendedCloseChoice.Leave:
                tracker.LeaveSuspended();
                return true;
            case SuspendedCloseChoice.Resume: break;
            default: return false;
        }
        var failures = new List<string>();
        foreach (var target in pending)
        {
            var process = new ProcessUsage(target.ProcessId, target.CreationTicks, "", 0);
            if (tracker.Resume(process, open) is { } error)
                failures.Add($"{Strings.Format(Strings.PID, target.ProcessId)}: {error}");
        }
        if (failures.Count == 0) return true;
        report(failures);
        return false;
    }
}
