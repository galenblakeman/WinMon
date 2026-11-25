namespace WinMon.Monitoring;

/// <summary>The stable identity of recurring application failures. Versions deliberately do not participate.</summary>
public sealed record CrashGroupKey(CrashKind Kind, string Application, string? Module, uint? ExceptionCode, ulong? Offset);

/// <summary>A version pair observed for one recurring failure signature.</summary>
public sealed record CrashVersionCount(string ApplicationVersion, string? ModuleVersion, int Count);

/// <summary>A recurring application failure within a retained event range.</summary>
public sealed record CrashGroup(CrashGroupKey Key, int Count, DateTimeOffset FirstTime, DateTimeOffset LastTime,
    IReadOnlyList<CrashVersionCount> Versions, IReadOnlyList<IncidentEvent> Events);

/// <summary>Validation shared by event readers and persisted incident recordings.</summary>
public static class CrashSignatures
{
    /// <summary>Checks the bounded, path-free structured fields accepted from the Windows event log.</summary>
    public static bool IsValid(CrashSignature? signature)
    {
        if (signature is null || !Enum.IsDefined(signature.Kind) || !ValidName(signature.Application) ||
            !ValidVersion(signature.ApplicationVersion)) return false;
        var crash = signature.Kind == CrashKind.Crash;
        return crash == (signature.Module is not null) && crash == (signature.ModuleVersion is not null) &&
            crash == signature.ExceptionCode.HasValue && crash == signature.Offset.HasValue &&
            (!crash || ValidName(signature.Module!) && ValidVersion(signature.ModuleVersion!));
    }

    private static bool ValidName(string value) => value is { Length: > 0 and <= 260 } && !string.IsNullOrWhiteSpace(value) &&
        value.IndexOfAny(['\0', '\r', '\n', '\t', '\\', '/', ':']) < 0 && !value.Any(char.IsControl);

    private static bool ValidVersion(string value) => value is { Length: > 0 and <= 260 } &&
        value.IndexOfAny(['\0', '\r', '\n', '\t']) < 0 && !value.Any(char.IsControl) &&
        Version.TryParse(value, out _);
}

/// <summary>Builds display-neutral recurring failure groups from structured Windows event data.</summary>
public static class CrashGroups
{
    /// <summary>Groups signed events case-insensitively by identity while retaining the first observed display casing.</summary>
    public static IReadOnlyList<CrashGroup> Build(IEnumerable<IncidentEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var groups = new Dictionary<CrashGroupKey, List<IncidentEvent>>(new KeyComparer());
        foreach (var item in events)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.CrashSignature is not { } signature || !CrashSignatures.IsValid(signature)) continue;
            var key = new CrashGroupKey(signature.Kind, signature.Application, signature.Module,
                signature.ExceptionCode, signature.Offset);
            if (!groups.TryGetValue(key, out var members)) groups.Add(key, members = []);
            members.Add(item);
        }

        return groups.Select(pair => Create(pair.Key, pair.Value)).OrderByDescending(group => group.Count)
            .ThenByDescending(group => group.LastTime).ToArray();
    }

    /// <summary>Returns the short display name for well-known exception codes, if one is defined.</summary>
    public static string? ExceptionName(uint? code) => code switch
    {
        0xC0000005 => "Access violation",
        0xC0000409 => "Stack buffer overrun",
        0xE0434352 => "Unhandled .NET exception",
        _ => null
    };

    private static CrashGroup Create(CrashGroupKey key, List<IncidentEvent> events)
    {
        var versions = events.Select(item => item.CrashSignature!).GroupBy(signature =>
                new VersionKey(signature.ApplicationVersion, signature.ModuleVersion), new VersionComparer())
            .Select(group => new CrashVersionCount(group.First().ApplicationVersion, group.First().ModuleVersion, group.Count()))
            .OrderByDescending(item => item.Count).ThenBy(item => item.ApplicationVersion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ModuleVersion, StringComparer.OrdinalIgnoreCase).ToArray();
        return new CrashGroup(key, events.Count, events.Min(item => item.Timestamp), events.Max(item => item.Timestamp), versions, events);
    }

    private sealed record VersionKey(string ApplicationVersion, string? ModuleVersion);

    private sealed class KeyComparer : IEqualityComparer<CrashGroupKey>
    {
        public bool Equals(CrashGroupKey? x, CrashGroupKey? y) => x is null ? y is null : y is not null &&
            x.Kind == y.Kind && x.ExceptionCode == y.ExceptionCode && x.Offset == y.Offset &&
            string.Equals(x.Application, y.Application, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Module, y.Module, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(CrashGroupKey value) => HashCode.Combine(value.Kind,
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Application),
            value.Module is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.Module), value.ExceptionCode, value.Offset);
    }

    private sealed class VersionComparer : IEqualityComparer<VersionKey>
    {
        public bool Equals(VersionKey? x, VersionKey? y) => x is null ? y is null : y is not null &&
            string.Equals(x.ApplicationVersion, y.ApplicationVersion, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ModuleVersion, y.ModuleVersion, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(VersionKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.ApplicationVersion),
            value.ModuleVersion is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value.ModuleVersion));
    }
}
