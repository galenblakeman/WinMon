# Connections toolbar

Keep Connections consistent with Processes: ranking at the leading edge, a flexible search field, then Individual/Grouped and App only. Put those controls on one row. Keep TCP/UDP totals and receive/send rates on one line beside the activity tabs.

Remove the session-local protocol filter to leave space for search at the minimum window width. TCP/UDP counts, ranking and endpoint protocol details remain available; both protocols participate in the list. No stored preferences change.

## Result

Implemented in `ConnectionsView.xaml` and `MainWindow.Connections.cs`. Removed the protocol selector and its filtering code; retained search, Top, ranking and grouping behavior. Smoke coverage uses the ranking menu for historical Escape handling and checks toolbar bounds in English and German. No departure from the layout described above.

Validation: `tools/Verify.ps1 -Smoke connections` passed the app and benchmark builds, all five test suites (1,213 passed), and the Connections smoke. Its fifteen renders were inspected, including light/dark, individual/grouped, traffic enabled/disabled and minimum-width English/German layouts. The tests required execution outside the sandbox after the sandboxed test host stalled.
