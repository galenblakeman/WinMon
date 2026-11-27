using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

/// <summary>Owns LibreHardwareMonitor CPU, board and GPU acquisition for one collector.</summary>
internal sealed partial class LibreHardwareMonitorProvider : ISensorProvider
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsMotherboardEnabled = true };
    private bool _opened;
    private bool _openAttempted;
    private bool _openFailed;
    private string? _hardwareId;
    private string? _sensorId;
    private string? _sensorName;

    public Task OpenAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_opened) return Task.CompletedTask;
        _hardwareId = null;
        _sensorId = null;
        _sensorName = null;
        _openAttempted = true;
        try
        {
            _computer.Open();
            _opened = true;
        }
        catch
        {
            // Computer.Close returns early when its internal open flag was never set. We must still attempt it,
            // but its public API cannot prove cleanup after a partial AddGroups failure.
            _openFailed = true;
            throw;
        }
        return Task.CompletedTask;
    }

    public Task<(SensorReading? Temperature, CpuPackagePowerReading? PackagePower)> ReadCpuAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_opened) throw new InvalidOperationException("CPU sensor provider is not open.");
        var cpuHardware = _computer.Hardware.Where(item => item.HardwareType == HardwareType.Cpu).ToArray();
        if (cpuHardware.Length != 1) return Task.FromResult<(SensorReading?, CpuPackagePowerReading?)>(default);

        var hardware = cpuHardware[0];
        var devices = Descendants(hardware).ToArray();
        var temperatures = devices.SelectMany(device => device.Sensors)
            .Where(sensor => sensor.SensorType == SensorType.Temperature).ToArray();
        // These are the Intel and AMD package power names in LibreHardwareMonitor 0.9.6.
        var powerSensors = hardware.Sensors.Where(sensor =>
            sensor.SensorType == SensorType.Power && sensor.Name is "CPU Package" or "Package").ToArray();
        var packageSensor = powerSensors.Length == 1 ? powerSensors[0] : null;

        // A retained Value alone is never enough: it may be from before a native failure.
        foreach (var sensor in temperatures)
        {
            sensor.ResetMin();
            sensor.ResetMax();
        }
        packageSensor?.ResetMin();
        packageSensor?.ResetMax();
        foreach (var device in devices) device.Update();

        CpuPackagePowerReading? packagePower = null;
        if (packageSensor is { Value: { } watts } &&
            watts is > 0 and <= SensorSnapshotValidation.MaximumCpuPackageWatts &&
            packageSensor.Min == watts && packageSensor.Max == watts)
            packagePower = new(watts, Stopwatch.GetTimestamp());

        var readings = new List<SensorReading>();
        AddReadings(temperatures, hardware.Identifier.ToString(), hardware.Name, readings);
        if (_sensorId is not null)
            return Task.FromResult((SensorSelection.SelectFixedCandidate(readings, _hardwareId!, _sensorId!, _sensorName!), packagePower));

        var selected = SensorSelection.SelectPackageCandidate(readings);
        if (selected is not null)
        {
            _hardwareId = selected.HardwareId;
            _sensorId = selected.SensorId;
            _sensorName = selected.SensorName;
        }
        return Task.FromResult((selected, packagePower));
    }

    private static IEnumerable<IHardware> Descendants(IHardware root)
    {
        yield return root;
        foreach (var child in root.SubHardware)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private static void AddReadings(IEnumerable<ISensor> sensors, string hardwareId, string hardwareName,
        ICollection<SensorReading> readings)
    {
        foreach (var sensor in sensors)
        {
            var celsius = SensorSelection.FreshCelsius(sensor.Value, sensor.Min, sensor.Max);
            readings.Add(new SensorReading(hardwareId, hardwareName, sensor.Identifier.ToString(), sensor.Name,
                celsius, "LibreHardwareMonitorLib 0.9.6", celsius is null ? 0 : Stopwatch.GetTimestamp(),
                celsius is null ? SensorReading.NotRefreshed : null));
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_openAttempted)
        {
            _computer.Close();
            _opened = false;
        }
        _hardwareId = null;
        _sensorId = null;
        _sensorName = null;
        _openAttempted = false;
        if (_openFailed)
        {
            _openFailed = false;
            throw new InvalidOperationException("LibreHardwareMonitor open failed; native cleanup cannot be proven.");
        }

        return ValueTask.CompletedTask;
    }
}
