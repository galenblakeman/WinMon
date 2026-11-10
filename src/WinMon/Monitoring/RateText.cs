using WinMon.Resources;

namespace WinMon.Monitoring;

/// <summary>Format bit-based network samples as decimal byte rates; link speeds retain bit units.</summary>
internal static class RateText
{
    internal static (string Number, string Unit) Parts(double bitsPerSecond)
    {
        var bytesPerSecond = bitsPerSecond / 8;
        var (value, unit) = bytesPerSecond switch
        {
            >= 1e9 => (bytesPerSecond / 1e9, Strings.GigabytesPerSecond),
            >= 1e6 => (bytesPerSecond / 1e6, Strings.MegabytesPerSecond),
            _ => (bytesPerSecond / 1e3, Strings.KilobytesPerSecond)
        };
        return (Number(value), unit);
    }

    internal static string Format(double bitsPerSecond)
    {
        var (number, unit) = Parts(bitsPerSecond);
        return $"{number} {unit}";
    }

    internal static string LinkSpeed(double bitsPerSecond)
    {
        return bitsPerSecond >= 1e9
            ? $"{Number(bitsPerSecond / 1e9)} {Strings.GigabitsPerSecond}"
            : $"{Number(bitsPerSecond / 1e6)} {Strings.MegabitsPerSecond}";
    }

    /// <summary>A compact gauge label that retains the byte-rate unit, such as "8MB/s".</summary>
    internal static string Short(double bitsPerSecond)
    {
        var (number, unit) = Parts(bitsPerSecond);
        return number + unit;
    }

    /// <summary>Format receive and send rates with the unit of the larger one so the pair fits one line.</summary>
    internal static string Pair(double receiveBits, double sendBits)
    {
        var unit = Parts(Math.Max(receiveBits, sendBits)).Unit;
        var divisor = (Math.Max(receiveBits, sendBits) / 8) switch { >= 1e9 => 8e9, >= 1e6 => 8e6, _ => 8e3 };
        return Strings.Format(Strings.NetworkRatePair, Number(receiveBits / divisor), Number(sendBits / divisor), unit);
    }

    private static string Number(double value)
    {
        return value switch { < 10 => $"{value:0.##}", < 100 => $"{value:0.#}", _ => $"{value:0}" };
    }
}
