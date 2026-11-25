using System.Globalization;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private sealed class SettingsEntryFlow : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly CultureInfo? _resourceCulture = Strings.Culture;

        internal SettingsEntryFlow(UpdateUi ui)
        {
            _ui = ui;
            ui.Window.ApplyTheme(false, false);
            ui.Window.Title = "Owned Settings entry fixture";
            ui.Window.Show();
        }

        internal Task? Write => (Task?)_ui.Field("_settingsWriteTask").GetValue(_ui.Window);
        internal int Notices { get; private set; }

        internal static IEnumerable<DependencyObject> Children(DependencyObject root)
        {
            yield return root;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
                foreach (var nested in Children(child)) yield return nested;
        }

        private static void SupplyOwnedDiscovery(SettingsWindow dialog)
        {
            foreach (var name in new[] { "_driveLoadTask", "_networkLoadTask" })
            {
                var field = typeof(SettingsWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new AssertionException("Missing discovery boundary: " + name);
                Assert.That(field.GetValue(dialog), Is.Null, "Intercept before native discovery starts");
                field.SetValue(dialog, Task.CompletedTask);
            }
            var primary = new SettingsWindow.DriveChoice("C:", "Owned fixture volume");
            dialog.PrimaryDriveSelector.ItemsSource = new[] { primary };
            dialog.PrimaryDriveSelector.SelectedItem = primary;
            var none = new SettingsWindow.DriveChoice(null, Strings.None);
            dialog.SecondaryDriveSelector.ItemsSource = new[] { none };
            dialog.SecondaryDriveSelector.SelectedItem = none;
        }

        internal void Open(Action<SettingsWindow> exercise, bool expectFailureNotice = false)
        {
            using var traceOutput = new StringWriter();
            using var trace = new TextWriterTraceListener(traceOutput);
            Trace.Listeners.Add(trace);
            var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
            var prepared = new HashSet<SettingsWindow>();
            DispatcherOperation? watch = null;
            Exception? failure = null;
            Notices = 0;
            var timer = new DispatcherTimer(DispatcherPriority.Send, _ui.Window.Dispatcher)
                { Interval = TimeSpan.FromSeconds(10) };
            Window[] OwnedWindows() => Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
            void Watch()
            {
                foreach (var window in OwnedWindows())
                {
                    if (window is SettingsWindow dialog)
                    {
                        if (!prepared.Add(dialog)) continue;
                        try
                        {
                            Assert.That(dialog.Owner, Is.SameAs(_ui.Window));
                            SupplyOwnedDiscovery(dialog);
                            dialog.ContentRendered += (_, _) =>
                            {
                                try { exercise(dialog); }
                                catch (Exception error) { failure ??= error; if (dialog.IsVisible) dialog.Close(); }
                            };
                        }
                        catch (Exception error) { failure ??= error; if (dialog.IsVisible) dialog.Close(); }
                    }
                    else if (window is MessageDialog notice)
                    {
                        Notices++;
                        try
                        {
                            var text = Children((DependencyObject)notice.Content).OfType<TextBlock>().Select(block => block.Text).ToArray();
                            Assert.That(expectFailureNotice, Is.True, "Unexpected Settings notice: " + notice.Title + ": " + string.Join(" | ", text));
                            Assert.That(notice.Owner, Is.SameAs(_ui.Window));
                            Assert.That(notice.Title, Is.EqualTo(Strings.WinMonSettings));
                            Assert.That(Children((DependencyObject)notice.Content).OfType<TextBlock>().Select(text => text.Text),
                                Is.EqualTo(new[] { Strings.SettingsCouldNotBeSavedCheckAccessToYourLocal }));
                            Children((DependencyObject)notice.Content).OfType<Button>().Single()
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception error) { failure ??= error; }
                        finally { if (notice.IsVisible) notice.Close(); }
                    }
                    else { failure ??= new AssertionException("Unexpected owned window: " + window.Title); window.Close(); }
                }
                watch = _ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
            }
            timer.Tick += (_, _) =>
            {
                failure ??= new AssertionException("Settings entry exceeded its owned modal bound");
                foreach (var window in OwnedWindows()) if (window.IsVisible) window.Close();
            };
            try
            {
                watch = _ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Watch));
                timer.Start();
                _ui.Window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (Write is { } write)
                    FinishWindowCheck(write.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default));
                FinishWindowCheck(_ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
                if (failure is not null) throw new AssertionException(failure.Message + "\n" + traceOutput);
                Assert.That(prepared.Count, Is.EqualTo(1));
                Assert.That(Notices, Is.EqualTo(expectFailureNotice ? 1 : 0));
                Assert.That(OwnedWindows(), Is.Empty);
            }
            finally
            {
                timer.Stop();
                Trace.Listeners.Remove(trace);
                watch?.Abort();
                foreach (var window in OwnedWindows()) if (window.IsVisible) window.Close();
            }
        }

        public void Dispose()
        {
            Strings.SetCulture(_resourceCulture);
            CultureInfo.DefaultThreadCurrentUICulture = _defaultCulture;
            CultureInfo.CurrentUICulture = _culture;
        }
    }

    private static string SettingsEntryPath(string root) => Path.Combine(root, "settings.json");
    private static (bool, bool, bool) SettingsEntryControls(UpdateUi ui) => (ui.Window.SettingsButton.IsEnabled,
        ui.Window.Processes.ProcessCountSelector.IsEnabled, ui.Window.Processes.ProcessSortSelector.IsEnabled);

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryClosingGuardDoesNotOpenADialogOrChangeDisabledControls()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.IsClosing = true;
        ui.Window.SettingsButton.IsEnabled = false;
        ui.Window.Processes.RankingEnabled = false;
        var windows = Application.Current.Windows.Cast<Window>().ToArray();
        ui.Window.SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(Application.Current.Windows.Cast<Window>(), Is.EqualTo(windows));
        Assert.That(SettingsEntryControls(ui), Is.EqualTo((false, false, false)));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryCancellationKeepsTheOwnedStoreAndRemembersItsLastSection()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        Directory.CreateDirectory(_directory);
        var path = SettingsEntryPath(_directory);
        File.WriteAllText(path, "owned settings sentinel");
        var preferences = ui.Window.Preferences;
        var previousWrite = flow.Write;
        var retry = new DispatcherTimer(DispatcherPriority.Background, ui.Window.Dispatcher)
            { Interval = TimeSpan.FromMinutes(1) };
        ui.Field("_hotkeyRetry").SetValue(ui.Window, retry);
        retry.Start();
        try
        {
            flow.Open(dialog =>
            {
                Assert.That(retry.IsEnabled, Is.False, "Settings must stop the shortcut retry before recording keys");
                dialog.ShowSection(SettingsSection.Monitoring);
                dialog.Close();
            });
        }
        finally { retry.Stop(); }
        flow.Open(dialog => { Assert.That(dialog.Section, Is.EqualTo(SettingsSection.Monitoring)); dialog.Close(); });
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned settings sentinel"));
        Assert.That(ui.Window.Preferences, Is.SameAs(preferences));
        Assert.That(flow.Write, Is.SameAs(previousWrite));
        Assert.That(SettingsEntryControls(ui), Is.EqualTo((true, true, true)));
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryAcceptedDialogWithoutAResultDoesNotApplyPreferences()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        var preferences = ui.Window.Preferences;
        var previousWrite = flow.Write;
        flow.Open(dialog => dialog.DialogResult = true);
        Assert.That(ui.Window.Preferences, Is.SameAs(preferences));
        Assert.That(flow.Write, Is.SameAs(previousWrite));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryDiagnosticModeDoesNotReadStartupRegistration()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        flow.Open(dialog => dialog.Close());
        Assert.Multiple(() =>
        {
            Assert.That(flow.Notices, Is.Zero);
            Assert.That(ui.Window.Preferences.StartWithWindows, Is.False);
        });
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntrySavedResultIsIgnoredWhenTheParentStartsClosing()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        var preferences = ui.Window.Preferences;
        var previousWrite = flow.Write;
        flow.Open(dialog => { ui.Window.IsClosing = true; FinishWindowCheck(dialog.SaveAsync()); });
        Assert.That(ui.Window.Preferences, Is.SameAs(preferences));
        Assert.That(flow.Write, Is.SameAs(previousWrite));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    [Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntrySavesTheOwnedPreferenceResultAndRestoresSettingsAndRanking(bool light)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        var theme = light ? "Light" : "Dark";
        flow.Open(dialog =>
        {
            dialog.ThemeSelector.SelectedValue = theme;
            dialog.LanguageSelector.SelectedValue = "en";
            FinishWindowCheck(dialog.SaveAsync());
        });
        Assert.That(flow.Write?.IsCompletedSuccessfully, Is.True);
        var restored = new PreferenceStore(SettingsEntryPath(_directory)).LoadAsync().GetAwaiter().GetResult();
        Assert.That(restored.Theme, Is.EqualTo(theme));
        Assert.That(restored.Language, Is.EqualTo("en"));
        Assert.That(ui.Window.Preferences.Theme, Is.EqualTo(theme));
        Assert.That(ui.Window.Preferences.Language, Is.EqualTo("en"));
        Assert.That(SettingsEntryControls(ui), Is.EqualTo((true, true, true)));
        Assert.That(ui.Window.SamplingTask, Is.Null);
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryWriteFailureShowsOnlyTheGenericNoticeAndRestoresControls()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        Directory.CreateDirectory(SettingsEntryPath(_directory));
        flow.Open(dialog => FinishWindowCheck(dialog.SaveAsync()), expectFailureNotice: true);
        Assert.That(flow.Write?.IsFaulted, Is.True);
        Assert.That(flow.Write!.Exception!.InnerException, Is.InstanceOf<UnauthorizedAccessException>());
        Assert.That(Directory.Exists(SettingsEntryPath(_directory)), Is.True);
        Assert.That(SettingsEntryControls(ui), Is.EqualTo((true, true, true)));
    }

    [Test, Apartment(ApartmentState.STA), NonParallelizable, SetUICulture("en-US")]
    public void WindowSettingsEntryOwnedCancellationSkipsTheFailureNoticeAndRestoresControls()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        using var flow = new SettingsEntryFlow(ui);
        flow.Open(dialog => { ui.Window.Lifetime.Cancel(); FinishWindowCheck(dialog.SaveAsync()); });
        Assert.That(flow.Write?.IsCanceled, Is.True);
        Assert.That(flow.Notices, Is.Zero);
        Assert.That(File.Exists(SettingsEntryPath(_directory)), Is.False);
        Assert.That(SettingsEntryControls(ui), Is.EqualTo((true, true, true)));
    }
}
