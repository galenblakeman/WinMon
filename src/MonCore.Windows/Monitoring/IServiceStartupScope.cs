namespace WinMon.Monitoring;

internal interface IServiceStartupScope : IDisposable
{
    bool IsTrusted { get; }
    uint State { get; }
    bool TryStart(out int error);
    TimeProvider Clock { get; }
}
