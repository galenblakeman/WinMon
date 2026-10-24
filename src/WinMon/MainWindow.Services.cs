using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private bool _serviceActionBusy;

    private void InitializeServices()
    {
        ServiceView.TopServiceCountChanged += count => _preferences = _preferences with { TopServiceCount = count };
        ProcessesTab.Checked += (_, _) => SelectActivityView();
        ServicesTab.Checked += (_, _) => SelectActivityView();
        UsageTab.Checked += (_, _) => SelectActivityView();
        ServiceView.ProcessRequested += process =>
        {
            // Use the existing process view for the selected PID, preserving the selected historical instant.
            Processes.ShowProcess(process);
            ProcessesTab.IsChecked = true;
        };
        ServiceView.ActionRequested += RunServiceAction;
        ServiceView.CopyRequested += CopyProcessDetails;
        ServiceView.PropertiesRequested += (name, details) =>
            new ServicePropertiesWindow(name, details, CopyProcessDetails) { Owner = this }.ShowDialog();
    }

    private void SelectActivityView()
    {
        Processes.Visibility = ProcessesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ServiceView.Visibility = ServicesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UsageView.Visibility = UsageTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ActivityActionsToggle.Visibility = Visibility.Visible;
        RefreshPresentation();
    }

    private void RefreshServices()
    {
        // The compact strip hides the Services tab as well.
        if (ServicesTab.IsChecked != true || CompactMode) return;
        var frame = Session.Services.Find(SelectedSample);
        // A failed collection must not leave stale live metrics or actions on screen.
        if (SelectedSample is null && _samplingError is not null) frame = null;
        var frames = Session.Services.Frames;
        var count = 0;
        if (frame is not null)
            for (var i = frames.Count - 1; i >= 0; i--)
                if (ReferenceEquals(frames[i], frame))
                {
                    count = i + 1;
                    break;
                }

        var history = frames.Take(count).ToArray();
        ServiceView.Update(frame, history, SelectedSample is not null);
    }

    // Fire-and-forget from a menu command; expected failures are reported in the view.
    // ReSharper disable once AsyncVoidMethod
    private async void RunServiceAction(ServiceObservation service, ProcessUsage? host, string action)
    {
        if (_serviceActionBusy || IsClosing || SelectedSample is not null || _diagnosticMode) return;
        if (action != "start" && !MessageDialog.Confirm(this, Strings.Format(Strings.ConfirmServiceAction,
                    action == "stop" ? Strings.StopService : Strings.RestartService, service.DisplayName),
                Strings.Services,
                action == "stop" ? Strings.StopService : Strings.RestartService)) return;
        if (IsClosing || SelectedSample is not null) return;
        _serviceActionBusy = true;
        var actionCancellation = Lifetime.Token;
        ServiceView.SetBusy(true);
        string? error = null;
        try
        {
            try
            {
                await Task.Run(() => WindowsServices.ExecuteAsync(service.Name, action,
                    host?.ProcessId ?? 0, host?.CreationTicks ?? 0, actionCancellation), actionCancellation);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                // Keep the monitor unelevated. The narrowly scoped helper exits after one SCM operation.
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                    { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--service-action");
                start.ArgumentList.Add(action);
                start.ArgumentList.Add(service.Name);
                start.ArgumentList.Add((host?.ProcessId ?? 0).ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add((host?.CreationTicks ?? 0).ToString(CultureInfo.InvariantCulture));
                using var helper = Process.Start(start) ?? throw new Win32Exception(5);
                await helper.WaitForExitAsync(actionCancellation);
                if (helper.ExitCode != 0) throw new Win32Exception(helper.ExitCode);
            }
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            error = Strings.Format(Strings.ServiceActionFailed, ex.Message);
        }
        finally
        {
            _serviceActionBusy = false;
            if (!IsClosing) ServiceView.SetBusy(false, error);
            if (CloseAfterServiceAction) Close();
        }
    }
}