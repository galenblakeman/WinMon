using System.ComponentModel;
using System.Diagnostics;

namespace WinMon.Monitoring;

internal enum EfficiencyModeState { Unavailable, Off, On, Mixed }

/// <summary>Validates one process handle immediately before a control operation.</summary>
internal static class ProcessController
{
    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped) =>
        ProcessPriorityChanger.IsEligible(process, live, grouped);

    internal static bool CanSuspend(ProcessUsage process, Func<int, IProcessControlTarget>? open = null,
        Func<NetworkServiceProcessStatus>? readService = null)
    {
        if (!SuspensionAllowed(process, readService)) return false;
        try
        {
            using var target = Open(process.ProcessId, WindowsProcessControlTarget.ProcessControlAccess.Suspend, open);
            return ValidationError(target, process) is null;
        }
        catch (Exception ex) when (IsControlError(ex)) { return false; }
    }

    internal static string? Suspend(ProcessUsage process, Func<int, IProcessControlTarget>? open = null,
        Func<NetworkServiceProcessStatus>? readService = null) =>
        !SuspensionAllowed(process, readService) ? Strings.SuspendProcessUnavailable :
        Apply(process, WindowsProcessControlTarget.ProcessControlAccess.Suspend, target => target.Suspend(), open);

    private static bool SuspensionAllowed(ProcessUsage process, Func<NetworkServiceProcessStatus>? readService)
    {
        if (!IsEligible(process, true, false) ||
            string.Equals(Path.GetFileNameWithoutExtension(process.Name), "dwm", StringComparison.OrdinalIgnoreCase)) return false;
        var service = (readService ?? NetworkServiceProcess.Read)();
        return !service.Unreadable && service.ProcessId != process.ProcessId;
    }

    internal static string? Resume(ProcessUsage process, Func<int, IProcessControlTarget>? open = null) =>
        Apply(process, WindowsProcessControlTarget.ProcessControlAccess.Suspend, target => target.Resume(), open, resume: true);

    internal static (ulong Process, ulong System, ushort Groups)? ReadAffinity(ProcessUsage process,
        Func<int, IProcessControlTarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return null;
        try
        {
            using var target = Open(process.ProcessId, WindowsProcessControlTarget.ProcessControlAccess.Affinity, open);
            if (ValidationError(target, process) is not null) return null;
            var groups = target.ProcessGroupCount;
            return groups == 1 ? (target.AffinityMask, target.SystemAffinityMask, groups) : (0UL, 0UL, groups);
        }
        catch (Exception ex) when (IsControlError(ex)) { return null; }
    }

    internal static string? SetAffinity(ProcessUsage process, ulong mask, Func<int, IProcessControlTarget>? open = null)
    {
        if (mask == 0) return Strings.CpuAffinityNone;
        return Apply(process, WindowsProcessControlTarget.ProcessControlAccess.Affinity, target =>
        {
            if (target.ProcessGroupCount != 1) throw new InvalidOperationException(Strings.CpuAffinityReadOnly);
            if ((mask & target.SystemAffinityMask) != mask) throw new InvalidOperationException(Strings.CpuAffinityUnavailable);
            target.SetAffinityMask(mask);
        }, open);
    }

    internal static EfficiencyModeState ReadEfficiency(ProcessUsage process, Func<int, IProcessControlTarget>? open = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299) || !IsEligible(process, true, false))
            return EfficiencyModeState.Unavailable;
        try
        {
            using var target = Open(process.ProcessId, WindowsProcessControlTarget.ProcessControlAccess.Efficiency, open);
            return ValidationError(target, process) is null ? ReadEfficiency(target) : EfficiencyModeState.Unavailable;
        }
        catch (Exception ex) when (IsControlError(ex)) { return EfficiencyModeState.Unavailable; }
    }

    internal static string? SetEfficiency(ProcessUsage process, bool enabled, Func<int, IProcessControlTarget>? open = null)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299) || !IsEligible(process, true, false))
            return Strings.EfficiencyModeUnavailable;
        try
        {
            using var target = Open(process.ProcessId, WindowsProcessControlTarget.ProcessControlAccess.Efficiency, open);
            if (ValidationError(target, process) is { } invalid) return invalid;
            var priorPower = target.PowerThrottling;
            _ = target.PriorityClass;
            var control = priorPower.ControlMask | ProcessPowerThrottlingState.ExecutionSpeed;
            var state = enabled ? priorPower.StateMask | ProcessPowerThrottlingState.ExecutionSpeed :
                priorPower.StateMask & ~ProcessPowerThrottlingState.ExecutionSpeed;
            try
            {
                target.SetPowerThrottling(new(control, state));
                try { target.SetPriorityClass(enabled ? ProcessPriorityClass.Idle : ProcessPriorityClass.Normal); }
                catch (Exception ex) when (IsControlError(ex))
                {
                    try { target.SetPowerThrottling(priorPower); }
                    catch (Exception rollback) when (IsControlError(rollback))
                    { return Strings.Format(Strings.EfficiencyModeRollbackFailure, ex.Message, rollback.Message); }
                    return Strings.Format(Strings.EfficiencyModePartialFailure, ex.Message);
                }
                return null;
            }
            finally { _ = ReadEfficiency(target); }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { return Strings.ProcessPriorityAccessDenied; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { return Strings.EfficiencyModeUnavailable; }
        catch (Exception ex) when (IsControlError(ex)) { return Strings.Format(Strings.EfficiencyModePartialFailure, ex.Message); }
    }

    private static string? Apply(ProcessUsage process, WindowsProcessControlTarget.ProcessControlAccess access,
        Action<IProcessControlTarget> apply, Func<int, IProcessControlTarget>? open, bool resume = false)
    {
        if (!IsEligible(process, true, false)) return Strings.SuspendProcessUnavailable;
        try
        {
            IProcessControlTarget target;
            try { target = Open(process.ProcessId, access, open); }
            catch (Win32Exception ex) when (resume && ex.NativeErrorCode == 87) { return null; }
            using (target)
            {
                if (resume && (target.CreationTicks != process.CreationTicks || target.HasExited)) return null;
                if (ValidationError(target, process) is { } error) return error;
                apply(target);
                return null;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5) { return Strings.ProcessPriorityAccessDenied; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { return Strings.ThisProcessHasAlreadyExitedOrIsNoLongerAvailable; }
        catch (Exception ex) when (IsControlError(ex)) { return ex.Message; }
    }

    internal static bool? HasExited(ProcessUsage process, Func<int, IProcessControlTarget>? open = null)
    {
        IProcessControlTarget target;
        try { target = Open(process.ProcessId, WindowsProcessControlTarget.ProcessControlAccess.Inspect, open); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87) { return true; }
        catch (Exception ex) when (IsControlError(ex)) { return null; }
        using (target)
        {
            try { return target.CreationTicks != process.CreationTicks || target.HasExited; }
            catch (Exception ex) when (IsControlError(ex)) { return null; }
        }
    }

    private static EfficiencyModeState ReadEfficiency(IProcessControlTarget target)
    {
        try
        {
            var power = target.PowerThrottling;
            var priority = target.PriorityClass;
            var explicitSpeed = (power.ControlMask & ProcessPowerThrottlingState.ExecutionSpeed) != 0;
            var speed = (power.StateMask & ProcessPowerThrottlingState.ExecutionSpeed) != 0;
            return explicitSpeed && speed && priority == ProcessPriorityClass.Idle ? EfficiencyModeState.On :
                explicitSpeed && !speed && priority == ProcessPriorityClass.Normal ? EfficiencyModeState.Off : EfficiencyModeState.Mixed;
        }
        catch (Exception ex) when (IsControlError(ex)) { return EfficiencyModeState.Unavailable; }
    }

    private static string? ValidationError(IProcessControlTarget target, ProcessUsage process) =>
        target.CreationTicks != process.CreationTicks ? Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen :
        target.HasExited ? Strings.ThisProcessHasAlreadyExited :
        target.IsCritical ? Strings.WindowsIdentifiesThisAsACriticalProcessWinMonWillNot : null;

    private static bool IsControlError(Exception ex) => ex is Win32Exception or UnauthorizedAccessException or InvalidOperationException;
    private static IProcessControlTarget Open(int id, WindowsProcessControlTarget.ProcessControlAccess access,
        Func<int, IProcessControlTarget>? open) => open is null ? WindowsProcessControlTarget.Open(id, access) : open(id);
}
