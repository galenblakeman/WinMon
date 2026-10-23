using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private readonly ServiceHistory _serviceHistory = new();
    private bool _serviceActionBusy;
    private bool _closeAfterServiceAction;

    private void InitializeServices()
    {
        ProcessesTab.Checked += (_, _) => SelectServiceView(false);
        ServicesTab.Checked += (_, _) => SelectServiceView(true);
        ServiceView.ProcessRequested += process =>
        {
            // Use the existing process view for the selected PID, preserving the selected historical instant.
            AppsOnlyCheckBox.IsChecked = false;
            PinnedOnlyCheckBox.IsChecked = false;
            IndividualChip.IsChecked = true;
            ProcessSearchBox.Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
            ProcessesTab.IsChecked = true;
        };
        ServiceView.ActionRequested += RunServiceAction;
        ServiceView.CopyRequested += CopyProcessDetails;
    }

    private void SelectServiceView(bool services)
    {
        ProcessesArea.Visibility = services ? Visibility.Collapsed : Visibility.Visible;
        ServiceView.Visibility = services ? Visibility.Visible : Visibility.Collapsed;
        RefreshPresentation();
    }

    private void RefreshServices()
    {
        if (ServicesTab.IsChecked != true) return;
        var frame = _serviceHistory.Find(_selectedSample);
        // A failed collection must not leave stale live metrics or actions on screen.
        if (_selectedSample is null && _samplingError is not null) frame = null;
        var index = frame is null ? -1 : _serviceHistory.Frames.ToList().FindIndex(f => ReferenceEquals(f, frame));
        var history = _serviceHistory.Frames.Take(index + 1).ToArray();
        ServiceView.Update(frame, history, _selectedSample is not null);
    }

    private async void RunServiceAction(ServiceObservation service, ProcessUsage? host, string action)
    {
        if (_serviceActionBusy || _closing || _selectedSample is not null || _diagnosticMode) return;
        if (action != "start" && !Controls.MessageDialog.Confirm(this, Strings.Format(Strings.ConfirmServiceAction,
                action == "stop" ? Strings.StopService : Strings.RestartService, service.DisplayName), Strings.Services,
                action == "stop" ? Strings.StopService : Strings.RestartService)) return;
        if (_closing || _selectedSample is not null) return;
        _serviceActionBusy = true;
        var actionCancellation = _lifetime.Token;
        ServiceView.SetBusy(true);
        string? error = null;
        try
        {
            try
            {
                await Task.Run(() => WindowsServices.ExecuteAsync(service.Name, action,
                    host?.ProcessId ?? 0, host?.CreationTicks ?? 0, actionCancellation));
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                // Keep the monitor unelevated. The narrowly scoped helper exits after one SCM operation.
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
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
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        { error = Strings.Format(Strings.ServiceActionFailed, ex.Message); }
        finally
        {
            _serviceActionBusy = false;
            if (!_closing) ServiceView.SetBusy(false, error);
            if (_closeAfterServiceAction) Close();
        }
    }
}
