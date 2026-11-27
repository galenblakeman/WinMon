using System.Diagnostics;
using System.Runtime.InteropServices;
using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>WDDM performance data, with retained adapter handles refreshed every 30 seconds.</summary>
internal sealed class WindowsGpuThermals(WindowsGpuThermals.IApi? api = null, Func<long>? timestamp = null) : IDisposable
{
    private const uint RegistryInfo = 8, NodePerfData = 61, PerfData = 62, PerfCaps = 63, PhysicalCount = 30;
    private static readonly long RefreshInterval = 30 * Stopwatch.Frequency;
    private readonly List<Adapter> _adapters = [];
    private readonly IApi _api = api ?? new NativeApi();
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private static readonly byte[] EmptyQuery = new byte[256];
    private long _refreshAt;
    public bool HasTemperature { get; private set; }

    public IReadOnlyList<GpuThermalReading> Read()
    {
        if (_timestamp() >= _refreshAt) Refresh();
        var result = new List<GpuThermalReading>(_adapters.Count);
        foreach (var adapter in _adapters)
        {
            var data = Query<Data>(adapter.Handle, PerfData, adapter.Physical);
            if (data is null) continue;
            var reading = new GpuThermalReading(adapter.Id, adapter.Name, Temperature(data.Value.Temperature),
                Fan(data.Value.FanRpm, adapter.MaxFan), Percent(data.Value.Power), adapter.Warning, adapter.Maximum);
            if (reading.TemperatureCelsius is null && reading.FanRpm is null && reading.PowerPercent is null &&
                reading.WarningCelsius is null && reading.MaxCelsius is null) continue;
            result.Add(reading);
        }
        if (result.Count > 0) HasTemperature = true;
        return result;
    }

    internal unsafe double? ReadGraphicsClock(string adapterId, uint nodeOrdinal)
    {
        foreach (var adapter in _adapters)
        {
            if (adapter.Id != adapterId) continue;
            var data = new NodeData { Node = nodeOrdinal, Physical = adapter.Physical };
            var query = new AdapterQuery
            {
                Handle = adapter.Handle, Type = NodePerfData, Data = (IntPtr)(&data), Size = (uint)sizeof(NodeData)
            };
            return _api.Query(ref query) == 0 && data.Frequency is > 0 and <= 1_000_000_000_000
                ? data.Frequency / 1_000_000d
                : null;
        }

        return null;
    }

    private void Refresh()
    {
        DisposeAdapters(); _refreshAt = _timestamp() + RefreshInterval;
        var adapters = new Enumeration();
        if (_api.Enumerate(ref adapters) != 0 || adapters.Count is 0 or > 128) return;
        var capacity = adapters.Count;
        adapters.Items = Marshal.AllocHGlobal(checked((int)capacity * Marshal.SizeOf<Info>()));
        try
        {
            if (_api.Enumerate(ref adapters) != 0 || adapters.Count > capacity) return;
            var stride = Marshal.SizeOf<Info>();
            for (uint i = 0; i < adapters.Count; i++)
            {
                var info = Marshal.PtrToStructure<Info>(IntPtr.Add(adapters.Items, checked((int)i * stride)));
                if (info.Handle == 0) continue;
                var retained = false;
                try
                {
                    var count = Query<uint>(info.Handle, PhysicalCount) ?? 1;
                    if (count is 0 or > 32) continue;
                    var name = ReadAdapterName(info.Handle);
                    for (uint physical = 0; physical < count; physical++)
                    {
                        var caps = Query<Caps>(info.Handle, PerfCaps, physical);
                        var id = $"0x{info.LuidHigh:X8}_0x{info.LuidLow:X8}/{physical}".ToLowerInvariant();
                        _adapters.Add(new Adapter(info.Handle, physical, id, name ?? id, caps?.MaxFan,
                            Temperature(caps?.Warning ?? 0), Temperature(caps?.Maximum ?? 0)));
                    }
                    retained = true;
                }
                finally { if (!retained) Close(info.Handle); }
            }
        }
        finally { Marshal.FreeHGlobal(adapters.Items); }
    }

