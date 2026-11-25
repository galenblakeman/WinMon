using System.Globalization;
using PropertyChangedEventArgs = System.ComponentModel.PropertyChangedEventArgs;
using System.Reflection;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void LanguageDiagnosticSavesEveryChoiceAndRestoresOriginalWindowState()
    {
        using var fixture = new LanguageSettingsDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var samples = fixture.Window.Session.History.Samples.ToArray();
        FinishWindowCheck(fixture.Run());
        fixture.AssertHealthy();
        Assert.That(fixture.Window.Preferences, Is.SameAs(original));
        Assert.That(fixture.Window.SelectedSample, Is.Null);
        Assert.That(fixture.Window.Processes.ProcessSearchBox.Text, Is.EqualTo("owned original query"));
        Assert.That(fixture.Window.Processes.ProcessSortSelector.SelectedIndex, Is.EqualTo((int)ProcessSort.Cpu));
        Assert.That(fixture.Window.Tray, Is.Null);
        Assert.That(fixture.Window.ShownSamplingError, Is.Null);
        Assert.That(fixture.Window.Session.History.Samples, Is.EqualTo(samples));
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
        Assert.That(fixture.Window.SamplingTask, Is.Null);
        Assert.That(fixture.Window.OwnedWindows.OfType<SettingsWindow>(), Is.Empty);
        var load = fixture.Window.Store.LoadAsync();
        FinishWindowCheck(load);
        Assert.That(load.Result.Language, Is.EqualTo(AppLanguages.SystemDefault));
        Assert.That(fixture.ObservedLanguages, Is.EqualTo(AppLanguages.Choices.Select(choice => choice.Name)
            .Concat(new[] { AppLanguages.SystemDefault, "en" })));
        foreach (var language in new[] { "en", "de", "ja", "zh-Hans" })
        {
            var path = Path.Combine(_directory, $"live-language-{language}.png");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.That(frame.PixelWidth, Is.GreaterThan(0));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            if (TestContext.Parameters.Get("LanguageDiagnosticArtifactDirectory") is { Length: > 0 } artifacts)
            {
                Directory.CreateDirectory(artifacts);
                stream.Position = 0;
                using var copy = File.Create(Path.Combine(artifacts, Path.GetFileName(path)));
                stream.CopyTo(copy);
            }
        }
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(4));
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void LanguageDiagnosticCancellationPreservesCompletedOwnedSaveAndRestoresWindow(bool afterJapaneseSave)
    {
        using var fixture = new LanguageSettingsDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        using var cancellation = new CancellationTokenSource();
        if (afterJapaneseSave) fixture.AfterLanguageChanged = language => { if (language == "ja") cancellation.Cancel(); };
        else cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Run(cancellation.Token)));
        fixture.AssertHealthy();
        fixture.AssertRestored(original);
        fixture.AssertSaved(afterJapaneseSave ? "ja" : AppLanguages.SystemDefault);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(afterJapaneseSave ? 3 : 0));
    }

    [TestCase("en"), TestCase("zh-Hans"), TestCase("de"), TestCase("ja"), Category("Integration")]
    public void LanguageDiagnosticImageFailurePreservesFileAndLastSavedLanguage(string language)
    {
        using var fixture = new LanguageSettingsDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var path = Path.Combine(_directory, $"live-language-{language}.png");
        File.WriteAllText(path, "owned language image sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Catch<IOException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned language image sentinel"));
        fixture.AssertHealthy();
        fixture.AssertRestored(original);
        fixture.AssertSaved(language);
    }

    [Test, Category("Integration")]
    public void LanguageDiagnosticMissingOutputDirectoryPreservesTheCompletedEnglishSave()
    {
        using var fixture = new LanguageSettingsDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var output = Path.Combine(_directory, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => FinishWindowCheck(fixture.Run(output: output)));
        fixture.AssertHealthy();
        fixture.AssertRestored(original);
        fixture.AssertSaved("en");
        Assert.That(Directory.Exists(output), Is.False);
    }

    [TestCase("info"), TestCase("settings"), TestCase("history"), TestCase("memory")]
    [TestCase("memory-name"), TestCase("tray-show"), TestCase("tray-exit")]
    [TestCase("sort-preference"), TestCase("sort-index"), TestCase("sort-item")]
    [TestCase("query"), TestCase("selection"), TestCase("empty-label"), Category("Integration")]
    public void LanguageDiagnosticRejectsStalePresentationWithoutRollingBackCompletedSave(string partition)
    {
        using var fixture = new LanguageSettingsDiagnostic(_directory);
        var original = fixture.Window.Preferences;
        var applied = false;
        var independentSortState = true;
        fixture.AfterLanguageChanged = language =>
        {
            if (language != AppLanguages.SystemDefault || applied) return;
            // ApplyLanguage finishes synchronously, then the diagnostic awaits Render.
            // Queue before that await so this fault runs after bindings refresh and before validation.
            fixture.Window.Dispatcher.BeginInvoke(() =>
            {
                applied = true;
                var window = fixture.Window;
                switch (partition)
                {
                    case "info": window.InfoButton.Content = "owned stale info"; break;
                    case "settings": window.SettingsButton.Content = "owned stale settings"; break;
                    case "history": window.Processes.ProcessHistoryHeader.Text = "owned stale history"; break;
                    case "memory": window.MemoryMetric.Label = "owned stale memory"; break;
                    case "memory-name": AutomationProperties.SetName(window.MemoryMetric.Toggle, "owned stale memory name"); break;
                    case "tray-show": window.Tray!.ContextMenuStrip!.Items[0].Text = "owned stale tray show"; break;
                    case "tray-exit": window.Tray!.ContextMenuStrip!.Items[1].Text = "owned stale tray exit"; break;
                    case "sort-preference": window.Preferences = window.Preferences with { ProcessSort = ProcessSort.Cpu }; break;
                    case "sort-index":
                        window.Processes.ProcessSortSelector.SelectedIndex = (int)ProcessSort.Cpu;
                        window.Preferences = window.Preferences with { ProcessSort = ProcessSort.Memory };
                        independentSortState = window.Processes.ProcessSortSelector.SelectedIndex == 0 &&
                            window.Preferences.ProcessSort == ProcessSort.Memory;
                        break;
                    case "sort-item":
                        var items = window.Processes.ProcessSortSelector.Items.Cast<object>().ToArray();
                        items[(int)ProcessSort.Memory] = "owned stale sort label";
                        window.Processes.ProcessSortSelector.ItemsSource = items;
                        window.Processes.ProcessSortSelector.SelectedIndex = (int)ProcessSort.Memory;
                        independentSortState = window.Processes.ProcessSortSelector.SelectedIndex == (int)ProcessSort.Memory &&
                            window.Preferences.ProcessSort == ProcessSort.Memory;
                        break;
                    case "query": window.Processes.ProcessSearchBox.Text = "owned stale query"; break;
                    case "selection": window.SelectedSample = null; break;
                    case "empty-label": window.Processes.EmptyLabel.Text = "owned stale empty label"; break;
                }
            }, DispatcherPriority.Render);
        };
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Run()));
        Assert.That(error!.Message, Is.EqualTo("Live language switching left stale text or changed recording state."));
        Assert.That(applied, Is.True);
        Assert.That(independentSortState, Is.True);
        fixture.AssertHealthy();
        fixture.AssertRestored(original);
        fixture.AssertSaved(AppLanguages.SystemDefault);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    private sealed class LanguageSettingsDiagnostic : IDisposable
    {
        private readonly MetricCaptureDiagnostic _fixture;
        private readonly CultureInfo? _resources = Strings.Culture;
        private readonly CultureInfo? _defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;
        private readonly CultureInfo _system = AppLanguages.SystemDisplayCulture;
        private readonly string _directory;
        private readonly DispatcherTimer _watchdog;
        private readonly List<Exception> _unexpected = [];
        private bool _timedOut;
        internal MainWindow Window => _fixture.Window;
        internal List<string> ObservedLanguages { get; } = [];
        internal Action<string>? AfterLanguageChanged { get; set; }
        internal LanguageSettingsDiagnostic(string directory)
        {
            _directory = directory;
            _fixture = new MetricCaptureDiagnostic(directory);
            AppLanguages.InitializeSystemDisplayCulture(CultureInfo.GetCultureInfo("de-DE"));
            Window.Preferences = Window.Preferences with { Language = "en" };
            Window.ApplyLanguage();
            Window.Processes.ProcessSearchBox.Text = "owned original query";
            Window.RefreshVisiblePresentation();
            Strings.Text.PropertyChanged += LanguageChanged;
            Window.Dispatcher.UnhandledException += Unexpected;
            _watchdog = new DispatcherTimer(DispatcherPriority.Send, Window.Dispatcher) { Interval = TimeSpan.FromSeconds(8) };
            _watchdog.Tick += Abort;
            _watchdog.Start();
        }
        private void LanguageChanged(object? sender, PropertyChangedEventArgs args)
        {
            var language = Window.Preferences.Language;
            ObservedLanguages.Add(language);
            AfterLanguageChanged?.Invoke(language);
        }
        private void Unexpected(object sender, DispatcherUnhandledExceptionEventArgs args)
        {
            _unexpected.Add(args.Exception);
            args.Handled = true;
            CloseDialogs();
        }
        private void Abort(object? sender, EventArgs args) { _timedOut = true; CloseDialogs(); }
        private void CloseDialogs()
        {
            foreach (var dialog in Window.OwnedWindows.OfType<SettingsWindow>().ToArray()) dialog.Close();
        }
        internal Task Run(CancellationToken token = default, string? output = null) => typeof(WindowDiagnostics)
            .GetMethod("CaptureLanguageSettingsDiagnosticsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, CancellationToken, Task>>(new WindowDiagnostics(Window))(output ?? _directory, token);
        internal void AssertHealthy()
        {
            Assert.That(_timedOut, Is.False, "Owned modal dialog exceeded the fixture deadline");
            Assert.That(_unexpected, Is.Empty, "Dispatcher callback exceptions are failures, never successful characterization");
        }
        internal void AssertSaved(string language)
        {
            var load = Window.Store.LoadAsync();
            FinishWindowCheck(load);
            Assert.That(load.Result.Language, Is.EqualTo(language));
        }
        internal void AssertRestored(MonitorPreferences original)
        {
            Assert.That(Window.Preferences, Is.SameAs(original));
            Assert.That(Window.SelectedSample, Is.Null);
            Assert.That(Window.Processes.ProcessSearchBox.Text, Is.EqualTo("owned original query"));
            Assert.That(Window.Tray, Is.Null);
            Assert.That(Window.ShownSamplingError, Is.Null);
            Assert.That(Window.OwnedWindows.OfType<SettingsWindow>(), Is.Empty);
            Assert.That(Window.SamplingTask, Is.Null);
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        public void Dispose()
        {
            _watchdog.Stop();
            _watchdog.Tick -= Abort;
            CloseDialogs();
            Window.Dispatcher.UnhandledException -= Unexpected;
            Strings.Text.PropertyChanged -= LanguageChanged;
            _fixture.Dispose();
            AppLanguages.InitializeSystemDisplayCulture(_system);
            CultureInfo.CurrentUICulture = _ui;
            CultureInfo.DefaultThreadCurrentUICulture = _defaultUi;
            Strings.SetCulture(_resources);
        }
    }
}
