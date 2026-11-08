# Service pins

The selected concept moves Workers immediately after search, replaces decorative service gears with pin buttons, and presents pinned services above ranked services with a Pinned only filter.

Pins use case-insensitive SCM service names rather than process IDs or host-group keys. They survive stops, host replacement and application restarts. A shared group is promoted once when any member is pinned; the host button pins all current members or removes their pins when any is already pinned. Expanded service rows let users pin members separately. Process/worker rows retain their process icons and have no service pin action.

Pinned groups follow insertion order and do not consume the ranked Top limit. Search/status filtering applies before either section is displayed. Pinned only and the pin names are persisted through MonitorPreferences and PreferenceStore. A name absent from the selected frame remains saved without displaying stale readings or a synthetic service row. Historical navigation resolves the same names against that frame. No service-management permissions or start/stop behavior change.

The view continues to reuse ServiceDisplayRow objects, graph buffers, the virtualized list and incremental row moves. Existing pin geometry, theme resources and localized labels supply the visuals. Pin controls have localized action/name tooltips and accessible names, and remain available to keyboard users. Removing a focused row returns focus to Pinned only. The toolbar order is Search → Workers → Pinned only → Status in both wide and wrapped layouts. At constrained widths, a 180-DIP identity column preserves the pin and service name while fixed metric columns scroll horizontally.

Verification covers pin order, ranked limits, deduplication, filtering, shared-host membership, process icons, stop/restart/history identity and preference normalization/round trips. Services smoke includes pinned dark/light, Pinned only, narrow/wrapped and translated layouts; native enumeration runs outside the restricted sandbox. Standard and Store verification cover the persisted preferences. Performance evidence is recorded in PERFORMANCE-HISTORY.md.

Rows notify bindings only for cells that changed. Pin labels refresh on pin, identity and language changes, avoiding repeated pin-binding work on metric ticks. A regression test enforces this notification behavior.

The pinned count sits immediately after the Service / process column heading, matching the Processes header placement and avoiding a separate toolbar row.

Landed in ServicesView, MainWindow.Services, MonitorPreferences, PreferenceStore and all ten string resource sets. The result follows the concept; shared-host pin semantics and missing-name handling make explicit the cases the static mockup did not depict. Existing adaptive memory/I/O units and application styling are retained.
