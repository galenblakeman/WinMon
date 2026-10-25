# Activity counts

Selected on 2026-10-25: use the space the **Hide actions** toggle left at the right end of the activity tab strip for the machine's process, thread and handle counts.

## Behavior

- The strip reads `Processes 312 · Threads 4,812 · Handles 152K`, with muted labels and values in the text colour.
- The counts come from the system-wide `ProcessCount`, `ThreadCount` and `HandleCount` in `ResourceSample`, which the CPU detail tooltip already shows. They are not sums of the table's rows, which leave out compacted and inaccessible processes.
- They follow the sample the table shows: live, or the captured moment in Capture mode. The top readouts stay live, as before.
- Values below 100,000 are shown in full. Larger values are shortened to thousands (`152K`) or, from 999,500, to millions with one decimal (`1.2M`). The tooltip and accessible name give exact values.
- An unavailable reading shows a dash.

## Landed

`MainWindow.xaml` adds the `SystemCounts` text, which binds its labels to the existing `ProcessesHeading`, `Threads` and `Handles` strings, so no translations changed. `MainWindow.Processes.cs` updates the three value runs only when a count changes, and `MainWindow.Details.cs` formats them (`CompactCount`, `ExactCount`). `MetricDetailsTests` covers the formatting thresholds, and the feature smoke checks that the strip shows live values and renders it in the action drawer captures.

## Departures

- The `K` and `M` suffixes are not translated, matching the network gauge's scale label.
