namespace WinMon.Monitoring;

/// <summary>Capture a system and process CPU timing observation.</summary>
internal interface ICpuSnapshotProvider
{
    /// <summary>Read current native timing values.</summary>
    /// <returns>The collected observation.</returns>
    CpuSnapshot Capture();
}