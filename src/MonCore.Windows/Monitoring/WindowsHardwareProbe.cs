using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Raw Windows hardware readings. The application formats and localizes them.</summary>
public static class WindowsHardwareProbe
{
    public sealed record CpuReading(IReadOnlyList<string> Names, int? PhysicalCores, uint LogicalProcessors);
    public sealed record GpuReading(string Name, ulong DedicatedBytes, ulong SharedLimitBytes);
    public sealed record VolumeReading(string Name, string? Label, string? Format, long? TotalBytes,
        long? AvailableBytes, bool Error);

    public static CpuReading Cpu()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (key is not null)
            foreach (var child in key.GetSubKeyNames())
            {
                using var processor = key.OpenSubKey(child);
                if (processor?.GetValue("ProcessorNameString") is string name) names.Add(name.Trim());
            }

        return new CpuReading(names.ToArray(), CoreCount(), GetActiveProcessorCount(0xFFFF));
    }

    public static (ulong? InstalledBytes, ulong? UsableBytes) Memory()
    {
        ulong? installed = GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes > 0
            ? kilobytes * 1024
            : null;
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        ulong? usable = GlobalMemoryStatusEx(ref status) ? status.TotalPhysical : null;
        return (installed, usable);
    }

    public static IReadOnlyList<GpuReading> Graphics()
    {
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        var readings = new List<GpuReading>();
        try
        {
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint index = 0; index < 64; index++)
            {
                var result = enumerate(factory, index, out var adapter);
                if (result == unchecked((int)0x887A0002)) break;
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var desc));
                    if ((desc.Flags & 2) != 0) continue;
                    readings.Add(new GpuReading(desc.Description, desc.DedicatedVideoMemory.ToUInt64(),
                        desc.SharedSystemMemory.ToUInt64()));
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

        return readings;
    }

    public static IReadOnlyList<VolumeReading> Volumes()
    {
        var readings = new List<VolumeReading>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
            try
            {
                readings.Add(drive.IsReady
                    ? new VolumeReading(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize,
                        drive.AvailableFreeSpace, false)
                    : new VolumeReading(drive.Name, null, null, null, null, false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                readings.Add(new VolumeReading(drive.Name, null, null, null, null, true));
            }

        return readings;
    }

    private static int? CoreCount()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref length);
        if (length is 0 or > 4 * 1024 * 1024) return null;
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
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual,
            AvailableVirtual, AvailableExtendedVirtual;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
