using System.Globalization;

namespace WinMon.Monitoring;

/// <summary>Search ranked rows without changing their order or grouped totals.</summary>
public static class ProcessSearch
{
    public static IEnumerable<ProcessUsage> Filter(IEnumerable<ProcessUsage> processes, string query, bool appsOnly = false)
    {
        if (appsOnly) processes = processes.Where(p => p.HasAppWindow == true);
        query = query.Trim();
        if (query.Length == 0) return processes;
        return processes.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.AppName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            p.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (p.MemberIds.Count > 0 ? p.MemberIds : [p.ProcessId])
            .Any(id => id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.Ordinal)));
    }
}
