# Keep history across update restarts

Proposed on 2026-10-24. Status: implemented on 2026-10-24 (Sequence steps 1 to 5), except the native update smoke test; see Implementation notes.

## Why

**Update now** exits WinMon, replaces the executable and starts the new version. Today that wipes the monitoring history, and the README warns: "Restarting clears the current monitoring history." Someone who installs an update in the middle of an investigation loses the spike they were looking at. This note covers only that restart. A normal exit, a crash, sign-out and reboot still clear history, as they do today.

The same file also carries the session totals behind the [Usage tab](USAGE-TAB.md), so "since the session started" means since WinMon started, through any number of updates.

## Behavior

- After an update restart, the chart, Capture mode and the process table show the samples recorded before the update, then a gap while WinMon was not running, then live samples.
- The Usage tab's totals and its "since" time carry over.
- These are not restored: the Live/Capture selection (WinMon opens in Live), the selected sample, service history (two minutes, refilled quickly), search text and row selection. Pins and list settings are already in preferences.
- If the history cannot be saved or restored, the update still goes ahead and WinMon starts with empty history, the same as today. A history failure never cancels or delays an update beyond the time budget below.
- Store builds are unchanged. The Store replaces the app without telling WinMon.

## Design

### When it is written

`MainWindow.OnClosing` already has an update-only path. The history is written after sampling has stopped (`await _samplingTask`), so history no longer changes, and before `UpdateLauncher.Commit()` in the `finally` block, so the helper does not replace the executable until the file is complete.

```
OnClosing (update pending)
  save preferences            existing; failure cancels the update
  Session.Close, cancel, await sampling
  write restart history       new; best effort, time-limited
  finally: Commit             existing; helper replaces the exe and launches it with --update-result <dir> <token>
```

### Where it is written

The file is `restart-history.bin` in the update's transaction directory, `%LOCALAPPDATA%\WinMon\Updates\<token>\`. Portable builds use the same location, because the transaction directory is already there.

- **One owner.** The new instance already receives that directory and token through `--update-result`, and `UpdateRuntime.AcceptResultArguments` validates both. A file from any other transaction, or a stale one, is never read.
- **Cleanup comes free.** `CompleteInterruptedTransaction` already deletes the transaction directory once the update notice has been shown. The new instance also deletes the file as soon as it has read it, whether the read succeeds or not.
- **The writer is always the version being updated from.** The running version saves before it exits, so a version without this feature writes nothing.
  - **Updating from 2.0.4 or earlier** into the first release with this feature carries no history. That update starts empty, as today.
  - **History survives an update** only when the version being updated from has the writer, which means from the first supporting release onwards.
- **If the update fails or rolls back,** the helper relaunches the version that wrote the file, with the same arguments. That version reads its own format, so a rollback keeps history whenever a file was written. A newer version must still read every older format it can be updated from (see Version compatibility below).

### When it is read

In `OnLoaded`, right after `Session.History.Resize(_preferences.RetentionSeconds)` and before `Session.Run` starts, and only when `UpdateRuntime.IsExpectedRestart` is true. Restored samples go through `MonitorSession.Record` (it already exists for synthetic frames), and that history already has the new version's retention, so it trims as it fills. [Restore](#restore) describes the read itself.

Two ordering constraints:

- **Read before cleanup.** The read must finish before `UpdateRuntime.SignalReady()`, whose stale-transaction cleanup may delete the directory. It does today, because `SignalReady` waits for the first drain.
- **Prove health with a live sample only.** `OnSamplesRecorded` signals the update helper that the new version is healthy once `Session.Latest is not null && Session.Error is null`. A restored sample sets `Latest`, so if the first live capture fails, a restored sample could satisfy that check and confirm a broken update. The session needs a `HasLiveSample` flag, set only in `Drain`, and the readiness check uses it.

### Timestamps

The design depends on this. `ProcessRanking.Moment` and `MonitorHistory.Trim` use `MonotonicTimestamp` when it is non-zero and fall back to wall-clock time when it is zero. Restored samples with zero next to live samples with Stopwatch ticks would mix two unrelated time bases. Averages and retention would break silently.

