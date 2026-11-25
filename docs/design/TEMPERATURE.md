# CPU and GPU temperature

Date: November 25, 2026. Status: proposal. Nothing is implemented. A short hardware spike must come before any UI work; see [Delivery and acceptance](#delivery-and-acceptance).

## Purpose and scope

Answer one question: **is heat slowing this machine down?** Today WinMon shows CPU clock and GPU utilization but no temperature. [DEVELOPMENT.md](../DEVELOPMENT.md), the [user guide](../USER-GUIDE.md), [Resource detail tooltips](HOVER-DETAILS.md) and [Hardware inventory in the CLI and MCP](HARDWARE-CLI-MCP.md) all list temperature as out of scope, because the GPU path avoids vendor libraries and Windows offers no unprivileged CPU die temperature.

This design adds temperature and throttling readings that need **no elevation, no driver and no new dependency**. They also work in the Store build. The display follows the rule that a missing reading is null, never zero: machines without a sensor look exactly as they do today.

Outside this design:

- True CPU package or per-core temperature. It needs a kernel driver (LibreHardwareMonitor with PawnIO; WinRing0 is on Microsoft's vulnerable-driver blocklist). See [Deferred](#deferred).
- Vendor SDKs (NVML, AMD ADLX, Intel IGCL) for GPU hotspot and memory-junction temperatures.
- Fan control, overclocking, voltages, motherboard or drive sensors, and any action that changes hardware.
- Danger colors based on a guessed maximum safe temperature (TjMax). GPU warnings use only the driver-reported threshold below; the CPU gets none.

## Repository baseline

At design time the repository is at product version 4.2.0 on .NET 10 Windows. Recheck before implementation.

- [ResourceSample](../../src/MonCore/Monitoring/ResourceSample.cs) holds nullable system readings. Null means unavailable, never zero.
- [SystemMetricCapabilities](../../src/MonCore/Monitoring/CollectorCapabilities.cs) has flags for Cpu, Memory, Disk, Network, Gpu and Services.
- [WindowsCpuClock](../../src/MonCore.Windows/Monitoring/WindowsCpuClock.cs) reads `CallNtPowerInformation(ProcessorInformation)` each tick for `CpuClockMhz`. The same structure carries `MhzLimit`, which WinMon does not read yet.
- [WindowsGpuSampler](../../src/MonCore.Windows/Monitoring/WindowsGpuSampler.cs) reports the busiest engine from `\GPU Engine(*)\Utilization Percentage`. [WindowsGpuMemory](../../src/MonCore.Windows/Monitoring/WindowsGpuMemory.cs) pairs PDH usage with DXGI capacity by adapter LUID and refreshes adapter names every 30 seconds.
- [WindowsHistoryEventReader](../../src/MonCore.Windows/Monitoring/WindowsHistoryEventReader.cs) already reads Kernel-Processor-Power 37 ("Firmware is limiting processor speed") into History.
- [CaptureRules](../../src/MonCore/Monitoring/CaptureRules.cs) and [ResourceTrendArchive](../../src/MonCore/Monitoring/ResourceTrendArchive.cs) enumerate the metrics that automatic captures and longer trends can use.

## Reading sources

| Reading | Source | Notes |
| --- | --- | --- |
| GPU temperature, fan RPM, power | `D3DKMTQueryAdapterInfo` with `KMTQAITYPE_ADAPTERPERFDATA` (`D3DKMT_ADAPTER_PERFDATA`) | Task Manager reads the same data. Works for every vendor with a WDDM 2.4+ driver. Temperature is in tenths of °C; power is in tenths of a percent. Integrated GPUs may report zero, which must become null. `PowerStateOverride` is not a power-off signal; see [Spike results](#spike-results). |
| GPU throttle threshold | `KMTQAITYPE_ADAPTERPERFDATA_CAPS` (`D3DKMT_ADAPTER_PERFDATACAPS`), read once per adapter | `TemperatureWarning` is where temperature-based throttling begins and `TemperatureMax` is the damage level, both in tenths of °C, reported by the driver. Zero means not reported. |
| CPU throttling | PDH `\Processor Information(_Total)\% Performance Limit` | The performance the processor guarantees, as a percentage of nominal. It reads 100 when nothing limits the CPU and drops below 100 under a thermal, power or firmware limit. `Performance Limit Flags` records why. Compare it with `MhzLimit` from the data `WindowsCpuClock` already reads, and keep whichever the spike shows is reliable. |
| System thermal zone | PDH `\Thermal Zone Information(*)\High Precision Temperature`, falling back to `Temperature` | ACPI zones, in tenths of a kelvin or whole kelvins. Usually a motherboard or chassis sensor, not the CPU die. Some firmware reports a value that never changes. |

The thermal zone is labelled **System thermal zone** everywhere, never "CPU temperature". The throttle reading is the main CPU signal, because it answers the purpose question directly.

## Data model

Add to `ResourceSample`, all nullable:

| Field | Unit |
| --- | --- |
| `CpuPerformanceLimitPercent` | Percentage of nominal performance; 100 means no limit |
| `ThermalZoneCelsius` | Hottest zone that passed the checks below |
| `ThermalZoneName` | Instance name of that zone |
| `GpuTemperatureCelsius` | For the adapter in `GpuId` |
| `GpuFanRpm` | For the adapter in `GpuId` |
| `GpuPowerPercent` | For the adapter in `GpuId` |
| `GpuTemperatureWarningCelsius` | Driver-reported throttle threshold for that adapter |
| `GpuTemperatureMaxCelsius` | Driver-reported maximum for that adapter |

Add a `SystemMetricCapabilities.Temperature` flag. It is set when at least one of the readings above was seen during the session. Without it, the UI shows nothing new.

Store every temperature in Celsius. A °C/°F setting changes display only.

`MonCore` stays a portable leaf: these are numbers with no Windows types. A future macOS collector can fill them from SMC sensors.

## Validation rules

- **GPU adapter.** Temperature, fan and power come from the same adapter LUID as `GpuId`, the adapter whose engine is busiest. Never average across adapters. The tooltip may list the others.
- **Zero means missing.** D3DKMT reports 0 for an unsupported field. Treat 0 °C and 0% power as null. Treat 0 RPM as null only when caps report no `MaxFanRpm`; with a maximum it means the fans have stopped, which many cards do when cool.
- **Powered-off GPU.** Do not use `PowerStateOverride` to hide readings: a running desktop RTX 5080 reports 0. A hybrid laptop run must show what a powered-off GPU returns and that the query does not wake it.
- **Plausible range.** Discard temperatures outside 1–150 °C.
- **Stuck thermal zones.** Discard a zone whose value has not changed for 10 minutes of sampling while CPU load varied by more than 20 points. Common fake constants (for example 27.8 °C) are caught by this rule, so no hard-coded list is needed.
- **Several zones.** The gauge shows the hottest valid zone. The tooltip lists each one.

## UI

The [mockup](#mockup) shows the three placements below, on the dark theme in Full view.

### Secondary gauge line

When a reading exists, append it after the existing sub-value: `4.30 GHz · 52 °C` for CPU and `3D · 61 °C` for GPU. With no reading the line stays exactly as it is today. Compact view uses the same line.

### Thermal-limit marker

While the performance limit is below 100 for at least two consecutive samples:

- an amber flame icon follows the "CPU" label, with the accessible name "Thermal limit";
- the clock value turns amber;
- the gauge tile gets an amber border.

The percentage arc keeps its resource color. Throttling is a separate signal from load and does not turn the gauge into a warning. The marker needs a non-color cue (the icon) and must read correctly in every theme and palette.

The GPU gauge shows the same marker when its temperature reaches the driver-reported `TemperatureWarning`. Without that threshold the GPU gets no marker. Neither GPU in the first hardware run reported one, so expect the GPU marker to be rare.

### Tooltips

Extend the existing [resource detail tooltips](HOVER-DETAILS.md):

- **CPU:** kernel share, clock and its maximum, the limit with its current duration ("Limited to 62% for 0:42"), system thermal zone with each zone listed, the 10-minute peak with its time, and the footnote "ACPI thermal zone, not the CPU die".
- **GPU:** adapter name, temperature with the throttle threshold when reported ("61 °C of 83 °C"), otherwise with `TemperatureMax` ("61 °C of 88 °C max"), fan, power, the existing dedicated memory, the 10-minute peak with its time, and the footnote "Same adapter as utilization".

All visible text, tooltips and accessibility names go in `Strings.resx` and the nine translations. Check German and Polish for wrapping in the gauge header and tooltip rows.

### Later placements

Not part of the first delivery, listed so the data model allows them:

- An optional temperature series in the [chart legend](CHART-RESOURCE-LEGEND.md), plotted against its own °C axis.
- Temperature in History, incident recordings and [longer trends](LONGER-RESOURCE-TRENDS.md). Longer trends keep each minute's maximum, not its average.
- Automatic-capture conditions such as "GPU temperature ≥ 85 °C for 30 s" or "CPU throttled for 10 s". This adds `CaptureMetric` values.
- CLI snapshot fields and MCP resource tools.

## Performance

One D3DKMT call per adapter per tick, opened with `D3DKMTOpenAdapterFromLuid` once and kept until adapters change; refresh on the same 30-second cycle as `WindowsGpuMemory` adapter names. Add the thermal and performance-limit counters to an existing PDH query rather than opening a new one. Measure before and after as [DEVELOPMENT.md](../DEVELOPMENT.md#performance) describes and record the result in [performance history](../PERFORMANCE-HISTORY.md).

## Delivery and acceptance

1. **Hardware spike.** [tools/TemperatureProbe.cs](../../tools/TemperatureProbe.cs) is a throwaway single-file console probe that prints, once a second for two minutes under idle and full load: D3DKMT perf data and caps for each adapter (including whether a powered-off GPU stays off), every thermal zone, `% Performance Limit` and `MhzLimit`. Run it on at least one desktop with a discrete GPU, one laptop with only an integrated GPU, and one laptop with both. Record the results in this note. If thermal zones are stuck or absent on most of these machines, drop the thermal zone and ship throttling and GPU readings only.
2. **Collectors and model.** The `ResourceSample` fields, the capability flag, validation rules and unit tests fed through the fake `IFrameSource`.
3. **UI.** Secondary line, thermal-limit marker, tooltips, strings and translations, °C/°F setting.
4. **Docs.** Remove the "temperature is out of scope" statements from README, the user guide, DEVELOPMENT.md and the design notes that carry them.

Acceptance requires:

- Unit tests for each validation rule: zero-as-missing, range limits, stuck-zone detection, hottest-zone choice and adapter pairing with `GpuId`.
- A characterization test showing that a sample with every new field null renders the gauges identically to today.
- A smoke mode, or an extension of an existing one, rendering normal, throttled, GPU-with-sensor and no-sensor states in Dark and Light, and in Compact view. Review the PNGs.
- `./tools/Verify.ps1` and `./tools/Verify.ps1 -Store` (the Settings page changes).
- Before/after measurements in performance history with no unexplained regression.

## Spike results

Run the probe with `dotnet run tools/TemperatureProbe.cs -- 120 > probe.csv`. On a machine without the .NET SDK, publish a standalone exe as the file header describes. The first half of the run is idle and the second half spins every logical processor; start a game or 3D app to load the GPU. Add one row per machine.

| Machine | GPU temperature, fan, power | GPU caps (warning/max) | Thermal zones | `% Performance Limit` under load | `MhzLimit` under load |
| --- | --- | --- | --- | --- | --- |
| Hyper-V VM, Ryzen 9 9950X3D host, Windows 10.0.26300 (2026-11-25) | Not available: Hyper-V Video and Remote Display adapters reject the perf query (`0xC000000D`) | 0 / 0 | None | 100 throughout, flags 0 | 4,300 = `MaxMhz` throughout |

| Desktop host of that VM: Ryzen 9 9950X3D (integrated Radeon) and GeForce RTX 5080, Windows 10.0.26300, 32 logical processors (2026-11-25) | **Works on both GPUs.** Radeon 51–55 °C, RTX 5080 37–40 °C, both moving; power 4.5–20.3%. Fan 0 RPM on both. A third, unnamed adapter rejects the query (`0xC000000D`). | Radeon 0 / 0. RTX 5080 warning 0, max 88 °C, max fan 3,600 RPM | None | 100 throughout, flags 0. `% of Maximum Frequency` 94–100 | 4,300 = `MaxMhz` throughout; `CurrentMhz` also fixed at 4,300 |

The VM confirms only that the probe runs and that every missing reading stays empty. The clock is fixed at 4,300 MHz under load there.

What the desktop shows:

- **The D3DKMT path and struct layout are correct.** Adapter names, temperatures and power come back for an AMD integrated GPU and an NVIDIA discrete GPU without vendor libraries.
- **`PowerStateOverride` does not mean "powered off".** The RTX 5080 reported 0 while its temperature and power kept changing. The rule that hid readings when it is 0 is removed. Whether a powered-off hybrid-laptop GPU reports anything is still unknown.
- **A fan reading of 0 is real.** The RTX 5080 stops its fans when cool and reports `MaxFanRpm` 3,600, so 0 means stopped. The Radeon reports no maximum, so its 0 means not reported.
- **No GPU reported `TemperatureWarning`.** The GPU thermal-limit marker would never appear on this machine. The tooltip can still show `TemperatureMax` ("38 °C of 88 °C max").
- **No thermal zones on this desktop.** CPU temperature is simply unavailable here, as expected for many desktop boards. The CPU side reduces to the throttle reading.
- **A minute of full CPU load caused no throttling** (`% Performance Limit` stayed at 100). That is expected for a well-cooled desktop. It still has to be observed falling below 100 on a laptop.
- **Side finding: WinMon's existing clock reading is static on this CPU.** `CallNtPowerInformation` `CurrentMhz`, which `WindowsCpuClock` reads, stayed at the 4,300 MHz base clock while the CPU was idling and boosting. The gauge's "4.30 GHz" therefore never moves here. The probe now also records `\Processor Information(_Total)\% Processor Performance` and multiplies it by `MaxMhz`. On the host, a two-minute run read 119–128%, an effective 5,121–5,516 MHz (lower under all-core load, as expected for boost), while `CurrentMhz` stayed at 4,300 throughout. The VM showed the same. Under full load `% Processor Utility` equals `% Processor Performance`, which fits utility being busy time × performance. Fixing `WindowsCpuClock` is a separate change from this feature.

The integrated-only laptop and the hybrid laptop are still to run.

## Deferred

- **CPU die temperature.** It would need a signed ring-0 driver loaded by the existing LocalSystem network service, as an explicit opt-in that the Store build cannot offer. Consider it only if users ask for it after throttling ships, and only with a driver that is not on the vulnerable-driver blocklist.
- **Vendor GPU SDKs.** Load NVML, ADLX or IGCL dynamically only if users ask for hotspot or memory-junction temperatures.
- **Thresholds and colors.** A user-set temperature threshold, if automatic-capture conditions prove useful.

## Mockup

An illustrative mockup was drawn during design review (not saved as a file). It shows, left to right, CPU normal (`4.30 GHz · 52 °C`), CPU throttled (amber flame, amber `2.10 GHz`, amber border), GPU with a sensor (`3D · 61 °C`) and GPU without one (`3D`, unchanged), then the CPU and GPU tooltips described above. All values were illustrative.

## Sources

- [D3DKMT_ADAPTER_PERFDATA](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdata): GPU temperature, fan RPM, power and power state fields.
- [D3DKMT_ADAPTER_PERFDATACAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdatacaps): `TemperatureWarning` and `TemperatureMax`, WDDM 2.4 and later.
- [PROCESSOR_POWER_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/power/processor-power-information-str): `CurrentMhz`, `MaxMhz` and `MhzLimit`.
- [Microsoft vulnerable driver blocklist](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules): why a WinRing0-style driver is not an option.

## What landed and departures

Not implemented yet. This section will record what shipped and where it departed from the design.
