using WinMon.Preferences;

namespace WinMon.Controls;

// The CLI links the basic formatter; temperature display preferences belong to the desktop.
internal static partial class GaugeReadoutText
{
    internal static string Temperature(double? celsius, TemperatureUnit unit)
    {
        if (celsius is not { } value || !double.IsFinite(value)) return Strings.Unavailable;
        return unit == TemperatureUnit.Fahrenheit
            ? Strings.Format(Strings.TemperatureFahrenheitValue, value * 9d / 5d + 32d)
            : Strings.Format(Strings.TemperatureCelsiusValue, value);
    }

    internal static string GpuDetail(double? clockMhz, double? temperatureCelsius, TemperatureUnit unit)
    {
        var temperature = Temperature(temperatureCelsius, unit);
        if (GpuClock(clockMhz) is not { } clock) return temperature;
        return temperature == Strings.Unavailable
            ? clock
            : Strings.Format(Strings.GaugeGpuTemperaturePair, clock, temperature);
    }

    internal static string? GpuClock(double? clockMhz)
    {
        return clockMhz is > 0 && double.IsFinite(clockMhz.Value)
            ? Strings.Format(Strings.GaugeGpuClockMHz, clockMhz.Value)
            : null;
    }

    internal static string CpuDetail(double? megahertz, double? temperatureCelsius, TemperatureUnit unit)
    {
        var clock = CpuClock(megahertz);
        var temperature = Temperature(temperatureCelsius, unit);
        return temperature == Strings.Unavailable ? clock : Strings.Format(Strings.GaugeCpuTemperaturePair, clock, temperature);
    }

    internal static string DiskDetail(double? bytesPerSecond, double? temperatureCelsius, TemperatureUnit unit)
    {
        var rate = DiskRate(bytesPerSecond);
        var temperature = Temperature(temperatureCelsius, unit);
        if (temperature == Strings.Unavailable) return rate;
        return bytesPerSecond is null ? temperature : Strings.Format(Strings.GaugeCpuTemperaturePair, rate, temperature);
    }
}
