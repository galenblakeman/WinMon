using System.Windows.Controls;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using Forms = System.Windows.Forms;

namespace WinMon;

/// <summary>
/// Members that diagnostics and <c>tools/WinMon.Performance</c> drive the window through, instead of its private
/// fields. Add a named member here when a fixture needs more; never reach the window by reflection.
/// </summary>
public partial class MainWindow
{
    /// <summary>The historical sample being inspected, or null for the live view. Set without refreshing.</summary>
    internal MonitorSample? SelectedSample
    {
        get => _selectedSample;
        set => _selectedSample = value;
    }

    /// <summary>End the window lifetime and wait for the capture loop, so a fixture can record its own samples.</summary>
    internal Task StopForFixtureAsync()
    {
        _lifetime.Cancel();
        return _samplingTask?.ContinueWith(_ => { }, TaskScheduler.Default) ?? Task.CompletedTask;
    }

    /// <summary>Close without the shutdown sequence, for a fixture that never saved preferences or started updates.</summary>
    internal void CloseForFixture()
    {
        _allowClose = true;
        Close();
    }

    internal bool AllowClose
    {
        get => _allowClose;
        set => _allowClose = value;
    }

    internal bool CloseAfterServiceAction
    {
        get => _closeAfterServiceAction;
        set => _closeAfterServiceAction = value;
    }

    internal bool IsClosing
    {
        get => _closing;
        set => _closing = value;
    }

    internal bool CompactMode
    {
        get => _compactMode;
        set => _compactMode = value;
    }

    internal bool InlinePinToolbar
    {
        get => _inlinePinToolbar;
        set => _inlinePinToolbar = value;
    }

    internal bool InlineProcessToolbar
    {
        get => _inlineProcessToolbar;
        set => _inlineProcessToolbar = value;
    }

    internal CancellationTokenSource Lifetime => _lifetime;

    internal MonitorPreferences Preferences
    {
        get => _preferences;
        set => _preferences = value;
    }

    internal PreferenceStore Store => _preferenceStore;

    internal MonitorSample? PresentedSample
    {
        get => _presentedSample;
        set => _presentedSample = value;
    }

    internal SamplingError? ShownSamplingError
    {
        get => _samplingError;
        set => _samplingError = value;
    }

    internal Task? SamplingTask
    {
        get => _samplingTask;
        set => _samplingTask = value;
    }

    internal bool ServiceActionBusy
    {
        get => _serviceActionBusy;
        set => _serviceActionBusy = value;
    }

    internal DispatcherTimer SnapshotCopiedTimer => _snapshotCopiedTimer;

    internal SnapshotReport? SnapshotReport
    {
        get => _snapshotReport;
        set => _snapshotReport = value;
    }

    internal List<(MetricReadout Source, Border Box, CpuGraph Graph, TextBlock Label, TextBlock Value, TextBlock Detail)> StripTiles => _stripTiles;

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
