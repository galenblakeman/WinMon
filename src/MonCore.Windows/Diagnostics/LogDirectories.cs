using System.Security.AccessControl;
using System.Security.Principal;

namespace WinMon.Diagnostics;

/// <summary>Protected shared locations for WinMon diagnostic logs.</summary>
public static class LogDirectories
{
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string Root = Path.Combine(ProgramData, "WinMon");
    private static readonly string Logs = Path.Combine(Root, "Logs");
    private static readonly string Apps = Path.Combine(Logs, "App");

    /// <summary>Per-user application log directory provisioned by network-service setup.</summary>
    public static string AppPath => Path.Combine(Apps, CurrentUserSid().Value);

    /// <summary>Network service log directory.</summary>
    public static string ServicePath => Path.Combine(Logs, "Service");

    /// <summary>Per-user fallback directory when protected shared logs are unavailable.</summary>
    public static string LocalPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", "Logs");

    internal static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(Administrators());
        }
    }

    internal static string PrepareElevatedAppPath()
    {
        // Elevated helpers must never open files in the ordinary user's writable log directory.
        Ensure(Root, ProtectedSecurity());
        Ensure(Logs, ProtectedSecurity());
        var elevated = Path.Combine(Logs, "Elevated");
        Ensure(elevated, ProtectedSecurity(allowUsers: false));
        var path = Path.Combine(elevated, CurrentUserSid().Value);
        Ensure(path, ProtectedSecurity(allowUsers: false));
        return path;
    }

    /// <summary>Creates the protected shared tree for an authorized network-monitoring user.</summary>
    public static void Provision(string requesterSid)
    {
        var user = new SecurityIdentifier(requesterSid);
        Ensure(Root, ProtectedSecurity());
        Ensure(Logs, ProtectedSecurity());
        Ensure(Apps, ProtectedSecurity());
        EnsureService(user);
        Ensure(AppPathFor(user), UserSecurity(user), user);
        ValidateServicePath();
        ValidateAppPath(user);
    }

    /// <summary>Rejects a service log path that an ordinary user could redirect or modify.</summary>
    public static void ValidateServicePath()
    {
        ValidateProtectedAncestors();
        Check(new DirectoryInfo(ServicePath), null);
    }

    /// <summary>Rejects an application log path that another user could modify.</summary>
    public static void ValidateAppPath()
        => ValidateAppPath(CurrentUserSid());

    private static void ValidateAppPath(SecurityIdentifier user)
    {
        ValidateProtectedAncestors();
        Check(new DirectoryInfo(AppPathFor(user)), user);
    }

    private static string AppPathFor(SecurityIdentifier user) => Path.Combine(Apps, user.Value);

    private static SecurityIdentifier CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new UnauthorizedAccessException("Missing current user identity.");
    }

    private static void ValidateProtectedAncestors()
    {
        Check(new DirectoryInfo(Root), null);
        Check(new DirectoryInfo(Logs), null);
        Check(new DirectoryInfo(Apps), null);
    }

    private static void Ensure(string path, DirectorySecurity security, SecurityIdentifier? user = null)
    {
        if (!Directory.Exists(path)) FileSystemAclExtensions.CreateDirectory(security, path);
        Check(new DirectoryInfo(path), user);
    }

    private static void EnsureService(SecurityIdentifier user)
    {
        if (!Directory.Exists(ServicePath))
            FileSystemAclExtensions.CreateDirectory(ServiceSecurity(user), ServicePath);
        else
        {
            Check(new DirectoryInfo(ServicePath), null);
            var security = new DirectoryInfo(ServicePath).GetAccessControl();
            Add(security, user, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
            new DirectoryInfo(ServicePath).SetAccessControl(security);
        }
        Check(new DirectoryInfo(ServicePath), null);
    }

    private static DirectorySecurity ProtectedSecurity(bool allowUsers = true)
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators());
        security.SetAccessRuleProtection(true, false);
        Add(security, LocalSystem(), FileSystemRights.FullControl);
        Add(security, Administrators(), FileSystemRights.FullControl);
        if (allowUsers) Add(security, Users(), FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
        return security;
    }

    private static DirectorySecurity UserSecurity(SecurityIdentifier user)
    {
        var security = new DirectorySecurity();
        security.SetOwner(Administrators());
        security.SetAccessRuleProtection(true, false);
        Add(security, LocalSystem(), FileSystemRights.FullControl);
        Add(security, Administrators(), FileSystemRights.FullControl);
        Add(security, user, FileSystemRights.FullControl);
        return security;
    }

    private static DirectorySecurity ServiceSecurity(SecurityIdentifier user)
    {
        var security = ProtectedSecurity(allowUsers: false);
        Add(security, user, FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize);
        return security;
    }

    private static void Add(DirectorySecurity security, SecurityIdentifier sid, FileSystemRights rights) =>
        security.AddAccessRule(new FileSystemAccessRule(sid, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

    internal static void ValidateDirectory(DirectoryInfo directory, SecurityIdentifier? user) => Check(directory, user);

    private static void Check(DirectoryInfo directory, SecurityIdentifier? user)
    {
        if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Untrusted log directory.");
        var security = directory.GetAccessControl();
        var owner = (SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!;
        if (!Trusted(owner) && (user is null || owner != user))
            throw new UnauthorizedAccessException("Untrusted log directory owner.");
        const FileSystemRights write = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                                      FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 && rule.AccessControlType == AccessControlType.Allow &&
                (rule.FileSystemRights & write) != 0 && !Trusted((SecurityIdentifier)rule.IdentityReference) &&
                (user is null || (SecurityIdentifier)rule.IdentityReference != user))
                throw new UnauthorizedAccessException("Untrusted log directory writer.");
    }

    private static bool Trusted(SecurityIdentifier sid) => sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
        sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
    private static SecurityIdentifier LocalSystem() => new(WellKnownSidType.LocalSystemSid, null);
    private static SecurityIdentifier Administrators() => new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static SecurityIdentifier Users() => new(WellKnownSidType.BuiltinUsersSid, null);
}
