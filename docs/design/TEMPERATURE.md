# CPU and GPU temperature

Date: November 25, 2026. Status: proposal. Nothing is implemented. A short hardware spike must come before any UI work; see [Delivery and acceptance](#delivery-and-acceptance).

## Purpose and scope

Answer one question: **is heat slowing this machine down?** Today WinMon shows CPU clock and GPU utilization but no temperature. [DEVELOPMENT.md](../DEVELOPMENT.md), the [user guide](../USER-GUIDE.md), [Resource detail tooltips](HOVER-DETAILS.md) and [Hardware inventory in the CLI and MCP](HARDWARE-CLI-MCP.md) all list temperature as out of scope, because the GPU path avoids vendor libraries and Windows offers no unprivileged CPU die temperature.

This design adds temperature and throttling readings that need **no elevation, no driver and no new dependency**. They also work in the Store build. The display follows the rule that a missing reading is null, never zero: machines without a sensor look exactly as they do today.

Outside this design:

- True CPU package or per-core temperature, and motherboard fans. Windows has no unprivileged source for them. [External sensor sources](#external-sensor-sources) lists the follow-up options.
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

Not part of the first delivery, listed so the data model allows them:

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

## External sensor sources

CPU die temperature and motherboard fans need ring-0 access to chip registers, which no Windows API offers. WinMon will not ship a kernel driver. It can instead detect sensor sources the user already has and read from them. None of this is part of the first delivery.

The CAM comparison on the spike desktop shows how other tools do it. CAM showed a 73 °C CPU temperature and a 1,775 RPM CPU fan, while that machine had six third-party kernel drivers loaded for this purpose: AMD's Ryzen Master SDK driver, a CPUID SDK driver, Corsair's low-level access driver, and MSI's `MsIo64` and two `NTIOLib` copies. `MsIo64` has a published arbitrary physical-memory read/write vulnerability (CVE-2019-18845).

### Monitoring tools the user runs

These publish sensors only while the tool is **running**. WinMon reads them unprivileged, so the Store build can use them too.

| Source | Detect | Read | Covers |
| --- | --- | --- | --- |
| HWiNFO | Shared memory `Global\HWiNFO_SENS_SM2` exists; needs "Shared Memory Support" on in HWiNFO | `MemoryMappedFile.OpenExisting`; documented header plus sensor and reading records | Broadest: CPU, motherboard, fans, drives, GPUs. The free version is believed to stop sharing after 12 hours until re-enabled. |
| LibreHardwareMonitor (app) | WMI namespace `root\LibreHardwareMonitor` exists | WMI query on its `Sensor` class; `System.Management` is already referenced | CPU, motherboard, fans, GPUs, drives |
| Core Temp | Its named shared-memory object exists | Documented fixed-layout struct | CPU temperatures only |

MSI Afterburner and AIDA64 also publish shared memory. Afterburner is mostly GPU and partly undocumented, and AIDA64 is a paid product, so neither is planned.

### Driver layers the user installs

These are detected as **installed**. They probably need elevation, so reads would run in the existing LocalSystem network service, which users already opt into once with elevation. The desktop app would ask the service for sensor values as it already does for traffic. The Store build cannot offer these.

| Source | Detect | Read | Notes |
| --- | --- | --- | --- |
| [PawnIO](https://pawnio.eu/) | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO` `InstallLocation`, falling back to `%ProgramFiles%\PawnIO` | Load `PawnIOLib` from the install folder; `pawnio_open`, `pawnio_load(module)`, `pawnio_execute`. Plain exports, so ordinary P/Invoke. | A signed, general-purpose driver that runs small hardware modules: `IntelMSR`, `IntelPCHThermal`, `AMDFamily17`, `RyzenSMU`, `LpcIO`, SMBus modules. The project asks apps to link users to pawnio.eu rather than bundle it. |
| LibreHardwareMonitorLib on PawnIO | As PawnIO | Reference the NuGet library in the network service | Supplies the chip-by-chip knowledge that raw PawnIO calls would need. A large dependency on a sampling path. |
| AMD Ryzen Master Monitoring SDK | `C:\Program Files\AMD\RyzenMasterSDK` (present on the spike desktop) | C++ class API, so it needs a native shim | AMD CPUs only. Tied to SDK versions. |

### Rejected

- **CPUID SDK:** commercial license.
- **WinRing0 and InpOut:** vulnerable, and blocklisted or flagged by Defender.
- **Motherboard-vendor drivers** (MSI `NTIOLib`/`MsIo64`, Corsair `LLAccess` and similar): undocumented, several with published vulnerabilities.
- **Intel:** there is no current public SDK. Power Gadget is discontinued, XTU has no public SDK, and Intel PCM needs its own driver on Windows.
- **Vendor ACPI-WMI interfaces** (some ASUS, Gigabyte, Dell and Lenovo boards): patchy, and different per vendor.
- **WMI `MSAcpi_ThermalZoneTemperature`:** needs admin and returns the same ACPI zones as the PDH counters.

### Rules for any external source

- **Untrusted input.** Another process's shared memory or WMI data crosses a trust boundary. Bounds-check every size and offset against the mapped length, cap the sensor count, and range-check values as the [validation rules](#validation-rules) do.
- **Attribution.** Tooltips name the source ("from HWiNFO") and keep the tool's own sensor names. A die temperature from these tools may appear under the CPU, labelled with its source. An ACPI zone never does.
- **One source per sensor.** When several are present, use a fixed order: HWiNFO, LibreHardwareMonitor, Core Temp, then a driver layer. Never mix sources for the same sensor.
- **Disappearing sources.** When a tool exits, its readings become null and the UI returns to the built-in view. Re-detect about every 30 seconds, not every tick.
- **No installs.** WinMon never installs a tool or driver. Settings may link to the project's own download page.

### Open questions

- **PawnIO elevation:** whether opening the driver needs admin. The project's sources don't say.
- **Zen 5:** whether `AMDFamily17` covers family 1Ah (the spike desktop's 9950X3D). No module is named for 19h or 1Ah.
- **Memory integrity:** whether PawnIO loads with Memory integrity (HVCI) on.
- **Licensing:**
  - The PawnIO driver is GPL-2.0, with an exception for programs that use only its device interface.
  - `PawnIOLib` and the modules are LGPL-2.1.
  - The signed binaries are proprietary, but their installer may be redistributed.
  - LibreHardwareMonitorLib is MPL-2.0.

  WinMon is MIT. Bundling module blobs or the library needs a licensing check.
- **Names and defaults:** the shared-memory object names, HWiNFO's free-version limit and whether LibreHardwareMonitor publishes WMI by default come from memory. Verify each against the tool before building on it.

A PawnIO mode in [the probe](../../tools/TemperatureProbe.cs), run elevated on the spike desktop and compared with CAM, would settle the first three questions. The cheapest first step is the HWiNFO and LibreHardwareMonitor readers: unprivileged and small, covering Intel and AMD for anyone running either tool.

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

Not implemented yet. This section will record what shipped and where it departed from the design.
