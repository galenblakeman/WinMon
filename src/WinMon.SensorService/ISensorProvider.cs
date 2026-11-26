namespace WinMon.SensorService;

/// <summary>
/// Isolates the native library. One worker owns an instance for its entire open/update/close lifetime.
/// </summary>
internal interface ISensorProvider : IAsyncDisposable
{
    Task OpenAsync(CancellationToken token);
    Task<SensorReading?> ReadCpuTemperatureAsync(CancellationToken token);
    IReadOnlyList<MonCore.Monitoring.HardwareFanReading> ReadFans(CancellationToken token) => [];
}
