using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace WinMon.Monitoring;

/// <summary>Reads friendly executable names on the sampling thread, once per cached path.</summary>
internal sealed class ProcessAppNameProvider
{
    private const int Capacity = 1024;
    private readonly Dictionary<string, (string Description, string Product, string Publisher)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, (string Description, string Product, string Publisher)> _read;

    internal ProcessAppNameProvider(Func<string, (string Description, string Product, string Publisher)>? read = null)
    {
        _read = read ?? Read;
    }

    internal (string Description, string Product, string Publisher) Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ("", "", "");
        if (_cache.TryGetValue(path, out var names)) return names;
        try
        {
            names = _read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or SecurityException)
        {
            // An inaccessible or removed executable must not interrupt process sampling.
            names = ("", "", "");
        }
        if (_cache.Count >= Capacity) _cache.Remove(_cache.Keys.First());
        _cache[path] = names;
        return names;
    }

    private static (string Description, string Product, string Publisher) Read(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return (info.FileDescription?.Trim() ?? "", info.ProductName?.Trim() ?? "", info.CompanyName?.Trim() ?? "");
    }
}
