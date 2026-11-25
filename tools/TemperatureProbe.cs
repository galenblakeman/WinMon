#:property TargetFramework=net10.0-windows
#:property PublishAot=false
#:property AllowUnsafeBlocks=true

// Hardware spike for docs/design/TEMPERATURE.md: which unprivileged temperature and throttle readings
// this machine reports, and whether they move under load. Not part of the app or verification.
//
//   dotnet run tools/TemperatureProbe.cs -- [seconds=120] [--no-load] > probe.csv
//
// For a machine without the .NET SDK, publish a standalone exe and copy it over:
//   dotnet publish tools/TemperatureProbe.cs -r win-x64 --self-contained -p:PublishSingleFile=true -o artifacts/probe
//
// % Performance Limit is 100 when nothing limits the processor; throttling shows as a lower value.
//
// stdout: one CSV row per second. stderr: adapters, sources and a summary. The second half of the run
// spins every logical processor to show whether readings respond; start a game or 3D app to load the GPU.

#pragma warning disable CS0649 // Struct fields are filled by native calls.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var seconds = args.Select(a => int.TryParse(a, out var s) ? s : 0).FirstOrDefault(s => s > 0, 120);
var load = !args.Contains("--no-load");

Console.Error.WriteLine($"OS {Environment.OSVersion.Version}, {Environment.ProcessorCount} logical processors, " +
                        $"CPU {Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "?")}".Trim());

var gpus = Gpu.Enumerate();
var pdh = new Pdh();
var utility = pdh.Add(@"\Processor Information(_Total)\% Processor Utility");
var limit = pdh.Add(@"\Processor Information(_Total)\% Performance Limit");
var limitFlags = pdh.Add(@"\Processor Information(_Total)\Performance Limit Flags");
var ofMax = pdh.Add(@"\Processor Information(_Total)\% of Maximum Frequency");
var performance = pdh.Add(@"\Processor Information(_Total)\% Processor Performance");
var zonesPrecise = pdh.Add(@"\Thermal Zone Information(*)\High Precision Temperature");
var zonesCoarse = pdh.Add(@"\Thermal Zone Information(*)\Temperature");
pdh.Collect();
Thread.Sleep(1000);
pdh.Collect();

// High Precision Temperature is tenths of a kelvin; the older Temperature counter is whole kelvins.
Dictionary<string, double> Zones() =>
    zonesPrecise is not null && Pdh.Array(zonesPrecise) is { Count: > 0 } precise
        ? precise.ToDictionary(z => z.Key, z => z.Value / 10 - 273.15)
        : Pdh.Array(zonesCoarse).ToDictionary(z => z.Key, z => z.Value - 273.15);

var zoneNames = Zones().Keys.Order().ToList();
Console.Error.WriteLine($"Thermal zones: {(zoneNames.Count == 0 ? "none" : string.Join(", ", zoneNames))} " +
                        $"({(zonesPrecise is not null && Pdh.Array(zonesPrecise).Count > 0 ? "high precision" : "whole kelvin")})");
foreach (var (name, counter) in new[] { ("% Performance Limit", limit), ("Performance Limit Flags", limitFlags), ("% of Maximum Frequency", ofMax), ("% Processor Performance", performance) })
    if (counter is null) Console.Error.WriteLine($"Counter missing: {name}");

var columns = new List<(string Name, Func<double?> Read)>
{
    ("cpu_utility_pct", () => Pdh.Value(utility)),
    ("perf_limit_pct", () => Pdh.Value(limit)),
    ("perf_limit_flags", () => Pdh.Value(limitFlags)),
    ("pct_of_max_freq", () => Pdh.Value(ofMax)),
    ("avg_mhz", () => Power.Read() is { Length: > 0 } p ? p.Average(x => x.CurrentMhz) : null),
    ("max_mhz", () => Power.Read() is { Length: > 0 } p ? p.Max(x => x.MaxMhz) : null),
    ("min_mhz_limit", () => Power.Read() is { Length: > 0 } p ? p.Min(x => x.MhzLimit) : null),
    // % Processor Performance passes 100 while boosting; times the base clock it gives the effective clock.
    ("pct_processor_performance", () => Pdh.Value(performance)),
    ("effective_mhz", () => Pdh.Value(performance) is { } pct && Power.Read() is { Length: > 0 } p ? pct / 100 * p.Max(x => x.MaxMhz) : null),
};
Dictionary<string, double> zones = [];
columns.AddRange(zoneNames.Select(z => (Csv($"zone_c {z}"), (Func<double?>)(() => zones.TryGetValue(z, out var c) ? c : null))));
foreach (var gpu in gpus)
{
    var g = gpu;
    var tag = Csv($"{g.Name} {g.Index}");
    columns.Add(($"gpu_c {tag}", () => g.Perf is { Temperature: > 0 } p ? p.Temperature / 10.0 : null));
    columns.Add(($"gpu_fan_rpm {tag}", () => g.Perf?.FanRpm));
    columns.Add(($"gpu_power_pct {tag}", () => g.Perf?.Power / 10.0));
    columns.Add(($"gpu_powered {tag}", () => g.Perf?.PowerStateOverride));
}

