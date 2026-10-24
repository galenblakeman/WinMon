using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinMon.Monitoring;

/// <summary>
/// Names, writes, counts and prunes automatic capture reports in a folder that may hold other files.
/// </summary>
/// <remarks>
/// A file is WinMon's only if it sits directly in the folder, its name matches the WinMon pattern exactly, it is a
/// plain file (not a link, read-only, hidden or system), and its first line is the fixed WinMon line. Anything else
/// is never deleted or counted, so a renamed report or someone else's similar file is safe.
/// </remarks>
internal static class CaptureReports
{
    internal const int Keep = 200;
    private const string FirstLinePrefix = "WinMon automatic capture v1 · id ";

    // Plain regular expressions: generated ones would add types outside the Monitoring namespace.
    private static readonly Regex NamePattern = new(
        @"^WinMon-capture_(?<time>[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{2}-[0-9]{2}-[0-9]{2})(?:_o(?<offset>[+-][0-9]{4}))?_(cpu|memory|disk1|disk2|network|gpu|process)(_(?<n>[2-9]|[1-9][0-9]{1,3}))?\.txt$",
        RegexOptions.CultureInvariant);

    private static readonly Regex FirstLinePattern =
        new("^WinMon automatic capture v1 · id [0-9a-f]{32}$", RegexOptions.CultureInvariant);

    /// <summary>The fixed, unlocalized line every report starts with.</summary>
    internal static string FirstLine(Guid id)
    {
        return FirstLinePrefix + id.ToString("N");
    }

    /// <summary>The rule token in file names; never localized.</summary>
    internal static string Token(CaptureMetric metric)
    {
        return metric switch
        {
            CaptureMetric.Cpu => "cpu", CaptureMetric.Memory => "memory", CaptureMetric.PrimaryDisk => "disk1",
            CaptureMetric.SecondaryDisk => "disk2", CaptureMetric.Network => "network", CaptureMetric.Gpu => "gpu",
            _ => "process"
        };
    }

    internal static string FileName(DateTimeOffset fired, CaptureMetric metric, int copy = 1)
    {
        var local = fired.ToLocalTime();
        var time = local.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var offset = local.Offset;
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();
        var offsetText = string.Create(CultureInfo.InvariantCulture, $"{sign}{absolute.Hours:00}{absolute.Minutes:00}");
        return $"WinMon-capture_{time}_o{offsetText}_{Token(metric)}{(copy > 1 ? "_" + copy.ToString(CultureInfo.InvariantCulture) : "")}.txt";
    }

    /// <summary>Write a report under a name no other file has, and return its path.</summary>
    /// <param name="text">The whole report, starting with <see cref="FirstLine"/>.</param>
    /// <exception cref="IOException">The folder cannot be created or written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    internal static string Write(string folder, DateTimeOffset fired, CaptureMetric metric, string text)
    {
        Directory.CreateDirectory(folder);
        for (var copy = 1; copy < 1000; copy++)
        {
            var path = Path.Combine(folder, FileName(fired, metric, copy));
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        throw new IOException("Too many reports in the same second.");
    }

    /// <summary>WinMon's reports in a folder, newest first by the time in their names.</summary>
    internal static IReadOnlyList<string> Find(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var reports = new List<(string Path, long Moment, int Copy)>();
        foreach (var path in Directory.EnumerateFiles(folder, "WinMon-capture_*.txt", SearchOption.TopDirectoryOnly))
        {
            var match = NamePattern.Match(Path.GetFileName(path));
            if (!match.Success || !IsOwn(path) || !TryMoment(path, match, out var moment)) continue;
            var copy = match.Groups["n"].Success
                ? int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture)
                : 1;
            reports.Add((path, moment, copy));
        }

        return
        [
            .. reports.OrderByDescending(r => r.Moment).ThenByDescending(r => r.Copy)
                .Select(r => r.Path)
        ];
    }

    private static bool TryMoment(string path, Match match, out long moment)
    {
        moment = 0;
        if (!DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd_HH-mm-ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        try
        {
            if (match.Groups["offset"].Success)
            {
                var text = match.Groups["offset"].Value;
                var hours = int.Parse(text.AsSpan(1, 2), CultureInfo.InvariantCulture);
                var minutes = int.Parse(text.AsSpan(3, 2), CultureInfo.InvariantCulture);
                if (minutes >= 60 || hours * 60 + minutes > 14 * 60) return false;
                var offset = TimeSpan.FromMinutes((text[0] == '-' ? -1 : 1) * (hours * 60 + minutes));
                moment = new DateTimeOffset(local, offset).UtcTicks;
                return true;
            }

            // Older names have no offset. During the repeated hour, their creation time is the only available clue.
            moment = TimeZoneInfo.Local.IsAmbiguousTime(local)
                ? File.GetCreationTimeUtc(path).Ticks
                : new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).UtcTicks;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon left capture report {0} alone: {1}", path, ex.Message);
            return false;
        }
    }

    /// <summary>Delete WinMon's reports beyond the newest <paramref name="keep"/>. Never throws.</summary>
    internal static void Prune(string folder, int keep = Keep)
    {
        try
        {
            foreach (var path in Find(folder).Skip(keep))
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning("WinMon could not prune capture report {0}: {1}", path, ex.Message);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon could not list capture reports: {0}", ex.Message);
        }
    }

    private static bool IsOwn(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.ReadOnly |
                               FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
            Span<byte> start = stackalloc byte[128];
            int read;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                read = stream.ReadAtLeast(start, start.Length, false);
            var text = Encoding.UTF8.GetString(start[..read]);
            var end = text.IndexOfAny(['\r', '\n']);
            return end > 0 && FirstLinePattern.IsMatch(text[..end]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("WinMon left capture report {0} alone: {1}", path, ex.Message);
            return false;
        }
    }
}
