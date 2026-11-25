using System.Windows;
using Microsoft.Win32;
using MonCore.Monitoring;
using WinMon.Controls;

namespace WinMon.Monitoring;

internal interface ICsvExportOperations
{
    CsvExportChoice? Choose(CsvExportKind initial, string? usageNote, Window owner);
    bool? ShowSave(SaveFileDialog dialog, Window owner);
    void Write(string path, CsvExportKind kind, IReadOnlyList<MonitorSample> samples, IReadOnlyList<AppUsage> apps,
        DateTimeOffset since, long busyTicks, long notAttributedTicks, IReadOnlyList<ServiceFrame>? services,
        IReadOnlyList<ConnectionCapture>? connections, UsagePeriod? period);
}

internal static class CsvExportOperations
{
    // Preserve worker captures and allocate the scoped override slot only in tests.
    private static AsyncLocal<ICsvExportOperations?>? _overrides;
    private static ICsvExportOperations? Current => Volatile.Read(ref _overrides)?.Value;

    internal static CsvExportChoice? Choose(CsvExportKind initial, string? usageNote, Window owner)
    {
        var operations = Current;
        if (operations is not null) return operations.Choose(initial, usageNote, owner);
        var dialog = new CsvExportDialog(initial, usageNote) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Choice : null;
    }

    internal static bool? ShowSave(SaveFileDialog dialog, Window owner)
    {
        var operations = Current;
        return operations is null ? dialog.ShowDialog(owner) : operations.ShowSave(dialog, owner);
    }

    internal static void Write(string path, CsvExportKind kind, IReadOnlyList<MonitorSample> samples,
        IReadOnlyList<AppUsage> apps, DateTimeOffset since, long busyTicks, long notAttributedTicks,
        IReadOnlyList<ServiceFrame>? services, IReadOnlyList<ConnectionCapture>? connections, UsagePeriod? period)
    {
        var operations = Current;
        if (operations is null)
            CsvExport.Write(path, kind, samples, apps, since, busyTicks, notAttributedTicks, services, connections, period);
        else
            operations.Write(path, kind, samples, apps, since, busyTicks, notAttributedTicks, services, connections, period);
    }

    internal static IDisposable OverrideForTests(ICsvExportOperations operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var slot = LazyInitializer.EnsureInitialized(ref _overrides);
        var previous = slot.Value;
        slot.Value = operations;
        return new RestoreScope(slot, previous);
    }

    private sealed class RestoreScope(AsyncLocal<ICsvExportOperations?> slot, ICsvExportOperations? previous) : IDisposable
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
