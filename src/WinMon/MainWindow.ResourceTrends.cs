using System.Diagnostics;
using MonCore.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private ResourceTrendArchive _resourceTrends = new();
    private FileStream? _resourceTrendLock;
    private Task _resourceTrendWrite = Task.CompletedTask;
    private Task? _resourceTrendLoad;
    private Task _resourceTrendClear = Task.CompletedTask;
    private long _lastResourceTrendCheckpoint;
    private bool _resourceTrendsReady;
    private bool _resourceTrendsClearing;
    private bool _resourceTrendsDirty;
    private bool _resourceTrendsRejected;
    private bool _resourceTrendsLocked;
    private bool _resourceTrendsSaveFailed;

    private string? ResourceTrendNotice => _resourceTrendsRejected ? Strings.ResourceTrendsLoadError
        : _resourceTrendsLocked ? Strings.ResourceTrendsLocked
        : _resourceTrendsSaveFailed || _resourceTrendWrite.IsFaulted ? Strings.ResourceTrendsSaveError
        : !_preferences.KeepResourceTrends ? Strings.ResourceTrendsOff : null;

    private Task LoadResourceTrendsAsync() => _resourceTrendLoad ??= LoadResourceTrendCoreAsync();

    private async Task LoadResourceTrendCoreAsync()
    {
        if (_diagnosticMode) return;
        try
        {
            AcquireResourceTrendLock();
            var loader = ResourceTrendLoaderForFixture ?? ResourceTrendArchive.LoadAsync;
            _resourceTrends = await Task.Run(() => loader(_preferenceStore.ResourceTrendArchivePath, Lifetime.Token), Lifetime.Token);
            _resourceTrendsDirty = File.Exists(_preferenceStore.ResourceTrendArchivePath);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
                                   System.Text.Json.JsonException or ArgumentException)
        {
            _resourceTrendsRejected = !_resourceTrendsLocked;
            _resourceTrendLock?.Dispose();
            _resourceTrendLock = null;
            Trace.TraceError("Resource trends could not be loaded; existing file preserved: {0}", ex);
        }
        finally
        {
            _resourceTrendsReady = true;
            _lastResourceTrendCheckpoint = Stopwatch.GetTimestamp();
        }
    }

    private void AcquireResourceTrendLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_preferenceStore.ResourceTrendArchivePath)!);
        try
        {
            _resourceTrendLock = new FileStream(_preferenceStore.ResourceTrendArchivePath + ".lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            _resourceTrendsLocked = true;
            throw;
        }
        _resourceTrendsLocked = false;
    }

    private void RecordResourceTrend(MonitorSample sample)
    {
        if (!_resourceTrendsReady || _diagnosticMode || _resourceTrendsClearing || !_preferences.KeepResourceTrends) return;
        _resourceTrends.Add(sample);
        _resourceTrendsDirty = true;
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastResourceTrendCheckpoint, now) < TimeSpan.FromMinutes(1)) return;
        if (!_resourceTrendWrite.IsCompleted) return;
        CheckpointResourceTrends();
        _lastResourceTrendCheckpoint = now;
    }

    private void CheckpointResourceTrends()
    {
        ObserveResourceTrendWrite();
        if (!_resourceTrendsDirty || _resourceTrendsClearing || _resourceTrendsRejected || _resourceTrendLock is null) return;
        if (!_resourceTrendWrite.IsCompleted) return;
        var state = _resourceTrends.Snapshot(DateTimeOffset.UtcNow);
        _resourceTrendsDirty = false;
        _resourceTrendWrite = Task.Run(() => ResourceTrendArchive.SaveAsync(_preferenceStore.ResourceTrendArchivePath, state));
    }

    private void ObserveResourceTrendWrite()
    {
        if (!_resourceTrendWrite.IsCompleted) return;
        _resourceTrendsSaveFailed = _resourceTrendWrite.IsFaulted;
        if (_resourceTrendWrite.Exception is { } failure)
        {
            _resourceTrendsDirty = true;
            Trace.TraceError("Resource trend checkpoint failed: {0}", failure);
        }
    }

    private async Task SaveResourceTrendsAsync()
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            try { await _resourceTrendClear.WaitAsync(ResourceTrendSaveRemaining(started)); }
            catch (Exception ex) when (ex is not TimeoutException) { Trace.TraceError("Resource trend clear failed: {0}", ex); }
            try { await _resourceTrendWrite.WaitAsync(ResourceTrendSaveRemaining(started)); }
            catch (Exception ex) when (ex is not TimeoutException) { ObserveResourceTrendWrite(); }
            CheckpointResourceTrends();
            await _resourceTrendWrite.WaitAsync(ResourceTrendSaveRemaining(started));
            ObserveResourceTrendWrite();
        }
        catch (TimeoutException)
        {
            Trace.TraceWarning("Resource trend save reached the shutdown deadline.");
        }
        catch (Exception ex)
        {
            ObserveResourceTrendWrite();
            Trace.TraceError("Resource trend shutdown save failed: {0}", ex);
        }
    }

    private static TimeSpan ResourceTrendSaveRemaining(long started)
    {
        var remaining = TimeSpan.FromSeconds(3) - Stopwatch.GetElapsedTime(started);
        return remaining > TimeSpan.Zero ? remaining : throw new TimeoutException();
    }

    private Task ClearResourceTrendsAsync()
    {
        if (_resourceTrendsClearing) return _resourceTrendClear;
        if (IsClosing) return Task.CompletedTask;
        _resourceTrendClear = ClearResourceTrendCoreAsync();
        return _resourceTrendClear;
    }

    private async Task ClearResourceTrendCoreAsync()
    {
        _resourceTrendsClearing = true;
        try
        {
            await LoadResourceTrendsAsync();
            try { await _resourceTrendWrite; }
            catch (Exception) { ObserveResourceTrendWrite(); }
            if (_resourceTrendLock is null) AcquireResourceTrendLock();
            await Task.Run(() => File.Delete(_preferenceStore.ResourceTrendArchivePath));
            _resourceTrendWrite = Task.CompletedTask;
            _resourceTrends = new ResourceTrendArchive();
            _resourceTrendsDirty = false;
            _resourceTrendsRejected = false;
            _resourceTrendsSaveFailed = false;
            _lastResourceTrendCheckpoint = Stopwatch.GetTimestamp();
            RefreshResourceTrends();
        }
        finally { _resourceTrendsClearing = false; }
    }

    private void RefreshResourceTrends()
    {
        ObserveResourceTrendWrite();
        _flightRecorder?.UpdateTrends(_resourceTrends.Snapshot(DateTimeOffset.UtcNow), ResourceTrendNotice);
    }

    private void SaveResourceTrendsForSessionEnd()
    {
        if (!_resourceTrendsDirty || _resourceTrendsClearing || _resourceTrendsRejected || _resourceTrendLock is null) return;
        var state = _resourceTrends.Snapshot(DateTimeOffset.UtcNow);
        var previous = _resourceTrendWrite;
        // Windows may end the process when this returns; no continuation may require the dispatcher.
        _resourceTrendWrite = Task.Run(async () =>
        {
            try { await previous.ConfigureAwait(false); }
            catch (Exception ex) { Trace.TraceError("Resource trend previous checkpoint failed: {0}", ex); }
            await ResourceTrendArchive.SaveAsync(_preferenceStore.ResourceTrendArchivePath, state).ConfigureAwait(false);
        });
        try
        {
            if (!_resourceTrendWrite.Wait(TimeSpan.FromSeconds(3)))
                Trace.TraceWarning("Resource trend save reached the session-end deadline.");
        }
        catch (AggregateException ex) { Trace.TraceError("Resource trend session-end save failed: {0}", ex.InnerException); }
    }

    private void ReleaseResourceTrendLock()
    {
        var archiveLock = _resourceTrendLock;
        _resourceTrendLock = null;
        var pending = Task.WhenAll(_resourceTrendLoad ?? Task.CompletedTask, _resourceTrendWrite, _resourceTrendClear);
        if (pending.IsCompleted) { _ = pending.Exception; archiveLock?.Dispose(); }
        else _ = pending.ContinueWith(completed => { _ = completed.Exception; archiveLock?.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
