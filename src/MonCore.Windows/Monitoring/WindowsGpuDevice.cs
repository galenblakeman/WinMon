using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonCore.Windows.Monitoring;

/// <summary>A persistent hardware identity paired with the current boot's counter identity.</summary>
public sealed record WindowsGpuDevice(string Id, string Name, string Adapter);

/// <summary>Resolve DXGI adapters to physical PnP keys, never persisting a boot-local LUID.</summary>
public static partial class WindowsGpuDevices
{
    public static WindowsGpuDevice[] Discover()
    {
        var devices = new List<WindowsGpuDevice>();
        try { WindowsGpuMemory.ReadCapacities(devices: devices); }
        catch (Exception ex) when (ex is COMException or System.ComponentModel.Win32Exception)
        {
            Trace.TraceWarning("GPU device discovery unavailable: {0}", ex.Message);
        }
        // Some virtual/software drivers expose the same hardware key for several adapters.
        // Such a key cannot identify one GPU safely; leave those adapters to Automatic.
        return devices.GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Single()).ToArray();
    }

    internal static void Add(List<WindowsGpuDevice> devices, uint low, uint high, string name)
    {
        var opened = new OpenAdapter { Low = low, High = high };
        if (D3DKMTOpenAdapterFromLuid(ref opened) != 0) return;
        try
        {
            var data = Marshal.AllocHGlobal(4);
            try
            {
                var countQuery = new Query { Handle = opened.Handle, Type = 30, Data = data, Size = 4 };
                Marshal.WriteInt32(data, 0);
                if (D3DKMTQueryAdapterInfo(ref countQuery) != 0) return;
                var count = Marshal.ReadInt32(data);
                // Reject corrupt/unexpected driver output rather than enumerating unbounded indices.
                if (count is < 1 or > 64) return;
                for (uint index = 0; index < count; index++)
                {
                    var id = ReadKey(opened.Handle, index);
                    if (id is not null)
                        devices.Add(new WindowsGpuDevice(id, name, $"0x{high:x8}_0x{low:x8}/{index}"));
                }
            }
            finally { Marshal.FreeHGlobal(data); }
        }
        finally
        {
            var handle = opened.Handle;
            _ = D3DKMTCloseAdapter(ref handle);
        }
    }

    // Windows 10 1703 / WDDM 2.2: D3DKMT_QUERY_PHYSICAL_ADAPTER_PNP_KEY.
    // https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_query_physical_adapter_pnp_key
    private static string? ReadKey(uint handle, uint index)
    {
        const int characters = 4096;
        var buffer = IntPtr.Zero;
        var length = IntPtr.Zero;
        var data = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(characters * 2);
            length = Marshal.AllocHGlobal(4);
            data = Marshal.AllocHGlobal(Marshal.SizeOf<PnpKey>());
            Marshal.WriteInt32(length, characters);
            Marshal.StructureToPtr(new PnpKey { Index = index, Type = 1, Buffer = buffer, Length = length }, data, false);
            var query = new Query { Handle = handle, Type = 41, Data = data, Size = (uint)Marshal.SizeOf<PnpKey>() };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) return null;
            var written = Marshal.ReadInt32(length);
            if (written is <= 1 or > characters) return null;
            var key = Marshal.PtrToStringUni(buffer, written - 1)?.TrimEnd('\0');
            return NormalizeKey(key);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(length);
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        // A registry hardware key includes the boot's ControlSet number. Persist only
        // the device instance path beneath Enum, not that mutable registry prefix.
        const string marker = "\\Enum\\";
        var start = key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var instance = key[(start + marker.Length)..];
        const string suffix = "\\Device Parameters";
        if (instance.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) instance = instance[..^suffix.Length];
        return string.IsNullOrWhiteSpace(instance) ? null : instance;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter { public uint Low, High, Handle; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Query { public uint Handle, Type; public IntPtr Data; public uint Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PnpKey { public uint Index, Type; public IntPtr Buffer, Length; }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTQueryAdapterInfo(ref Query query);
    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(ref uint handle);
}
