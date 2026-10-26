using System.ComponentModel;
using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Reads or changes the priority of one confirmed process lifetime.</summary>
internal static class ProcessPriorityChanger
{
    internal interface ITarget : IProcessPriorityTarget { }

    internal static readonly ProcessPriorityClass[] Choices =
    [
        ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal,
        ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal
    ];

    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped)
    {
        return ProcessTerminator.IsEligible(process, live, grouped) && process.MemberIds.Count == 0;
    }

    internal static ProcessPriorityClass? Read(ProcessUsage process, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return null;
        try
        {
            using var target = (open is null ? WindowsProcessPriorityTarget.Open(process.ProcessId) : open(process.ProcessId));
            return Valid(target, process) ? target.PriorityClass : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? Set(ProcessUsage process, ProcessPriorityClass priority, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false) || !Choices.Contains(priority))
            return Strings.ProcessPriorityUnavailable;
        try
        {
            using var target = (open is null ? WindowsProcessPriorityTarget.Open(process.ProcessId) : open(process.ProcessId));
            if (target.CreationTicks != process.CreationTicks)
                return Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen;
            if (target.HasExited) return Strings.ThisProcessHasAlreadyExited;
            if (target.IsCritical) return Strings.WindowsIdentifiesThisAsACriticalProcessWinMonWillNot;
            target.SetPriorityClass(priority);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Strings.ProcessPriorityAccessDenied;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
        {
            return Strings.ThisProcessHasAlreadyExitedOrIsNoLongerAvailable;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return Strings.Format(Strings.ProcessPriorityFailed, ex.Message);
        }
    }

    private static bool Valid(IProcessPriorityTarget target, ProcessUsage process)
    {
        return target.CreationTicks == process.CreationTicks && target is { HasExited: false, IsCritical: false };
    }

}
