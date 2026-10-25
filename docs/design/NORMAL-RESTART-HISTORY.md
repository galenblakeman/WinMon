# Keep history across normal restarts

## Behavior

An opt-in Monitoring setting saves the retained compacted resource and process samples on an ordinary close or Windows sign-out. On launch, WinMon reads the file before sampling, keeps only samples within the selected 10, 30, or 60 minute retention, and consumes the file. A reboot within that period is supported. Usage totals and Service history start fresh. An update restart continues to use its existing transaction file and takes precedence. Crashes and forced termination do not trigger a save.

## Storage and clocks

`history.bin` lives beside `settings.json`, including for portable copies. The update restart codec provides versioned, compressed, CRC-checked newest-first chunks, a trailer, atomic replacement, and a 1.5 second save budget. The normal file has its own token and a 64 MiB limit. A failed save leaves the previous complete snapshot. When the setting is off, WinMon deletes the normal file.

Update history can keep its saved `Stopwatch` ticks because the updater proves a same-boot restart. If the saving process had shifted its live samples (below), its newest sample is ahead of the clock reading in the file header; the new process adopts that lead as its own offset so live samples still follow the restored ones. Normal history uses the samples' recorded wall time to establish the newest sample's age and the saved monotonic intervals to preserve spacing. It rebases the samples onto the current process clock. When uptime is shorter than the retention window, the session shifts subsequent live samples by the same positive offset. The first live reading after restart is already a gap. Files with future or expired newest samples are discarded. A material wall-clock change across a reboot can cause history to be discarded or positioned inaccurately; there is no trusted persistent clock spanning boots.

## Verification

Round-trip tests cover the existing codec. Normal-restart tests cover rebasing after a clock reset, retention trimming, future and expired files, deletion after reading, and alignment of live, full, and Service samples. Verify both app variants and inspect the Monitoring Settings smoke render and a close/reopen chart render. Measure exit-save and startup-read time at the process-entry budget before release.

## What landed

The normal file reuses the update codec, but does not carry its Usage ledger. The snapshot is written on graceful exit and sign-out and consumed on startup. Periodic crash-recovery checkpoints were left out of this feature.

A later bug hunt found that an update restart after a shifted session dropped the offset, so new live samples were drawn before the restored history; the update read now recovers it. Unfinished temporary files left when a session-end save outlived its wait are removed on the next start.
