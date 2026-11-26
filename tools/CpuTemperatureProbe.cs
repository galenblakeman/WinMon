// CPU-only LibreHardwareMonitor spike for the optional, separate sensor service.
// Install official PawnIO separately; this tool never runs an installer.
// dotnet run --project tools/CpuTemperatureProbe -- --samples 6
using System.Globalization;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;

if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
{
    Console.Error.WriteLine("CpuTemperatureProbe requires 64-bit Windows.");
    return 2;
}
var count = 6;
if (args.Length != 0 && (args.Length != 2 || args[0] != "--samples" ||
    !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count is < 1 or > 600))
{
    Console.Error.WriteLine("Usage: CpuTemperatureProbe [--samples <1..600>]");
    return 2;
}
Console.Error.WriteLine($"provider=LibreHardwareMonitorLib; package=0.9.6; assembly={typeof(Computer).Assembly.GetName().Version}; PawnIO={PawnIo.Version?.ToString() ?? "not installed"}");
if (!PawnIo.IsInstalled)
{
    Console.Error.WriteLine("Official PawnIO is not registered. Install it separately before native validation. No driver was installed by this probe.");
    return 3;
}
var computer = new Computer { IsCpuEnabled = true };
var found = false;
try
{
    computer.Open();
    for (var sample = 0; sample < count; sample++)
    {
        var readings = new List<CpuTemperatureReading>();
        foreach (var cpu in computer.Hardware.Where(hardware => hardware.HardwareType == HardwareType.Cpu))
        {
            cpu.Update();
            Read(cpu, cpu.Identifier.ToString(), cpu.Name, readings);
        }
        var selected = CpuTemperatureRules.SelectPackageCandidate(readings);
        found |= readings.Any(reading => reading.Celsius is not null);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ObservedUtc = DateTimeOffset.UtcNow,
            Source = "LibreHardwareMonitorLib 0.9.6",
            Readings = readings,
            PackageCandidate = selected,
            Acquisition = "Library values after Update; native per-sensor freshness is not independently proven"
        }));
        if (sample + 1 < count) Thread.Sleep(TimeSpan.FromSeconds(1));
    }
    if (!found) Console.Error.WriteLine("No valid CPU temperature was returned. This is unavailable, not zero or successful hardware acceptance.");
    return found ? 0 : 4;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine($"CPU sensor acquisition failed: {ex.Message}");
    return 4;
}
finally
{
    computer.Close();
}

static void Read(IHardware hardware, string cpuId, string cpuName, List<CpuTemperatureReading> readings)
{
    foreach (var sensor in hardware.Sensors.Where(sensor => sensor.SensorType == SensorType.Temperature))
        readings.Add(new(cpuId, cpuName, sensor.Identifier.ToString(), sensor.Name,
            CpuTemperatureRules.Normalize(sensor.Value)));
    foreach (var child in hardware.SubHardware)
    {
        child.Update();
        Read(child, cpuId, cpuName, readings);
    }
}
