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

    internal static string GpuDetail(ulong? usedBytes, ulong? totalBytes, double? temperatureCelsius,
        TemperatureUnit unit)
    {
        var temperature = Temperature(temperatureCelsius, unit);
        return temperature == Strings.Unavailable
            ? GpuMemory(usedBytes, totalBytes)
            : Strings.Format(Strings.GaugeGpuTemperaturePair, GpuMemory(usedBytes, totalBytes), temperature);
    }

    internal static string CpuDetail(double? megahertz, double? temperatureCelsius, TemperatureUnit unit)
    {
        var clock = CpuClock(megahertz);
        var temperature = Temperature(temperatureCelsius, unit);
        return temperature == Strings.Unavailable ? clock : Strings.Format(Strings.GaugeCpuTemperaturePair, clock, temperature);
    }
}
