using System.Security.AccessControl;
using System.Security.Principal;

namespace WinMon.NetworkSetup;

internal static class ProtectedInstallation
{
    internal static string Parent => Path.GetDirectoryName(SetupPaths.InstallDirectory)!;

    internal static void PrepareParent()
    {
        Check(new DirectoryInfo(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        if (!Directory.Exists(Parent)) FileSystemAclExtensions.CreateDirectory(Security(), Parent);
        Check(new DirectoryInfo(Parent));
    }

    internal static DirectorySecurity Security()
    {
        var security = new DirectorySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights) in new[]
                 {
                     (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize)
                 })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
        return security;
    }

    internal static FileSecurity FileSecurity()
    {
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights) in new[]
                 {
                     (WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl),
                     (WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize)
                 })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), rights,
                AccessControlType.Allow));
        return security;
    }

    internal static void CreateDirectory(string path)
    {
        new DirectoryInfo(path).Create(Security());
    }

    internal static FileStream CreateFile(string path, FileMode mode, FileAccess access, FileShare share,
        FileOptions options = FileOptions.None)
    {
        return FileSystemAclExtensions.Create(new FileInfo(path), mode,
            access switch
            {
                FileAccess.Read => FileSystemRights.ReadData, FileAccess.Write => FileSystemRights.WriteData,
                FileAccess.ReadWrite => FileSystemRights.ReadData | FileSystemRights.WriteData,
                _ => throw new ArgumentOutOfRangeException(nameof(access))
            } | FileSystemRights.Synchronize,
            share, 4096, options, FileSecurity());
    }

    internal static IReadOnlyList<FileSystemInfo> Tree(string root)
    {
        var result = new List<FileSystemInfo>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));
        while (pending.TryPop(out var directory))
        {
            Check(directory);
            result.Add(directory);
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                if (result.Count + pending.Count >= 4096)
                    throw new InvalidDataException("Installation contains too many files.");
                Check(item);
                if (item is DirectoryInfo child) pending.Push(child);
                else result.Add(item);
            }
        }

        return result;
    }

    internal static void Check(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Reparse installation path.");
        var security = item is DirectoryInfo directory
            ? (FileSystemSecurity)directory.GetAccessControl()
            : ((FileInfo)item).GetAccessControl();
        if (!Trusted((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!))
            throw new UnauthorizedAccessException("Untrusted installation owner.");
        const FileSystemRights write = FileSystemRights.Write | FileSystemRights.Delete |
                                       FileSystemRights.DeleteSubdirectoriesAndFiles |
                                       FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & write) != 0 && !Trusted((SecurityIdentifier)rule.IdentityReference))
                throw new UnauthorizedAccessException("Untrusted installation writer.");
    }

    private static bool Trusted(SecurityIdentifier sid)
    {
        return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
               sid.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    }
}