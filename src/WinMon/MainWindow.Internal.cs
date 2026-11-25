using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Preferences;
using Forms = System.Windows.Forms;

namespace WinMon;

// Fixture-facing properties intentionally forward to fields shared with the live window code.
// ReSharper disable ConvertToAutoProperty
/// <summary>
/// Members that diagnostics and <c>tools/WinMon.Performance</c> drive the window through, instead of its private
/// fields. Add a named member here when a fixture needs more; never reach the window by reflection.
/// </summary>
public partial class MainWindow
{
    /// <summary>Dispatch a diagnostic key with observed modifiers; the event binding retains native reads.</summary>
    internal void DispatchWindowKeyForFixture(KeyEventArgs e, ModifierKeys modifiers) => HandleWindowKeyDown(e, modifiers);

    /// <summary>Optional synthetic Windows event source for diagnostics; null uses the Windows reader.</summary>
    internal Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<IncidentEventResult>>? FlightRecorderEventReader { get; set; }

    internal UsageArchive? DailyUsageArchive { get => _usageArchive; set => _usageArchive = value; }
    internal Task LoadUsageArchiveForFixtureAsync() => LoadUsageArchiveAsync();
    internal Task SaveUsageArchiveForFixtureAsync() => SaveUsageArchiveAsync();
    internal ResourceTrendArchive ResourceTrendsForFixture => _resourceTrends;
    internal Func<string, CancellationToken, Task<ResourceTrendArchive>>? ResourceTrendLoaderForFixture { get; set; }
    internal string? ResourceTrendNoticeForFixture => ResourceTrendNotice;
    internal Task LoadResourceTrendsForFixtureAsync() => LoadResourceTrendsAsync();
    internal Task SaveResourceTrendsForFixtureAsync() => SaveResourceTrendsAsync();
    internal Task ClearResourceTrendsForFixtureAsync() => ClearResourceTrendsAsync();
    internal void RecordResourceTrendForFixture(MonitorSample sample) => RecordResourceTrend(sample);
    internal void SetResourceTrendWriteForFixture(Task write) => _resourceTrendWrite = write;
    internal void SetConnectionsForFixture(NetworkConnectionsFrame frame)
    {
        _connections = frame;
        RefreshConnections();
    }

    internal void SetProcessNetworkForFixture(ProcessNetworkFrame? frame)
    {
        if (!_diagnosticMode) throw new InvalidOperationException("Synthetic traffic requires diagnostic mode.");
        _networkForFixture = frame;
        RefreshConnections();
    }

    internal void RememberWindowPositionForFixture()
    {
        RememberWindowPosition();
    }

    internal void NotifyDisplayChangeForFixture()
    {
        OnDisplaySettingsChanged(null, EventArgs.Empty);
    }

    /// <summary>The historical sample being inspected, or null for the live view. Set without refreshing.</summary>
    internal MonitorSample? SelectedSample { get; set; }

    /// <summary>End the window lifetime and wait for the capture loop, so a fixture can record its own samples.</summary>
    // ReSharper disable once UnusedMember.Global -- used by tools/WinMon.Performance, outside this solution.
    internal Task StopForFixtureAsync()
    {
        Lifetime.Cancel();
        return SamplingTask?.ContinueWith(_ => { }, TaskScheduler.Default) ?? Task.CompletedTask;
    }

    /// <summary>Close without the shutdown sequence, for a fixture that never saved preferences or started updates.</summary>
    internal void CloseForFixture()
    {
        _usageArchiveLock?.Dispose();
        ReleaseResourceTrendLock();
        _allowClose = true;
        Close();
    }

    internal bool AllowClose
    {
        set => _allowClose = value;
    }

    internal bool CloseAfterServiceAction { get; set; }

    internal bool IsClosing { get; set; }

    /// <summary>Supply a delayed report write so diagnostics can check shutdown waits for it.</summary>
    internal Task WriteCaptureForFixtureAsync(CaptureMarker marker, Task<string> write)
    {
        return WriteCaptureAsync(marker, "", write);
    }

    /// <summary>Seed a diagnostic capture without starting a report write or notification.</summary>
    internal void AddCaptureMarkerForFixture(CaptureMarker marker) => _captureMarkers.Add(marker);

    internal bool CompactMode { get; set; }

    internal bool InlinePinToolbar { get; set; }

    internal bool InlineProcessToolbar
    {
        get => _inlineProcessToolbar;
        set => _inlineProcessToolbar = value;
    }

    internal CancellationTokenSource Lifetime { get; } = new();

    internal MonitorPreferences Preferences
    {
        get => _preferences;
        set => _preferences = value;
    }

    internal PreferenceStore Store => _preferenceStore;

    internal MonitorSample? PresentedSample => _presentedSample;

    internal SamplingError? ShownSamplingError
    {
        get => _samplingError;
        set => _samplingError = value;
    }

    internal Task? SamplingTask { get; private set; }

    internal bool ServiceActionBusy
    {
        set => _serviceActionBusy = value;
    }

    internal DispatcherTimer SnapshotCopiedTimer => _snapshotCopiedTimer;

    internal SnapshotReport? SnapshotReport => _snapshotReport;

    internal List<(MetricReadout Source, Border Box, MetricReadout Readout)> CompactTiles => _compactTiles;

    internal Forms.NotifyIcon? Tray
    {
        get => _tray;
        set => _tray = value;
    }

    internal bool Wide
    {
        get => _wide;
        set => _wide = value;
    }
}
