using Microsoft.Win32;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class InstalledRegistrationCharacterizationTests
{
    [Test]
    [Explicit("Requires the existing trusted WinMon network-service uninstall registration and read permission.")]
    public void InstalledRegistrationCaptureReturnsIndependentAllowedValuesWithoutChangingTheKey()
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WinMonNetworkMonitor";
        string[] allowed = ["DisplayName", "DisplayVersion", "Publisher", "InstallLocation", "UninstallString",
            "NoModify", "NoRepair"];
        using var key = Registry.LocalMachine.OpenSubKey(path, false);
        Assert.That(key, Is.Not.Null, "The explicit installed-registration fixture must exist.");
        var before = Snapshot(key!);
        var security = key!.GetAccessControl().GetSecurityDescriptorBinaryForm();
        var expected = before.Where(pair => allowed.Contains(pair.Key, StringComparer.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Multiple(() =>
        {
            Assert.That(expected["InstallLocation"], Is.EqualTo(SetupPaths.InstallDirectory));
            Assert.That(expected["UninstallString"], Is.EqualTo($"\"{SetupPaths.InstalledSetupExecutable}\" --uninstall"));
        });

        var first = SetupRegistration.Capture();
        Assert.That(first, Is.Not.Null);
        Assert.That(first, Is.EquivalentTo(expected));
        foreach (var pair in expected)
            Assert.That(first![pair.Key].GetType(), Is.EqualTo(pair.Value.GetType()), pair.Key);
        first!.Clear();
        var second = SetupRegistration.Capture();
        Assert.That(second, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second, Is.EquivalentTo(expected), "Mutating a captured dictionary must not affect the registration.");
            Assert.That(Snapshot(key), Is.EquivalentTo(before), "All values, including unrelated ones, remain unchanged.");
            Assert.That(key.GetAccessControl().GetSecurityDescriptorBinaryForm(), Is.EqualTo(security));
            Assert.That(key.Handle.IsClosed, Is.False);
            foreach (var pair in expected)
                Assert.That(second![pair.Key].GetType(), Is.EqualTo(pair.Value.GetType()), pair.Key);
        });
    }

    private static Dictionary<string, object> Snapshot(RegistryKey key) => key.GetValueNames()
        .ToDictionary(name => name, name => key.GetValue(name)!);
}
