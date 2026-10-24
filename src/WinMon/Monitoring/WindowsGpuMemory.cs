using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Pair adapter-wide dedicated usage with DXGI capacity for the same adapter LUID.</summary>
internal sealed class WindowsGpuMemory : IDisposable
{
    private SafeQuery? _query;
    private IntPtr _counter;
    private IntPtr _sharedCounter;
    private IReadOnlyDictionary<string, ulong> _capacities = new Dictionary<string, ulong>();
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private long _capacitiesRead;

    internal ResourceSample Attach(ResourceSample sample)
    {
        try
        {
            var now = Stopwatch.GetTimestamp();
            if (_capacitiesRead == 0 || now - _capacitiesRead > 30 * Stopwatch.Frequency)
            {
                _names.Clear();
                _capacities = ReadCapacities(_names);
                _capacitiesRead = now;
            }

            var name = _names.GetValueOrDefault(sample.GpuId);
            if (name is null) return sample;
            sample = sample with { GpuName = name };
            _capacities.TryGetValue(sample.GpuId, out var capacity);
            if (_query is null)
            {
                Check(PdhOpenQueryW(null, UIntPtr.Zero, out var query));
                _query = query;
                Check(PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Dedicated Usage", UIntPtr.Zero,
                    out _counter));
                var sharedStatus = PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Shared Usage", UIntPtr.Zero,
                    out _sharedCounter);
                if (sharedStatus != 0)
                {
                    Trace.TraceWarning("GPU shared-memory counter unavailable: 0x{0:X8}", sharedStatus);
                    _sharedCounter = IntPtr.Zero;
                }
            }

            Check(PdhCollectQueryData(_query));
            var used = SelectUsage(WindowsGpuSampler.ReadCounters(_counter), sample.GpuId);
            ulong? shared = null;
            if (_sharedCounter == IntPtr.Zero)
                return sample with
                {
                    GpuMemoryUsedBytes = capacity == 0 ? null : used,
                    GpuMemoryTotalBytes = used is null || capacity == 0 ? null : capacity,
                    GpuSharedMemoryUsedBytes = shared
                };
            try
            {
                shared = SelectUsage(WindowsGpuSampler.ReadCounters(_sharedCounter), sample.GpuId);
            }
            catch (Win32Exception ex)
            {
                Trace.TraceWarning("GPU shared-memory reading unavailable: {0}", ex.Message);
                _sharedCounter = IntPtr.Zero;
            }

            return sample with
            {
                GpuMemoryUsedBytes = capacity == 0 ? null : used,
                GpuMemoryTotalBytes = used is null || capacity == 0 ? null : capacity,
                GpuSharedMemoryUsedBytes = shared
            };
        }
        catch (Exception ex) when (ex is Win32Exception or COMException)
        {
            Trace.TraceWarning("GPU memory unavailable: {0}", ex.Message);
            Dispose();
            return sample;
        }
    }

    internal static ulong? SelectUsage(IEnumerable<WindowsGpuSampler.EngineCounter> counters, string adapter)
    {
        ulong? usage = null;
        foreach (var counter in counters)
        {
            var instance = WindowsGpuSampler.ParseInstance("pid_0_" + counter.Name + "_eng_0_engtype_Memory");
            if (instance?.Adapter != adapter) continue;
            // A missing, invalid or ambiguous adapter reading must never become zero or another GPU's usage.
            if (usage is not null || counter.Status is not (0 or 1) || !double.IsFinite(counter.Value) ||
                counter.Value < 0 || counter.Value >= ulong.MaxValue) return null;
            usage = (ulong)counter.Value;
        }

        return usage;
    }

    internal static IReadOnlyDictionary<string, ulong> ReadCapacities(Dictionary<string, string>? names = null)
    {
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var iid = new Guid("770AAE78-F26F-4DBA-A829-253C83D1B387"); // IDXGIFactory1
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            // IDXGIFactory1::EnumAdapters1 follows IUnknown, IDXGIObject and IDXGIFactory (slot 12).
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0;; index++)
            {
                var status = enumerate(factory, index, out var adapter);
                if (status == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                Marshal.ThrowExceptionForHR(status);
                try
                {
                    // IDXGIAdapter1::GetDesc1 follows IDXGIAdapter (slot 10).
                    Marshal.ThrowExceptionForHR(Method<GetDesc>(adapter, 10)(adapter, out var description));
                    var id = $"0x{description.LuidHigh:x8}_0x{description.LuidLow:x8}/0";
                    names?[id] = description.Description.TrimEnd('\0', ' ');
                    if ((description.Flags & 2) != 0) continue; // Software adapter has no physical capacity.
                    // DXGI describes one adapter. Do not reuse its capacity for linked physical GPUs.
                    result[id] = description.DedicatedVideoMemory.ToUInt64();
                }
                finally
                {
                    Marshal.Release(adapter);
                }
            }
        }
        finally
        {
            Marshal.Release(factory);
        }

        return result;
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate
    {
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),
            slot * IntPtr.Size));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate int GetDesc(IntPtr adapter, out AdapterDescription description);

    // https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow, LuidHigh, Flags;
    }

    private static void Check(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status));
    }

    public void Dispose()
    {
        _query?.Dispose();
        _query = null;
        _counter = IntPtr.Zero;
        _sharedCounter = IntPtr.Zero;
    }

    // Created by the P/Invoke marshaller.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class SafeQuery() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle()
        {
            return PdhCloseQuery(handle) == 0;
        }
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafeQuery query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint
        PdhAddEnglishCounterW(SafeQuery query, string path, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(SafeQuery query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}