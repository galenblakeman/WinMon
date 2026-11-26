# History Events sidebar

Events navigate an investigation; Compare periods and Selected moment need width for process columns. Put Events on the right below the full-width timeline, with a draggable divider and a visible collapse button.

Start collapsed in a 44-DIP rail showing the Events label, the number of nearby events (excluding the empty-state row), and the expand button. Collapse hides the existing controls so selection and scrolling survive. The process area uses the freed width. Keep crash groups and log coverage inside the sidebar. At minimum window height, coverage can shorten with an ellipsis and its full tooltip so the event list remains inside the available detail area.

The disclosure button supports keyboard activation and UI Automation ExpandCollapse. If collapse hides keyboard focus, return it to the button. The divider supports pointer dragging and arrow-key resizing. Store collapsed state and a bounded pane-width ratio through MainWindow's existing preferences, preserving them across dialog and app restarts.

## Verification

Capture smoke renders expanded and collapsed layouts in Dark and Light at 1000 and 620 DIPs, checks the sidebar's position, focus recovery, selection retention, reclaimed width, reopening preferences and UI Automation expansion. Explicit sidebar content renders use a visual brush so the root margin and other desktop windows cannot clip the evidence; native captions are outside those content captures. The focused performance workload is `WinMon.Performance <result.json> --history-layout-only`; it measures synthetic History selection, layout and content rendering.

## Landed

Implemented in FlightRecorderWindow.xaml and its code-behind, with MainWindow preference wiring and translated control names. The timeline and trend views retain their full width. The sidebar moved to the right after review; collapse and resize behavior is unchanged.