Console.WriteLine("time,phase," + string.Join(",", columns.Select(c => c.Name)));
var history = columns.Select(_ => new List<(bool Load, double? Value)>()).ToArray();
var clock = Stopwatch.StartNew();
for (var t = 0; t < seconds; t++)
{
    var loading = load && t >= seconds / 2;
    if (loading && !Spin.Running) Spin.Start();
    Thread.Sleep(Math.Max(0, (int)((t + 1) * 1000 - clock.ElapsedMilliseconds)));
    pdh.Collect();
    zones = Zones();
    foreach (var gpu in gpus) gpu.Refresh();
    var values = columns.Select(c => c.Read()).ToArray();
    for (var i = 0; i < values.Length; i++) history[i].Add((loading, values[i]));
    Console.WriteLine($"{DateTime.Now:HH:mm:ss},{(loading ? "load" : "idle")}," +
                      string.Join(",", values.Select(v => v is { } d ? d.ToString("0.#") : "")));
}
Spin.Stop();

Console.Error.WriteLine();
Console.Error.WriteLine("column: valid samples | idle min-max | load min-max | distinct values");
for (var i = 0; i < columns.Count; i++)
{
    var valid = history[i].Where(h => h.Value is not null).ToList();
    string Range(bool phase) => valid.Where(h => h.Load == phase).Select(h => h.Value!.Value).ToList() is { Count: > 0 } r
        ? $"{r.Min():0.#}-{r.Max():0.#}" : "-";
    Console.Error.WriteLine($"{columns[i].Name}: {valid.Count}/{history[i].Count} | {Range(false)} | {Range(true)} | " +
                            $"{valid.Select(h => Math.Round(h.Value!.Value, 1)).Distinct().Count()}");
}
foreach (var gpu in gpus) gpu.Dispose();
pdh.Dispose();
return;

static string Csv(string s) => s.Replace(',', ' ').Replace('"', ' ');

static class Spin
{
    private static volatile bool _running;
    public static bool Running => _running;

    public static void Start()
    {
        _running = true;
        for (var i = 0; i < Environment.ProcessorCount; i++)
            new Thread(() => { while (_running) { } }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }.Start();
        Console.Error.WriteLine($"{DateTime.Now:HH:mm:ss} CPU load started");
    }

    public static void Stop() => _running = false;
}

/// <summary>One physical GPU behind a WDDM adapter handle.</summary>
sealed unsafe class Gpu : IDisposable
{
    private const uint RegistryInfo = 8, PhysicalAdapterCount = 30, PerfDataType = 62, PerfDataCapsType = 63;
    private readonly uint _handle;
    private readonly bool _owner;
    public string Name { get; }
    public uint Index { get; }
    public PerfData? Perf { get; private set; }

    private Gpu(uint handle, bool owner, string name, uint index) => (_handle, _owner, Name, Index) = (handle, owner, name, index);

    public static List<Gpu> Enumerate()
    {
        var e = new EnumAdapters2();
        Check(D3DKMTEnumAdapters2(ref e), "D3DKMTEnumAdapters2");
        var infos = new AdapterInfo[e.Count];
        fixed (AdapterInfo* p = infos)
        {
            e.Adapters = (IntPtr)p;
            Check(D3DKMTEnumAdapters2(ref e), "D3DKMTEnumAdapters2");
        }

        var gpus = new List<Gpu>();
        foreach (var info in infos.AsSpan(0, (int)Math.Min(e.Count, (uint)infos.Length)))
        {
            var registry = new AdapterRegistryInfo();
            var name = Query(info.Handle, RegistryInfo, ref registry) == 0 ? new string(registry.AdapterString) : "unnamed";
            uint physical = 1;
            _ = Query(info.Handle, PhysicalAdapterCount, ref physical);
            Console.Error.WriteLine($"Adapter {name}, handle 0x{info.Handle:X8}, LUID {info.LuidHigh:X8}-{info.LuidLow:X8}, {physical} physical");
            for (uint i = 0; i < Math.Max(1, physical); i++)
            {
                var caps = new PerfDataCaps { PhysicalAdapterIndex = i };
                var capsStatus = Query(info.Handle, PerfDataCapsType, ref caps);
                Console.Error.WriteLine(capsStatus == 0
                    ? $"  [{i}] caps: warning {caps.TemperatureWarning / 10.0} C, max {caps.TemperatureMax / 10.0} C, max fan {caps.MaxFanRpm} RPM"
                    : $"  [{i}] caps: status 0x{capsStatus:X8}");
                var gpu = new Gpu(info.Handle, i == 0, name, i);
                var perfStatus = gpu.Refresh();
                Console.Error.WriteLine(perfStatus == 0
                    ? $"  [{i}] perf: {gpu.Perf!.Value.Temperature / 10.0} C, fan {gpu.Perf.Value.FanRpm} RPM, power {gpu.Perf.Value.Power / 10.0}%, powered {gpu.Perf.Value.PowerStateOverride}"
                    : $"  [{i}] perf: status 0x{perfStatus:X8}");
                gpus.Add(gpu);
            }
        }

        return gpus;
    }

