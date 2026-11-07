using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class InfoWindowTests
{
    [TestCase("#FFFFFF")]
    [TestCase("#102030")]
    public void SectionHeaders_UseThemeTextInBothExpansionStates(string color)
    {
        var window = new HardwareInfoWindow();
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            var section = new Expander
            {
                Header = "CPU", Tag = "CPU summary", Content = new TextBlock { Text = "Details" },
                Style = (Style)window.FindResource(typeof(Expander))
            };
            section.Resources["Text"] = brush;
            foreach (var expanded in new[] { false, true })
            {
                section.IsExpanded = expanded;
                section.Measure(new Size(550, 500));
                section.Arrange(new Rect(0, 0, 550, 500));
                section.UpdateLayout();
                var label = VisualDescendants(section).OfType<TextBlock>().Single(text => text.Text == "CPU");
                Assert.That(((SolidColorBrush)label.Foreground).Color, Is.EqualTo(brush.Color));
            }
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var nested in VisualDescendants(child)) yield return nested;
        }
    }

    [Test]
    public void Tabs_SeparateHardwareActionsGuideAndProductDetails()
    {
        var window = new HardwareInfoWindow();
        try
        {
            Assert.That(window.Tabs.Items.Cast<TabItem>().Select(t => t.Header),
                Is.EqualTo(new[] { Strings.InfoHardware, Strings.InfoGuide, Strings.InfoAbout }));
            var hardware = (TabItem)window.Tabs.Items[0];
            Assert.That(Descendants(hardware).Contains(window.CopyButton), Is.True);
            Assert.That(Descendants(hardware).Contains(window.CheckButton), Is.False);
            var topics = Descendants((TabItem)window.Tabs.Items[1]).OfType<Expander>().ToArray();
            Assert.That(topics, Has.Length.EqualTo(5));
            Assert.That(topics.Count(t => t.IsExpanded), Is.EqualTo(1));
            Assert.That(window.VersionText.Text, Does.Contain(AppInfo.Version));
            Assert.That(window.LicenseText.Text, Does.Contain(AppInfo.Copyright).And.Contain(AppInfo.License));
            Assert.That(window.RepositoryLink.NavigateUri.AbsoluteUri, Is.EqualTo(AppInfo.RepositoryUrl));
        }
        finally { window.Close(); }
    }

    [Test]
    public void About_OffersUpdatesOnlyInOrdinaryBuildsAndKeepsBusyActionsDisabled()
    {
        var window = new HardwareInfoWindow();
        try
        {
            window.SetUpdateState("new release", true, busy: true);
            Assert.That(window.CheckButton.IsEnabled, Is.False);
            Assert.That(window.UpdateButton.IsEnabled, Is.False);
            window.SetUpdateState("new release", true);
            Assert.That(window.CheckButton.Visibility == Visibility.Visible, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled));
            Assert.That(window.UpdateButton.Visibility == Visibility.Visible, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled));
            Assert.That(window.UpdateStatusText.Text,
                Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled ? "new release" : Strings.InfoStoreUpdates));
            window.SetUpdateState(Strings.UpToDate, false);
            Assert.That(window.UpdateButton.Visibility, Is.EqualTo(Visibility.Collapsed));
        }
        finally { window.Close(); }
    }

    [Test]
    public void Check_UsesExistingCallbackAndRestoresActionsAfterFailure()
    {
        var window = new HardwareInfoWindow();
        var checks = 0;
        window.CheckForUpdatesRequested += () =>
        {
            checks++;
            Assert.That(window.CheckButton.IsEnabled, Is.False);
            Assert.That(window.UpdateButton.IsEnabled, Is.False);
            window.SetUpdateState("available", true);
            return Task.CompletedTask;
        };
        try
        {
            window.CheckButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(checks, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled ? 1 : 0));
            Assert.That(window.CheckButton.IsEnabled, Is.True);
            if (!UpdatePolicy.GitHubUpdatesEnabled) return;
            window.CheckForUpdatesRequested += () => throw new IOException("offline");
            window.CheckButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(window.UpdateStatusText.Text, Is.EqualTo(Strings.UpdateFailed));
            Assert.That(window.CheckButton.IsEnabled, Is.True);
            Assert.That(window.UpdateButton.IsEnabled, Is.True);
            Assert.That(window.UpdateButton.Visibility, Is.EqualTo(Visibility.Visible), "A known offer survives a failed recheck");
        }
        finally { window.Close(); }
    }

    [Test]
    public void Update_ClosesDialogBeforeRequestingExistingHandoff()
    {
        var window = new HardwareInfoWindow();
        var closed = false;
        var updates = 0;
        window.Closed += (_, _) => closed = true;
        window.UpdateRequested += () =>
        {
            Assert.That(closed, Is.True);
            updates++;
        };
        window.SetUpdateState("available", true);
        try
        {
            window.UpdateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.That(updates, Is.EqualTo(UpdatePolicy.GitHubUpdatesEnabled ? 1 : 0));
        }
        finally { if (!closed) window.Close(); }
    }

    [Test]
    public void ClosedDialog_IgnoresLateUpdateResult()
    {
        var window = new HardwareInfoWindow();
        var status = window.UpdateStatusText.Text;
        window.Close();
        window.SetUpdateState("late result", true);
        Assert.That(window.UpdateStatusText.Text, Is.EqualTo(status));
        Assert.That(window.UpdateButton.Visibility, Is.EqualTo(Visibility.Collapsed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PendingCheck_PreventsDuplicatesAndIgnoresResultsAfterClosing(bool closeEarly)
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        var window = new HardwareInfoWindow();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        window.CheckForUpdatesRequested += async () =>
        {
            calls++;
            await release.Task;
            window.SetUpdateState("new version", true);
        };
        var previous = SynchronizationContext.Current;
        Task check;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            check = window.CheckForUpdatesAsync();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        try
        {
            Assert.That(check.IsCompleted, Is.False);
            Assert.That(window.CheckForUpdatesAsync().IsCompletedSuccessfully, Is.True);
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(window.CheckButton.IsEnabled, Is.False);
            if (closeEarly) window.Close();
            release.SetResult();
            var frame = new DispatcherFrame();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (check.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.That(check.IsCompletedSuccessfully, Is.True);
            Assert.That(window.UpdateStatusText.Text, Is.EqualTo(closeEarly ? Strings.UpdateChecking : "new version"));
            Assert.That(window.UpdateButton.Visibility, Is.EqualTo(closeEarly ? Visibility.Collapsed : Visibility.Visible));
        }
        finally
        {
            release.TrySetResult();
            if (!closeEarly) window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
