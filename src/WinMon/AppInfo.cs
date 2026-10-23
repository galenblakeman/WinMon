using System.Reflection;

namespace WinMon;

/// <summary>Product details stamped into the assembly from Directory.Build.props.</summary>
internal static class AppInfo
{
    private static readonly Assembly Assembly = typeof(AppInfo).Assembly;

    /// <summary>Gets the release version, such as "1.0.0".</summary>
    public static string Version { get; } =
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetName().Version?.ToString(3) ?? "";

    /// <summary>Gets the one-line product description.</summary>
    public static string Description => Strings.ApplicationDescription;

    /// <summary>Gets the copyright notice.</summary>
    public static string Copyright { get; } =
        Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>Gets the SPDX license identifier, such as "MIT".</summary>
    public static string License { get; } = Metadata("License");

    /// <summary>Gets the source repository address.</summary>
    public static string RepositoryUrl { get; } = Metadata("RepositoryUrl");

    private static string Metadata(string key)
    {
        return Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";
    }
}