using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

/// <summary>
/// Isolates the native library. One worker owns an instance for its entire open/update/close lifetime.
/// </summary>
internal interface ISensorProvider : IAsyncDisposable
{
    Task OpenAsync(CancellationToken token);
    Task<(SensorReading? Temperature, CpuPackagePowerReading? PackagePower)> ReadCpuAsync(CancellationToken token);
    PeripheralSensorPass ReadPeripherals(CancellationToken token) => default;
}

internal readonly record struct PeripheralSensorPass(IReadOnlyList<HardwareFanReading>? Fans,
    IReadOnlyList<HardwareSensorReading>? Sensors);