    public int Refresh()
    {
        var perf = new PerfData { PhysicalAdapterIndex = Index };
        var status = Query(_handle, PerfDataType, ref perf);
        Perf = status == 0 ? perf : null;
        return status;
    }

    public void Dispose()
    {
        if (!_owner) return;
        var close = new CloseAdapter { Handle = _handle };
        _ = D3DKMTCloseAdapter(ref close);
    }

    private static int Query<T>(uint handle, uint type, ref T data) where T : unmanaged
    {
        fixed (T* p = &data)
        {
            var query = new QueryAdapterInfo { Handle = handle, Type = type, Data = (IntPtr)p, Size = (uint)sizeof(T) };
            return D3DKMTQueryAdapterInfo(ref query);
        }
    }

    private static void Check(int status, string call)
    {
        if (status != 0) throw new InvalidOperationException($"{call} failed with 0x{status:X8}");
    }

    private struct EnumAdapters2 { public uint Count; public IntPtr Adapters; }
    private struct AdapterInfo { public uint Handle; public uint LuidLow; public int LuidHigh; public uint Sources; public int PrecisePresent; }
    private struct QueryAdapterInfo { public uint Handle; public uint Type; public IntPtr Data; public uint Size; }
    private struct CloseAdapter { public uint Handle; }
    private struct AdapterRegistryInfo { public fixed char AdapterString[260]; public fixed char BiosString[260]; public fixed char DacType[260]; public fixed char ChipType[260]; }
    private struct PerfDataCaps { public uint PhysicalAdapterIndex; public ulong MaxMemoryBandwidth, MaxPcieBandwidth; public uint MaxFanRpm, TemperatureMax, TemperatureWarning; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 e);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo q);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref CloseAdapter c);
}

/// <summary>D3DKMT_ADAPTER_PERFDATA: temperature in tenths of a degree C, power in tenths of a percent.</summary>
struct PerfData
{
    public uint PhysicalAdapterIndex;
    public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOc, MemoryBandwidth, PcieBandwidth;
    public uint FanRpm, Power, Temperature;
    public byte PowerStateOverride;
}

static class Power
{
    public struct ProcessorPower { public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState; }

    public static ProcessorPower[] Read()
    {
        var processors = new ProcessorPower[GetActiveProcessorCount(0xFFFF)];
        return CallNtPowerInformation(11, IntPtr.Zero, 0, processors, (uint)(processors.Length * Marshal.SizeOf<ProcessorPower>())) == 0
            ? processors : [];
    }

    [DllImport("powrprof.dll")] private static extern int CallNtPowerInformation(int level, IntPtr input, uint inputLength, [Out] ProcessorPower[] output, uint outputLength);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
}

sealed class Pdh : IDisposable
{
    private const uint FormatDouble = 0x200, NoCap100 = 0x8000;
    private readonly IntPtr _query;

    public Pdh()
    {
        var status = PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (status != 0) throw new InvalidOperationException($"PdhOpenQuery failed with 0x{status:X8}");
    }

    public IntPtr? Add(string path) => PdhAddEnglishCounterW(_query, path, IntPtr.Zero, out var counter) == 0 ? counter : null;
    public void Collect() => _ = PdhCollectQueryData(_query);
    public void Dispose() => _ = PdhCloseQuery(_query);

    public static double? Value(IntPtr? counter) =>
        counter is { } c && PdhGetFormattedCounterValue(c, FormatDouble | NoCap100, IntPtr.Zero, out var v) == 0 && v.Status <= 1
            ? v.Value : null;

    public static Dictionary<string, double> Array(IntPtr? counter)
    {
        var result = new Dictionary<string, double>();
        if (counter is not { } c) return result;
        uint size = 0;
        _ = PdhGetFormattedCounterArrayW(c, FormatDouble | NoCap100, ref size, out _, IntPtr.Zero);
        if (size == 0) return result;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(c, FormatDouble | NoCap100, ref size, out var count, buffer) != 0) return result;
            for (var i = 0; i < count; i++)
            {
                var item = buffer + i * 24; // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, status, padding, double.
                if (Marshal.ReadInt32(item, 8) <= 1)
                    result[Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "?"] = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, 16));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }

        return result;
    }

    private struct CounterValue { public uint Status; public double Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint size, out uint count, IntPtr buffer);
}
