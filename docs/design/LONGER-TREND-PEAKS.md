# Peaks in Longer trends

Retain the maximum reading and its UTC observation time alongside each minute's weighted average. A reading for an interval that straddles a minute boundary contributes to both buckets and carries its actual observation time in each. Preserve gaps and device changes. At zoomed-out scales, draw a peak mark for the highest observed peak in each pixel column while keeping the weighted average line and its continuity rules.

Selecting a minute exposes the peak value, time and observed coverage. History jumps to the matching source observation while it is retained. After expiry, look in the configured automatic capture folder on demand. Verify the saved recording contains the same observation time and metric value before presenting its contributors. Search the report filename time first and validate candidate `.wmincident` files through the existing bounded loader. A minute with no matching incident keeps its resource measurement and shows an explicit status when the search completes.

## What landed

Trend archive schema 3 adds peak times and keeps versions 1 and 2 readable. Minutes recorded under those versions retain their original chart meaning. A partially restored legacy minute does not claim a known peak when new samples arrive. Thermal plots retain their existing minute-maximum line. The network detail action follows the higher recorded transfer rate of the two directions. The trend archive remains free of process inventories. For expired detailed readings, contributors depend on an automatic incident covering the peak.
