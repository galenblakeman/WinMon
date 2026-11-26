using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

/// <summary>Reads CPU temperatures and board fan tachometers through LibreHardwareMonitor.</summary>
internal sealed class LibreHardwareMonitorProvider : ISensorProvider
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

    public Task<SensorReading?> ReadCpuTemperatureAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_opened) throw new InvalidOperationException("CPU sensor provider is not open.");
        var readings = new List<SensorReading>();
        var cpuHardware = _computer.Hardware.Where(item => item.HardwareType == HardwareType.Cpu).ToArray();
        if (cpuHardware.Length != 1) return Task.FromResult<SensorReading?>(null);
        foreach (var hardware in cpuHardware)
        {
            var devices = Descendants(hardware).ToArray();
            var sensors = devices.SelectMany(device => device.Sensors)
                .Where(sensor => sensor.SensorType == SensorType.Temperature).ToArray();
            // Resetting extrema before every pass gives public-API evidence that Update assigned the value this pass.
            // A retained Value alone is never enough: it may be from before a native failure.
            foreach (var sensor in sensors)
            {
                sensor.ResetMin();
                sensor.ResetMax();
            }

            foreach (var device in devices) device.Update();
            AddReadings(sensors, hardware.Identifier.ToString(), hardware.Name, readings);
        }

        if (_sensorId is not null)
            return Task.FromResult(SensorSelection.SelectFixedCandidate(readings, _hardwareId!, _sensorId, _sensorName!));

        var selected = SensorSelection.SelectPackageCandidate(readings);
        if (selected is not null)
        {
            _hardwareId = selected.HardwareId;
            _sensorId = selected.SensorId;
            _sensorName = selected.SensorName;
        }
        return Task.FromResult(selected);
    }

    public IReadOnlyList<HardwareFanReading> ReadFans(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_opened) return [];
        var boards = _computer.Hardware.Where(device => device.HardwareType == HardwareType.Motherboard);
        var result = new List<HardwareFanReading>();
        foreach (var device in boards.SelectMany(Descendants))
        {
            token.ThrowIfCancellationRequested();
            var sensors = device.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan).ToArray();
            // Update even without active sensors so providers can discover fan channels.
            foreach (var sensor in sensors)
            {
                sensor.ResetMin();
                sensor.ResetMax();
            }
            device.Update();
            foreach (var sensor in sensors)
            {
                var name = sensor.Name;
                var value = sensor.Value;
                if (value is not (>= 0 and <= 30000) || sensor.Min != value || sensor.Max != value ||
                    string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl)) continue;
                result.Add(new HardwareFanReading(name, value.Value,
                    name.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)));
                if (result.Count == SensorSnapshotValidation.MaximumFans) return result;
            }
        }
        return result;
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
                celsius is null ? "not-refreshed" : null));
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
