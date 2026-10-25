# CSV export

Selected on 2026-10-25 from the third CSV concept. Status: implemented on 2026-10-25.

## Goal

Let a normal user save WinMon's existing observations for a spreadsheet or analysis tool. Keep export read-only and local, with no elevation, helper service, new sampler or persistent recording.

## Interaction

- **Export** sits beside Settings in the Full-view title bar, so it is available from Processes, Services and Usage. It is hidden in Compact view.
- The modal dialog defaults to process samples from Processes, Usage totals from Usage, and resource history from Services. Users can choose another dataset.
- Resource and process exports offer all retained history or the current chart range. Usage totals are cumulative since start or Reset, so the time controls are hidden for that choice.
- The dialog proposes a dated file name, then the Windows Save dialog chooses the actual location and confirms overwrite. Save writes a sibling temporary file and replaces the destination only after completion. The UI remains responsive while it writes; exit waits for a write in progress.

## Data contract

- Resource history has one row per retained sample, including gaps. A blank field is unavailable, never zero.
- Process samples have one row per retained process observation. Idle observations removed by history compaction are absent; the `sample_compacted_processes` column records their count. Process I/O is total I/O, not drive or adapter specific.
- Usage totals have one row per app, including exited apps, plus a Not attributed row. They are not scoped to chart time.
- Headers are stable English identifiers. Timestamps are UTC ISO 8601; numbers use invariant culture and raw units. The CSV is UTF-8 with BOM for spreadsheet compatibility. Text fields that could execute as spreadsheet formulas are prefixed with an apostrophe.
- Exported files may expose process names and paths. No export data is sent over the network.

## What landed

`MainWindow.Export.cs` opens the dialog and passes immutable snapshots to `Monitoring/CsvExport.cs`, which writes the CSV on a worker thread. `Controls/CsvExportDialog.xaml` holds the layout. `CsvExportTests` cover range selection, raw/invariant values, quoting and formula safety, Usage totals and atomic failure behavior. Feature smoke captures the dialog in Dark, Light and Slate themes.

The concept showed “Current session” as a time choice; the implementation says “All retained history,” because the in-memory history can be shorter than the whole session. The concept's time choice is hidden for Usage totals. The button label is **Export**, as requested.

Save CSV and Cancel use the standard button appearance and vector icon treatment. This replaces the earlier accent and Slate-specific Save CSV fills, which could resemble a disabled button.

Export was later hidden in Compact view to keep its title bar focused on the live gauges. Full view restores the button.
