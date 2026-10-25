using System.Windows.Controls;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;
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
        _allowClose = true;
        Close();
    }

    internal bool AllowClose
    {
        set => _allowClose = value;
    }

    internal bool CloseAfterServiceAction { get; set; }

    internal bool IsClosing { get; set; }

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
