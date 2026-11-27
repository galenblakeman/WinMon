// Exercises the same CPU-only provider used by the optional service, without installing a service.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LibreHardwareMonitor.PawnIo;
using WinMon.SensorService;

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
Console.Error.WriteLine($"provider=WinMon.SensorService/LibreHardwareMonitorLib; package=0.9.6; PawnIO={PawnIo.Version?.ToString() ?? "not installed"}");
if (!PawnIo.IsInstalled)
{
    Console.Error.WriteLine("Official PawnIO is not registered. Install it separately. This probe installs no driver or service.");
    return 3;
}
await using var provider = new LibreHardwareMonitorProvider();
var found = false;
try
{
    var open = Stopwatch.StartNew();
    await provider.OpenAsync(CancellationToken.None);
    Console.Error.WriteLine($"open_ms={open.Elapsed.TotalMilliseconds:F3}");
    for (var sample = 0; sample < count; sample++)
    {
        using var process = Process.GetCurrentProcess();
        var beforeCpu = process.TotalProcessorTime;
        var clock = Stopwatch.GetTimestamp();
        var (reading, packagePower) = await provider.ReadCpuAsync(CancellationToken.None);
        var updateMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        process.Refresh();
        found |= reading?.Celsius is not null;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            ObservedUtc = DateTimeOffset.UtcNow,
            Source = "LibreHardwareMonitorLib 0.9.6",
            Reading = reading,
            CpuPackagePower = packagePower,
            UpdateMs = updateMs,
            ProcessCpuMs = (process.TotalProcessorTime - beforeCpu).TotalMilliseconds,
            WorkingSetBytes = process.WorkingSet64,
            Acquisition = "Supported Min/Max reset detects library assignments during this pass; hardware accuracy is not established"
        }));
        if (sample + 1 < count) await Task.Delay(TimeSpan.FromSeconds(2));
    }
    if (!found) Console.Error.WriteLine("No valid CPU temperature was returned. Sensor data remains unavailable.");
    return found ? 0 : 4;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
{
    Console.Error.WriteLine($"CPU sensor acquisition failed: {ex.Message}");
    return 4;
}