`Stopwatch.GetTimestamp` is `QueryPerformanceCounter`, which is system-wide and keeps counting across process restarts on the same boot. So restored samples **keep their original monotonic timestamps unchanged**, and the restart simply appears as elapsed time between the last saved sample and the first new one.

That only holds within one boot. **The guarantee comes from the update handshake, not from a clock comparison.** `UpdateRuntime.AcceptResultArguments` sets `IsExpectedRestart` only after `EventWaitHandle.TryOpenExisting` opens the helper's `Local\WinMon.Update.<token>.Healthy` event. That named event exists only while the helper that created it is still running in this logon session, so a reboot or sign-out between the save and the read leaves no event, and the file is never read. A WinMon started after a reboot, by the Run key or by hand, also has no `--update-result` arguments.

The file also gets a sanity check, but it uses only monotonic clocks, never wall-clock time:

- `Stopwatch.Frequency` must match the header.
- Elapsed time since the save, measured by both `Stopwatch.GetTimestamp()` and `Environment.TickCount64`, must be between 0 and 5 minutes on each clock, and the two must agree within 2 seconds.

Wall-clock changes, such as a time sync, a DST change or a manual change between save and restore, cannot fail either check. A failed check falls back to today's behavior: the file is deleted and WinMon starts with empty history. It is traced, and nothing is shown to the user.

A wall-clock change does have one visible effect. Each sample's `Timestamp` (its displayed time) is restored as recorded, so restored and live labels can be offset by the correction. Ranking averages, retention trimming, gap detection and chart order all use `MonotonicTimestamp`, so they stay correct. Copy capture of a restored sample shows the time it was originally recorded.

The first live sample after the restart is already a gap (the CPU sampler has no baseline yet), so the chart breaks the line without a synthetic marker.

### Format

A versioned binary file made of **self-validating chunks**. JSON would be too large for 60 minutes of process entries and too slow to write during an exit.

| Part | Contents |
| --- | --- |
| Header | Magic `WMRH`, format version, writing app version, transaction token, `Stopwatch.Frequency`, and `Stopwatch.GetTimestamp()` and `Environment.TickCount64` at write time |
| Ledger chunk | The [Usage tab](USAGE-TAB.md) state: per-app totals with each app's `LastActive` sample key, machine busy ticks, Since, and the counted-lifetime set as `(ProcessId, CreationTicks, app key, absence count)`. Without that set, processes still running after the update would be counted as new lifetimes. Empty until that feature lands. |
| Sample chunks | Newest first, about 60 samples each. Each chunk has its own string table: every distinct process name, image path, app name, product, publisher, GPU engine and GPU ID in that chunk, written once and referred to by index. Then the samples in the chunk, oldest first: the `MonitorSample` fields, the `ResourceSample` fields with a presence bit per nullable value, and the compacted process entries. |
| Trailer | Fixed size at the end of the file: magic, chunk count, and a CRC32 of the header |

- **Chunk framing.** Every chunk is `length · CRC32 · payload`. The payload is compressed with `DeflateStream` at `CompressionLevel.Fastest`, and the CRC covers the compressed bytes, so a damaged chunk is rejected before it is decompressed.
- **Strings per chunk.** Repeating strings in each chunk costs a little size: a path appears once per 60 samples instead of once per file, and compression absorbs most of it. In exchange, a chunk can be written, checked and read on its own, which the write deadline and partial restore both depend on.
- **Safe writes.** Write to a temporary name, then move, the same as `PreferenceStore.SaveAsync`. The reader opens only the final name.
- **Rejected files.** A header or trailer that fails the magic, token, version, frequency or restart checks discards the whole file. A chunk that fails its CRC or doesn't parse ends the restore at that chunk.
- **Code location.** The reader and writer live in `Monitoring` (`RestartHistory`), which keeps the layer rule: no WPF, Controls or Preferences.

**Version compatibility.** An update always goes old to new, so a version must read the format its predecessor wrote. Each format version gets a checked-in fixture file and a test that the current reader loads it. A version that cannot read a fixture removes it deliberately, in the same commit that changes the format.

