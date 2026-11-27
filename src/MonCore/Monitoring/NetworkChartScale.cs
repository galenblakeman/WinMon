namespace MonCore.Monitoring;

/// <summary>Calculates portable network throughput values and chart limits.</summary>
public static class NetworkChartScale
{
    /// <summary>Returns the busier network direction in bits per second when both directions are available.</summary>
    public static double? BitsPerSecond(ResourceSample resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (resources is not { ReceiveBytesPerSecond: { } receive, SendBytesPerSecond: { } send } ||
            !double.IsFinite(receive) || !double.IsFinite(send) || receive < 0 || send < 0)
            return null;

        var bits = Math.Max(receive, send) * 8;
        return double.IsFinite(bits) ? bits : null;
    }

    /// <summary>
    ///     Rounds the visible network peak, in bits per second, up to a 1-2-5 step of bytes per second with a 100 KB/s
    ///     minimum. Rates are shown in bytes, so rounding there keeps the scale and its quarter ticks short.
    /// </summary>
    public static double Maximum(IEnumerable<MonitorSample> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);

        var peak = 0d;
        foreach (var sample in visible)
        {
            if (!sample.IsGap && BitsPerSecond(sample.Resources) is { } rate)
                peak = Math.Max(peak, rate);
        }

        var rounded = RoundUp(Math.Max(peak / 8, 100_000)) * 8;
        return double.IsFinite(rounded) ? rounded : double.MaxValue;
    }

    /// <summary>Rounds a positive chart maximum up to the next 1, 2 or 5 times a power of ten.</summary>
    public static double RoundUp(double value)
    {
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var step = value / magnitude;
        return (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * magnitude;
    }
}
