# Connections header pin count

Match the Processes presentation: show the localized saved-pin count after Process / PID, separated by a muted dot and using the existing pin accent. Hide the separator/count together when there are no saved pins. Preserve the count across filtering and disconnected pins.

Remove the network-status and UDP explanatory footer lines from populated lists. Preserve collection diagnostics in the column-header tooltip and existing per-row traffic tooltips. Keep empty/loading/error messages when no rows are available, and keep the enable-monitoring link.

When visible pins precede ranked results, show the existing localized `Ranked by {0}` separator above the first unpinned row, using the selected effective ranking. Match the Processes top rule, padding and muted text. Hide it for Pinned only, filtered-out pins, no ranked results and no pins. Keep the separator with the first ranked item in the existing virtualized list, and expose it as that item's accessibility help text.

## Result

Implemented in `ConnectionsView.xaml` and its update code without new strings or settings. The existing Connections smoke checks first-column pin bounds at normal and narrow German widths and verifies that unavailable network status uses the header tooltip rather than a footer row. Existing pin and disabled-monitoring tests cover localized count updates and collapsed status rows. No departure from the requested layout; empty-list feedback remains available.

The ranking separator uses the existing row grid rather than another row wrapper, preserving virtualization, selection and pin-button focus. A regression test covers ranking changes, Pinned only, filtered pins and removing the last pin. Final `tools/Verify.ps1 -Smoke connections` passed all 1,234 tests and 16 renders; English/German, pinned/ranked, Pinned only and network unavailable renders were inspected. Output: `artifacts/verify/20261108-093316/connections/`. One unrelated diagnostic-log test failed once in the preceding run; its isolated rerun and final full verification passed.
