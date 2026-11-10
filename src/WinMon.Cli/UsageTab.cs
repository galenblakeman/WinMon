using System.Globalization;
using System.Text;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

/// <summary>Terminal representation of the desktop Usage tab.</summary>
public static class UsageTab
{
    /// <summary>Builds the filtered and ranked executable-usage table for the terminal.</summary>
    public static TerminalTable Build(IReadOnlyList<AppUsage> apps, DateTimeOffset since, long machineBusyTicks,
        DateTimeOffset? latest, int sort, string filter, bool appsOnly, int top)
    {
        var visible = UsageQuery.Select(apps, sort, filter, appsOnly, top, DisplayName);
        var rows = visible.Select(app => new TerminalRow(app.Key,
            () => [
                DisplayName(app), CpuTime(app.CpuTicks), Share(app.CpuTicks, machineBusyTicks), Bytes(app.IoBytes),
                app.GpuSeconds is { } gpu ? Duration(TimeSpan.FromSeconds(gpu)) : Strings.MissingReading,
                Processes(app), LastActive(app.LastActive, latest)
            ], () => Details(app, since, machineBusyTicks, latest)) { PinIdentity = CliPin.Executable(app.ImagePath) }).ToArray();
        return new TerminalTable(
            [Strings.UsageApp, Strings.UsageCpuTime, Strings.UsageShare, Strings.IO, Strings.UsageGpuTime,
                Strings.ProcessesTab, Strings.UsageLastActive],
            rows, string.Format(CultureInfo.CurrentCulture, Strings.UsageReportSince, since.ToLocalTime().DateTime));
    }

    private static string DisplayName(AppUsage app)
    {
        return app.Key == UsageLedger.OtherAppsKey ? Strings.UsageOtherApps :
            app.AppName.Length > 0 ? app.AppName : app.Name;
    }

    private static string Details(AppUsage app, DateTimeOffset since, long machineBusyTicks, DateTimeOffset? latest)
    {
        var text = new StringBuilder();
        text.AppendLine(DisplayName(app));
        if (app.ImagePath.Length > 0) text.AppendLine(app.ImagePath);
        if (app.Publisher.Length > 0) text.AppendLine(app.Publisher);
        text.AppendLine(app.IsApp ? Strings.ProcessKindApp : Strings.ProcessKindBackground);
        text.AppendLine(string.Format(CultureInfo.CurrentCulture, Strings.UsageReportSince,
            since.ToLocalTime().DateTime));
        text.AppendLine($"{Strings.UsageCpuTime}: {CpuTime(app.CpuTicks)} ({Share(app.CpuTicks, machineBusyTicks)})");
        text.AppendLine($"{Strings.IO}: {Bytes(app.IoBytes)}");
        text.AppendLine($"{Strings.UsageGpuTime}: {(app.GpuSeconds is { } gpu ? Duration(TimeSpan.FromSeconds(gpu)) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.ProcessesTab}: {Processes(app)}");
        text.Append($"{Strings.UsageLastActive}: {LastActive(app.LastActive, latest)}");
        return text.ToString();
    }

    private static string CpuTime(long ticks) => Duration(TimeSpan.FromTicks(Math.Max(0, ticks)));

    private static string Share(long part, long whole)
    {
        return whole > 0
            ? string.Format(CultureInfo.CurrentCulture, Strings.PercentOneDecimal, 100d * part / whole)
            : Strings.MissingReading;
    }

    private static string Duration(TimeSpan time)
    {
        var whole = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, time.TotalSeconds)));
        return string.Create(CultureInfo.CurrentCulture,
            $"{(long)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}");
    }

    private static string Bytes(double bytes) => Controls.UsageText.Bytes(bytes);

    private static string Processes(AppUsage app)
    {
        return string.Create(CultureInfo.CurrentCulture, $"{app.Lifetimes:N0} ({app.Running:N0})");
    }

    private static string LastActive(SampleKey? active, DateTimeOffset? latest)
    {
        if (active is not { } key) return Strings.MissingReading;
        if (latest is { } now && (now - key.Timestamp).TotalSeconds <= 5) return Strings.UsageNow;
        var local = key.Timestamp.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date ? local.ToString("T", CultureInfo.CurrentCulture) :
            local.ToString("g", CultureInfo.CurrentCulture);
    }
}
