using DependencyPropertyDescriptor = System.ComponentModel.DependencyPropertyDescriptor;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(1000), TestCase(1400), Category("Integration")]
    public void SearchDiagnosticPreservesAllCapturesSortingAndFinalLiveState(double width)
    {
        using var fixture = new SearchDiagnostic(_directory, width: width);
        var visualWidth = ((FrameworkElement)fixture.Window.Content).ActualWidth;
        FinishWindowCheck(fixture.Start());
        var expected = new[] { "process-search-empty.png", "process-search-idle.png", "process-search-wide.png",
            "process-sort-Threads-live.png", "process-sort-Threads-grouped.png", "process-sort-Threads-history.png",
            "process-sort-Handles-live.png", "process-sort-Handles-grouped.png", "process-sort-Handles-history.png" };
        Assert.That(Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName), Is.EquivalentTo(expected));
        foreach (var file in expected)
            fixture.AssertPng(file, file == "process-search-wide.png" ? visualWidth + 886 - width : visualWidth, width);
        Assert.That(File.ReadAllText(Path.Combine(_directory, "search-passed.txt")),
            Is.EqualTo("Native search filtering, clear, count sorting and header checks passed."));
        fixture.AssertFinalState(width);
    }

    [TestCase(false), TestCase(true)]
    public void SearchDiagnosticPreservesMissingSampleGuardsBeforeChangingTheQuery(bool missingCpu)
    {
        using var fixture = new SearchDiagnostic(_directory, seed: false);
        if (missingCpu) fixture.Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0));
        fixture.Window.Processes.ProcessSearchBox.Text = "owned existing query";
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(error!.Message, Is.EqualTo("Search diagnostics need native samples."));
        Assert.That(fixture.Window.Processes.ProcessSearchBox.Text, Is.EqualTo("owned existing query"));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    [TestCase("clear"), TestCase("placeholder"), TestCase("row"), TestCase("empty-visibility"), TestCase("empty-text")]
    [Category("Integration")]
    public void SearchDiagnosticPreservesEveryFilteringValidationFailure(string partition)
    {
        using var fixture = new SearchDiagnostic(_directory);
        var view = fixture.Window.Processes;
        if (partition == "clear") fixture.ForceProperty(view.ClearProcessSearchButton, UIElement.VisibilityProperty, Visibility.Collapsed);
        if (partition == "placeholder") fixture.ForceProperty(view.ProcessSearchPlaceholder, UIElement.VisibilityProperty, Visibility.Visible);
        if (partition == "row") fixture.ForceProperty(view.Rows.Single(), UIElement.VisibilityProperty, Visibility.Visible);
        if (partition == "empty-visibility") fixture.ForceProperty(view.EmptyLabel, UIElement.VisibilityProperty, Visibility.Collapsed);
        if (partition == "empty-text") fixture.ForceProperty(view.EmptyLabel, TextBlock.TextProperty, "owned wrong empty message");
        fixture.Reject("Process search did not filter the visible rows.");
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        Assert.That(view.ProcessSearchBox.Text, Is.EqualTo("__winmon_search_no_match__"));
    }

    [TestCase("query"), TestCase("clear"), TestCase("placeholder"), Category("Integration")]
    public void SearchDiagnosticPreservesEveryClearedFieldValidationFailure(string partition)
    {
        using var fixture = new SearchDiagnostic(_directory);
        var view = fixture.Window.Processes;
        bool Cleared() => view.ProcessSearchBox.Text.Length == 0;
        if (partition == "query") fixture.ForceProperty(view.ProcessSearchBox, TextBox.TextProperty, "owned returned query", Cleared);
        if (partition == "clear") fixture.ForceProperty(view.ClearProcessSearchButton, UIElement.VisibilityProperty, Visibility.Visible, Cleared);
        if (partition == "placeholder") fixture.ForceProperty(view.ProcessSearchPlaceholder, UIElement.VisibilityProperty, Visibility.Collapsed, Cleared);
        fixture.Reject("Clearing process search did not restore the idle field.");
        Assert.That(Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName), Is.EqualTo(new[] { "process-search-empty.png" }));
    }

    [TestCase(ProcessSort.Threads, "preference"), TestCase(ProcessSort.Threads, "heading")]
    [TestCase(ProcessSort.Handles, "preference"), TestCase(ProcessSort.Handles, "heading"), Category("Integration")]
    public void SearchDiagnosticPreservesDropdownPreferenceAndHeadingValidation(ProcessSort sort, string partition)
    {
        using var fixture = new SearchDiagnostic(_directory);
        var window = fixture.Window;
        var view = window.Processes;
        if (partition == "preference")
        {
            Action<ProcessListSettings> invalidate = settings =>
            {
                if (settings.Sort == sort) window.Preferences = window.Preferences with { ProcessSort = ProcessSort.Cpu };
            };
            view.SettingsChanged += invalidate;
            fixture.Cleanup(() => view.SettingsChanged -= invalidate);
        }
        else fixture.ForceProperty(sort == ProcessSort.Threads ? view.ThreadsHeader : view.HandlesHeader,
            TextBlock.TextProperty, "owned missing sort arrow", () => window.Preferences.ProcessSort == sort);
        fixture.Reject($"The {sort} dropdown did not update its column heading.");
        Assert.That(File.Exists(Path.Combine(_directory, "search-passed.txt")), Is.False);
        Assert.That(window.SelectedSample, Is.Null);
    }

    [TestCase(ProcessSort.Threads, "preference"), TestCase(ProcessSort.Threads, "dropdown")]
    [TestCase(ProcessSort.Handles, "preference"), TestCase(ProcessSort.Handles, "dropdown"), Category("Integration")]
    public void SearchDiagnosticPreservesHeadingClickPreferenceAndDropdownValidation(ProcessSort sort, string partition)
    {
        using var fixture = new SearchDiagnostic(_directory);
        var window = fixture.Window;
        var view = window.Processes;
        var header = sort == ProcessSort.Threads ? view.ThreadsHeader : view.HandlesHeader;
        var called = false;
        MouseButtonEventHandler invalidate = (_, _) =>
        {
            called = true;
            if (partition == "preference") window.Preferences = window.Preferences with { ProcessSort = ProcessSort.Cpu };
            else view.ProcessSortSelector.SelectedIndex = -1;
        };
        header.AddHandler(UIElement.MouseLeftButtonUpEvent, invalidate, handledEventsToo: true);
        fixture.Cleanup(() => header.RemoveHandler(UIElement.MouseLeftButtonUpEvent, invalidate));
        fixture.Reject($"Clicking the {sort} heading did not update the dropdown.");
        Assert.That(called, Is.True);
        Assert.That(File.Exists(Path.Combine(_directory, "search-passed.txt")), Is.False);
    }

    [TestCase("process-search-empty.png"), TestCase("process-search-idle.png"), TestCase("process-search-wide.png")]
    [Category("Integration")]
    public void SearchDiagnosticPreservesQueryAndWidthAtEachInitialCaptureFailure(string file)
    {
        using var fixture = new SearchDiagnostic(_directory);
        var path = Path.Combine(_directory, file);
        File.WriteAllText(path, "owned search image sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned search image sentinel"));
        Assert.That(fixture.Window.Processes.ProcessSearchBox.Text,
            Is.EqualTo(file == "process-search-empty.png" ? "__winmon_search_no_match__" : ""));
        // Preserve the original diagnostic's lack of a width-restoration finally block.
        Assert.That(fixture.Window.Width, Is.EqualTo(file == "process-search-wide.png" ? 886 : 1000));
        Assert.That(File.Exists(Path.Combine(_directory, "search-passed.txt")), Is.False);
    }

    [Test, Category("Integration")]
    public void SearchDiagnosticPreservesMissingOutputDirectoryFailureAndFilteredQuery()
    {
        using var fixture = new SearchDiagnostic(_directory);
        var output = Path.Combine(_directory, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => FinishWindowCheck(fixture.Start(output)));
        Assert.That(Directory.Exists(output), Is.False);
        Assert.That(fixture.Window.Processes.ProcessSearchBox.Text, Is.EqualTo("__winmon_search_no_match__"));
        Assert.That(fixture.Window.Width, Is.EqualTo(1000));
    }

    [Test, Category("Integration")]
    public void SearchDiagnosticPreservesCompletionWriteFailureAfterAllCapturesAndStateChanges()
    {
        using var fixture = new SearchDiagnostic(_directory);
        var path = Path.Combine(_directory, "search-passed.txt");
        File.WriteAllText(path, "owned completion sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Throws<IOException>(() => FinishWindowCheck(fixture.Start()));
        Assert.That(File.ReadAllText(path), Is.EqualTo("owned completion sentinel"));
        Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(9));
        fixture.AssertFinalState(1000);
    }

    [Test, Category("Integration")]
    public void SearchDiagnosticPreservesPreCancellationAfterApplyingItsFilter()
    {
        using var fixture = new SearchDiagnostic(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(fixture.Start(token: cancellation.Token)));
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
        Assert.That(fixture.Window.Processes.ProcessSearchBox.Text, Is.EqualTo("__winmon_search_no_match__"));
        Assert.That(fixture.Window.Width, Is.EqualTo(1000));
        Assert.That(fixture.Window.Session.HasLiveSample, Is.False);
    }

    private sealed class SearchDiagnostic : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly string _directory;
        private readonly List<Action> _cleanup = [];
        internal MainWindow Window => _ui.Window;
        internal SearchDiagnostic(string directory, double width = 1000, bool seed = true)
        {
            _ui = new UpdateUi(directory, true);
            try
            {
                _directory = directory;
                Directory.CreateDirectory(directory);
                Window.ApplyTheme(false, false);
                if (seed)
                {
                    var process = new ProcessUsage(int.MaxValue - 400, 100, "owned search process", 3, 1_048_576)
                    { HasAppWindow = true, ThreadCount = 7, HandleCount = 11 };
                    FinishWindowCheck(ProcessIconProvider.GetAsync(process));
                    var resources = new ResourceSample { TotalMemoryBytes = 32UL * 1_073_741_824,
                        UsedMemoryBytes = 16UL * 1_073_741_824 };
                    Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(1), 7, [process], 0)
                    { Resources = resources });
                    Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(2), 9,
                        [process with { CpuPercent = 4, ThreadCount = 9, HandleCount = 15 }], 0) { Resources = resources });
                }
                Window.ShowActivated = false;
                Window.ShowInTaskbar = false;
                Window.Title = "Owned search diagnostic fixture";
                Window.Width = width;
                Window.Height = 840;
                Window.Show();
                FinishWindowCheck(Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
                Window.RefreshVisiblePresentation();
                if (seed) Assert.That(Window.Processes.Rows.Any(row => row.Visibility == Visibility.Visible), Is.True);
            }
            catch { _ui.Dispose(); throw; }
        }
        internal Task Start(string? output = null, CancellationToken token = default) =>
            new WindowDiagnostics(Window).CaptureSearchDiagnosticsAsync(output ?? _directory, token);
        internal void Reject(string message)
        {
            var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(Start()));
            Assert.That(error!.Message, Is.EqualTo(message));
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        internal void Cleanup(Action action) => _cleanup.Add(action);
        internal void ForceProperty(DependencyObject element, DependencyProperty property, object badValue, Func<bool>? when = null)
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(property, element.GetType());
            Assert.That(descriptor, Is.Not.Null);
            EventHandler apply = (_, _) =>
            {
                if ((when?.Invoke() ?? true) && !Equals(element.GetValue(property), badValue)) element.SetValue(property, badValue);
            };
            descriptor.AddValueChanged(element, apply);
            Cleanup(() => descriptor.RemoveValueChanged(element, apply));
        }
        internal void AssertPng(string file, double expectedWidth, double artifactWidth)
        {
            using var stream = File.Open(Path.Combine(_directory, file), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
            Assert.That(frame.PixelWidth, Is.EqualTo((int)Math.Ceiling(expectedWidth)));
            Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            Assert.That(frame.DpiX, Is.EqualTo(96).Within(.1));
            var directory = TestContext.Parameters.Get("SearchDiagnosticArtifactDirectory", "");
            if (directory.Length == 0) return;
            Directory.CreateDirectory(directory);
            using var copy = File.Create(Path.Combine(directory, $"{artifactWidth:0}-{file}"));
            stream.Position = 0;
            stream.CopyTo(copy);
        }
        internal void AssertFinalState(double width)
        {
            Assert.That(Window.Width, Is.EqualTo(width));
            Assert.That(Window.Processes.ProcessSearchBox.Text, Is.Empty);
            Assert.That(Window.Processes.ClearProcessSearchButton.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(Window.Processes.ProcessSearchPlaceholder.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Window.Preferences.ProcessSort, Is.EqualTo(ProcessSort.Cpu));
            Assert.That(Window.Processes.ProcessSortSelector.SelectedIndex, Is.EqualTo((int)ProcessSort.Cpu));
            // The diagnostic clears SelectedSample before its individual-chip assignment, but
            // Processes.IsHistorical still preserves the live grouped preference for that change.
            Assert.That(Window.Preferences.GroupProcesses, Is.True);
            Assert.That(Window.Processes.GroupedChip.IsChecked, Is.True);
            Assert.That(Window.Processes.IndividualChip.IsChecked, Is.False);
            Assert.That(Window.SelectedSample, Is.Null);
            Assert.That(Window.Session.HasLiveSample, Is.False);
        }
        public void Dispose()
        {
            foreach (var cleanup in _cleanup.AsEnumerable().Reverse()) cleanup();
            _ui.Dispose();
        }
    }
}
