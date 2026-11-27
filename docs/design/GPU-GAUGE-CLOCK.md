# GPU gauge clock and peak

The GPU gauge keeps busiest-engine utilization as its live value. Its corner and tooltip show utilization at the peak within the configured gauge window. The tooltip includes the sample time. Dedicated and shared memory, power and fan speed are available in GPU details when reported.

The subline shows available clock and temperature values, for example `1,860 MHz · 37 °C` in US English. Frequency comes from Windows node performance data for the 3D engine on the same physical adapter as the utilization reading. If multiple 3D engines are reported for that adapter, WinMon uses the busiest one. Temperature respects the configured unit. One available reading can appear by itself. The Needle style exposes clock speed through GPU details. [Microsoft’s node performance data reference](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_node_perfdata)

## Delivered

Full and Compact views share the readout. The Windows collector reuses the adapter handle from temperature sampling and issues at most one additional clock query for a valid gauge interval. Drivers that omit the 3D node frequency yield an unavailable clock. Restart history and incident recordings preserve the sampled clock as an optional field. Older recordings open with the clock unavailable.
