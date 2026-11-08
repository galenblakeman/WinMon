# Services filter persistence

Remember the Services ranking column, status filter and Workers checkbox through the existing preference store and exit-save path. Keep defaults for older profiles: CPU sorting, all statuses, workers enabled. Restore Top before sorting because Name is only available with All. Invalid sort/status values fall back to CPU/all; choosing a Top limit while sorted by Name saves the resulting CPU selection.

Landed in MonitorPreferences, PreferenceStore, ServicesView and MainWindow. View tests cover restoration without change events, user changes and the Name-to-Top fallback; store tests cover round trips, legacy defaults and invalid choices. No departure from the scope: search, expanded rows and the selected activity tab remain session state. The sampling and per-tick row paths are unchanged, so no performance benchmark is required.
