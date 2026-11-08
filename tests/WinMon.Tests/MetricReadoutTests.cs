using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MetricReadoutTests
{
    [Test]
    public void PlainGauge_KeepsItsCheckboxInTheChartAcrossGaugeStyles()
    {
        var readout = new MetricReadout { ShowSeriesToggle = false, Label = "CPU" };
        var host = new ContentControl { Content = readout.Toggle };
        readout.Toggle.IsChecked = false;
        foreach (var style in Enum.GetValues<GaugeStyle>())
        {
            readout.GaugeStyle = style;
            readout.Label = "Memory";
            Assert.Multiple(() =>
            {
                Assert.That(readout.Toggle.Parent, Is.SameAs(host));
                Assert.That(readout.Toggle.IsChecked, Is.False);
                Assert.That(readout.Toggle.Content, Is.EqualTo("Memory"));
                Assert.That(readout.Toggle.Margin, Is.EqualTo(new Thickness()));
                Assert.That(AutomationProperties.GetName(readout), Is.EqualTo("Memory"));
            });
        }
    }

    private static string Text(MetricReadout readout)
    {
        return new TextRange(readout.ValueLabel.ContentStart, readout.ValueLabel.ContentEnd).Text;
    }

    [Test]
    public void ShowValue_ReusesItsRuns_AndRecoversAfterAMissingReading()
    {
        // Dials always use the smaller value font; full-width text starts at the larger one.
        var readout = new MetricReadout { GaugeStyle = GaugeStyle.None };
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