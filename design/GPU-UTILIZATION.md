# Basic GPU utilization

Feature design approved October 19, 2026. The superseded GPU mockup has been removed.

The existing .NET 10 WPF view now adds a cyan GPU readout, visibility toggle, and
0–100% chart trace. Five readouts fit the existing 600-pixel minimum width using
27-pixel values. CPU process ranking and network throughput semantics are retained.

The single GPU percentage represents the busiest physical engine across available
adapters. PDH GPU Engine instances provide process contributions, which are summed
by adapter LUID, physical adapter index, and engine number before selecting the
busiest engine. Engine types alone are not unique identities. Invalid contributions
invalidate their engine; unavailable data is not converted to zero. The readout
tooltip identifies the adapter and engine. Adapter changes break the chart trace.
The initial basic view omitted adapter selection, GPU memory, sensors, and GPU process ranking.

GPU uses its own query and failure handling. Rate formatting waits for the second
collection, and pause, reset and collection gaps establish a new interval. Handles
and native buffers are released. Collection continues when the series is hidden.
`ShowGpu` defaults to true independently of the legacy four-series preference mask,
preserving existing visibility choices and persisting an explicitly hidden GPU.

Validation:

- Release build completed with zero warnings or errors; 93 NUnit tests passed.
- Native smoke exited 0: `artifacts/gpu-utilization-verified/smoke.json` records
  GPU utilization 1.2524%, adapter `0x00000000_0x000076eb/0`, engine `3D`.
- Native checks cover toggles, historical inspection, pause/resume, hidden/minimized
  recording, expansion and light/dark rendering. PNG renders at 100%, 150% and 200%
  are bitmap layout evidence, not actual mixed-monitor DPI transition tests.
- Illustrative design renders include GPU data separately from native evidence.
- The earlier `artifacts/gpu-utilization` run exposed a first-sample formatting
  reset loop; the verified run follows its correction.
- No cross-vendor/multiple-GPU hardware matrix, synchronized Task Manager comparison,
  or sustained overhead benchmark was performed.

API references: [PDH formatted counter arrays](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw),
[English counter paths](https://learn.microsoft.com/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw),
[Microsoft GPU utilization semantics](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/).

## Process GPU column and ranking

The follow-up adds a GPU % column beside CPU %, and a remembered CPU/GPU ranking
selector. CPU remains the default. Both columns use the five-second history window;
GPU averages include only readable intervals, including valid zero readings. GPU
ranking includes processes with GPU activity even when CPU is zero. Sparklines
remain CPU history, and CPU coverage calculations remain independent of GPU sorting.
The Other processes GPU cell is a dash because those percentages are not additive.

The same PDH collection supplies per-process readings, selecting each PID's busiest
physical engine. Attribution requires a process creation time predating the GPU
baseline and an independent query-only lifetime check after collection. Missing
counters, inaccessible/exited processes and reused PIDs do not receive fabricated
zero readings. Retained samples carry GPU percentages and engine identity for
historical inspection. Collection still uses the existing CPU-accessible process set.

Validation: 98 Release tests passed; the native smoke in `artifacts/gpu-processes`
exited 0 and recorded real process GPU percentages and idle zero readings. It also
checks live/historical GPU ranking with an explicitly illustrative CPU-idle GPU
worker. Final published-executable renders and report are under
`artifacts/gpu-processes-final`. The new six-column table retains the 600-pixel
minimum width, and long process names are clipped within their own column.
Hardware-matrix and synchronized Task Manager comparison limitations still apply.
