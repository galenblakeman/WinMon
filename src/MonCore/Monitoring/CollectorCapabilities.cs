namespace WinMon.Monitoring;

/// <summary>Measurements and actions a collector implementation can provide. Individual readings may still be unavailable.</summary>
[Flags]
public enum SystemMetricCapabilities
{
    None = 0,
    Cpu = 1,
    Memory = 2,
    Disk = 4,
    Network = 8,
    Gpu = 16,
    Services = 32
}

[Flags]
public enum ProcessMetricCapabilities
{
    None = 0,
    Cpu = 1,
    ResidentMemory = 2,
    PrivateMemory = 4,
    Io = 8,
    Gpu = 16,
    Threads = 32,
    Handles = 64,
    ExecutablePath = 128,
    AppWindow = 256
}

[Flags]
public enum ProcessActionCapabilities
{
    None = 0,
    Terminate = 1,
    ChangePriority = 2,
    CreateDump = 4,
    ControlServices = 8
}

public sealed record CollectorCapabilities(
    SystemMetricCapabilities SystemMetrics,
    ProcessMetricCapabilities ProcessMetrics,
    ProcessActionCapabilities ProcessActions)
{
    public static CollectorCapabilities None { get; } = new(
        SystemMetricCapabilities.None, ProcessMetricCapabilities.None, ProcessActionCapabilities.None);
}
