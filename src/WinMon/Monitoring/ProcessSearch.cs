using System.Globalization;

namespace WinMon.Monitoring;

/// <summary>Search ranked rows without changing their order or grouped totals.</summary>
public static class ProcessSearch
{
    public static IEnumerable<ProcessUsage> Filter(IEnumerable<ProcessUsage> processes, string query,
        bool appsOnly = false)
    {
        if (appsOnly) processes = processes.Where(p => p.HasAppWindow == true);
        query = query.Trim();
        if (query.Length == 0) return processes;
        // A PID is formatted as digits and possibly a sign, so any other character rules out a PID match.
        var numeric = !query.AsSpan().ContainsAnyExcept("-0123456789");
        return processes.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    p.AppName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    p.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                    (numeric && MatchesId(p, query)));
    }

    private static bool MatchesId(ProcessUsage process, string query)
    {
        return process.MemberIds.Count == 0
            ? Contains(process.ProcessId, query)
            : process.MemberIds.Any(id => Contains(id, query));

        static bool Contains(int id, string query)
        {
            Span<char> digits = stackalloc char[11];
            return id.TryFormat(digits, out var length, default, CultureInfo.InvariantCulture) &&
                   digits[..length].Contains(query, StringComparison.Ordinal);
        }
    }
}