using System.Windows.Documents;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MetricReadoutTests
{
    private static string Text(MetricReadout readout) =>
        new TextRange(readout.ValueLabel.ContentStart, readout.ValueLabel.ContentEnd).Text;

    [Test]
    public void ShowValue_ReusesItsRuns_AndRecoversAfterAMissingReading()
    {
        var readout = new MetricReadout();
        readout.ShowValue("12.5", "Mbps");
        var number = readout.ValueLabel.Inlines.FirstInline;
        var unit = readout.ValueLabel.Inlines.LastInline;

        readout.ShowValue("3.2", "Kbps");
        Assert.Multiple(() =>
        {
            Assert.That(Text(readout), Is.EqualTo("3.2 Kbps"));
            Assert.That(readout.ValueLabel.Inlines.FirstInline, Is.SameAs(number));
            Assert.That(readout.ValueLabel.Inlines.LastInline, Is.SameAs(unit));
            Assert.That(unit.FontSize, Is.EqualTo(16));
        });

        // A missing reading replaces the runs with plain text; the next value restores them.
        readout.ValueLabel.Text = "—";
        Assert.That(Text(readout), Is.EqualTo("—"));
        readout.ShowValue("1.0", "Gbps");
        Assert.Multiple(() =>
        {
            Assert.That(Text(readout), Is.EqualTo("1.0 Gbps"));
            Assert.That(readout.ValueLabel.Inlines, Has.Count.EqualTo(2));
            Assert.That(readout.ValueLabel.Inlines.LastInline.FontSize, Is.EqualTo(16));
        });
    }
}
