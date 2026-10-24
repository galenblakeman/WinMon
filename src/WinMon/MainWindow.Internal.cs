using WinMon.Monitoring;

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
}
