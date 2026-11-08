# Remembered activity tab

Persist the selected activity tab through MonitorPreferences and the existing exit-save path. Keep stable numeric identifiers for Processes, Services, Usage and Connections, with Processes as the fallback for missing or invalid settings. Restore after loading each view's preferences. Startup must preserve a collapsed activity panel; subsequent user tab changes retain their normal expand behavior. Compact startup keeps the selection for the next Full view.

Landed in MonitorPreferences, PreferenceStore and MainWindow. ActivityTabTests cover disk round trips for all tabs and invalid-value fallback. The feature smoke checks selection, disk persistence, restoration into a new window with the panel collapsed, and subsequent tab changes for all four tabs. No departure from the design. This only changes startup and selection handling; sampling and per-tick rendering are unchanged.
