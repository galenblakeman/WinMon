# Remember preferences per chart type

Each chart type remembers its own checked resources, time range and resource-control visibility. Focus has one configuration shared by its resource choices and remembers the last focused resource. The selected chart type also returns after restart. Controls default to collapsed until opened for that type. Logical processors always enables CPU and has no resource controls.

Switching chart types snapshots the outgoing configuration and restores the incoming one without changing sampling or history. The existing preference save lifecycle persists the configuration on Settings save, normal close, update restart and Windows session end; this does not add crash recovery or a new background writer. A range longer than the current retention falls back to two minutes without discarding the saved longer choice. An unconfigured second disk retains its checked state independently in every chart.

Settings → Monitoring → Chart preferences explains the behavior and offers Reset chart preferences. Reset is deferred until Save; Cancel discards it. Reset restores Overlay, all resource selections, two minutes, CPU focus and collapsed controls for every chart. Ordinary Settings changes preserve chart choices made while saving.

## Storage and migration

`MonitorPreferences.ChartConfigurations` contains one `ResourceChartPreferences` per chart type. Existing top-level chart fields remain the active chart's values for compatibility with existing rendering and preferences. `PreferenceStore.Validate` normalizes entries and snapshots those active values before writing. Switching charts snapshots the active fields before restoring the target entry. Missing entries inherit the existing shared resource selections and range, with controls collapsed. Unknown types and duplicate entries are discarded; masks, ranges and focus indices are bounded and null arrays/entries are tolerated.

## Requirements and verification

| Requirement | Status |
| --- | --- |
| Separate selections, range and control visibility per type | Implemented |
| One Focus configuration and restored last chart | Implemented |
| Legacy migration and malformed-entry normalization | Implemented |
| Preserve temporarily unconfigured disk selections | Implemented |
| Localized Settings explanation and reset on Save | Implemented |
| Regression coverage for migration, restart, switching, reset and edge cases | Added; execution stopped at user request |
| Standard and Store compilation | Passed for WinMon.Tests and referenced app projects; zero warnings/errors |
| Live Settings, restart and translated-layout validation | Not run; tests and smokes remain stopped at user request |

## Landed result and departures

Implemented in the preferences model/store, MainWindow chart controls and SettingsWindow, with strings in all ten resource files. The existing save lifecycle is reused; no dependency, polling or extra disk writes per sample were added. The Settings reset returns to Overlay as part of restoring chart defaults. Automated tests are present but have not been run at the user's request.
