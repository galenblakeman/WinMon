using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Caches executable paths by process lifetime on the sampling thread.</summary>
internal sealed class ProcessImagePathProvider
{
    private readonly Func<int, long, string> _read;
    private readonly Func<int, string> _fallback;
    private Dictionary<(int, long), string> _paths = [];
    private Dictionary<(int, long), string> _nextPaths = [];
    private Dictionary<(int, long), string> _pending = [];
    private Dictionary<(int, long), string> _nextPending = [];
    private readonly char[] _pathBuffer = new char[1024];
    private char[]? _nativeBuffer;

    internal ProcessImagePathProvider(Func<int, long, string>? read = null, Func<int, string>? fallback = null)
    {
        _read = read ?? Read;
        _fallback = fallback ?? ReadByProcessId;
    }

    internal void Capture(List<ProcessCpuTimes> processes)
    {
        _nextPaths.Clear();
        _nextPending.Clear();
        for (var i = 0; i < processes.Count; i++)
        {
            var process = processes[i];
            var key = (process.ProcessId, process.CreationTicks);
            // A PID-only query is made after one system snapshot, then accepted only if the
            // next snapshot still contains that exact lifetime. Never publish an unverified path.
            if (!_pending.TryGetValue(key, out var path) && !_paths.TryGetValue(key, out path))
            {
                path = _read(process.ProcessId, process.CreationTicks);
                if (path.Length == 0)
                {
                    var candidate = _fallback(process.ProcessId);
                    if (candidate.Length > 0) _nextPending[key] = candidate;
                }
            }
            _nextPaths[key] = path;
            processes[i] = process with { ImagePath = path };
        }
        (_paths, _nextPaths) = (_nextPaths, _paths);
        (_pending, _nextPending) = (_nextPending, _pending);
    }

    private string Read(int processId, long created)
    {
        using var handle = NativeMethods.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (handle.IsInvalid || !NativeMethods.GetProcessTimes(handle, out var actual, out _, out _, out _) || actual != created) return "";
        var length = _pathBuffer.Length;
        return NativeMethods.QueryFullProcessImageNameW(handle, 0, _pathBuffer, ref length) ? new string(_pathBuffer, 0, length) : "";
    }

    internal string ReadByProcessId(int processId)
    {
        try
        {
            // SystemProcessIdInformation (88) is an undocumented native information class,
            // also used by System Informer (phlib/nativeprocess.c). It needs no process handle.
            // Use a bounded UNICODE_STRING buffer; unsupported classes and all failed queries
            // simply leave the executable path unavailable.
            var buffer = _nativeBuffer ??= GC.AllocateArray<char>(32767, pinned: true);
            var address = Marshal.UnsafeAddrOfPinnedArrayElement(buffer, 0);
            var info = new ProcessIdInformation
            {
                ProcessId = processId,
                ImageName = new UnicodeString { MaximumLength = checked((ushort)(buffer.Length * 2)), Buffer = address }
            };
            var status = NativeMethods.NtQuerySystemInformation(88, ref info, Marshal.SizeOf<ProcessIdInformation>(), out _);
            if (status != 0 || info.ImageName.Buffer != address || info.ImageName.Length == 0 ||
                info.ImageName.Length > buffer.Length * 2 || info.ImageName.Length % 2 != 0) return "";
            var nativePath = new string(buffer, 0, info.ImageName.Length / 2);
            return ToDosPath(nativePath, drive =>
            {
                if (NativeMethods.QueryDosDeviceW(drive, buffer, buffer.Length) == 0) return null;
                var end = Array.IndexOf(buffer, '\0');
                return end > 0 ? new string(buffer, 0, end) : null;
            });
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return "";
        }
    }

    internal static string ToDosPath(string path, Func<string, string?> queryDevice)
    {
        if (path.StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase))
        {
            var dosPath = path[4..];
            if (dosPath.Length >= 3 && char.IsAsciiLetter(dosPath[0]) && dosPath[1] == ':' && dosPath[2] == '\\') return dosPath;
        }
        if (!path.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) return "";
        for (var drive = 'A'; drive <= 'Z'; drive++)
        {
            var name = $"{drive}:";
            var target = queryDevice(name);
            if (!string.IsNullOrEmpty(target) && path.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase))
                return name + path[target.Length..];
        }
        return "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal nint Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessIdInformation
    {
        internal nint ProcessId;
        internal UnicodeString ImageName;
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, [Out] char[] path, ref int size);

        [DllImport("ntdll.dll")]
        internal static extern uint NtQuerySystemInformation(int informationClass, ref ProcessIdInformation information, int length, out int returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, int length);
    }
}
