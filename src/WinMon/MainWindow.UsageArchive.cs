using System.Diagnostics;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private UsageArchive? _usageArchive;
    private FileStream? _usageArchiveLock;
    private Task _usageArchiveWrite = Task.CompletedTask;
    private DateTimeOffset _lastUsageArchiveWrite;
    private bool _usageArchiveError;

    private async Task LoadUsageArchiveAsync()
    {
        if (_diagnosticMode) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferenceStore.UsageArchivePath)!);
            _usageArchiveLock = new FileStream(_preferenceStore.UsageArchivePath + ".lock", FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
            _usageArchive = await UsageArchive.LoadAsync(_preferenceStore.UsageArchivePath, Lifetime.Token);
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or
            ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            _usageArchiveError = true;
            _usageArchiveLock?.Dispose();
            _usageArchiveLock = null;
            Trace.TraceError("Usage archive could not be loaded; existing file preserved: {0}", ex);
        }
    }

    private void RecordDailyUsage(MonitorSample sample)
    {
        if (_diagnosticMode || _usageArchive is null) return;
        _usageArchive.Add(sample);
        if (sample.Timestamp - _lastUsageArchiveWrite < TimeSpan.FromMinutes(1) || !_usageArchiveWrite.IsCompleted) return;
        CheckpointUsageArchive();
        _lastUsageArchiveWrite = sample.Timestamp;
    }

    private void CheckpointUsageArchive()
    {
        if (_usageArchive is null || _usageArchiveLock is null) return;
        if (_usageArchiveWrite.IsFaulted)
        {
            _usageArchiveError = true;
            Trace.TraceError("Usage archive checkpoint failed: {0}", _usageArchiveWrite.Exception);
        }
        var state = _usageArchive.Export();
        _usageArchiveWrite = Task.Run(() => UsageArchive.SaveAsync(_preferenceStore.UsageArchivePath, state));
    }

    private async Task SaveUsageArchiveAsync()
    {
        try
        {
            try { await _usageArchiveWrite; } catch (IOException ex) { Trace.TraceError("Usage archive checkpoint failed: {0}", ex); }
            CheckpointUsageArchive();
            await _usageArchiveWrite;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _usageArchiveError = true;
            Trace.TraceError("Usage archive save failed: {0}", ex);
        }
    }
}