    public void Reset() { DisposeAdapters(); _refreshAt = 0; HasTemperature = false; }
    public void Dispose() => Reset();
    private void DisposeAdapters() { foreach (var handle in _adapters.Select(x => x.Handle).Distinct()) Close(handle); _adapters.Clear(); }
    private void Close(uint handle) => _api.Close(handle);
    internal static double? Temperature(uint tenths) => tenths is >= 10 and <= 1500 ? tenths / 10d : null;
    // Driver power is not utilization; preserve valid values above 100%.
    internal static double? Percent(uint tenths) => tenths > 0 ? tenths / 10d : null;
    internal static double? Fan(uint rpm, uint? maximum) => rpm != 0 || maximum is > 0 ? rpm : null;
    private string? ReadAdapterName(uint handle)
    {
        // D3DKMT_ADAPTERREGISTRYINFO contains four WCHAR[260] arrays, not just AdapterString.
        const int bytes = 4 * 260 * sizeof(char);
        var memory = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.Copy(new byte[bytes], 0, memory, bytes);
            var query = new AdapterQuery { Handle = handle, Type = RegistryInfo, Data = memory, Size = bytes };
            return _api.Query(ref query) == 0 ? Marshal.PtrToStringUni(memory, 260)?.TrimEnd('\0', ' ') : null;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private T? Query<T>(uint handle, uint type, uint physical = 0) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>(); var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(EmptyQuery, 0, memory, size);
            Marshal.WriteInt32(memory, unchecked((int)physical));
            var query = new AdapterQuery { Handle = handle, Type = type, Data = memory, Size = (uint)size };
            return _api.Query(ref query) == 0 ? Marshal.PtrToStructure<T>(memory) : null;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private sealed record Adapter(uint Handle, uint Physical, string Id, string Name, uint? MaxFan, double? Warning, double? Maximum);
    [StructLayout(LayoutKind.Sequential)] internal struct Enumeration { public uint Count; public IntPtr Items; }
    [StructLayout(LayoutKind.Sequential)] internal struct Info { public uint Handle, LuidLow; public int LuidHigh; public uint Sources; public int PrecisePresent; }
    [StructLayout(LayoutKind.Sequential)] private struct CloseAdapter { public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] internal struct AdapterQuery { public uint Handle, Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)] internal struct Data { public uint Physical; public ulong A, B, C, D, E; public uint FanRpm, Power, Temperature; public byte State; }
    // D3DKMT_NODE_PERFDATA layout in the Windows 10.0.26100 SDK.
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    private struct NodeData
    {
        [FieldOffset(0)] public uint Node;
        [FieldOffset(4)] public uint Physical;
        [FieldOffset(8)] public ulong Frequency;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Caps { public uint Physical; public ulong A, B; public uint MaxFan, Maximum, Warning; }
    internal interface IApi
    {
        int Enumerate(ref Enumeration adapters);
        int Query(ref AdapterQuery query);
        void Close(uint handle);
    }
    private sealed class NativeApi : IApi
    {
        public int Enumerate(ref Enumeration adapters) => Native.Enum(ref adapters);
        public int Query(ref AdapterQuery query) => Native.Query(ref query);
        public void Close(uint handle)
        {
            var value = new CloseAdapter { Handle = handle };
            _ = Native.Close(ref value);
        }
    }
    private static class Native
    {
        [DllImport("gdi32.dll", EntryPoint = "D3DKMTEnumAdapters2")] internal static extern int Enum(ref Enumeration value);
        [DllImport("gdi32.dll", EntryPoint = "D3DKMTQueryAdapterInfo")] internal static extern int Query(ref AdapterQuery value);
        [DllImport("gdi32.dll", EntryPoint = "D3DKMTCloseAdapter")] internal static extern int Close(ref CloseAdapter value);
    }
}
