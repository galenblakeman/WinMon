namespace WinMon.Monitoring;

/// <summary>Shared filtering and ranking policy for executable usage totals.</summary>
public static class UsageQuery
{
    /// <summary>
    /// Returns entries matching <paramref name="filter"/>, ranked by the Usage tab column identified by
    /// <paramref name="sort"/>. Zero <paramref name="top"/> keeps all matching entries.
    /// </summary>
    /// <remarks>
    /// The display-name callback deliberately belongs to the caller: the monitor core does not own localized
    /// presentation names, while the callback still makes name searching and tie-breaking identical for every UI.
    /// </remarks>
    public static IReadOnlyList<AppUsage> Select(IReadOnlyList<AppUsage> apps, int sort, string filter,
        bool appsOnly, int top, Func<AppUsage, string> displayName)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(displayName);

        var query = filter.Trim();
        var filtered = apps.Where(app => (!appsOnly || app.IsApp) && Matches(app, query, displayName));
        var ordered = sort switch
        {
            1 => filtered.OrderByDescending(app => app.IoBytes),
            2 => filtered.OrderByDescending(app => app.GpuSeconds ?? -1),
            3 => filtered.OrderByDescending(app => app.Lifetimes).ThenByDescending(app => app.Running),
            4 => filtered.OrderByDescending(app => app.LastActive?.Timestamp ?? DateTimeOffset.MinValue),
            _ => filtered.OrderByDescending(app => app.CpuTicks)
        };
        var named = ordered.ThenBy(displayName, StringComparer.CurrentCultureIgnoreCase);
        return top == 0 ? [.. named] : [.. named.Take(top)];
    }

    private static bool Matches(AppUsage app, string query, Func<AppUsage, string> displayName)
    {
        return query.Length == 0 ||
               displayName(app).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               app.ImagePath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
