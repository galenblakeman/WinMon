using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WinMon.Monitoring;

internal interface IServiceActionOperations
{
    Task ExecuteAsync(string name, string action, int processId, long creationTicks, CancellationToken cancellationToken);
    Process? Start(ProcessStartInfo start);
}

internal static class ServiceActionOperations
{
    // Keep the default worker closure unchanged and allocate override state only in tests.
    private static AsyncLocal<IServiceActionOperations?>? _overrides;
    private static IServiceActionOperations? Current => Volatile.Read(ref _overrides)?.Value;

    internal static Task ExecuteAsync(string name, string action, int processId, long creationTicks,
        CancellationToken cancellationToken)
    {
        var operations = Current;
        return operations is null
            ? WindowsServices.ExecuteAsync(name, action, processId, creationTicks, cancellationToken)
            : operations.ExecuteAsync(name, action, processId, creationTicks, cancellationToken);
    }

    internal static Process? Start(ProcessStartInfo start)
    {
        var operations = Current;
        return operations is null ? Process.Start(start) : operations.Start(start);
    }

    internal static IDisposable OverrideForTests(IServiceActionOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var slot = LazyInitializer.EnsureInitialized(ref _overrides);
        var previous = slot.Value;
        slot.Value = operations;
        return new RestoreScope(slot, previous);
    }

    private sealed class RestoreScope(AsyncLocal<IServiceActionOperations?> slot, IServiceActionOperations? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            slot.Value = previous;
            _disposed = true;
        }
    }
}
