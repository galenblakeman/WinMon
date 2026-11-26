# Temperature history and captures

November 26, 2026. Extends [built-in temperatures](TEMPERATURE.md), [hardware sensors](HARDWARE-SENSORS.md) and [sensor settings](SENSORS-SETTINGS.md) in WinMon 5.0.0 on .NET 10.

## Behavior

Temperature chart choices are shown only while the optional sensor service is enabled. This applies to the full chart, Detailed history and Longer trends. Disabling the service falls back to the standard chart/resource selection; stored temperature observations remain intact and readable in the detailed summary. Store does not expose these service-dependent chart choices.

The full chart's Temperature choice plots the selected CPU, GPU and configured drive temperature series on a dedicated axis that fits the visible time window. The bounds include 3 °C of padding before rounding outward to multiples of 10 °C, with a floor of 0 °C. The axis expands immediately and contracts by up to 10 °C per edge every five seconds of sample time. Changing the time range or sensor selection fits the new view immediately. The same Celsius observations determine geometry in either unit. Resource colors, line patterns, time range, keyboard selection and capture markers use the existing chart infrastructure. Memory and Network controls are hidden in this view. A missing reading, sampling gap or sensor identity change breaks a line; no ACPI zone is relabeled as CPU die temperature. CPU package selection preserves the service's generation and actual sensor identity. GPU temperature follows the physical adapter used for utilization; disk temperatures follow the configured volumes.

History adds CPU/GPU/primary/secondary drive temperature and CPU performance-limited/GPU thermal-limit choices. A temperature selection uses the temperature chart and permits navigation to its peak. Comparison and contributor views never claim that a process owns a machine temperature or limit reading. The selected reading appears first in the detailed summary. Historical observations are observations at their recorded time, never evidence of a current service reading.

## Storage and compatibility

Restart-history format 5 appends one bounded thermal payload to each resource sample. It stores CPU reading identity, generation, source and Celsius; CPU performance-limit percentage and raw flags; all reported thermal zones and GPU thermal fields; and primary/secondary disk Celsius. Its reader continues to accept formats 1–4, which restore these fields as unavailable. Board fans remain omitted from restart history.

Incident format 3 includes CPU temperature in the existing resource JSON, alongside GPU, zone, disk and limit observations. Formats 1–2 remain readable. There are no restored acquisition clocks, leases or service connections. Temperature values, identities, collection counts and payload length are validated before historical data is used.

Trend schema 2 reads schema 1 and adds four temperature metrics and two limit-state metrics. Temperature values retain each minute's maximum rather than its weighted mean; the bucket also retains its observed minimum and coverage. Limit state is 1 when active and 0 when clear, and its minute peak records whether the limit was seen during the observed part of that minute. Missing observations add no coverage. CPU sensor/generation, physical GPU or volume changes mark mixed minutes unavailable. Existing utilization and throughput metrics retain their prior weighted-average semantics. Only live samples feed this archive; restored detailed history does not backfill it.

## Automatic captures

Six disabled-by-default rules are added:

- CPU and GPU temperatures at or above 85 °C for 30 valid samples.
- Primary and secondary drive temperatures at or above 60 °C for 30 valid samples.
- Reported CPU performance limitation or GPU thermal limitation for 10 valid samples.

Temperature thresholds are stored and edited in Celsius, explicitly labeled in Settings, independently of display units. These defaults are configurable capture conditions, not declarations of safe hardware temperatures. Limit rules have no editable threshold: CPU is active only with a reported performance limit below 100%; GPU is active only when its paired temperature reaches the driver's warning threshold. Missing thresholds remain unavailable. CPU performance limitation is not labeled as thermal throttling.

Temperature rules include equality at the threshold; existing non-sensor rules retain their strict comparison. Missing readings, explicit gaps, elapsed gaps and sensor changes reset the run. Existing cooldown and recovery hysteresis apply. Capture reports and filenames identify the sensor metric, and no per-process temperature attribution is produced.

## What landed and departures

Implemented: the dedicated Temperature chart and localized help, History choices, restart/incident storage with older readers, minute temperature/limit peaks, six capture rules, Settings/report integration and regression coverage.

Departure: the temperature series use a dedicated chart choice rather than overlaying a second scale on utilization. This keeps the existing percentage and throughput layouts intact and gives temperatures an unambiguous axis. Fans, vendor GPU hotspot/memory-junction sensors, CLI/MCP sensor tools and separate notifications beyond automatic captures remain outside this change. Physical sensor accuracy, HVCI/coexistence and clean-machine driver/setup acceptance remain separate work in the owning sensor designs.

Verification progress and stopped runs are recorded in `artifacts/thermal-history/verification.md`. The user stopped test execution before final desktop/Store acceptance completed. The subsequent service-visibility guard and its new regression cases have not been run.
