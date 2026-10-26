using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using EnumerationOptions = System.Management.EnumerationOptions;

namespace WinMon.Monitoring;

/// <summary>Read-only hardware observations supplied by Windows Management Instrumentation.</summary>
public interface IHardwareQuery
{
    IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties);
}

public sealed class HardwareRow : Dictionary<string, object?>
{
    public HardwareRow() : base(StringComparer.OrdinalIgnoreCase) { }

    public string Text(string key) => TryGetValue(key, out var value)
        ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
        : "";

    public ulong? Number(string key) => ulong.TryParse(Text(key), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out var number) ? number : null;
}

public sealed class WmiHardwareQuery : IHardwareQuery
{
    public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties)
    {
        try
        {
            var options = new EnumerationOptions
            {
                ReturnImmediately = true, Rewindable = false,
                Timeout = TimeSpan.FromSeconds(2), BlockSize = 1
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope),
                new ObjectQuery($"SELECT {string.Join(",", properties)} FROM {className}"), options);
            using var results = searcher.Get();
            var rows = new List<HardwareRow>();
            var elapsed = Stopwatch.StartNew();
            foreach (var item in results)
                using (item)
                {
                    if (rows.Count >= 256 || elapsed.Elapsed > TimeSpan.FromSeconds(4))
                        return null;
                    var row = new HardwareRow();
                    foreach (var property in properties) row[property] = item[property];
                    rows.Add(row);
                }

            return rows;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or
                                       SecurityException or TimeoutException)
        {
            Trace.TraceInformation("Hardware inventory unavailable ({0}): {1}", className, ex.Message);
            return null;
        }
    }
}
