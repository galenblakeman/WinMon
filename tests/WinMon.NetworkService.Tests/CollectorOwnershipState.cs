using System.Collections;
using System.Reflection;
using WinMon.NetworkService;

namespace WinMon.Tests;

internal sealed class CollectorOwnershipState
{
    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly IDictionary _owners;
    private readonly IDictionary _rejected;

    internal CollectorOwnershipState(EtwNetworkCollector collector, DateTime started, long offset = 100,
        int lifetimeLimit = 8192, bool attachLedger = true)
    {
        Collector = collector;
        Ledger = new(System.Diagnostics.Stopwatch.Frequency, offset, lifetimeLimit);
        Set("_sessionStartedUtc", started);
        Set("_qpcOffset", offset);
        if (attachLedger) Set("_ledger", Ledger);
        _owners = (IDictionary)Get("_owners")!;
        _rejected = (IDictionary)Get("_rejectedUntil")!;
    }

    internal EtwNetworkCollector Collector { get; }
    internal NetworkTrafficLedger Ledger { get; }
    internal int OwnerCount => _owners.Count;
    internal int RejectedCount => _rejected.Count;
    internal int RateGap => (int)Get("_rateGapPending")!;

    internal CachedOwner? Cached(int pid)
    {
        var value = _owners[pid];
        if (value is null) return null;
        return new(Read<long>(value, "CreationTicks"), Read<long>(value, "StartedQpc"), Read<long>(value, "ValidThroughQpc"));
    }

    internal RejectedOwner? Rejected(int pid)
    {
        var value = _rejected[pid];
        if (value is null) return null;
        return new(Read<long>(value, "UntilQpc"), Read<bool>(value, "Unknown"), Read<long>(value, "OtherOwnerCreationTicks"));
    }

    internal void End(int pid, long timestamp)
    {
        Ledger.End(pid, timestamp);
        _owners.Remove(pid);
    }

    internal void Set(string name, object? value) => typeof(EtwNetworkCollector).GetField(name, Fields)!.SetValue(Collector, value);
    private object? Get(string name) => typeof(EtwNetworkCollector).GetField(name, Fields)!.GetValue(Collector);
    private static T Read<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;
}

internal sealed record CachedOwner(long CreationTicks, long StartedQpc, long ValidThroughQpc);
internal sealed record RejectedOwner(long UntilQpc, bool Unknown, long OtherOwnerCreationTicks);
