# Automatic incident saving

Keep the existing immediate text report and save a companion `.wmincident` in the same capture folder. Include up to 30 seconds before the rule fires and 15 seconds after. Preserve the firing observation at full process detail and select it when opening the file. Scoped rules open the corresponding resource chart, while the text report identifies the exact trigger. Use only retained samples and aligned app traffic. Keep gaps and omit endpoints and event log queries.

Reuse `IncidentRecording` for validation, file size limits and atomic publication. The existing report name and its unique header ID identify the companion for pruning. A History file picker starts in the configured capture folder when available. Opening a recorded temperature chart works even if live temperature chart options are disabled. Graceful shutdown saves pending captures with the follow-up collected so far and waits for their writes.

## What landed

The desktop writes the same format as manual History and uses the 200-report retention policy for companion incidents. The lead and follow-up are capped at 30 and 15 observations respectively, and the firing observation is always included. The background writer infers process starts and stops where adjacent retained inventories are complete. A forced exit before the follow-up is written can leave only the immediate text report.
