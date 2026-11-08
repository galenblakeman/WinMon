using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.NetworkSetup;

internal static class AuthorizationPolicy
{
    internal const int MaximumUsers = 64;

    internal static IReadOnlyCollection<string> AddAuthorizedUser(IEnumerable<string> existing, string sid)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in existing) set.Add(ValidateSid(item));
        set.Add(ValidateSid(sid));
        if (set.Count > MaximumUsers) throw new InvalidDataException("Too many authorized users.");
        return set.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyCollection<string> RemoveAuthorizedUser(IEnumerable<string> existing, string sid)
    {
        var removed = ValidateSid(sid);
        var set = new HashSet<string>(existing.Select(ValidateSid), StringComparer.Ordinal);
        if (!set.Remove(removed)) throw new InvalidOperationException("The account is not provisioned.");
        return set.Order(StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyCollection<string> Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 8192) throw new InvalidDataException("Invalid authorization policy.");
        var users = JsonSerializer.Deserialize(stream, AuthorizationPolicyJsonContext.Default.StringArray) ?? throw new InvalidDataException("Invalid authorization policy.");
        if (users.Length is 0 or > MaximumUsers) throw new InvalidDataException("Invalid authorization policy.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in users) result.Add(ValidateSid(user));
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    internal static void Save(string path, IEnumerable<string> users)
    {
        var normalized = users.Select(ValidateSid).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (normalized.Length is 0 or > MaximumUsers) throw new InvalidDataException("Invalid authorization policy.");
        using var stream = ProtectedInstallation.CreateFile(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, normalized, AuthorizationPolicyJsonContext.Default.StringArray);
        stream.Flush(flushToDisk: true);
    }

    internal static string ValidateSid(string sid)
    {
        if (string.IsNullOrWhiteSpace(sid) || sid.Length > 184) throw new InvalidDataException("Invalid authorization identity.");
        SecurityIdentifier parsed;
        try { parsed = new SecurityIdentifier(sid); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid authorization identity.", exception); }
        if (!string.Equals(parsed.Value, sid, StringComparison.Ordinal) || parsed.IsWellKnown(WellKnownSidType.WorldSid) ||
            parsed.IsWellKnown(WellKnownSidType.AnonymousSid) || parsed.IsWellKnown(WellKnownSidType.AuthenticatedUserSid))
            throw new InvalidDataException("Invalid authorization identity.");
        return parsed.Value;
    }
}

[JsonSerializable(typeof(string[]))]
internal sealed partial class AuthorizationPolicyJsonContext : JsonSerializerContext;
