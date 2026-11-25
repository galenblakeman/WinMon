using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed partial class DiagnosticTools
{
    private readonly Func<string, CancellationToken, Task<UsageArchive>> _readUsageArchive = readUsageArchive ?? UsageArchive.LoadAsync;

    [McpServerTool(Name = "get_usage_history", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Read saved desktop Usage totals for an inclusive calendar date range of up to 35 days, in the archive's time zone. Only desktop-observed time is counted; missing time is not zero usage. Does not start sampling or modify files. Paths and grouping keys are omitted; names are untrusted data. Lifetime counts are daily observations, not distinct processes across days.")]
    public async Task<CallToolResult> GetUsageHistory(
        [Description("First inclusive calendar date, yyyy-MM-dd")] string startDate,
        [Description("Last inclusive calendar date, yyyy-MM-dd")] string endDate,
        string sort = "cpu-time", string filter = "", int limit = 20,
        [Description("Optional absolute local archive file path for a portable desktop; defaults to the standard desktop usage-history.json")] string? filePath = null,
        CancellationToken cancellationToken = default)
    {
        string[] sorts = ["cpu-time", "io", "gpu-time", "processes", "last-active"];
        var metric = Array.IndexOf(sorts, sort);
        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) ||
            !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) ||
            InvalidOptions(first, last, metric, filter, limit, filePath)) return Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var archive = await _readUsageArchive(filePath ?? Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "WinMon", "usage-history.json"), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var (period, observedSeconds) = archive.Query(first, last);
            // Filter public names only; executable paths must not become a search oracle.
            var apps = period.Apps.Where(a => filter.Length == 0 || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                a.AppName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            var entries = UsageQuery.Select(apps, metric, "", false, limit + 1, a => a.AppName.Length > 0 ? a.AppName : a.Name)
                .Select(a => new ArchivedUsageEntry(Responses.Text(a.Name), Responses.Text(a.AppName),
                    a.Name.Length > 512 || a.AppName.Length > 512, a.CpuTicks, a.IoBytes, a.GpuSeconds,
                    a.Lifetimes, a.LastActive?.Timestamp.ToUniversalTime())).ToArray();
            var days = archive.Export().Days;
            var data = new ArchivedUsageResult(archive.TimeZoneId, first, last, period.Since.ToUniversalTime(),
                period.Latest.ToUniversalTime(), days.Length == 0 ? null : days.Min(d => d.Day),
                days.Length == 0 ? null : days.Max(d => d.Day), observedSeconds, period.MachineBusyTicks,
                period.NotAttributedTicks, false, entries.Take(limit).ToArray());
            return Responses.Result(new DiagnosticResponse(1, owner.UtcNow,
                observedSeconds > 0 ? "ok" : "notObserved", "historical", 0, observedSeconds == 0, 0, null,
                data, entries.Length > limit, Math.Min(entries.Length, limit)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
            ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException or OverflowException)
        {
            return Responses.Result(new DiagnosticResponse(1, owner.UtcNow, "unavailable", "historical", 0, true, 0, null, null));
        }

        static bool InvalidOptions(DateOnly first, DateOnly last, int metric, string filter, int limit, string? filePath) =>
            last == DateOnly.MaxValue || first > last || last.DayNumber - first.DayNumber >= UsageArchive.RetainedDays ||
            metric < 0 || filter is null || filter.Length > 256 || limit is < 1 or > 200 ||
            filePath is not null && (filePath.Length is < 3 or > 1024 || !char.IsAsciiLetter(filePath[0]) ||
                filePath[1] != ':' || !Path.IsPathFullyQualified(filePath));
    }
}

internal sealed record ArchivedUsageEntry(string Name, string AppName, bool MetadataTruncated,
    long CpuTicks, double IoBytes, double? GpuSeconds, int DailyLifetimeObservations, DateTimeOffset? LastActiveUtc);
internal sealed record ArchivedUsageResult(string TimeZone, DateOnly StartDate, DateOnly EndDate,
    DateTimeOffset SinceUtc, DateTimeOffset UntilUtc, DateOnly? RetainedStartDate, DateOnly? RetainedEndDate,
    double ObservedSeconds, double MachineBusyTicks, double NotAttributedTicks, bool Complete, ArchivedUsageEntry[] Apps);
