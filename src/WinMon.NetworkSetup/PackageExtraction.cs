using System.IO.Compression;

namespace WinMon.NetworkSetup;

internal static class PackageExtraction
{
    private const int MaximumEntries = 256;
    private const long MaximumUncompressedBytes = 256L * 1024 * 1024;

    internal static void ExtractServiceArchive(Stream archive, string stagingDirectory)
        => ExtractServiceArchive(archive, stagingDirectory, ProtectedInstallation.CreateDirectory,
            path => ProtectedInstallation.CreateFile(path, FileMode.CreateNew, FileAccess.Write, FileShare.None));

    internal static void ExtractServiceArchive(Stream archive, string stagingDirectory, Action<string> createDirectory, Func<string, FileStream> createFile)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(stagingDirectory);
        ArgumentNullException.ThrowIfNull(createDirectory);
        ArgumentNullException.ThrowIfNull(createFile);
        if (Directory.Exists(stagingDirectory)) throw new IOException("Staging directory already exists.");
        createDirectory(stagingDirectory);
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count is 0 or > MaximumEntries) throw new InvalidDataException("Invalid service archive.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = ValidateEntryName(entry.FullName);
            if (new[] { SetupPaths.PolicyFileName, SetupPaths.SetupExecutableName, InstalledFiles.FileName }.Contains(name, StringComparer.OrdinalIgnoreCase) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Archive contains setup-owned state or a symbolic link.");
            if (!seen.Add(name)) throw new InvalidDataException("Archive contains colliding paths.");
            if (entry.Length < 0 || entry.Length > MaximumUncompressedBytes - total) throw new InvalidDataException("Archive is too large.");
            total += entry.Length;
            var destination = Path.Combine(stagingDirectory, name);
            var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Invalid service archive path.");
            createDirectory(parent);
            using var source = entry.Open();
            using var target = createFile(destination);
            var buffer = new byte[64 * 1024];
            long written = 0;
            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                if (read > entry.Length - written) throw new InvalidDataException("Archive entry exceeds its declared size.");
                target.Write(buffer, 0, read);
                written += read;
            }
            if (target.Length != entry.Length) throw new InvalidDataException("Truncated service archive entry.");
        }
        if (!File.Exists(Path.Combine(stagingDirectory, SetupPaths.ServiceExecutableName)))
            throw new InvalidDataException("Service executable is missing.");
    }

    internal static string ValidateEntryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 || value.IndexOf(':') >= 0 || value.IndexOf('\0') >= 0 ||
            value.StartsWith('/') || value.StartsWith('\\') || Path.IsPathRooted(value)) throw new InvalidDataException("Invalid service archive path.");
        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.None);
        if (parts.Length == 0 || parts.Any(static part => string.IsNullOrEmpty(part) || part is "." or ".." ||
            part.EndsWith(' ') || part.EndsWith('.') || IsReservedDeviceName(part))) throw new InvalidDataException("Invalid service archive path.");
        return string.Join(Path.DirectorySeparatorChar, parts);
    }

    private static bool IsReservedDeviceName(string part)
    {
        var stem = part.Split('.', 2)[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             stem[3] is >= '1' and <= '9');
    }
}
