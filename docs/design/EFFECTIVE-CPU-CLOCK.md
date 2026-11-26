# Effective CPU clock

The clock under the CPU gauge ("4.30 GHz") came from `CurrentMhz` in
`CallNtPowerInformation(ProcessorInformation)`, averaged across logical processors. While designing
[temperature](TEMPERATURE.md) readings, a probe showed that on a Ryzen 9 9950X3D desktop that value stays at
4,300 MHz, which is `MaxMhz`, the rated clock, through idle and all-core load. Its Hyper-V guest showed the same. Meanwhile
`\Processor Information(_Total)\% Processor Performance` read 119–128%, an effective 5,121–5,516 MHz. The
gauge never moved.

## Design

- Clock = highest `MaxMhz` across logical processors × `% Processor Performance` / 100. `MaxMhz` comes from
  the same `CallNtPowerInformation` call as before; `CurrentMhz` is no longer read. The highest rated clock is
  the base speed Windows reports, so the result matches Task Manager's Speed, including on hybrid processors
  whose core types have different rated clocks.
- `% Processor Performance` is a rate counter, so the first collection has no value. The clock is null then
  and after any gap, query reopen or failed collection. It is never replaced by the rated clock, which keeps
  the existing rule that a failure must never substitute the rated maximum.
- Non-positive, NaN and infinite readings are unavailable.
- The counter goes into the existing memory-pressure PDH query (`MemoryPressureCounter`), which already
  handles rate baselines, the five-second interval limit, gaps and retry after failure. The sampler opens no
  new query. The value is read with `PDH_FMT_NOCAP100`, so readings above 100% are not clipped.
- Nothing downstream caps the clock. `GaugeReadoutText.CpuClock`, CPU details, the CLI, CSV export, restart
  history and incident files store or format the double as is. The logical-processor chart does not show
  clocks, and MCP does not expose it. Info's maximum clock stays the reported specification.

## Rejected

- **Processor 0 only.** Reading `\Processor Information(0,0)` costs about 0.01 ms instead of 0.3 ms or more.
  But it measures one core, which can be parked or boosting on its own, so it is not the machine clock.
- **Undocumented kernel performance-distribution classes.** These could be cheaper than PDH, but they are
  undocumented and could change between Windows builds.

## What landed

`WindowsCpuClock.ReadMhz(double?)` and `EffectiveMhz` in `src/MonCore.Windows/Monitoring/WindowsCpuClock.cs`;
the counter and its `ProcessorPerformancePercent` reading in `MemoryPressureCounter.cs`; the sampler reads
memory pressure first and passes the reading to the clock. Unit tests cover the calculation, values above
the rated clock, hybrid rated clocks, first-sample, reset, failure and invalid readings. An integration test
checks that the real sampler reports null first and then a positive value.

Departure from the design: the `_Total` instance costs more than expected. The Processor Information provider
aggregates every logical processor for it. That added about 0.5 ms to each one-second resource sample
(1.39 → 1.88 ms median) on the sampling worker, off the UI thread, with no extra allocation. See the
[performance history](../PERFORMANCE-HISTORY.md#effective-cpu-clock-november-25-2026). The unused
`CpuClockDescription` string still describes the old reading; no UI shows it.

Correction after a hybrid laptop run: the first version averaged `MaxMhz`. On a Core Ultra 9 275HX (8 performance cores
at 2,700 MHz, 16 efficient cores at 2,100 MHz) that average is 2,300 MHz, while Task Manager's base speed is 2.70 GHz,
so WinMon read about 15% below Task Manager's Speed. The clock now uses the highest `MaxMhz`. Processors whose cores
share one rated clock, such as the Ryzen 9 9950X3D, give the same result as before. See the
[temperature spike results](TEMPERATURE.md#spike-results).
