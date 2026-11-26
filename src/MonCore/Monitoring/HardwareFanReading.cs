namespace MonCore.Monitoring;

/// <summary>A measured board fan speed, attributed to the CPU only when the board labels it as such.</summary>
public sealed record HardwareFanReading(string Name, double Rpm, bool IsCpu);
