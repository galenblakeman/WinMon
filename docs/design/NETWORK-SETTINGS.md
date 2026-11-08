# Separate Network settings

## Goal

Reduce Settings scrolling and make network configuration visible directly in the sidebar.

## Design

- Place Network immediately after Monitoring in the existing Settings sidebar.
- Keep history retention, history across restarts, the gauge peak window and disk selection under Monitoring.
- Move adapter selection, process-network monitoring, service status, repair and removal to Network.
- Reuse the localized Network label and existing controls, with one shared Save/Cancel footer. Keep pending edits while navigating.
- Select Network before applying a process-network change so progress and failures remain visible even when Save was pressed on another page.
- Preserve the Store variant's existing unavailable-service behavior.

## Landed

Implemented in `SettingsWindow`, with section placement and save-failure navigation covered by `SettingsLayoutTests`. Capture smoke renders both pages in Dark and Light and rejects scrolling at full designed height. No preference schema or monitoring runtime changes were needed. The result follows the design without departures.

Verification on November 7, 2026: desktop and Store builds, all five test projects and capture smoke passed outside the sandbox. Desktop verification used an isolated build directory because another profiling run held the shared executable open. Both pages reported zero overflow in Dark and Light; the affected screenshots were inspected. Native service installation was not exercised.
