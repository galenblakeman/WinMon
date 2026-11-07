# Full-width headers at short heights

The System header, activity tabs and activity toolbars extended past a 20-DIP inset grid using negative margins. When the rows' minimum heights exceeded a short window, WPF applied a layout clip to the inset parent, cutting away the header backgrounds and controls at both sides.

Keep the main layout full width and inset only gauges and chart content. Each activity view uses two 20-DIP gutter columns around its content column; its toolbar spans all three. Toolbar and action-bar wrapping uses the content column's width.

The feature smoke checks the System header, activity tabs and each selected activity toolbar against the full-view width and ancestor layout clips at 900, 720 and 600 DIPs high, and saves screenshots for each tab/height pair.

Landed in `MainWindow.xaml`, the three activity views and the activity-panel feature smoke. No departure from the design. Existing vertical minimums remain: a very short window can still hide lower content vertically.
