using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using WinMon.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Only elevated setup may provision this fixed, administrator-protected policy.</summary>
internal static class ServicePolicy
{
    internal static string InstallDirectory => NetworkServiceProtocol.InstallDirectory;

    internal static IReadOnlyCollection<string> Load()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing service executable.");
        if (!string.Equals(Path.GetDirectoryName(executable), InstallDirectory, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                "Network service must run from its protected installation directory.");
        var directory = new DirectoryInfo(InstallDirectory);
        var programFiles = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        for (var current = directory; current is not null; current = current.Parent)
        {
            CheckProtected(current);
            if (string.Equals(current.FullName, programFiles, StringComparison.OrdinalIgnoreCase)) break;
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(directory);
        var entries = 0;
        while (pending.TryPop(out var current))
            foreach (var entry in current.EnumerateFileSystemInfos())
            {
                if (++entries > 4096) throw new InvalidDataException("Service installation contains too many entries.");
                CheckProtected(entry);
                if (entry is DirectoryInfo child) pending.Push(child);
            }

        var path = Path.Combine(InstallDirectory, "allowed-users.json");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 8192) throw new InvalidDataException("Invalid authorization policy size.");
        var users = JsonSerializer.Deserialize<string[]>(stream) ??
                    throw new InvalidDataException("Missing authorization policy.");
        return ValidateUsers(users);
    }

    internal static IReadOnlyCollection<string> ValidateUsers(string[] users)
    {
        if (users.Length is 0 or > 64) throw new InvalidDataException("Invalid number of provisioned users.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var user in users)
        {
            if (string.IsNullOrEmpty(user) || user.Length > 184)
                throw new InvalidDataException("Invalid provisioned SID.");
            var sid = new SecurityIdentifier(user);
            if (sid.Value != user || sid.IsWellKnown(WellKnownSidType.WorldSid) ||
                sid.IsWellKnown(WellKnownSidType.AnonymousSid) ||
                sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid))
                throw new InvalidDataException("Authorization requires explicit account SIDs.");
            result.Add(sid.Value);
        }

        return result;
    }

    private static void CheckProtected(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Service installation cannot contain reparse points.");
        var security = item is DirectoryInfo directory
            ? (FileSystemSecurity)directory.GetAccessControl()
            : ((FileInfo)item).GetAccessControl();
        if (!TrustedWriter((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!))
            throw new UnauthorizedAccessException("Service installation has an untrusted owner.");
        const FileSystemRights write = FileSystemRights.Write | FileSystemRights.Delete |
                                       FileSystemRights.DeleteSubdirectoriesAndFiles |
                                       FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 &&
                rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & write) != 0 &&
                !TrustedWriter((SecurityIdentifier)rule.IdentityReference))
                throw new UnauthorizedAccessException("Service installation is writable by an untrusted account.");
    }

    private static bool TrustedWriter(SecurityIdentifier sid)
    {
        return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
               sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
        // TrustedInstaller
    }
}