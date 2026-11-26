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
        TemperatureUnit unit, double? powerPercent)
    {
        var temperature = Temperature(temperatureCelsius, unit);
        if (powerPercent is { } power && double.IsFinite(power))
        {
            var powerText = Strings.Format(Strings.GaugeGpuPower, Strings.Format(Strings.PercentInteger, power));
            return temperature == Strings.Unavailable
                ? powerText
                : Strings.Format(Strings.GaugeGpuTemperaturePair, temperature, powerText);
        }

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

    internal static string DiskDetail(double? bytesPerSecond, double? temperatureCelsius, TemperatureUnit unit)
    {
        var rate = DiskRate(bytesPerSecond);
        var temperature = Temperature(temperatureCelsius, unit);
        if (temperature == Strings.Unavailable) return rate;
        return bytesPerSecond is null ? temperature : Strings.Format(Strings.GaugeCpuTemperaturePair, rate, temperature);
    }
}
