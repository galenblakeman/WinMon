using System.Text.Json;
using Microsoft.Win32;

namespace WinMon.Updates;

internal sealed record UpdateJob(string Target, int ParentId, long ParentStartTicks, string Version, bool Installed, string Token);
internal sealed record UpdateState(string Status, string? Error = null);

internal static class UpdateFiles
{
    internal static void WriteJson<T>(string path, T value)
    {
        using var journalLock = LockJournal(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static FileStream LockJournal(string path)
    {
        var deadline = Environment.TickCount64 + 10000;
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (Environment.TickCount64 < deadline) { Thread.Sleep(25); }
        }
    }

    internal static T ReadJson<T>(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Invalid update transaction.");
    }

    internal static string ValidateDirectory(string directory)
    {
        var full = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(UpdateClient.UpdateRoot), StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(full), "N", out _) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Invalid update staging directory.");
        return full;
    }

    internal static string BackupPath(UpdateJob job) => Path.Combine(Path.GetDirectoryName(job.Target)!, ".WinMon-" + job.Token + ".backup.exe");
    internal static string StagedPath(UpdateJob job) => Path.Combine(Path.GetDirectoryName(job.Target)!, ".WinMon-" + job.Token + ".new.exe");
}

internal static class UpdateInstallation
{
    internal const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1";

    internal static bool IsInstalled(string target)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return MatchesInstallDirectory(target, key?.GetValue("InstallLocation") as string);
    }

    internal static bool MatchesInstallDirectory(string target, string? registeredDirectory) =>
        !string.IsNullOrWhiteSpace(registeredDirectory) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(target))?.TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(registeredDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}

internal static class PortableUpdate
{
    internal static void Apply(string staged, string target, string backup)
    {
        // All three paths must share a directory/volume. Never delete the old executable first.
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(staged)), Path.GetDirectoryName(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(Path.GetFullPath(backup)), Path.GetDirectoryName(Path.GetFullPath(target)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Replacement files must share the application directory.");
        File.Replace(staged, target, backup);
    }

    internal static void Restore(string target, string backup)
    {
        if (File.Exists(target)) File.Replace(backup, target, null);
        else File.Move(backup, target);
    }
}
