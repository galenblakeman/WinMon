using System.Diagnostics;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;

namespace WinMon.SensorService;

internal sealed partial class LibreHardwareMonitorProvider
{
    private static readonly long DeviceRefreshInterval = 30 * Stopwatch.Frequency;
    private bool _gpuInitializationAttempted;
    private long _nextStorageRead;
    private IReadOnlyList<HardwareSensorReading> _storageReadings = [];

    public PeripheralSensorPass ReadPeripherals(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_opened) return default;
        var fans = new List<HardwareFanReading>();
        var sensors = new List<HardwareSensorReading>();

        if (!_gpuInitializationAttempted)
        {
            _gpuInitializationAttempted = true;
            TryRead(() => _computer.IsGpuEnabled = true, "GPU initialization");
        }

        var hardware = _computer.Hardware;
        TryRead(() => ReadMotherboards(hardware, fans, sensors, token), "motherboard");
        TryRead(() => ReadGpus(hardware, sensors, token), "GPU");

        var now = Stopwatch.GetTimestamp();
        if (now >= _nextStorageRead)
        {
            _nextStorageRead = now + DeviceRefreshInterval;
            _storageReadings = [];
            TryRead(() => _storageReadings = WindowsNvmeHealth.ReadFixedVolumes(token), "NVMe");
        }
        sensors.AddRange(_storageReadings.Take(SensorSnapshotValidation.MaximumHardwareSensors - sensors.Count));
        return new(fans, sensors);
    }

    private static void TryRead(Action read, string category)
    {
        try { read(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceWarning("WinMon {0} sensor acquisition failed: {1}", category, ex.Message);
        }
    }

    private static void ReadMotherboards(IEnumerable<IHardware> hardware, List<HardwareFanReading> fans,
        List<HardwareSensorReading> readings, CancellationToken token)
    {
        const int maximumPerKind = 8;
        var temperatures = 0;
        var voltages = 0;
        foreach (var device in hardware.Where(device => device.HardwareType == HardwareType.Motherboard).SelectMany(Descendants))
        {
            token.ThrowIfCancellationRequested();
            var sensors = device.Sensors.Where(sensor => sensor.SensorType == SensorType.Fan ||
                !sensor.IsDefaultHidden && sensor.SensorType is SensorType.Temperature or SensorType.Voltage).ToArray();
            Update(device, sensors);
            var now = Stopwatch.GetTimestamp();
            foreach (var sensor in sensors)
            {
                if (FreshValue(sensor) is not { } value) continue;
                var name = sensor.Name;
                if (sensor.SensorType == SensorType.Fan)
                {
                    if (fans.Count < SensorSnapshotValidation.MaximumFans && value is >= 0 and <= 30000 &&
                        !string.IsNullOrWhiteSpace(name) && name.Length <= 256 && !name.Any(char.IsControl))
                        fans.Add(new(name, value, name.StartsWith("CPU", StringComparison.OrdinalIgnoreCase)));
                }
                else if (sensor.SensorType == SensorType.Temperature && temperatures < maximumPerKind)
                {
                    if (Add(readings, new(HardwareSensorKind.BoardTemperature, null, name, value, now))) temperatures++;
                }
                else if (sensor.SensorType == SensorType.Voltage && voltages < maximumPerKind)
                {
                    if (Add(readings, new(HardwareSensorKind.BoardVoltage, null, name, value, now))) voltages++;
                }
            }
        }
    }

    private static void ReadGpus(IEnumerable<IHardware> hardware, List<HardwareSensorReading> readings, CancellationToken token)
    {
        var devices = hardware.OfType<GenericGpu>()
            .Where(gpu => !string.IsNullOrWhiteSpace(gpu.DeviceId))
            .GroupBy(gpu => gpu.DeviceId, StringComparer.OrdinalIgnoreCase);
        foreach (var group in devices)
        {
            token.ThrowIfCancellationRequested();
            if (group.Count() != 1) continue;
            var device = group.Single();
            var sensors = device.Sensors.Select(sensor => (Sensor: sensor, Kind: GpuKind(sensor)))
                .Where(item => item.Kind.HasValue).ToArray();
            Update(device, sensors.Select(item => item.Sensor));
            var readAt = Stopwatch.GetTimestamp();
            foreach (var (sensor, kind) in sensors)
                if (FreshValue(sensor) is { } value)
                    Add(readings, new(kind!.Value, group.Key, sensor.Name, value, readAt));
        }
    }

    private static HardwareSensorKind? GpuKind(ISensor sensor) => sensor.SensorType switch
    {
        SensorType.Temperature when sensor.Name is "GPU Hot Spot" or "GPU Memory Junction" or "GPU Memory" =>
            HardwareSensorKind.GpuTemperature,
        SensorType.Power when sensor.Name is "GPU Package" or "GPU Power" => HardwareSensorKind.GpuPower,
        SensorType.Fan => HardwareSensorKind.GpuFan,
        _ => null
    };

    private static void Update(IHardware device, IEnumerable<ISensor> sensors)
    {
        foreach (var sensor in sensors)
        {
            sensor.ResetMin();
            sensor.ResetMax();
        }
        device.Update();
    }

    private static float? FreshValue(ISensor sensor) => sensor.Value is { } value &&
        sensor.Min == value && sensor.Max == value ? value : null;

    private static bool Add(List<HardwareSensorReading> readings, HardwareSensorReading sensor)
    {
        if (readings.Count >= SensorSnapshotValidation.MaximumHardwareSensors ||
            !SensorSnapshotValidation.IsValidHardwareSensor(sensor, sensor.LastReadAt)) return false;
        readings.Add(sensor);
        return true;
    }
}
