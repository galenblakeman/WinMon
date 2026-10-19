namespace WinMon.Controls;

/// <summary>Format network rates with three significant digits and an adaptive bit unit.</summary>
internal static class RateText
{
    internal static (string Number, string Unit) Parts(double bitsPerSecond)
    {
        var (value, unit) = bitsPerSecond switch
        {
            >= 1e9 => (bitsPerSecond / 1e9, "Gbps"),
            >= 1e6 => (bitsPerSecond / 1e6, "Mbps"),
            _ => (bitsPerSecond / 1e3, "Kbps")
        };
        return (value switch { < 10 => $"{value:0.##}", < 100 => $"{value:0.#}", _ => $"{value:0}" }, unit);
    }

    internal static string Format(double bitsPerSecond)
    {
        var (number, unit) = Parts(bitsPerSecond);
        return $"{number} {unit}";
    }

    /// <summary>Format receive and send rates with the unit of the larger one so the pair fits one line.</summary>
    internal static string Pair(double receiveBits, double sendBits)
    {
        var unit = Parts(Math.Max(receiveBits, sendBits)).Unit;
        var divisor = unit switch { "Gbps" => 1e9, "Mbps" => 1e6, _ => 1e3 };
        static string Number(double value) => value switch { 0 => "0", < 10 => $"{value:0.##}", < 100 => $"{value:0.#}", _ => $"{value:0}" };
        return $"Rx {Number(receiveBits / divisor)} · Tx {Number(sendBits / divisor)} {unit}";
    }
}
