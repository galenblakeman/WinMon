using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static HashSet<Task> CurrentExportWrites =>
        (HashSet<Task>)(typeof(MainWindow).GetField("ExportWrites", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing export ownership boundary")).GetValue(null)!;

    private static void InvokeExportEntry(UpdateUi ui, Action<CsvExportDialog>? exercise)
    {
        ui.Window.ApplyTheme(false, false);
        if (exercise is not null && !ui.Window.IsVisible)
        {
            ui.Window.Title = "Owned export entry fixture";
            ui.Window.ShowActivated = false;
            ui.Window.ShowInTaskbar = false;
            ui.Window.Show();
        }
        Assert.That(CurrentExportWrites, Is.Empty);
        var existing = Application.Current.Windows.Cast<Window>().ToHashSet();
        var observed = new HashSet<CsvExportDialog>();
        var context = new WindowActionContext(ui.Window.Dispatcher);
        var previous = SynchronizationContext.Current;
        DispatcherOperation? watch = null;
        Exception? failure = null;
        Window[] OwnedDialogs() => Application.Current.Windows.Cast<Window>().Where(window => !existing.Contains(window)).ToArray();
        void Watch()
        {
            foreach (var window in OwnedDialogs())
            {
                if (window is not CsvExportDialog dialog)
                {
                    failure ??= new AssertionException("Unexpected export window: " + window.Title);
                    window.Close();
                    continue;
                }
                if (!observed.Add(dialog)) continue;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        Assert.That(exercise, Is.Not.Null, "A closing window must not prompt");
                        Assert.That(dialog.Owner, Is.SameAs(ui.Window));
                        Assert.That(dialog.Choice, Is.Null);
                        exercise!(dialog);
                    }
                    catch (Exception error) { failure ??= error; }
                    finally { if (dialog.IsVisible) dialog.Close(); }
                };
            }
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Watch));
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Send, ui.Window.Dispatcher) { Interval = TimeSpan.FromSeconds(10) };
        timeout.Tick += (_, _) =>
        {
            failure ??= new AssertionException("Export entry exceeded its owned modal bound");
            foreach (var dialog in OwnedDialogs()) if (dialog.IsVisible) dialog.Close();
        };
        try
        {
            watch = ui.Window.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(Watch));
            timeout.Start();
            SynchronizationContext.SetSynchronizationContext(context);
            ui.Window.ExportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SynchronizationContext.SetSynchronizationContext(previous);
            FinishWindowCheck(context.Completion.Task);
            FinishWindowCheck(ui.Window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task);
            if (failure is not null) throw new AssertionException(failure.ToString());
            Assert.That(context.Errors, Is.Empty, "Unexpected async-void export failure");
            Assert.That(observed.Count, Is.EqualTo(exercise is null ? 0 : 1));
            Assert.That(OwnedDialogs(), Is.Empty);
            Assert.That(CurrentExportWrites, Is.Empty);
            Assert.That(ui.Window.Session.History.Samples, Is.Empty);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            timeout.Stop();
            watch?.Abort();
            foreach (var dialog in OwnedDialogs()) if (dialog.IsVisible) dialog.Close();
        }
    }

    [Test]
    public void ExportEntryClosingGuardDoesNotOpenAChoiceOrRegisterAWrite()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.IsClosing = true;
        InvokeExportEntry(ui, null);
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [TestCase("compact", CsvExportKind.ResourceHistory)]
    [TestCase("usage", CsvExportKind.UsageTotals)]
    [TestCase("services", CsvExportKind.Services)]
    [TestCase("connections", CsvExportKind.Connections)]
    [TestCase("processes", CsvExportKind.ProcessSamples)]
    [TestCase("resources", CsvExportKind.ResourceHistory)]
    public void ExportEntrySelectsTheExistingInitialKindAndCancelCreatesNoWrite(string mode, CsvExportKind expected)
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        foreach (var tab in new[] { ui.Window.UsageTab, ui.Window.ServicesTab, ui.Window.ConnectionsTab, ui.Window.ProcessesTab })
            tab.IsChecked = false;
        switch (mode)
        {
            case "compact": ui.Window.CompactMode = true; ui.Window.UsageTab.IsChecked = true; break;
            case "usage": ui.Window.UsageTab.IsChecked = true; break;
            case "services": ui.Window.ServicesTab.IsChecked = true; break;
            case "connections": ui.Window.ConnectionsTab.IsChecked = true; break;
            case "processes": ui.Window.ProcessesTab.IsChecked = true; break;
        }
        InvokeExportEntry(ui, dialog =>
        {
            var choices = new[] { dialog.ResourceChoice, dialog.ProcessChoice, dialog.UsageChoice, dialog.ServiceChoice,
                dialog.ConnectionChoice, dialog.TrafficChoice };
            var selected = expected switch
            {
                CsvExportKind.ProcessSamples => dialog.ProcessChoice,
                CsvExportKind.UsageTotals => dialog.UsageChoice,
                CsvExportKind.Services => dialog.ServiceChoice,
                CsvExportKind.Connections => dialog.ConnectionChoice,
                _ => dialog.ResourceChoice
            };
            Assert.That(choices.Where(choice => choice.IsChecked == true), Is.EqualTo(new[] { selected }));
            if (expected == CsvExportKind.UsageTotals) Assert.That(dialog.DataNote.Text, Is.EqualTo(Strings.ExportUsageNote));
            dialog.Close();
            Assert.That(dialog.Choice, Is.Null);
            Assert.That(dialog.DialogResult, Is.False);
        });
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test]
    public void ExportEntryCanceledDialogCanBeOpenedAgainWithoutRetainingAChoice()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        CsvExportDialog? first = null;
        for (var attempt = 0; attempt < 2; attempt++)
            InvokeExportEntry(ui, dialog =>
            {
                Assert.That(dialog, Is.Not.SameAs(first));
                dialog.FileNameBox.Text = "owned-unsaved-name.csv";
                dialog.RangeChoice.IsChecked = true;
                dialog.Close();
                Assert.That(dialog.Choice, Is.Null);
                first = dialog;
            });
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test]
    public void ExportEntryExpiredHistoricalUsageKeepsTheUnavailablePeriodNote()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        ui.Window.UsageTab.IsChecked = true;
        var selected = new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0);
        ui.Window.SelectedSample = selected;
        InvokeExportEntry(ui, dialog =>
        {
            Assert.That(dialog.UsageChoice.IsChecked, Is.True);
            Assert.That(dialog.DataNote.Text, Is.EqualTo(Strings.UsagePeriodUnavailable));
            dialog.Close();
        });
        Assert.That(ui.Window.SelectedSample, Is.SameAs(selected));
        Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test]
    public void ExportEntryAcceptedDialogWithoutAChoiceStopsBeforeTheNativeSavePicker()
    {
        using var ui = new UpdateUi(_directory, diagnostic: true);
        InvokeExportEntry(ui, dialog =>
        {
            dialog.DialogResult = true;
            Assert.That(dialog.Choice, Is.Null);
        });
        Assert.That(Directory.Exists(_directory), Is.False);
    }
}
