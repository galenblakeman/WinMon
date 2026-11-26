# Scoped rules and disk response time

Keep the existing automatic capture settings array and allow up to eight distinct targets for each application or service metric. Preserve configured targets, thresholds and durations when loading old preferences. Each rule tracks its own streak, hysteresis and cooldown using the existing shared duration setting. The existing Any process rule still throttles its notifications across process lifetimes. Target rows can be added and removed in Settings. Duplicate target text for the same metric is rejected case-insensitively.

Trend the Windows response-time measurement for each configured volume. Convert sampled seconds per transfer to milliseconds for chart, thresholds and reports. Keep observed-time-weighted minute averages and sampled peaks. Apply the existing volume identity boundaries. Each volume has an optional sustained latency rule, initially disabled, with editable illustrative defaults of 25 ms for 10 seconds. Missing samples, time gaps and volume changes break a streak. Switching volumes starts the new volume with fresh cooldown state.

## What landed

The existing rule table adds a scoped metric picker with Add and Remove controls. The settings model deduplicates stored targets and preserves existing rule values. Longer trends, saved incidents and the History comparison summary surface disk response times. The detailed graph still shows the resource timeline for context, and the report and History state that process I/O covers all devices. The feature uses the current Windows collector and leaves readings unavailable when Windows supplies no response value.
