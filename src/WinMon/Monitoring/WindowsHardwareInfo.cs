using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinMon.Monitoring;

/// <summary>On-demand hardware inventory; never runs in the resource sampling loop.</summary>
internal static class WindowsHardwareInfo
{
    internal sealed record Detail(string Name, string Text);
    internal sealed record Section(string Name, string Details, IReadOnlyList<Detail>? Items = null, string DetailHeading = "Details");

    internal static IReadOnlyList<Section> Read()
    {
        var query = new WmiHardwareQuery();
        return [HardwareInventory.Cpu(query, ReadSection("CPU", Cpu).Details), Memory(query),
            ReadSection("Graphics adapters", Graphics), HardwareInventory.Disks(query), ReadSection("Local volumes", Volumes),
            ReadSection("Network adapters", Network)];
    }

    private static Section ReadSection(string name, Func<string> read)
    {
        try { return new Section(name, read()); }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or IOException or
            UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            return new Section(name, "Unavailable on this system.");
        }
    }

    private static string Cpu()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (key is not null)
            foreach (var child in key.GetSubKeyNames())
            {
                using var processor = key.OpenSubKey(child);
                if (processor?.GetValue("ProcessorNameString") is string name) names.Add(name.Trim());
            }
        var cores = CoreCount();
        var logical = GetActiveProcessorCount(0xFFFF); // ALL_PROCESSOR_GROUPS, independent of process affinity.
        return $"{(names.Count == 0 ? "Model unavailable" : string.Join("\n", names))}\n" +
            $"{(cores is { } count ? $"{count} physical cores" : "Physical core count unavailable")} · {logical} logical processors";
    }

    private static int? CoreCount()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length); // RelationProcessorCore
        if (length == 0 || length > 4 * 1024 * 1024) return null;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(0, buffer, ref length)) return null;
            var cores = 0;
            for (var offset = 0; offset + 8 <= length;)
            {
                var size = Marshal.ReadInt32(buffer, offset + 4);
                if (size < 8 || size > length - offset) return null;
                if (Marshal.ReadInt32(buffer, offset) == 0) cores++;
                offset += size;
            }
            return cores > 0 ? cores : null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static Section Memory(IHardwareQuery query)
    {
        ulong? installed = GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes > 0 ? kilobytes * 1024 : null;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        ulong? usable = GlobalMemoryStatusEx(ref status) ? status.TotalPhysical : null;
        return HardwareInventory.Memory(query, installed, usable);
    }

    private static string Graphics()
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        var descriptions = new List<string>();
        try
        {
            // COM vtable slots include IUnknown, IDXGIObject and the base DXGI interface.
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; index < 64; index++)
            {
                var result = enumerate(factory, index, out var adapter);
                if (result == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var desc));
                    if ((desc.Flags & 2) != 0) continue; // Software adapter, not physical hardware.
                    descriptions.Add($"{desc.Description}\nDedicated video memory: {desc.DedicatedVideoMemory.ToUInt64() / 1_073_741_824d:0.##} GiB" +
                        $" · shared system memory limit: {desc.SharedSystemMemory.ToUInt64() / 1_073_741_824d:0.##} GiB");
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return descriptions.Count == 0 ? "No hardware adapters reported by Windows." : string.Join("\n\n", descriptions);
    }

    private static string Volumes()
    {
        var descriptions = new List<string>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
        {
            try
            {
                descriptions.Add(drive.IsReady
                    ? $"{drive.Name} {drive.VolumeLabel} · {drive.DriveFormat}\n{drive.TotalSize / 1_073_741_824d:0.#} GiB capacity · {drive.AvailableFreeSpace / 1_073_741_824d:0.#} GiB available"
                    : $"{drive.Name} Not ready");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { descriptions.Add($"{drive.Name} Details unavailable"); }
        }
        return descriptions.Count == 0 ? "No local volumes reported." : string.Join("\n\n", descriptions);
    }

    private static string Network()
    {
        var descriptions = new List<string>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            try
            {
                var speed = adapter.Speed > 0 ? Controls.RateText.Format(adapter.Speed) : "Link speed unavailable";
                descriptions.Add($"{adapter.Name} · {adapter.OperationalStatus}\n{adapter.Description}\n{speed}");
            }
            catch (NetworkInformationException) { descriptions.Add($"{adapter.Name} · Details unavailable"); }
        }
        return descriptions.Count == 0 ? "No network adapters reported." : string.Join("\n\n", descriptions);
    }

    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong kilobytes);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
