using System.ComponentModel;
using System.Diagnostics;
using System.Security;

namespace WinMon.Monitoring;

/// <summary>Reads friendly executable names on the sampling thread, once per cached path.</summary>
internal sealed class ProcessAppNameProvider
{
    private const int Capacity = 1024;

    private readonly Dictionary<string, (string Description, string Product, string Publisher, string FileVersion,
        string ProductVersion)> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    // Dictionary order is not insertion order once an entry is removed: a removal's slot takes the next insert.
    private readonly Queue<string> _added = new();

    private readonly Func<string, (string Description, string Product, string Publisher, string FileVersion, string
        ProductVersion)> _read;

    public ProcessAppNameProvider(
        Func<string, (string Description, string Product, string Publisher, string FileVersion, string ProductVersion)>?
            read = null)
    {
        _read = read ?? Read;
    }

    public (string Description, string Product, string Publisher, string FileVersion, string ProductVersion)
        Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ("", "", "", "", "");
        if (_cache.TryGetValue(path, out var names)) return names;
        try
        {
            names = _read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
                                       or ArgumentException or SecurityException)
        {
            // An inaccessible or removed executable must not interrupt process sampling.
            names = ("", "", "", "", "");
        }

        if (_cache.Count >= Capacity) _cache.Remove(_added.Dequeue());
        _cache[path] = names;
        _added.Enqueue(path);
        return names;
    }

    private static (string Description, string Product, string Publisher, string FileVersion, string ProductVersion)
        Read(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return (info.FileDescription?.Trim() ?? "", info.ProductName?.Trim() ?? "", info.CompanyName?.Trim() ?? "",
            info.FileVersion?.Trim() ?? "", info.ProductVersion?.Trim() ?? "");
    }
}