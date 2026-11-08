using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.NetworkSetup;

internal static class InstalledFiles
{
    internal const string FileName = "installed-files.json";
    private const string Owner = "WinMonNetworkMonitor.v1";

    internal static void Write(string root)
    {
        var files = ProtectedInstallation.Tree(root).OfType<FileInfo>()
            .Select(file => Path.GetRelativePath(root, file.FullName)).ToArray();
        var manifest = new InstalledFileList(Owner, files);
        using var stream = ProtectedInstallation.CreateFile(Path.Combine(root, FileName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, InstallationJsonContext.Default.InstalledFileList));
        stream.Flush(true);
    }

    internal static IReadOnlyList<FileSystemInfo> Validate(string root)
    {
        var tree = ProtectedInstallation.Tree(root);
        var path = Path.Combine(root, FileName);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > 64 * 1024) throw new InvalidDataException("Invalid installed file list.");
        var manifest = JsonSerializer.Deserialize(stream, InstallationJsonContext.Default.InstalledFileList)
                       ?? throw new InvalidDataException("Missing installed file list.");
        var expected = ValidateList(manifest);
        var actual = tree.OfType<FileInfo>().Select(file => Path.GetRelativePath(root, file.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(expected))
            throw new InvalidDataException("Installation contains missing or unrelated files.");
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "." };
        foreach (var file in expected)
            for (var parent = Path.GetDirectoryName(file);
                 !string.IsNullOrEmpty(parent);
                 parent = Path.GetDirectoryName(parent))
                directories.Add(parent);
        if (tree.OfType<DirectoryInfo>()
            .Any(directory => !directories.Contains(Path.GetRelativePath(root, directory.FullName))))
            throw new InvalidDataException("Installation contains unrelated directories.");
        return tree;
    }

    internal static HashSet<string> ValidateList(InstalledFileList manifest)
    {
        if (manifest.Owner != Owner || manifest.Files is null || manifest.Files.Length is < 3 or > 4095)
            throw new InvalidDataException("Invalid installed file list.");
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { FileName };
        foreach (var file in manifest.Files)
            if (!result.Add(PackageExtraction.ValidateEntryName(file)))
                throw new InvalidDataException("Duplicate installed file.");
        if (!result.Contains(SetupPaths.ServiceExecutableName) || !result.Contains(SetupPaths.SetupExecutableName) ||
            !result.Contains(SetupPaths.PolicyFileName))
            throw new InvalidDataException("Incomplete installed file list.");
        return result;
    }
}

internal sealed record InstalledFileList(string Owner, string[] Files);

[JsonSerializable(typeof(InstalledFileList))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext;