using System.Reflection;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA), NonParallelizable, Category("Integration")]
public sealed class NativeDriveDiscoveryCharacterizationTests
{
    [Test]
    [Explicit("Requires stable native logical drives with readable metadata and at least one ready drive.")]
    public void AvailableNativeDriveDiscoveryPreservesOrderNamesLabelsAndReturnsFreshChoices()
    {
        var before = ReadNativeDrives();
        Assert.That(before.Any(drive => drive.Ready), Is.True,
            "The explicit native fixture must have at least one ready, readable drive.");
        var discover = typeof(SettingsWindow).GetMethod("DiscoverDrives", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<SettingsWindow.DriveChoice[]>>();

        var first = discover();
        AssertChoices(first, before);
        first[0] = new(null, "Owned result mutation");
        var second = discover();

        Assert.That(second, Is.Not.SameAs(first));
        AssertChoices(second, before);
        Assert.That(ReadNativeDrives(), Is.EqualTo(before), "Drive metadata must remain stable and unchanged.");
    }

    private static void AssertChoices(SettingsWindow.DriveChoice[] choices, NativeDrive[] native)
    {
        Assert.That(choices.Select(choice => choice.Name), Is.EqualTo(native.Select(drive => drive.Root[..2].ToUpperInvariant())));
        Assert.That(choices.Select(choice => choice.Name).Distinct().Count(), Is.EqualTo(choices.Length));
        for (var index = 0; index < native.Length; index++)
        {
            var name = native[index].Root[..2].ToUpperInvariant();
            var label = native[index].Label;
            Assert.That(choices[index].Label,
                Is.EqualTo(native[index].Ready && label is { Length: > 0 } ? name + " — " + label : name));
        }
    }

    private static NativeDrive[] ReadNativeDrives() => DriveInfo.GetDrives().Select(drive =>
    {
        Assert.That(drive.Name.Length, Is.EqualTo(3), "The explicit fixture requires canonical native drive roots.");
        Assert.That(drive.Name[0], Is.InRange('A', 'Z'));
        Assert.That(drive.Name[1..], Is.EqualTo(":\\"));
        var ready = drive.IsReady;
        return new NativeDrive(drive.Name, ready, ready ? drive.VolumeLabel : null);
    }).ToArray();

    private sealed record NativeDrive(string Root, bool Ready, string? Label);
}
