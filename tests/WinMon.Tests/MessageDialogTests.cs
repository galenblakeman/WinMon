using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[SetUICulture("en")]
public sealed class MessageDialogTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void Dialog_UsesThemeResourcesAndKeepsDismissalAsDefault(bool confirmation)
    {
        var dialog = new MessageDialog("Services", "Example service", confirmation ? "Stop" : null);
        dialog.Resources["Surface"] = Brushes.Navy;
        dialog.Resources["Text"] = Brushes.White;
        var buttons = Descendants<Button>(dialog.Content).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Background, Is.SameAs(Brushes.Navy));
            Assert.That(dialog.Foreground, Is.SameAs(Brushes.White));
            Assert.That(buttons.Last().IsDefault, Is.True);
            Assert.That(buttons.Last().IsCancel, Is.True);
            Assert.That(buttons.Last().Content, Is.EqualTo(confirmation ? Strings.Cancel : Strings.Close));
            Assert.That(buttons.Take(buttons.Length - 1).Any(b => b.IsDefault), Is.False);
        });
        dialog.Close();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Confirmation_OnlyExplicitActionAccepts(bool accept)
    {
        var dialog = new MessageDialog("Services", "Stop example service?", "Stop");
        // The testhost has no App.xaml resources; supply the production theme keys for native chrome.
        dialog.Resources["Surface"] = Brushes.Navy;
        dialog.Resources["Text"] = Brushes.White;
        dialog.ContentRendered += (_, _) =>
        {
            var buttons = Descendants<Button>(dialog.Content).ToArray();
            buttons[accept ? 0 : 1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        Assert.That(dialog.ShowDialog(), Is.EqualTo(accept));
    }

    private static IEnumerable<T> Descendants<T>(object root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        if (root is not DependencyObject parent) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var result in Descendants<T>(child)) yield return result;
    }
}
