# Expanded gauge details

A horizontal divider under the meter strip exposes three extra rows per meter. Drag it down to expand or up to collapse. The control follows the drag and snaps to the nearest resting position on release. When focused, it also accepts Up and Down. The detail columns align with the gauge columns, including when the secondary disk is unconfigured.

The gauge divider shares its grip, line, and focus styling with the chart and process divider.

| Meter | Rows in display order |
| --- | --- |
| CPU | Processes, Threads, Kernel. With hardware sensor monitoring enabled, Fan and Power replace Processes and Threads. |
| Memory | Commit as a percentage of the commit limit, Cached, Paging reads per second. |
| Disk | Latency, Queue length, Free space for each configured volume. |
| Network | Session bytes received, Link speed, Adapter. |
| GPU | VRAM, Shared memory, Engine. With hardware sensor monitoring enabled, Fan appears first and replaces Engine. |

The rows use the current live resource sample and the existing CPU package power cache. The GPU fan comes from the adapter paired to the gauge. Missing readings display the same placeholder as the gauges. Long values trim to fit and show their full text on hover.

## What landed

The Full and Compact views use the same control and share the divider position until the window closes. The window starts with details collapsed. Compact grows with the drag. The application uses its existing hardware sensor preference to choose the CPU and GPU rows. The mockup's sensor preview checkbox is omitted.
