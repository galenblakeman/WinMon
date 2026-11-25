using System.Runtime.CompilerServices;

namespace WinMon.Monitoring;

internal static class HardwareInfoReader
{
    private static ConditionalWeakTable<object, Func<IReadOnlyList<WindowsHardwareInfo.Section>>>? _overrides;

    internal static Func<IReadOnlyList<WindowsHardwareInfo.Section>> For(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var readers = Volatile.Read(ref _overrides);
        return readers is not null && readers.TryGetValue(owner, out var reader) ? reader : WindowsHardwareInfo.Read;
    }

    internal static IDisposable OverrideForTests(object owner, Func<IReadOnlyList<WindowsHardwareInfo.Section>> reader)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(reader);
        var readers = LazyInitializer.EnsureInitialized(ref _overrides);
        lock (readers)
        {
            readers.TryGetValue(owner, out var previous);
            readers.AddOrUpdate(owner, reader);
            return new RestoreScope(readers, owner, previous);
        }
    }

    private sealed class RestoreScope(ConditionalWeakTable<object, Func<IReadOnlyList<WindowsHardwareInfo.Section>>> readers,
        object owner, Func<IReadOnlyList<WindowsHardwareInfo.Section>>? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (readers)
            {
                if (previous is null) readers.Remove(owner);
                else readers.AddOrUpdate(owner, previous);
            }
        }
    }
}
