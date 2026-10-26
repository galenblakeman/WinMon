using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class AssemblyBoundaryTests
{
    [Test]
    public void MonCore_HasNoPlatformOrUiAssemblyReferencesAndNoNativeImports()
    {
        var assembly = typeof(MonitorSession).Assembly;
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.That(references, Has.None.Matches<string>(name =>
            name is not null && (name.StartsWith("Presentation", StringComparison.Ordinal) ||
                                 name.StartsWith("WindowsBase", StringComparison.Ordinal) ||
                                 name.StartsWith("System.Windows.Forms", StringComparison.Ordinal) ||
                                 name.StartsWith("System.Management", StringComparison.Ordinal) ||
                                 name.StartsWith("Avalonia", StringComparison.Ordinal) ||
                                 name.StartsWith("MonCore.Windows", StringComparison.Ordinal) ||
                                 name.StartsWith("WinMon", StringComparison.Ordinal))));

        var imports = assembly.GetTypes().SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(method => method.IsDefined(typeof(DllImportAttribute), false) ||
                             method.IsDefined(typeof(LibraryImportAttribute), false))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}");
        Assert.That(imports, Is.Empty);
    }
}
