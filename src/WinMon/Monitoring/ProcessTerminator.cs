using System.ComponentModel;

namespace WinMon.Monitoring;

/// <summary>Ends only the confirmed process lifetime, using one handle for validation and termination.</summary>
internal static class ProcessTerminator
{
    internal interface ITarget : IProcessTerminationTarget { }

    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped)
    {
        return live && !grouped && process is { MemberCount: 1, ProcessId: > 4 } &&
               process.ProcessId != Environment.ProcessId && process.CreationTicks > 0;
    }

    /// <summary>Capture exact live lifetimes before confirmation, including idle group members.</summary>
    internal static ProcessUsage[] CaptureTargets(ProcessUsage row, bool live, bool grouped,
        IReadOnlyList<ProcessUsage> current)
    {
        if (!live) return [];
        if (!grouped) return IsEligible(row, true, false) ? [row] : [];
        var key = ProcessRanking.GroupKey(row);
        var targets = current.Where(p => string.Equals(ProcessRanking.GroupKey(p), key,
                StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => (p.ProcessId, p.CreationTicks)).OrderBy(p => p.ProcessId).ToArray();
        // Never silently omit a protected or unidentified member from an all-process request.
        return targets.All(p => IsEligible(p, true, false)) ? targets : [];
    }

    internal static IReadOnlyList<string> EndAll(IReadOnlyList<ProcessUsage> targets, Func<int, ITarget>? open = null)
    {
        var errors = new List<string>();
        // Termination is a side effect, not a projection.
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var target in targets)
        {
            var error = End(target, open);
            if (error is not null) errors.Add($"{target.Name} (PID {target.ProcessId}): {error}");
        }

        return errors;
    }

    // Return null only when Windows accepted the termination request. It can complete asynchronously.
    internal static string? End(ProcessUsage process, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return Strings.ThisProcessCannotBeEndedFromWinMon;
        try
        {
            using var target = (open is null ? WindowsProcessTerminator.Open(process.ProcessId) : open(process.ProcessId));
            if (target.CreationTicks != process.CreationTicks)
                return Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen;
            if (target.HasExited) return Strings.ThisProcessHasAlreadyExited;
            if (target.IsCritical) return Strings.WindowsIdentifiesThisAsACriticalProcessWinMonWillNot;
            target.Terminate();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Strings.AccessDeniedWindowsDidNotAllowWinMonToEndThis;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
        {
            return Strings.ThisProcessHasAlreadyExitedOrIsNoLongerAvailable;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return Strings.Format(Strings.TheProcessCouldNotBeEnded, ex.Message);
        }
    }

}
