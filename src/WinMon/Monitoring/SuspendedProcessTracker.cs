using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.Monitoring;

/// <summary>Serializes one outstanding balanced Suspend/Resume request per process lifetime.</summary>
internal sealed class SuspendedProcessTracker
{
    private readonly object _gate = new();
    private readonly HashSet<(int ProcessId, long CreationTicks)> _pending = [];
    internal bool Contains(ProcessUsage process)
    {
        lock (_gate) return _pending.Contains((process.ProcessId, process.CreationTicks));
    }
    internal string? Suspend(ProcessUsage process, Func<int, IProcessControlTarget>? open = null,
        Func<NetworkServiceProcessStatus>? readService = null)
    {
        lock (_gate)
        {
            if (Contains(process)) return Strings.SuspendProcessUnavailable;
            if (ProcessController.Suspend(process, open, readService) is { } error) return error;
            _pending.Add((process.ProcessId, process.CreationTicks));
            return null;
        }
    }
    internal string? Resume(ProcessUsage process, Func<int, IProcessControlTarget>? open = null)
    {
        lock (_gate)
        {
            if (!Contains(process)) return Strings.ResumeProcessUnavailable;
            var error = ProcessController.Resume(process, open);
            if (error is null) _pending.Remove((process.ProcessId, process.CreationTicks));
            return error;
        }
    }
    internal void RemoveExited(Func<int, IProcessControlTarget>? open = null)
    {
        lock (_gate)
        {
            foreach (var identity in _pending.ToArray())
                if (ProcessController.HasExited(new(identity.ProcessId, identity.CreationTicks, "", 0), open) == true)
                    _pending.Remove(identity);
        }
    }
    internal IReadOnlyList<(int ProcessId, long CreationTicks)> Pending { get { lock (_gate) return _pending.Order().ToArray(); } }
    internal void LeaveSuspended() { lock (_gate) _pending.Clear(); }
}
