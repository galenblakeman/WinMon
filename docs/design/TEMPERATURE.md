# CPU and GPU temperature

Date: November 25, 2026. Status: built-in collection and presentation implemented; verification and remaining native acceptance are tracked in [What landed and departures](#what-landed-and-departures).

## Purpose and scope

Answer one question: **is heat slowing this machine down?** Before this change WinMon showed CPU clock and GPU utilization but no temperature. The original resource-detail and hardware-inventory designs excluded temperatures because the GPU path avoids vendor libraries and Windows offers no unprivileged CPU die temperature. This delivery extends desktop resource details; CLI/MCP hardware inventory still excludes live sensors.

This design adds temperature and throttling readings that need **no elevation, no driver and no new dependency**. They also work in the Store build. The display follows the rule that a missing reading is null, never zero: machines without a sensor look exactly as they do today.

Outside this design:

- True CPU package or per-core temperature, and motherboard fans. Windows has no unprivileged source for them. [External sensor sources](#external-sensor-sources) lists the follow-up options.
- Vendor SDKs (NVML, AMD ADLX, Intel IGCL) for GPU hotspot and memory-junction temperatures.
- Fan control, overclocking, voltages, motherboard or drive sensors, and any action that changes hardware.
- Danger colors based on a guessed maximum safe temperature (TjMax). GPU warnings use only the driver-reported threshold below; the CPU gets none.

## Repository baseline

Implemented against product version 4.3.0 on .NET 10 Windows.

- [ResourceSample](../../src/MonCore/Monitoring/ResourceSample.cs) holds nullable system readings. Null means unavailable, never zero.
- [SystemMetricCapabilities](../../src/MonCore/Monitoring/CollectorCapabilities.cs) includes Temperature when collection has observed available readings during the session.
- CPU clock uses the existing `% Processor Performance` counter in [MemoryPressureCounter](../../src/MonCore.Windows/Monitoring/MemoryPressureCounter.cs), with the existing [WindowsCpuClock](../../src/MonCore.Windows/Monitoring/WindowsCpuClock.cs) power-information fallback. The temperature change adds optional performance-limit counters to that shared query; it does not use `MhzLimit` as a thermal signal.
- [WindowsGpuSampler](../../src/MonCore.Windows/Monitoring/WindowsGpuSampler.cs) reports the busiest engine from `\GPU Engine(*)\Utilization Percentage`. [WindowsGpuMemory](../../src/MonCore.Windows/Monitoring/WindowsGpuMemory.cs) pairs PDH usage with DXGI capacity by adapter LUID and refreshes adapter names every 30 seconds.
- [WindowsHistoryEventReader](../../src/MonCore.Windows/Monitoring/WindowsHistoryEventReader.cs) already reads Kernel-Processor-Power 37 ("Firmware is limiting processor speed") into History.
- [CaptureRules](../../src/MonCore/Monitoring/CaptureRules.cs) and [ResourceTrendArchive](../../src/MonCore/Monitoring/ResourceTrendArchive.cs) enumerate the metrics that automatic captures and longer trends can use.

## Reading sources

| Reading | Source | Notes |
| --- | --- | --- |
| GPU temperature, fan RPM, power | `D3DKMTQueryAdapterInfo` with `KMTQAITYPE_ADAPTERPERFDATA` (`D3DKMT_ADAPTER_PERFDATA`) | Available when the graphics driver supports the query; individual fields may be absent. Temperature is in tenths of °C; power is in tenths of a percent. Integrated GPUs may report zero, which must become null. `PowerStateOverride` is not a power-off signal; see [Spike results](#spike-results). |
| GPU throttle threshold | `KMTQAITYPE_ADAPTERPERFDATA_CAPS` (`D3DKMT_ADAPTER_PERFDATACAPS`), read once per adapter | `TemperatureWarning` is where temperature-based throttling begins and `TemperatureMax` is the damage level, both in tenths of °C, reported by the driver. Zero means not reported. |
| CPU performance limit | PDH `\Processor Information(_Total)\% Performance Limit` | A value below 100 indicates reduced available performance; it does not establish a thermal cause. Optional `Performance Limit Flags` are shown as raw flags without guessing their meaning. Missing values remain unavailable. |
| System thermal zone | PDH `\Thermal Zone Information(*)\High Precision Temperature`, falling back to `Temperature` | ACPI zones, in tenths of a kelvin or whole kelvins. Usually a motherboard or chassis sensor, not the CPU die. Some firmware reports a value that never changes. |

The thermal zone is labelled **System thermal zone** everywhere, never "CPU temperature". The throttle reading is the main CPU signal, because it answers the purpose question directly.

## Data model

Add to `ResourceSample`:

| Field | Type | Meaning |
| --- | --- | --- |
| `CpuPerformanceLimitPercent` | `double?` | Percentage of nominal performance; 100 means no limit |
| `CpuPerformanceLimitFlags` | `uint?` | Raw `Performance Limit Flags`, kept so a later build can name the reason |
| `ThermalZones` | `IReadOnlyList<ThermalZoneReading>` | Every zone that passed the checks below; empty when none |
| `GpuThermals` | `IReadOnlyList<GpuThermalReading>` | One per physical adapter that reported anything; empty when none |

`ThermalZoneReading` is `(string Name, double Celsius, bool Unchanging)`. `GpuThermalReading` is `(string AdapterId, string Name, double? TemperatureCelsius, double? FanRpm, double? PowerPercent, double? WarningCelsius, double? MaxCelsius)`. Both are immutable records, and the lists are never mutated after the sample is built.

`AdapterId` uses the same `luid/physical-index` form as `GpuId`, which `WindowsGpuSampler` builds from the engine instance name. The gauge shows the `GpuThermals` entry whose `AdapterId` equals `GpuId`, so a linked-adapter GPU pairs by physical index as well as LUID.

Add a `SystemMetricCapabilities.Temperature` flag. It is set when at least one of the readings above was seen during the session. Without it, the UI shows nothing new.

Store every temperature in Celsius. A °C/°F setting changes display only.

`MonCore` stays a portable leaf: these are numbers with no Windows types. A future macOS collector can fill them from SMC sensors.

## Validation rules

- **GPU adapter.** The gauge's temperature, fan and power come from the `GpuThermals` entry matching `GpuId` (LUID and physical index), the adapter whose engine is busiest. Never average across adapters. The tooltip lists the other entries under their own names.
- **Zero means missing.** D3DKMT reports 0 for an unsupported field. Treat 0 °C and 0% power as null. Treat 0 RPM as null only when caps report no `MaxFanRpm`; with a maximum it means the fans have stopped, which many cards do when cool.
- **Powered-off GPU.** Do not use `PowerStateOverride` to hide readings: a running desktop RTX 5080 reports 0. A hybrid laptop run must show what a powered-off GPU returns and that the query does not wake it.
- **Plausible range.** Discard temperatures outside 1–150 °C.
- **Unchanging thermal zones.** A steady value is not proof of a broken sensor: a chassis sensor can stay level while the CPU works. So never drop a zone for being steady. Set `Unchanging` when a High Precision zone has not changed for 10 minutes of sampling while CPU load varied by more than 20 points, and show it in the tooltip as "unchanged for 10 min". Clear the flag on the next change. A sampling gap or collector reset clears the history, as it does for other rates. Whole-kelvin zones are never flagged, because a one-kelvin step hides small changes.
- **Several zones.** The tooltip lists every zone, hottest first. No zone appears on the gauge.
- **Peaks follow identity.** A 10-minute peak belongs to one zone name or one `AdapterId`. When `GpuId` switches to another adapter, the GPU tooltip shows that adapter's own peak, never one carried over from the previous adapter. A peak's history resets with the same gaps and resets as above.

## UI

The [mockup](#mockup) shows the three placements below, on the dark theme in Full view.

### Secondary gauge line

GPU only: when the paired adapter reports a temperature, append it after the existing sub-value, as in `3D · 61 °C`. With no reading the line stays exactly as it is today. Compact view uses the same line.

The CPU line stays the clock alone. A thermal zone is not the CPU's temperature, and a number under the CPU label would read as one, so zones appear only in the tooltip under their own heading.

### Limit markers

**CPU: performance limited.** While `CpuPerformanceLimitPercent` is below 100 for at least two consecutive samples:

- a limit icon (a gauge with a downward arrow, not a flame) follows the "CPU" label, with the accessible name "Performance limited";
- the clock value turns amber;
- the gauge tile gets an amber border.

The wording stays "Performance limited", because the counter drops for power and firmware limits as well as heat. `CpuPerformanceLimitFlags` is stored with every sample. A later build may say "Thermal limit" only once real hardware has tied a flag bit to thermal throttling. Until then the tooltip shows the raw flags value for diagnosis.

**GPU: thermal limit.** The GPU gauge shows a flame icon, accessible name "Thermal limit", when the paired adapter's temperature reaches its driver-reported `TemperatureWarning`. That threshold is defined as where temperature-based throttling begins, so this marker is thermal by definition. Without the threshold the GPU gets no marker. Neither GPU in the first hardware run reported one, so expect it to be rare.

The percentage arc keeps its resource color in both cases. A limit is a separate signal from load and does not turn the gauge into a warning. Each marker has a non-color cue (its icon) and must read correctly in every theme and palette.

### Tooltips

Extend the existing [resource detail tooltips](HOVER-DETAILS.md):

- **CPU:** kernel share, clock and its maximum, the limit with its current duration and raw flags ("Limited to 62% for 0:42, flags 0x2"), a "System thermal zones" heading with every zone, hottest first, each with its own 10-minute peak and time, and the footnote "ACPI thermal zone, not the CPU die".
- **GPU:** adapter name, temperature with the throttle threshold when reported ("61 °C of 83 °C"), otherwise with `TemperatureMax` ("61 °C of 88 °C max"), fan, power, the existing dedicated memory, that adapter's 10-minute peak with its time, other adapters' temperatures by name, and the footnote "Same adapter as utilization".

All visible text, tooltips and accessibility names go in `Strings.resx` and the nine translations. Check German and Polish for wrapping in the gauge header and tooltip rows.

### Later placements

Temperature chart series, History/incident persistence, longer-trend peaks and automatic-capture conditions are now implemented in [Temperature history and captures](TEMPERATURE-HISTORY.md). The list below records the original follow-up scope; CLI/MCP sensor exposure remains deferred:

- An optional temperature series in the [chart legend](CHART-RESOURCE-LEGEND.md), plotted against its own °C axis.
- Temperature in History, incident recordings and [longer trends](LONGER-RESOURCE-TRENDS.md). Longer trends keep each minute's maximum, not its average.
- Automatic-capture conditions such as "GPU temperature ≥ 85 °C for 30 s" or "CPU performance limited for 10 s". This adds `CaptureMetric` values.
- CLI snapshot fields and MCP resource tools.

## Performance

One D3DKMT call per adapter per tick, opened with `D3DKMTOpenAdapterFromLuid` once and kept until adapters change; refresh on the same 30-second cycle as `WindowsGpuMemory` adapter names. Add the thermal and performance-limit counters to an existing PDH query rather than opening a new one. Measure before and after as [DEVELOPMENT.md](../DEVELOPMENT.md#performance) describes and record the result in [performance history](../PERFORMANCE-HISTORY.md).

## Delivery and acceptance

1. **Hardware spike.** [tools/TemperatureProbe.cs](../../tools/TemperatureProbe.cs) is a throwaway single-file console probe that prints, once a second for two minutes under idle and full load: D3DKMT perf data and caps for each adapter (including whether a powered-off GPU stays off), every thermal zone, `% Performance Limit` and `MhzLimit`. Run it on at least one desktop with a discrete GPU, one laptop with only an integrated GPU, and one laptop with both. Record the results in this note. If thermal zones are stuck or absent on most of these machines, drop the thermal zone and ship throttling and GPU readings only.
2. **Collectors and model.** The `ResourceSample` fields, the capability flag, validation rules and unit tests fed through the fake `IFrameSource`.
3. **UI.** GPU secondary line, limit markers, tooltips, strings and translations, °C/°F setting.
4. **Docs.** Remove the "temperature is out of scope" statements from README, the user guide, DEVELOPMENT.md and the design notes that carry them.

Acceptance requires:

- Unit tests for each validation rule: zero-as-missing, range limits, setting and clearing `Unchanging` (including after a gap), zone ordering, adapter pairing with `GpuId` including the physical index, and peaks staying with their own zone or adapter when `GpuId` switches.
- A characterization test showing that a sample with every new field null renders the gauges identically to today.
- A smoke mode, or an extension of an existing one, rendering normal, CPU performance-limited, GPU thermal-limit, GPU-with-sensor and no-sensor states in Dark and Light, and in Compact view. Review the PNGs.
- `./tools/Verify.ps1` and `./tools/Verify.ps1 -Store` (the Settings page changes).
- Before/after measurements in performance history with no unexplained regression.

## Spike results

Run the probe with `dotnet run tools/TemperatureProbe.cs -- 120 > probe.csv`. On a machine without the .NET SDK, publish a standalone exe as the file header describes. The first half of the run is idle and the second half spins every logical processor; start a game or 3D app to load the GPU. Add one row per machine.

| Machine | GPU temperature, fan, power | GPU caps (warning/max) | Thermal zones | `% Performance Limit` under load | `MhzLimit` under load |
| --- | --- | --- | --- | --- | --- |
| Hyper-V VM, Ryzen 9 9950X3D host, Windows 10.0.26300 (2026-11-25) | Not available: Hyper-V Video and Remote Display adapters reject the perf query (`0xC000000D`) | 0 / 0 | None | 100 throughout, flags 0 | 4,300 = `MaxMhz` throughout |

| Desktop host of that VM: Ryzen 9 9950X3D (integrated Radeon) and GeForce RTX 5080, Windows 10.0.26300, 32 logical processors (2026-11-25) | **Works on both GPUs.** Radeon 51–55 °C, RTX 5080 37–40 °C, both moving; power 4.5–20.3%. Fan 0 RPM on both. A third, unnamed adapter rejects the query (`0xC000000D`). | Radeon 0 / 0. RTX 5080 warning 0, max 88 °C, max fan 3,600 RPM | None | 100 throughout, flags 0. `% of Maximum Frequency` 94–100 | 4,300 = `MaxMhz` throughout; `CurrentMhz` also fixed at 4,300 |
| Hybrid laptop: Core Ultra 9 275HX (Intel Graphics) and GeForce RTX 5080 Laptop GPU, Windows 10.0.26300, 24 logical processors (2026-11-25) | **RTX 5080 Laptop works:** 34–44 °C, moving, rising under CPU load. Power 0 and fan 0 on it. **Intel Graphics reports 0 for everything.** Two unnamed adapters report zeros or reject the query. | RTX 5080 Laptop warning 0, max 89 °C, max fan 0. Others 0 / 0. | **`\_TZ.THRM`, high precision, moving:** 43–49 °C idle, 47–67 °C under load | 100 throughout, flags 0. `% of Maximum Frequency` 87–100 | `CurrentMhz` fixed (mean 2,300). `MaxMhz` up to 2,700, `MhzLimit` down to 2,100. |

The VM confirms only that the probe runs and that every missing reading stays empty. The clock is fixed at 4,300 MHz under load there.

What the desktop shows:

- **The D3DKMT path and struct layout are correct.** Adapter names, temperatures and power come back for an AMD integrated GPU and an NVIDIA discrete GPU without vendor libraries.
- **`PowerStateOverride` does not mean "powered off".** The RTX 5080 reported 0 while its temperature and power kept changing. The rule that hid readings when it is 0 is removed. Whether a powered-off hybrid-laptop GPU reports anything is still unknown.
- **A fan reading of 0 is real.** The RTX 5080 stops its fans when cool and reports `MaxFanRpm` 3,600, so 0 means stopped. The Radeon reports no maximum, so its 0 means not reported.
- **No GPU reported `TemperatureWarning`.** The GPU thermal-limit marker would never appear on this machine. The tooltip can still show `TemperatureMax` ("38 °C of 88 °C max").
- **No thermal zones on this desktop.** CPU temperature is simply unavailable here, as expected for many desktop boards. The CPU side reduces to the throttle reading.
- **A minute of full CPU load caused no throttling** (`% Performance Limit` stayed at 100). That is expected for a well-cooled desktop. It still has to be observed falling below 100 on a laptop.
- **Side finding, since fixed: WinMon's clock reading was static on this CPU.** `CallNtPowerInformation` `CurrentMhz`, which `WindowsCpuClock` read at the time, stayed at the 4,300 MHz base clock while the CPU was idling and boosting. The gauge's "4.30 GHz" therefore never moves here. The probe now also records `\Processor Information(_Total)\% Processor Performance` and multiplies it by `MaxMhz`. On the host, a two-minute run read 119–128%, an effective 5,121–5,516 MHz (lower under all-core load, as expected for boost), while `CurrentMhz` stayed at 4,300 throughout. The VM showed the same. Under full load `% Processor Utility` equals `% Processor Performance`, which fits utility being busy time × performance. `WindowsCpuClock` now reports `MaxMhz` × `% Processor Performance` / 100; see [Effective CPU clock](EFFECTIVE-CPU-CLOCK.md). The `4.30 GHz` examples elsewhere in this note show the base clock and are illustrative.

What the hybrid laptop shows:

- **A real thermal zone, and it is not the CPU die.** `\_TZ.THRM` moved with load, from 43–49 °C idle to 67 °C after a minute of all-core load. A 275HX die under that load runs far hotter, so this is a slower chassis or controller sensor. That supports keeping zones off the CPU gauge and labelling them "System thermal zone". It changed steadily, so the unchanging-zone flag would not fire.
- **The discrete GPU reports temperature; the integrated one reports nothing.** The RTX 5080 Laptop gave moving temperatures with no fan or power data. Laptop fans belong to the system, so 0 RPM with no `MaxFanRpm` correctly becomes null. Intel Graphics gave 0 for temperature, power and fan, which the zero-as-missing rule turns into null. Both rules held on real hardware.
- **Whether the probe wakes a sleeping discrete GPU is still open.** The RTX 5080 Laptop reported `PowerStateOverride` 0 throughout, yet returned a temperature every second. Either it was already awake (the CPU load warmed it through the shared cooler) or the query works without waking it. A run on battery with nothing using the GPU, watching Task Manager's GPU activity before and during the probe, would tell them apart.
- **Still no throttling.** `% Performance Limit` stayed at 100 with flags 0 through a minute of all-core load at about 175% performance. A drop below 100 is still unobserved; a longer load, on battery, is the next attempt.
- **Hybrid cores have different rated clocks.** `MaxMhz` reached 2,700 (performance cores) and `MhzLimit` went down to 2,100 (efficient cores). The probe's `effective_mhz` multiplies by the highest `MaxMhz` and read 4,717–4,736 MHz under load. The first version of the [effective clock](EFFECTIVE-CPU-CLOCK.md) multiplied by the **mean** `MaxMhz`, about 2,300 here (8 × 2,700 + 16 × 2,100, divided by 24). Task Manager on this laptop shows a base speed of 2.70 GHz and a Speed of 4.21 GHz at about 156% performance, so it scales the highest rated clock. The mean would have read about 15% low. The clock now uses the highest `MaxMhz`. On the desktop every core shares one rated clock, so it isn't affected.

The integrated-only laptop is still to run.

## External sensor sources

CPU die temperature and motherboard fans need ring-0 access to chip registers, which no Windows API offers. WinMon will not ship a kernel driver of its own. None of this is part of the first delivery.

**Decision:** [Optional CPU temperature](HARDWARE-SENSORS.md) is authoritative for CPU die/package readings: stable LibreHardwareMonitorLib NuGet in its own service, with explicit consent to install the service and official driver. The provider now includes the selected CPU sensor and supported board fan tachometers, as described in [sensor settings](SENSORS-SETTINGS.md). WinMon's UI stays unelevated, and the network service gains no sensor collection. The monitoring-tool readers below are rejected alternatives retained for reference. Only the common validation, attribution and unit principles below apply to the selected service; its lifecycle and freshness protocol are defined in that design.

The CAM comparison on the spike desktop shows how other tools do it. CAM showed a 73 °C CPU temperature and a 1,775 RPM CPU fan, while that machine had six third-party kernel drivers loaded for this purpose: AMD's Ryzen Master SDK driver, a CPUID SDK driver, Corsair's low-level access driver, and MSI's `MsIo64` and two `NTIOLib` copies. `MsIo64` has a published arbitrary physical-memory read/write vulnerability (CVE-2019-18845).

### Monitoring tools the user runs

These alternatives would read sensors from a **running** monitoring tool without elevating WinMon. No such reader is planned, and this does not add sensor support to the Store build.

| Source | Detect | Read | Covers |
| --- | --- | --- | --- |
| HWiNFO | Shared memory `Global\HWiNFO_SENS_SM2` exists; needs "Shared Memory Support" on in HWiNFO | `MemoryMappedFile.OpenExisting`; documented header plus sensor and reading records | Broadest: CPU, motherboard, fans, drives, GPUs. The free version turns shared memory off after 12 hours of run time, and the user has to re-enable it ([HWiNFO licenses](https://www.hwinfo.com/licenses/)). |
| LibreHardwareMonitor (app) | WMI namespace `root\LibreHardwareMonitor` exists | WMI query on its `Sensor` class; `System.Management` is already referenced | CPU, motherboard, fans, GPUs, drives |
| Core Temp | Shared memory `CoreTempMappingObjectEx` exists (older versions: `CoreTempMappingObject`) | Documented `core_temp_shared_data_ex`, 4-byte aligned, `uiStructVersion` 2 ([Core Temp developers](https://www.alcpu.com/CoreTemp/developers.html)) | CPU temperatures only. Values need conversion first; see below. |

MSI Afterburner and AIDA64 also publish shared memory. Afterburner is mostly GPU and partly undocumented, and AIDA64 is a paid product, so neither is planned.

### Driver layers the user installs

The original option was to reuse an installed driver from the network service. That hosting approach was rejected in favor of [a separate sensor service](HARDWARE-SENSORS.md#why-a-separate-service). The selected setup may install or upgrade PawnIO after explicit consent; the UI never opens the driver. The Store build cannot offer this feature.

| Source | Detect | Read | Notes |
| --- | --- | --- | --- |
| [PawnIO](https://pawnio.eu/) | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` `InstallLocation`, falling back to `%ProgramFiles%\PawnIO` | Load `PawnIOLib` from the install folder; `pawnio_open`, `pawnio_load(module)`, `pawnio_execute`. Plain exports, so ordinary P/Invoke. | A signed, general-purpose driver that runs small hardware modules: `IntelMSR`, `IntelPCHThermal`, `AMDFamily17`, `RyzenSMU`, `LpcIO`, SMBus modules. The project asks apps to link users to pawnio.eu rather than bundle it. |
| LibreHardwareMonitorLib on PawnIO | As PawnIO | Library in a separate service | Selected: stable LibreHardwareMonitorLib 0.9.6, CPU-only initially; the library owns hardware access and decoding. See [Optional CPU temperature](HARDWARE-SENSORS.md). |

### Rejected

- **CPUID SDK:** commercial license.
- **AMD Ryzen Master SDK** (installed on the spike desktop at `C:\Program Files\AMD\RyzenMasterSDK`, version V29 driver):
  - Its `License_Public.txt` allows use only under a Ryzen Master SDK license agreement signed with AMD, and forbids distribution beyond that agreement. That rules it out for an MIT app unless such an agreement is signed.
  - The sample readme requires an administrator prompt.
  - It is an overclocking SDK: next to read calls such as `GetCurrentTemperature` and `GetEffectiveFrequency`, it exposes write calls such as `SetOverclockFreqAllCores` and `SetPPTLimit`.
  - The API is C++ classes and covers AMD CPUs only.
  - It was not added to the probe.
- **WinRing0 and InpOut:** vulnerable, and blocklisted or flagged by Defender.
- **Motherboard-vendor drivers** (MSI `NTIOLib`/`MsIo64`, Corsair `LLAccess` and similar): undocumented, several with published vulnerabilities.
- **Intel:** there is no current public SDK. Power Gadget is discontinued, XTU has no public SDK, and Intel PCM needs its own driver on Windows.
- **Vendor ACPI-WMI interfaces** (some ASUS, Gigabyte, Dell and Lenovo boards): patchy, and different per vendor.
- **WMI `MSAcpi_ThermalZoneTemperature`:** needs admin and returns the same ACPI zones as the PDH counters.

### Rules for any external source

- **Untrusted input.** Another process's shared memory or WMI data crosses a trust boundary. Bounds-check every size and offset against the mapped length, cap the sensor count, and range-check values as the [validation rules](#validation-rules) do.
- **Attribution.** Tooltips name the source ("from HWiNFO") and keep the tool's own sensor names. A die temperature from these tools may appear under the CPU, labelled with its source. An ACPI zone never does.
- **One source per sensor.** The selected service supplies LibreHardwareMonitor CPU readings initially; it does not discover or prioritize running monitoring tools. Keep package identity, sensor meaning and source with each reading, and reset peaks when identity or collector generation changes.
- **Freshness, not presence.** Apply the selected service's [per-sensor acquisition and timestamp contract](HARDWARE-SENSORS.md#protocol). A completed library pass is not proof that each sensor was read successfully. Unavailable or unverified readings are null; successfully reacquired unchanged values remain valid.
- **Historical reader caveat.** Finding a shared-memory object or answering WMI namespace would not establish current data. WinMon's own mapping handle can keep an object alive after its producer exits ([Windows mapping lifetime](https://learn.microsoft.com/en-us/windows/win32/memory/sharing-files-and-memory)). A future reader would need producer-liveness and acquisition/heartbeat evidence. Core Temp has no timestamp in the documented structure; unchanged values alone cannot distinguish a frozen producer from stable hardware. Its reader remains unplanned rather than assigning a false freshness guarantee based on value changes.
- **Units and meaning.** Normalize temperature readings to absolute Celsius before validation, display or peaks; fan readings stay in RPM. Validate by sensor kind: temperatures must be finite and within 1–150 °C; RPM must be finite and nonnegative, with a confirmed zero meaning stopped and unavailable data remaining null. Reject unsupported kinds. If the unplanned Core Temp reader is revisited, characterize its Fahrenheit and distance-to-TjMax flags, including their combination and per-core TjMax mapping, before accepting values. Absolute Fahrenheit and Fahrenheit temperature differences require different conversions; do not apply an absolute-temperature offset to a delta.
- **Installation policy.** Built-in temperature collection installs nothing. Optional hardware sensors use the separate service's explicit-consent setup to install or upgrade PawnIO; no monitoring tool is installed. The earlier universal "no installs" rule applied to rejected readers and is superseded by [that setup design](HARDWARE-SENSORS.md#setup-and-pawnio).

### Open questions

- **PawnIO elevation:** whether opening the driver needs admin. The project's sources don't say. Only an unelevated run can answer it; an elevated run shows only that admin works.
- **Zen 5:** whether the pinned stable LibreHardwareMonitorLib package supplies a correctly labelled, accurate reading on the spike desktop's 9950X3D. Module names alone do not establish support.
- **Memory integrity:** whether PawnIO loads with Memory integrity (HVCI) on. A successful run proves this only if Memory integrity was actually on at the time.
- **Licensing:**
  - The PawnIO driver is GPL-2.0, with an exception for programs that use only its device interface.
  - `PawnIOLib` and the modules are LGPL-2.1.
  - The signed binaries are proprietary, but their installer may be redistributed.
  - LibreHardwareMonitorLib is MPL-2.0.

  WinMon is MIT. Bundling module blobs or the library needs a licensing check.
- **Names and defaults:** HWiNFO's object name, its header poll-time field, and whether LibreHardwareMonitor publishes WMI by default come from memory. Verify them against each tool before building on it. Core Temp's names and HWiNFO's 12-hour limit are verified from the linked official pages.

The service-backed LibreHardwareMonitor CPU probe described in [Optional CPU temperature](HARDWARE-SENSORS.md#validation) investigates these questions on both the AMD desktop and Intel laptop. Run it unelevated and elevated, recording the NuGet package and driver versions and whether Memory integrity was on. Native accuracy on both paths remains acceptance work after service implementation; the built-in [temperature probe](../../tools/TemperatureProbe.cs) remains independent of PawnIO.

## Deferred
- **Vendor GPU SDKs.** Load NVML, ADLX or IGCL dynamically only if users ask for hotspot or memory-junction temperatures.
- **Thresholds and colors.** A user-set temperature threshold, if automatic-capture conditions prove useful.

## Mockup

An illustrative mockup was drawn during design review (not saved as a file). It shows, left to right, CPU normal (`4.30 GHz · 52 °C`), CPU throttled (amber flame, amber `2.10 GHz`, amber border), GPU with a sensor (`3D · 61 °C`) and GPU without one (`3D`, unchanged), then the CPU and GPU tooltips described above. All values were illustrative. The mockup predates the design review: it put a thermal-zone temperature on the CPU gauge and used a flame for the CPU marker, and both have since changed (see [UI](#ui)).

## Sources

- [D3DKMT_ADAPTER_PERFDATA](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdata): GPU temperature, fan RPM, power and power state fields.
- [D3DKMT_ADAPTER_PERFDATACAPS](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_adapter_perfdatacaps): `TemperatureWarning` and `TemperatureMax`, WDDM 2.4 and later.
- [PROCESSOR_POWER_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/power/processor-power-information-str): `CurrentMhz`, `MaxMhz` and `MhzLimit`.
- [PawnIO](https://pawnio.eu/), [PawnIO](https://github.com/namazso/PawnIO), [PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) and its [wiki](https://github.com/namazso/PawnIO.Modules/wiki): driver editions, installation, detection, API and licenses.
- [Microsoft vulnerable driver blocklist](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules): why a WinRing0-style driver is not an option.

## What landed and departures

Implemented in the working tree:

- Portable nullable CPU performance-limit readings and immutable thermal-zone/GPU reading records.
- Optional thermal-zone and performance-limit counters in the existing memory-pressure PDH query, with per-zone high-precision/coarse fallback and state reset rules.
- D3DKMT GPU collection with physical-adapter identity, driver capabilities/names and 30-second handle caching. Unsupported fields remain missing.
- GPU gauge temperature, CPU performance-limit and GPU thermal markers, identity-specific ten-minute peaks, details, persisted Celsius/Fahrenheit preference, translations and Full/Compact diagnostic fixtures.
- The shared query now preserves `PDH_FMT_NOSCALE` and also uses the correct `PDH_FMT_NOCAP100` value (`0x8000`); the former `0x1000` constant was actually NOSCALE and could clip the existing processor-performance reading.

Verification is recorded in `artifacts/temperature-implementation/verification.md`. Full desktop and Store test selections passed 11,242 and 11,079 tests. A capture smoke found Appearance overflow from the unit selector; reduced page padding, then the affected Settings/preview reruns passed 80 tests in each variant and capture smokes passed 57 desktop / 51 Store renders. Gauges and Compact passed 57 / 112 renders per variant. All twelve temperature states were visually inspected; Store images match desktop byte-for-byte. The final LibreHardwareMonitor prototype/collector run passed 38 tests, and its locked restore, build, publish and missing-PawnIO diagnostic passed. The earlier direct-register prototype is superseded.

Four alternating performance pairs measured UI frame medians of 4.599 ms before / 4.646 ms after, with unchanged absent-sensor pixels and about 552 added bytes/frame. Native resource-sampler medians were 1.401 / 1.416 ms. See [performance history](../PERFORMANCE-HISTORY.md) for fixture results, variation and measurement limits. Physical sensor accuracy and optional CPU service native acceptance remain outside these completed checks.

Departures and native limits:

- The first delivery kept temperatures and CPU limits live-only. The [history extension](TEMPERATURE-HISTORY.md) now persists them using restart format 5 and incident format 3; older files retain unavailable fields.
- The existing desktop/hybrid hardware spike informed implementation. The integrated-only laptop, a confirmed sleeping-GPU power-state comparison and an observed real performance limit below 100 remain unverified.
- A six-sample idle probe in this development environment completed and reported unsupported GPU performance queries, no zones and CPU limit 100. This verifies graceful absence here, not sensor accuracy on physical GPUs.
- Optional CPU die/package temperature uses the separate LibreHardwareMonitor service; its native acceptance and service status belong to [Optional CPU temperature](HARDWARE-SENSORS.md).
