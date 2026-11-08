using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ProcessRowFocusTests
{
    [OneTimeSetUp]
    public void LoadApplicationResources()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["RangeChip"] = new Style(typeof(RadioButton));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void RankingChange_PreservesFocusOnHyperlinkOrMovedRow(bool focusHyperlink)
    {
        var view = new ProcessesView();
        var window = new Window { Content = view, Width = 1000, Height = 900 };
        var link = new Hyperlink(new Run("About"));
        var dialog = new Window { Content = new TextBlock(link), Width = 300, Height = 200 };

        void Update(bool reverse)
        {
            ProcessUsage[] processes =
            [
                new(20, 10, "first", 1, reverse ? 100UL : 200UL),
                new(30, 10, "second", 1, reverse ? 200UL : 100UL)
            ];
            var sample = new MonitorSample(DateTimeOffset.Now, 10, processes, 0);
            view.Update(new([sample], sample, sample, null, sample, sample, false, null, true,
                new(10, ProcessSort.Memory, false, false, false)));
        }

        try
        {
            Update(false);
            window.Show();
            window.Activate();
            window.UpdateLayout();
            var rows = (StackPanel)view.FindName("ProcessRows");
            var first = rows.Children.OfType<ProcessRow>().First();
            IInputElement target = first.Children.OfType<Panel>().SelectMany(panel => panel.Children.OfType<Button>())
                .Single();
            if (focusHyperlink)
            {
                dialog.Owner = window;
                dialog.Show();
                dialog.Activate();
                target = link;
            }

            Assert.That(Keyboard.Focus(target), Is.SameAs(target), "The regression requires actual keyboard focus");

            Assert.DoesNotThrow(() => Update(true));

            Assert.Multiple(() =>
            {
                Assert.That(rows.Children.OfType<ProcessRow>().Skip(1).First(), Is.SameAs(first),
                    "The focused process row must actually move");
                Assert.That(Keyboard.FocusedElement, Is.SameAs(target));
            });
        }
        finally
        {
            dialog.Close();
            window.Close();
        }
    }
}