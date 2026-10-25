using System.Diagnostics;
using System.Windows;
using WinMon.Monitoring;
using WinMon.Updates;

namespace WinMon;

/// <summary>Coordinates signed-release discovery and the in-window update affordance.</summary>
public partial class MainWindow
{
    private UpdateClient? _updateClient;
    private UpdateOffer? _updateOffer;
    private Task? _updateCheckTask;
    private readonly CancellationTokenSource _updateLifetime = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private CancellationTokenSource? _updateDownload;
    private Task? _updateDownloadTask;
    private bool _updateDismissed;
    private bool _updateRestartPending;
    private DateTimeOffset? _lastUpdateCheck;
    private bool _updateDownloading;

    // Where this exit saves history for the new version: the update's transaction directory and token.
    private (string Path, string Token)? _restartHistoryTarget;

    private void InitializeUpdates()
    {
        if (_diagnosticMode || !UpdatePolicy.GitHubUpdatesEnabled) return;
        _updateClient = new UpdateClient();
        var notice = UpdateRuntime.ConsumeNotice();
        if (!string.IsNullOrWhiteSpace(notice)) ShowUpdateStatus(notice, true);
        _updateCheckTask = MonitorUpdatesAsync(_updateLifetime.Token);
    }

    private async Task MonitorUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (Volatile.Read(ref _preferences).AutomaticallyCheckForUpdates)
                    await CheckForUpdatesAsync(false, cancellationToken);
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon update monitor stopped: {0}", ex);
        }
    }

    private async Task CheckForUpdatesAsync(bool requestedByUser, CancellationToken cancellationToken = default)
    {
        if (_updateClient is null || IsClosing || _updateDownloading) return;
        if (requestedByUser)
        {
            _updateDismissed = false;
            ShowUpdateStatus(Strings.UpdateChecking, true);
        }

        var gateHeld = false;
        try
        {
            await _updateGate.WaitAsync(cancellationToken);
            gateHeld = true;
            var offer = await _updateClient.CheckAsync(AppInfo.Version, cancellationToken);
            _lastUpdateCheck = DateTimeOffset.Now;
            if (IsClosing) return;
            var changedOffer = offer?.Version != _updateOffer?.Version;
            _updateOffer = offer;
            if (changedOffer) _updateDismissed = false;
            if (offer is null)
            {
                if (requestedByUser) ShowUpdateStatus(Strings.UpToDate, true);
                // Hide a withdrawn offer, but not a capture notice sharing the footer.
                else if (changedOffer && UpdateNowButton.Visibility == Visibility.Visible)
                    UpdateFooter.Visibility = Visibility.Collapsed;
                return;
            }

            if (_updateDismissed) return;
            ShowUpdateOffer(offer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("WinMon update check failed: {0}", ex.Message);
            if (requestedByUser) ShowUpdateFailure(Strings.UpdateFailed);
        }
        finally
        {
            if (gateHeld) _updateGate.Release();
        }
    }

    internal void ShowUpdateStatus(string text, bool dismissible)
    {
        UpdateStatusText.Text = text;
        UpdateFooter.Visibility = Visibility.Visible;
        ReleaseNotesButton.Visibility = Visibility.Collapsed;
        UpdateNowButton.Visibility = Visibility.Collapsed;
        DismissUpdateButton.Visibility = dismissible ? Visibility.Visible : Visibility.Collapsed;
        CancelUpdateButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Offer <paramref name="offer"/>, under <paramref name="text"/> or its availability line.</summary>
    private void ShowUpdateOffer(UpdateOffer offer, string? text = null)
    {
        ShowUpdateStatus(text ?? Strings.Format(Strings.UpdateAvailable, offer.Version), true);
        ReleaseNotesButton.Visibility = Visibility.Visible;
        UpdateNowButton.Visibility = Visibility.Visible;
    }

    /// <summary>Show <paramref name="text"/>, keeping a pending offer's buttons so it can be retried at once.</summary>
    private void ShowUpdateFailure(string text)
    {
        if (_updateOffer is { } offer && !_updateDismissed) ShowUpdateOffer(offer, text);
        else ShowUpdateStatus(text, true);
    }

    private void OnDismissUpdate(object sender, RoutedEventArgs e)
    {
        _updateDismissed = true;
        UpdateFooter.Visibility = Visibility.Collapsed;
    }

    private void OnCancelUpdate(object sender, RoutedEventArgs e)
    {
        _updateDownload?.Cancel();
    }

    private void OnReleaseNotes(object sender, RoutedEventArgs e)
    {
        if (_updateOffer?.ReleaseNotesUrl is not { } url) return;
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("WinMon could not open release notes: {0}", ex.Message);
        }
    }

    // WPF requires a void click handler; all transfer and replacement failures leave the current app running.
    // ReSharper disable once AsyncVoidEventHandlerMethod
    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_updateClient is null || _updateOffer is null || IsClosing) return;
        UpdateNowButton.IsEnabled = false;
        ReleaseNotesButton.IsEnabled = false;
        DismissUpdateButton.IsEnabled = false;
        _updateDownloadTask = DownloadAndLaunchAsync();
        await _updateDownloadTask;
    }

    private async Task DownloadAndLaunchAsync()
    {
        var gateHeld = false;
        try
        {
            await _updateGate.WaitAsync(_updateLifetime.Token);
            gateHeld = true;
            if (_updateClient is null || _updateOffer is null || IsClosing) return;
            // No concurrent check can now replace the version or footer while its matching asset downloads.
            _updateDownloading = true;
            _updateDownload = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token, _updateLifetime.Token);
            ShowUpdateStatus(Strings.UpdateChecking, false);
            CancelUpdateButton.Visibility = Visibility.Visible;
            if (await LatestOfferAsync(_updateClient, _updateDownload.Token) is not { } offer)
            {
                ShowUpdateStatus(Strings.UpToDate, true);
                return;
            }

            var progress = new Progress<int>(percent =>
            {
                if (_updateDownloading) UpdateStatusText.Text = Strings.Format(Strings.DownloadingUpdate, percent);
            });
            UpdateStatusText.Text = Strings.Format(Strings.DownloadingUpdate, 0);
            var prepared = await _updateClient.DownloadAsync(offer, progress, _updateDownload.Token);
            ShowUpdateStatus(Strings.RestartingToUpdate, false);
            var (directory, token) = await UpdateLauncher.StartAsync(prepared, _updateDownload.Token);
            _restartHistoryTarget = (Path.Combine(directory, RestartHistory.FileName), token);
            _updateRestartPending = true;
            Close();
        }
        catch (OperationCanceledException) when (_updateDownload?.IsCancellationRequested == true)
        {
            ShowUpdateFailure(Strings.UpdateCancelled);
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon update download or handoff failed: {0}", ex);
            ShowUpdateFailure(Strings.UpdateFailed);
        }
        finally
        {
            _updateDownload?.Dispose();
            _updateDownload = null;
            _updateDownloading = false;
            if (gateHeld) _updateGate.Release();
            if (!IsClosing)
            {
                UpdateNowButton.IsEnabled = true;
                ReleaseNotesButton.IsEnabled = true;
                DismissUpdateButton.IsEnabled = true;
            }
        }
    }

    /// <summary>The newest release, checked again on Update now because the footer's offer may be hours old.</summary>
    /// <remarks>
    /// An unchanged release costs one 304 response. When the check itself fails, the signed offer already shown is
    /// used, and its download reports any lasting network fault.
    /// </remarks>
    private async Task<UpdateOffer?> LatestOfferAsync(UpdateClient client, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await client.CheckAsync(AppInfo.Version, cancellationToken);
            _lastUpdateCheck = DateTimeOffset.Now;
            _updateOffer = latest;
            return latest;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Trace.TraceWarning("WinMon could not recheck the update before downloading: {0}", ex.Message);
            return _updateOffer;
        }
    }

    /// <summary>Save history for the new version once sampling has stopped. Best effort, and never throws.</summary>
    /// <remarks>
    /// The wait ends at the restart budget even if a write is blocked, so a stalled disk cannot hold the update;
    /// the save then discards its temporary file instead of moving it into place.
    /// </remarks>
    private async Task SaveRestartHistoryAsync()
    {
        if (_restartHistoryTarget is not { } target) return;
        try
        {
            // Copied on the UI thread: sampling has stopped, but the ledger is not thread-safe.
            var samples = Session.History.Samples;
            var usage = Session.Usage.Export();
            await Task.Run(() => RestartHistory.Save(target.Path, target.Token, AppInfo.Version, samples, usage,
                RestartClock.System)).WaitAsync(RestartHistory.Budget);
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning("WinMon restart history was still saving at its deadline; updating without it.");
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon restart history save failed: {0}", ex);
        }
    }

    /// <summary>Restore what the previous version saved for this update restart, before sampling starts.</summary>
    /// <remarks>The read finishes before anything is recorded, so the session gets all of it or none.</remarks>
    private async Task RestoreRestartHistoryAsync()
    {
        if (_diagnosticMode || UpdateRuntime.RestartHistory is not { } source) return;
        try
        {
            var contents = await Task.Run(() => RestartHistory.Read(source.Path, source.Token, RestartClock.System));
            if (contents is null) return;
            foreach (var sample in contents.Samples) Session.Record(sample);
            if (contents.Usage is { } usage) Session.Usage.Restore(usage);
        }
        catch (Exception ex)
        {
            Trace.TraceError("WinMon restart history restore failed: {0}", ex);
        }
    }

    internal void AbortPendingUpdate()
    {
        if (!_updateRestartPending) return;
        _updateRestartPending = false;
        UpdateLauncher.Cancel();
    }

    internal async Task ShutdownUpdatesAsync()
    {
        if (_updateDownload is not null) await _updateDownload.CancelAsync();
        await _updateLifetime.CancelAsync();
        if (_updateDownloadTask is not null)
            try
            {
                await _updateDownloadTask;
            }
            catch (OperationCanceledException)
            {
            }

        if (_updateCheckTask is not null)
            try
            {
                await _updateCheckTask;
            }
            catch (OperationCanceledException)
            {
            }

        // A manual Settings check uses the same gate but is not the periodic task.
        await _updateGate.WaitAsync();
        _updateGate.Release();
        _updateClient?.Dispose();
        _updateLifetime.Dispose();
        _updateGate.Dispose();
    }

    internal void ShowCompletedUpdateNotice()
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) return;
        _ = ShowCompletedUpdateNoticeAsync();
    }

    private async Task ShowCompletedUpdateNoticeAsync()
    {
        try
        {
            // The helper persists success just after receiving the first-sample readiness signal.
            for (var attempt = 0; attempt < (UpdateRuntime.IsExpectedRestart ? 20 : 1) && !IsClosing; attempt++)
            {
                var notice = UpdateRuntime.ConsumeNotice();
                if (string.IsNullOrWhiteSpace(notice))
                {
                    await Task.Delay(250, _updateLifetime.Token);
                    continue;
                }

                ShowUpdateStatus(notice, true);
                await HideTransientNoticeAsync(notice, _updateLifetime.Token);
                return;
            }
        }
        catch (OperationCanceledException) when (_updateLifetime.IsCancellationRequested)
        {
        }
    }

    private async Task HideTransientNoticeAsync(string notice, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            if (!IsClosing && UpdateStatusText.Text == notice) UpdateFooter.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void RefreshUpdateText()
    {
        if (_updateDismissed || _updateDownloading || UpdateFooter.Visibility != Visibility.Visible) return;
        if (_updateOffer is not null)
            UpdateStatusText.Text = Strings.Format(Strings.UpdateAvailable, _updateOffer.Version);
    }
}
