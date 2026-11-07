# Resource chart views

The Full view resource chart has one dropdown in its header: Overlay, Small multiples, Heatmap, Logical processors, then Focus choices for each configured resource. Overlay preserves the existing chart. Small multiples, Heatmap and Focus follow the concepts selected in chat on 2026-10-24; [Logical processors](LOGICAL-PROCESSORS.md) was added on 2026-11-07. The chart type and focused resource remain saved in the same window preferences. Compact tiles and process sparklines keep their existing presentation.

The views share the same samples, time range, series visibility, gap handling, selected sample, Capture panel and automatic capture markers. Small multiples gives each enabled resource its own aligned lane and scale cue. Heatmap uses resource-colored intensity over time; it does not stack unrelated percentages or rates. Focus gives one enabled resource a large chart and places the other enabled resources in narrow context lanes. The network resource always uses the chart's rounded throughput scale.

The implementation uses the existing `CpuGraph` drawing and input path so every layout retains the same click, keyboard and marker behavior. Preferences validate unknown chart types and clamp the focus index. All chart-choice labels and accessibility names are localized.

`--chart-smoke-output <directory>` exercises the dropdown and capture inspection with native samples. `--snapshot-smoke-output` renders full synthetic histories for each chart type, a selected capture, and a second configured disk.

Landed in `CpuGraph`, `MainWindow`, `MonitorPreferences` and `PreferenceStore`. The production heatmap uses one flat intensity strip per resource instead of the mockup's multi-row blocks, because each recorded interval has one scalar reading per resource. The combined dropdown keeps the chart type and resource visible in its selected label. Compact lane labels omit numeric scale cues when space is tight; the large focus chart retains its axis.

The Focus disk choices take their drive letters from preferences when the menu is built, including the second volume. This keeps the choices distinct before either readout receives its first sample.

The non-overlay views now share a measured left label column. Axis ticks and lane labels end at the same inset from the plot, so the chart can use the space previously reserved by the fixed 105-pixel gutter while still fitting longer localized names.

On 2026-10-25, the chart header label was shortened from "System resources" to "System" in every language. The unavailable header and capture-inspection tooltip use the same short name with their status suffixes. No chart behavior or layout changed.

On 2026-11-07, the header was renamed to "Chart" to identify the visualization and its display controls more clearly. The label, unavailable status and capture-inspection tooltip use the same term in every language. The change landed in the existing localized resource values; chart behavior, layout and resource identifiers did not change.

On 2026-11-07, the Chart heading and chart dropdown gained a wrapped, localized guide explaining all five chart types. The heading tooltip retains its live, unavailable or capture-inspection status above the guide. This landed in MainWindow and all ten string resources, with a chart smoke assertion and tooltip render; no departures from the wording-only design.

The selected follow-up mockup separates the guide into five sections with semibold option names above muted descriptions, thin dividers and 10-DIP vertical spacing. Both tooltips reuse one XAML content resource with a separate instance per popup. Descriptions are individual localized resources, and headings reuse the chart selector labels. The live heading is Chart types; capture and unavailable status remain visible instead when applicable. Chart smoke checks all five sections and renders both tooltips in Dark and Light. This landed in MainWindow and the existing resource files; the implementation uses native theme colors and type sizes rather than the enlarged mockup scale.
