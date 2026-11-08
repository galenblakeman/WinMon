using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace WinMon.NetworkSetup;

internal static class SetupRegistration
{
    private const string KeyName = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinMonNetworkMonitor";

    private static readonly string[] Names =
        ["DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "UninstallString", "NoModify", "NoRepair"];

    internal static Dictionary<string, object>? Capture()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyName);
        if (key is null) return null;
        ValidateSecurity(key.GetAccessControl());
        if (!Equals(key.GetValue("InstallLocation"), SetupPaths.InstallDirectory) ||
            !Equals(key.GetValue("UninstallString"), $"\"{SetupPaths.InstalledSetupExecutable}\" --uninstall"))
            throw new UnauthorizedAccessException("Unrelated uninstall registration.");
        return Names.Where(name => key.GetValue(name) is not null)
            .ToDictionary(name => name, name => key.GetValue(name)!);
    }

    internal static void Write()
    {
        using var key = CreateKey();
        key.SetValue("DisplayName", "WinMon Network Monitoring Service");
        key.SetValue("DisplayVersion", typeof(SetupRegistration).Assembly.GetName().Version!.ToString(3));
        key.SetValue("Publisher", "Galen Blakeman");
        key.SetValue("InstallLocation", SetupPaths.InstallDirectory);
        key.SetValue("UninstallString", $"\"{SetupPaths.InstalledSetupExecutable}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    internal static void Restore(Dictionary<string, object>? values)
    {
        Registry.LocalMachine.DeleteSubKeyTree(KeyName, false);
        if (values is null) return;
        using var key = CreateKey();
        foreach (var (name, value) in values)
            key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    internal static RegistrySecurity Security()
    {
        var security = new RegistrySecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        security.SetAccessRuleProtection(true, false);
        foreach (var (sid, rights) in new[]
                 {
                     (WellKnownSidType.LocalSystemSid, RegistryRights.FullControl),
                     (WellKnownSidType.BuiltinAdministratorsSid, RegistryRights.FullControl),
                     (WellKnownSidType.BuiltinUsersSid, RegistryRights.ReadKey)
                 })
            security.AddAccessRule(new RegistryAccessRule(new SecurityIdentifier(sid, null), rights,
                AccessControlType.Allow));
        return security;
    }

    internal static void ValidateSecurity(RegistrySecurity security)
    {
        if (!Trusted((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))!))
            throw new UnauthorizedAccessException("Untrusted uninstall registration owner.");
        const RegistryRights write = RegistryRights.SetValue | RegistryRights.CreateSubKey | RegistryRights.CreateLink |
                                     RegistryRights.Delete |
                                     RegistryRights.ChangePermissions | RegistryRights.TakeOwnership;
        foreach (RegistryAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if ((rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 &&
                rule.AccessControlType == AccessControlType.Allow &&
                (rule.RegistryRights & write) != 0 && !Trusted((SecurityIdentifier)rule.IdentityReference))
                throw new UnauthorizedAccessException("Untrusted uninstall registration writer.");
    }

    private static RegistryKey CreateKey()
    {
        return Registry.LocalMachine.CreateSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryOptions.None, Security());
    }

    private static bool Trusted(SecurityIdentifier sid)
    {
        return sid.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid);
    }
}