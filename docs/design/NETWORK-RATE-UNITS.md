# Network traffic units

Traffic rates use decimal KB/s, MB/s and GB/s across gauges, peaks, chart axes and hover text, snapshots, automatic capture reports and Connections. Adapter link speeds retain Mbps/Gbps in resource details and hardware inventory.

Keep sample values and graph scales in their existing internal units. `RateText` converts bit-based traffic samples to byte-rate text and provides a separate link-speed formatter. Automatic capture settings display MB/s but retain stored Mbps thresholds, converting on input and output so existing rules keep their behavior.

Connections totals and owner details explain that rates average five seconds, normally ending three seconds behind the current time to allow network events to arrive. The gauge uses the latest adapter sample. Include the explanation in accessible descriptions as well as tooltips, translated into every supported language.

## Result

Implemented in `RateText`, chart hover text, capture settings/reports and Connections details. Existing process rows already use decimal byte rates. No sampling, attribution, persisted threshold or graph-scale changes were needed. Compact peak labels retain the full byte-rate unit, for example `8MB/s`.
