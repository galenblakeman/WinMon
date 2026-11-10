namespace WinMon.Monitoring;

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

    /// <summary>Rounds the visible network peak up to a 1-2-5 step, with a 1 Mbps minimum.</summary>
    public static double Maximum(IEnumerable<MonitorSample> visible)
    {
        ArgumentNullException.ThrowIfNull(visible);

        var peak = 0d;
        foreach (var sample in visible)
        {
            if (!sample.IsGap && BitsPerSecond(sample.Resources) is { } rate)
                peak = Math.Max(peak, rate);
        }

        var target = Math.Max(peak, 1_000_000);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var step = target / magnitude;
        var rounded = (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * magnitude;
        return double.IsFinite(rounded) ? rounded : double.MaxValue;
    }
}
