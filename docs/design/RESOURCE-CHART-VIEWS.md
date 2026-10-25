# Resource chart views

The Full view resource chart has one dropdown in its header: Overlay, Small multiples, Heatmap, then Focus choices for each configured resource. Overlay preserves the existing chart. The other three follow the concepts selected in chat on 2026-10-24. The chart type and focused resource remain saved in the same window preferences. Compact tiles and process sparklines keep their existing presentation.

The views share the same samples, time range, series visibility, gap handling, selected sample, Capture panel and automatic capture markers. Small multiples gives each enabled resource its own aligned lane and scale cue. Heatmap uses resource-colored intensity over time; it does not stack unrelated percentages or rates. Focus gives one enabled resource a large chart and places the other enabled resources in narrow context lanes. The network resource always uses the chart's rounded throughput scale.

The implementation uses the existing `CpuGraph` drawing and input path so every layout retains the same click, keyboard and marker behavior. Preferences validate unknown chart types and clamp the focus index. All chart-choice labels and accessibility names are localized.

`--chart-smoke-output <directory>` exercises the dropdown and capture inspection with native samples. `--snapshot-smoke-output` renders full synthetic histories for each chart type, a selected capture, and a second configured disk.

Landed in `CpuGraph`, `MainWindow`, `MonitorPreferences` and `PreferenceStore`. The production heatmap uses one flat intensity strip per resource instead of the mockup's multi-row blocks, because each recorded interval has one scalar reading per resource. The combined dropdown keeps the chart type and resource visible in its selected label. Compact lane labels omit numeric scale cues when space is tight; the large focus chart retains its axis.
