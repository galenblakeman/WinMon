using System.Diagnostics;
using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>State for optional ACPI zones supplied by the shared memory-pressure PDH query.</summary>
internal sealed class WindowsThermalZones(Func<long>? timestamp = null) : IDisposable
{
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private readonly Dictionary<string, History> _history = new(StringComparer.OrdinalIgnoreCase);
    public bool HasTemperature { get; private set; }

    public IReadOnlyList<ThermalZoneReading> Read(IReadOnlyList<ThermalZoneRaw> raw, double? cpuPercent, bool reset)
    {
        if (reset) Reset();
        var now = _timestamp(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        double? cpu = cpuPercent is { } value && double.IsFinite(value) && value is >= 0 and <= 100 ? value : null;
        var result = new List<ThermalZoneReading>(raw.Count);
        foreach (var item in raw)
        {
            var celsius = ToCelsius(item.Kelvin, item.HighPrecision);
            if (celsius is null || string.IsNullOrWhiteSpace(item.Name)) continue;
            names.Add(item.Name);
            if (!_history.TryGetValue(item.Name, out var history) || history.HighPrecision != item.HighPrecision ||
                Math.Abs(history.Celsius - celsius.Value) > 0.001)
                history = new History(celsius.Value, now, cpu, cpu, item.HighPrecision);
            else if (cpu is { } current)
                history = history with { MinimumCpu = history.MinimumCpu is { } min ? Math.Min(min, current) : current,
                    MaximumCpu = history.MaximumCpu is { } max ? Math.Max(max, current) : current };
            _history[item.Name] = history;
            var varied = history.MinimumCpu is { } low && history.MaximumCpu is { } high && high - low > 20;
            result.Add(new ThermalZoneReading(item.Name, celsius.Value,
                item.HighPrecision && varied && now - history.ChangedAt >= 10 * 60 * Stopwatch.Frequency));
        }
        foreach (var missing in _history.Keys.Where(key => !names.Contains(key)).ToArray()) _history.Remove(missing);
        if (result.Count != 0) HasTemperature = true;
        return result;
    }

    internal static double? ToCelsius(double kelvin, bool highPrecision)
    {
        if (!double.IsFinite(kelvin)) return null;
        var celsius = (highPrecision ? kelvin / 10 : kelvin) - 273.15;
        return celsius is >= 1 and <= 150 ? celsius : null;
    }
    public void Reset() { _history.Clear(); HasTemperature = false; }
    public void Dispose() => Reset();
    private sealed record History(double Celsius, long ChangedAt, double? MinimumCpu, double? MaximumCpu, bool HighPrecision);
}
