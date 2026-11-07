# Collapsible chart section

The chart needs the same space-saving disclosure as the activity panel. Place a chevron at the right of the existing chart header and keep that header visible when the plot and snapshot are hidden. Each panel can collapse independently, including both at once.

Reuse `ActivityPanelButton` for keyboard operation and UI Automation expand/collapse state. Move focus to the chart chevron when hiding a focused chart or divider. Save `ChartPanelCollapsed` beside the activity state and preserve `ActivityPanelRatio` whenever either panel is hidden. Restore the previous split when both panels are expanded. Keep monitoring and history collection running.

## Landed

Implemented in `MainWindow.xaml`, `MainWindow.ActivityPanel.cs` and `MonitorPreferences`. Labels are localized in all supported languages. Preference tests and the feature smoke cover persistence, focus, reclaimed space, independent states, Compact view and split restoration. No departures from the design.
