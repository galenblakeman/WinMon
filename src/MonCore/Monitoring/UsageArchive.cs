using System.Text.Json;

namespace MonCore.Monitoring;

public sealed record UsageArchiveDay(DateOnly Day, UsageLedgerState Totals, double ObservedSeconds);
public sealed record UsageArchiveState(int SchemaVersion, string TimeZoneId, UsageArchiveDay[] Days);

/// <summary>35 local calendar days of observed usage; missing monitoring time is never filled in.</summary>
public sealed class UsageArchive
{
    public const int RetainedDays = 35;
    public const long MaximumFileBytes = 32 * 1024 * 1024;
    private readonly SortedDictionary<DateOnly, (UsageLedger Ledger, double Seconds)> _days = [];
    private readonly TimeZoneInfo _zone;
    public string TimeZoneId => _zone.Id;
    public UsageArchive(TimeZoneInfo? zone = null) => _zone = zone ?? TimeZoneInfo.Local;

    public void Add(MonitorSample sample)
    {
        if (!UsageLedger.IsValid(sample) || !double.IsFinite(sample.IntervalSeconds) || sample.IntervalSeconds is <= 0 or > 60) return;
        var end = sample.Timestamp.ToUniversalTime();
        var start = end.AddSeconds(-sample.IntervalSeconds);
        while (start < end)
        {
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, _zone).DateTime);
            var boundary = DayStart(date.AddDays(1)).ToUniversalTime();
            var until = boundary < end ? boundary : end;
            var seconds = (until - start).TotalSeconds;
            if (seconds <= 0) break;
            if (!_days.TryGetValue(date, out var day)) day = (new UsageLedger(start), 0);
            day.Ledger.Add(sample with { Timestamp = until, IntervalSeconds = seconds,
                IntervalCpuTicks = (long)Math.Round(sample.IntervalCpuTicks * seconds / sample.IntervalSeconds) });
            _days[date] = (day.Ledger, day.Seconds + seconds);
            start = until;
        }
        var oldest = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(end, _zone).DateTime).AddDays(1 - RetainedDays);
        foreach (var date in _days.Keys.TakeWhile(d => d < oldest).ToArray()) _days.Remove(date);
        while (_days.Count > RetainedDays) _days.Remove(_days.Keys.First());
    }

    public (UsagePeriod Period, double ObservedSeconds) Query(DateOnly first, DateOnly last)
    {
        var days = _days.Where(p => p.Key >= first && p.Key <= last).Select(p => p.Value).ToArray();
        var apps = days.SelectMany(d => d.Ledger.Apps).GroupBy(a => a.Key, ProcessIdentityPolicy.Windows.Comparer).Select(group =>
        {
            var latest = group.Last();
            var gpu = group.Where(a => a.GpuSeconds is not null).ToArray();
            return latest with { CpuTicks = (long)Math.Min(long.MaxValue, group.Sum(a => (double)a.CpuTicks)), IoBytes = group.Sum(a => a.IoBytes),
                GpuSeconds = gpu.Length == 0 ? null : gpu.Sum(a => a.GpuSeconds!.Value),
                Lifetimes = group.Sum(a => a.Lifetimes), Running = 0,
                LastActive = group.OrderByDescending(a => a.LastActive?.Timestamp).First().LastActive };
        }).ToArray();
        var since = DayStart(first);
        var latestTime = DayStart(last.AddDays(1));
        return (new UsagePeriod(apps, days.Sum(d => d.Ledger.MachineBusyTicks), days.Sum(d => d.Ledger.NotAttributedCpuTicks),
            since, latestTime, false) { Archived = true }, days.Sum(d => d.Seconds));
    }

    private DateTimeOffset DayStart(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (_zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = _zone.IsAmbiguousTime(local) ? _zone.GetAmbiguousTimeOffsets(local).Max() : _zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    public UsageArchiveState Export() => new(1, _zone.Id, _days.Select(p => new UsageArchiveDay(p.Key, p.Value.Ledger.Export(), p.Value.Seconds)).ToArray());

    public static UsageArchive Restore(UsageArchiveState state)
    {
        Validate(state);
        var archive = new UsageArchive(TimeZoneInfo.FindSystemTimeZoneById(state.TimeZoneId));
        foreach (var day in state.Days)
        {
            var ledger = new UsageLedger(day.Totals.Since);
            ledger.Restore(day.Totals);
            archive._days.Add(day.Day, (ledger, day.ObservedSeconds));
        }
        return archive;
    }

    private static void Validate(UsageArchiveState state)
    {
        if (state.SchemaVersion != 1 || state.Days is null || state.Days.Length > RetainedDays || state.Days.Any(d => d is null) ||
            state.Days.Select(d => d.Day).Distinct().Count() != state.Days.Length) throw new InvalidDataException("Invalid usage archive.");
        foreach (var day in state.Days)
            ValidateDay(day);
    }

    private static void ValidateDay(UsageArchiveDay day)
    {
        ValidateDayMetadata(day);
        var totals = day.Totals;
        if (totals.Apps.Any(a => !ValidApp(a)) ||
            totals.Apps.Select(a => a.Key).Distinct(ProcessIdentityPolicy.Windows.Comparer).Count() != totals.Apps.Count ||
            totals.IdentityPolicyId != ProcessIdentityPolicy.Windows.Id ||
            totals.Lifetimes.Any(l => l.ProcessId < 0 || l.CreationTicks < 0 || l.Absences < 0 || (uint)l.App >= (uint)totals.Apps.Count))
            throw new InvalidDataException("Invalid usage totals.");
    }

    private static void ValidateDayMetadata(UsageArchiveDay day)
    {
        var totals = day.Totals;
        if (!ValidTotal(day.ObservedSeconds, 1e9) || totals?.Apps is null || totals.Lifetimes is null || totals.Apps.Count > UsageLedger.MaxApps + 1 || totals.Lifetimes.Count > 100_000 ||
            !ValidTotal(totals.MachineBusyTicks, 1e17) || !ValidTotal(totals.AttributedTicks, 1e17))
            throw new InvalidDataException("Invalid usage totals.");
    }

    private static bool ValidApp(UsageAppState app) => app is not null && ValidAppIdentity(app) && ValidAppTotals(app);

    private static bool ValidAppIdentity(UsageAppState app) =>
        !string.IsNullOrEmpty(app.Key) && app.Key.Length <= 4096 &&
        app.Name is not null && app.ImagePath is not null && app.AppName is not null && app.Publisher is not null &&
        app.Name.Length <= 4096 && app.ImagePath.Length <= 4096 && app.AppName.Length <= 4096 && app.Publisher.Length <= 4096;

    private static bool ValidAppTotals(UsageAppState app) =>
        ValidTotal(app.CpuTicks, 1e17) && ValidTotal(app.IoBytes, 1e24) &&
        app.Lifetimes is >= 0 and <= 60_000_000 &&
        !(app.GpuSeconds is { } gpu && !ValidTotal(gpu, 1e10));

    private static bool ValidTotal(double value, double maximum) => double.IsFinite(value) && value >= 0 && value <= maximum;

    public static async Task<UsageArchive> LoadAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) return new UsageArchive();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            4096, FileOptions.Asynchronous);
        if (stream.Length > MaximumFileBytes) throw new InvalidDataException("Usage archive is too large.");
        return Restore(await JsonSerializer.DeserializeAsync<UsageArchiveState>(stream, cancellationToken: token)
            ?? throw new InvalidDataException("Empty usage archive."));
    }

    public static async Task SaveAsync(string path, UsageArchiveState state, CancellationToken token = default)
    {
        Validate(state);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        if (bytes.LongLength > MaximumFileBytes) throw new InvalidDataException("Usage archive is too large.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
