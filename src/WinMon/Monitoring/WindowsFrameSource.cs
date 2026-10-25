using System.ComponentModel;
using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Captures processes, resources and services from Windows, keeping what remains readable.</summary>
internal sealed class WindowsFrameSource : IFrameSource
{
    private readonly WindowsCpuSampler _cpu = new();
    private readonly WindowsResourceSampler _resources = new();
    private readonly WindowsServices.Reader _services = new();
    private readonly ServiceTree _serviceTree = new();

    public CapturedFrame Capture(SamplingOptions options)
    {
        ServiceCatalog? catalog = null;
        try
        {
            catalog = _services.Read();
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Service enumeration unavailable: {0}", ex.Message);
        }

        MonitorSample sample;
        SamplingError? error = null;
        try
        {
            sample = _cpu.Sample();
        }
        catch (Exception ex) when (ex is Win32Exception or PlatformNotSupportedException)
        {
            _cpu.Reset();
            error = SamplingError.CpuUnavailable;
            Trace.TraceWarning("CPU sampling unavailable: {0}", ex.Message);
            sample = new MonitorSample(DateTimeOffset.Now, null, [], 0, false, Stopwatch.GetTimestamp());
        }

        var full = _resources.Sample(sample, options.PrimaryDrive, options.SecondaryDrive);
        return new CapturedFrame(full, _serviceTree.Capture(full, catalog), error);
    }

    public void Reset()
    {
        _cpu.Reset();
        _resources.Reset();
    }

    public void Dispose()
    {
        _resources.Dispose();
        _cpu.Dispose();
    }
}