### Time budget

Writing must not make the update feel stuck. One **1.5 s** deadline covers the whole save: serializing, compressing, writing, flushing and the final move. It starts when the save starts.

**The write streams straight to the temporary file**, newest chunk first. Each chunk carries its own strings and is compressed on its own (see [Format](#format)), so there is no buffered body or global string table to write at the end. The writer works through these steps:

1. Stop starting new chunks once 1.2 s has passed. The remaining 0.3 s is the margin for the trailer, flush and move.
2. Write the trailer, then flush with `Flush(flushToDisk: true)`.
3. Check the deadline once more, then move the temporary file into place. If 1.5 s has already passed, delete the temporary file and continue the update without history, the same as today.

Writes, the flush and the move run on the thread pool with a cancellation token set to the deadline. `OnClosing` waits for the save with a `WaitAsync(deadline)`. If a single write blocks past the deadline, for example on a stalled disk, the update goes ahead and the temporary file is deleted when that write returns. If the process exits first, the file is never moved into place: stale-transaction cleanup removes the directory, and the reader only accepts the final name.

Stopping early drops the oldest history and keeps the newest, which matters most. A file with fewer chunks is complete and valid; it looks like a shorter retention.

### Restore

Restoring is **a bounded read, then an all-or-nothing commit**. `MonitorSession` is never touched until the read has finished and been validated.

1. `OnLoaded` starts the read on the thread pool (`RestartHistory.ReadAsync`) with its own **1.5 s** deadline, so a large file does not freeze the window. It runs before `Session.Run`.
2. The reader checks the header (magic, version, token, frequency and the restart checks in [Timestamps](#timestamps)). It then checks the fixed-size trailer, read by seeking to the end of the file: magic, chunk count, and a CRC of the header. A file that fails either check was not written by a finished save, and the whole file is discarded.
3. It reads chunks newest first, checking each chunk's length and CRC before decompressing it, and parses samples into a private list. It stops at the first chunk that fails its check, after the last chunk the trailer counts, or at the deadline.
4. **What is kept** is the contiguous newest run of valid chunks. A bad chunk, or the deadline, only costs the older history behind it, never a gap in the middle.
5. Back on the UI thread, the list is put in oldest-first order and passed to `MonitorSession.Record` in one loop, before sampling starts. Either every kept sample goes in, or none does.

The usage ledger is its own chunk with its own CRC. If it is invalid, the Usage tab starts fresh and the samples are still restored.

`WinMon.Performance` gets a scenario that writes and reads a full 60-minute history at the 500,000-entry budget and records size and time in the developer guide. The 1.5 s figure is a starting point to confirm there.

## Decisions

| Area | Decision |
| --- | --- |
| Scope | Update restarts only. Persisting across every exit was rejected: it raises the privacy and size questions on every close, and a reboot invalidates the monotonic timestamps anyway. |
| Location | The update transaction directory, owned by the token. No new long-lived file in `%LOCALAPPDATA%` or beside a portable exe. |
| Failure | Best effort. Preferences still gate the update; history never does. |
| Deadline | One 1.5 s deadline for the whole save, including flush and move. Past it, the temporary file is deleted and the update continues. |
| Restore | Bounded read into a private list, then one commit. Never a partial or interleaved restore into the session. |
| Integrity | Chunks validated on their own. The newest contiguous valid run is kept; the header and trailer gate the whole file. |
| Time base | Keep the original monotonic timestamps. The same boot is guaranteed by the update's named event, checked with monotonic clocks only. No rebasing, and no wall-clock boot estimate. |
| Rollback | Kept whenever the version that wrote the file is relaunched. Updates from 2.0.4 or earlier write no file. |
| Services | Not persisted. Service history is two minutes and memory-bound. |
| Triggers | Restored samples do not run [auto-capture](AUTO-CAPTURE.md) rules. Rules see only live drains. |

## Docs to change

- README, Updates section: replace "Restarting clears the current monitoring history" with "History recorded before the update is kept."
- README, Catch the spike: history "is cleared when WinMon exits", except across an in-app update.
- PRIVACY.md: during an in-app update, history (including process names and executable paths) is written to the update folder for the restart and deleted after it is read.

## Tests

- Round trip: every `MonitorSample`, `ResourceSample` and `ProcessUsage` field, including nulls, gaps and compacted counts.
- Rejected files: a wrong token, unknown version, different `Stopwatch.Frequency`, a bad header or trailer, a missing trailer, monotonic elapsed time over 5 minutes or negative, or the two monotonic clocks disagreeing are all discarded, and startup continues with empty history.
- Chunks: a bad CRC in chunk *k* restores chunks 1 to *k*−1 (the newest) and nothing older. An invalid ledger chunk still restores the samples.
- All or nothing: a read cancelled at its deadline commits the valid newest run in one go, and the session is untouched until then. A failure while parsing leaves the session empty.
- A wall-clock jump of ±1 hour between save and restore does not reject the file, and ranking and trimming results match an uninterrupted run.
- The file is deleted after both a successful and a failed read.
- Write deadline, using an injectable clock and a slow stream:
  - the save stops starting chunks at 1.2 s;
  - if the trailer, flush or move would end past 1.5 s, no final file exists and the temporary file is deleted;
  - `OnClosing` continues the update after the deadline even when a write is blocked.
- Readiness: with restored samples and a failing first live capture, the update helper is not told the new version is healthy.
- Restored and live samples mixed: `ProcessRanking` five-second averages, `MonitorHistory.Trim` age, and chart gaps behave as if WinMon had never exited.
- Retention: a 60-minute file restored into a 10-minute setting trims to 10 minutes.
- Budget: a save cut short keeps the newest samples and is a valid file.
- Rollback: a file written by version N is restored when the helper relaunches version N after a failed update.
- Fixture compatibility for each format version.
- Native smoke: run a real update through the local update test path, then check sample counts before and after.

## Sequence

1. `RestartHistory` reader and writer in `Monitoring`, with round-trip, rejection and fixture tests.
2. Write in the `OnClosing` update path, and read in `OnLoaded` on an expected restart.
3. Benchmark scenario and recorded measurements.
4. README and PRIVACY.md updates.
5. Fill the ledger chunk once the [Usage tab](USAGE-TAB.md) lands. Until then the writer emits it empty.

## Implementation notes

All five steps landed together, after the Usage ledger, so the ledger chunk was filled from the start.

- `Monitoring/RestartHistory.cs` holds the writer, reader and a table-driven CRC-32 (the runtime has no CRC-32 without a package). `RestartClock` injects the two monotonic clocks, so the deadline and restart checks are tested without waiting.
- `UpdateLauncher.StartAsync` now returns the transaction directory and token; `MainWindow` saves under them in `OnClosing` right after sampling stops, and `UpdateRuntime.RestartHistory` gives the new instance the path to read in `OnLoaded`.
- `MonitorSession.HasLiveSample` is set only by a drain, and the update readiness check uses it.
- `UsageLedger.Export` and `Restore` carry the totals and counted lifetimes; running counts restart at zero until the first valid sample.
- Tests: `RestartHistoryTests` covers the round trip (every settable property is set by reflection, so a field the format forgets fails), every rejection, a damaged sample chunk and ledger chunk, the 1.2 s cut-off and 1.5 s deadline with a stepping clock, a failing write, retention trimming and the checked-in format 1 fixture `tests/WinMon.Tests/Fixtures/restart-history-v1.bin`. `MonitorSessionTests` covers `HasLiveSample`. `Fixture_Write` regenerates the fixture and is explicit.
- Not yet done: the native smoke test through a real update, and an automated check that `OnClosing` stops waiting at the deadline when a write blocks. Both paths are covered only by reading the code and the unit tests of `RestartHistory.Save`.
- Measured on the development machine (see the developer guide): at the 500,000-entry budget, 1,961 samples save in about 0.94 s into a 15 MB file and read back in about 0.6 s, so the 1.2 s cut-off is not reached there. Slower disks would drop the oldest chunks first.
