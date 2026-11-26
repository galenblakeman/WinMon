# Optional CPU temperature

Date: November 25, 2026. Status: LibreHardwareMonitor CPU prototype implemented; optional service remains gated on native AMD/Intel acceptance. See [Verify before building](#verify-before-building) and [What landed and departures](#what-landed-and-departures).

## Purpose and scope

Show real CPU die/package temperature, which [CPU and GPU temperature](TEMPERATURE.md) cannot provide without kernel access. Use the official stable **LibreHardwareMonitorLib 0.9.6 NuGet package** inside a separate, optional, privileged Windows service. WinMon itself stays unelevated. LibreHardwareMonitor owns hardware identification, register access and decoding; WinMon owns sensor selection, validation, freshness, authorization and presentation. Enable CPU hardware first. Fans and other sensors can later use the same provider without replacing its acquisition stack.

| Area | Decision |
| --- | --- |
| Default | Disabled. Ordinary monitoring installs no service or driver. |
| Hosting | Its own service, `WinMonSensorMonitor`, separate from `WinMonNetworkMonitor`. It reuses the network service's setup, pipe and verification code by generalizing it, not copying it. |
| Driver | WinMon's elevated sensor setup installs PawnIO with PawnIO's own signed installer, after explicit consent. WinMon ships no driver of its own. |
| First presentation | CPU temperature on the CPU gauge, with the library's actual sensor meaning and LibreHardwareMonitor source named in the tooltip and accessible text. |
| Other sources | None. The unprivileged HWiNFO and LibreHardwareMonitor-app readers listed in the temperature note are not planned. |
| Store channel | Unavailable, as for network monitoring. |
| Channels | Installer and portable desktop builds. |

Outside this design: fan and motherboard monitoring, tuning or fan-control writes, overclocking, a sensor browser, chart series, History, incident recordings, automatic-capture conditions, CLI and MCP output, and sensor alerts. Register-selection writes required to acquire a reading are allowed inside the fixed official module operation; hardware configuration writes are not.

## Repository baseline

At design time the repository is at product version 4.3.0 on .NET 10 Windows. Recheck before implementation.

- [Optional per-process network monitoring](PER-PROCESS-NETWORK.md) and [the service README](../../src/WinMon.NetworkService/README.md) define the pattern this design reuses:
  - a one-time elevated, signed setup into `%ProgramFiles%\WinMon\NetworkMonitor`;
  - a LocalSystem service that starts on demand;
  - an admin-protected `allowed-users.json` account allow-list;
  - SCM rights for listed users limited to query and start.
- [NetworkServiceProtocol](../../src/MonCore.Windows/Monitoring/NetworkServiceProtocol.cs) defines the pipe protocol:
  - length-prefixed JSON with four operations: status, subscribe, snapshot, unsubscribe;
  - requests capped at 4 KiB and responses at 2 MiB;
  - one snapshot per second per connection, at most eight connections.
- [NetworkServiceClient](../../src/MonCore.Windows/Monitoring/NetworkServiceClient.cs) and [NetworkServiceIdentity](../../src/MonCore.Windows/Monitoring/NetworkServiceIdentity.cs) verify the registered service and the connected pipe server.
- [NetworkSetupPackage](../../src/MonCore.Windows/Monitoring/NetworkSetupPackage.cs) verifies and installs the signed payload.
- [TEMPERATURE.md](TEMPERATURE.md) records the CAM comparison: six third-party kernel drivers on the spike desktop. It also holds the external-source rules this design follows: untrusted input, attribution, freshness and normalized units.

## Why a separate service

- **Contained failures.** CPU register reads can hang or fail. A sensor failure must not stop network monitoring.
- **Independent updates.** LibreHardwareMonitor may need updates for new processors. Each package update is an elevated sensor-service update; it should not force one on network-only users.
- **Smaller exposure.** Network-only users never get PawnIO or the sensor service.
- **Independent removal.** Removing sensors leaves network monitoring alone, and the reverse.

The cost is a second service, a second Apps & Features entry and a second admin prompt. A combined setup that installs both services with one prompt is a later option.

## Implementation evidence: Tray-Temps

Source inspection of [Tray-Temps](https://github.com/nmd-113/Tray-Temps) on November 25, 2026 shows another application using the same sensor stack. These are observations of its `master` source, not native acceptance results for WinMon or a dependency selection for this proposal.

- **Library and runtime.** Its [project file](https://github.com/nmd-113/Tray-Temps/blob/master/Tray-Temps.csproj) targets .NET Framework 4.8 and references `LibreHardwareMonitorLib` **0.9.7-pre722**, a prerelease. It also embeds `PawnIO_setup.exe`. WinMon targets .NET 10 and pins stable **0.9.6**, rather than copying that prerelease dependency.
- **Driver setup.** Its [PawnIO setup helper](https://github.com/nmd-113/Tray-Temps/blob/master/Helpers/Infrastructure/PawnIoSetupHelper.cs) pins the bundled installer to **2.2.0**, checks its expected length and SHA-256, and uses `-install -silent`. It declares **2.0** as its minimum compatible PawnIO version. These are useful prototype inputs; they do not establish WinMon's minimum version, installer exit-code behavior or driver-load success. WinMon also requires the Authenticode and authenticated-payload checks described below.
- **Reading temperatures.** Its [monitoring code](https://github.com/nmd-113/Tray-Temps/blob/master/MainForm.cs) opens a LibreHardwareMonitor `Computer` with CPU and GPU categories enabled. A background task updates the selected CPU hardware, and the app reads the selected temperature sensor's nullable `Value`. Automatic selection prefers names containing `Package`, then `Tctl`, then `Core Max`, then the highest-valued temperature sensor; users can override the selection. The hardware access is supplied by LibreHardwareMonitor and PawnIO, rather than a new Windows CPU-temperature API.
- **Elevation.** Its [startup code](https://github.com/nmd-113/Tray-Temps/blob/master/Program.cs) and monitoring code offer to restart the desktop application with the `runas` verb for more complete sensor access. That is evidence of its chosen privilege model, not proof that every PawnIO operation requires administrator rights.

WinMon's access path is **unelevated UI → authenticated local pipe → optional sensor service → LibreHardwareMonitorLib → PawnIO → CPU sensors**. Only setup and the service perform privileged work. GPU collection stays on WinMon's built-in D3DKMT path. Select a characterized CPU sensor and preserve its meaning rather than choosing an arbitrary hottest sensor.

## LibreHardwareMonitor provider

Initial native acceptance targets remain the Ryzen 9 9950X3D desktop and Core Ultra 9 275HX laptop. Package compatibility does not establish that either machine returns a correct temperature.

- **Version and boundary:** reference [LibreHardwareMonitorLib 0.9.6](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6) only from the hardware provider/service, initially the isolated prototype. Keep it out of MonCore, the WPF process, CLI and MCP. Commit the package lock file. No prerelease, library fork, direct register decoder or WinRing0 fallback.
- **Lifecycle:** one owning worker creates `Computer` with `IsCpuEnabled = true` and every other category disabled. It opens once, calls `Update()` before reading CPU temperature sensors and closes in `finally`. Updates, resets and close never overlap. See the [exact-version lifecycle source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Computer.cs).
- **Identity and selection:** preserve hardware identifier, sensor identifier and the actual sensor name. Prefer a characterized `CPU Package`, `Core (Tdie)`, `Core (Tctl/Tdie)` or `Core (Tctl)` reading, in that order; never relabel the latter meanings as Tdie. Reject ambiguous duplicate meanings and multiple packages until explicit package selection exists. A hottest core is not a package fallback.
- **Driver:** 0.9.6 embeds its PawnIO modules and uses the installed PawnIO device directly; it does not require WinMon to load external module files or PawnIOLib. The probe checks the library's installed-PawnIO indication before opening hardware. Registry presence alone does not prove driver access. See the [exact-version PawnIO implementation](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/PawnIo/PawnIo.cs).
- **Future sensors:** add explicitly enabled categories and typed sensor projections behind this same provider when fans or other readings are approved. Do not enable motherboard/controller/storage scanning during the CPU-only phase. Never expose library control/write APIs over the pipe.

**Acquisition caveat:** `Update()` returning and a retained non-null `ISensor.Value` do not independently prove a fresh native acquisition. The prototype labels values accordingly. Characterize the selected version's lock-contention/failure paths before publishing successful per-sensor freshness through the service; never restamp an old cached value as current.

## Shared infrastructure

Generalize these parts of the network service so both services use them, each keyed by a service descriptor (service name, pipe name, install folder, executable name, protocol version):

- signed setup payload verification and transactional installation;
- the protected install folder and allow-list checks;
- the length-prefixed protocol framing and limits;
- SCM registration checks, pipe-server verification and impersonation-based authorization;
- leases with a reconnect grace period.

Network-specific code (ETW, ledger, ownership probes) stays in the network service. The sensor service gets its own install folder, `%ProgramFiles%\WinMon\SensorMonitor`, and pipe, `WinMonSensorMonitor.v1`.

## Setup and PawnIO

Settings gets a **CPU temperature** toggle with this explanation: "Show CPU temperature. First-time setup installs a privileged Windows service and the PawnIO hardware-access driver, and needs administrator approval."

1. **Check without elevation.** On enable, check the sensor service and PawnIO without elevation:
   - **PawnIO:** `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO`, then `%ProgramFiles%\PawnIO`.
   - **The service:** its identity, version and health, as the network service is checked.
2. **Ask for consent.** If setup is needed, show what will be installed. Name PawnIO, its publisher, and that other software may also use it. Then launch the signed setup with UAC.
3. **Install PawnIO, then the service.** The elevated setup installs PawnIO first:
   - **Already installed:** reuse an existing PawnIO of at least the required version.
   - **Older version:** upgrade it only with the same consent.
   - **Pinned, verified installer:** run the official `PawnIO_setup.exe` bundled in the sensor setup payload, pinned by version and SHA-256 in the authenticated release metadata. Verify its Authenticode signature before running it. Never download it at setup time.

   Then setup installs and registers the service and authorizes the original user, exactly as for the network service.
4. **Fail cleanly.** If PawnIO cannot load, report the reason and leave the feature disabled without a half-installed service. Possible reasons: Memory integrity, the vulnerable-driver blocklist, policy, or a cancelled install.
5. **Later launches.** They reconnect without UAC. A later PawnIO or service failure keeps the preference, shows the readings as unavailable and offers repair.

**Removal.** **Remove hardware sensor service…** needs elevation. It stops and removes the service, its files and its allow-list.

PawnIO is **not** uninstalled automatically: other tools may depend on it, and WinMon cannot know. The removal screen says PawnIO stays installed and how to remove it from Apps & Features. If setup installed PawnIO and the user explicitly asks, removal may run PawnIO's own uninstaller.

## Service lifecycle

- **Start on demand.** LocalSystem, unless the verification step shows a narrower identity can open PawnIO.
- **Collection.** The first subscription opens the supported CPU reader and its PawnIO module. One worker owns all native calls, including open, read and close; those calls never run under the pipe or subscription-state locks. No other hardware categories are scanned.
- **Timer.** A hardware update pass runs every 2 seconds by default, never on a client request. Passes are serialized, with no overlapping timer callbacks. Each snapshot is the latest completed pass, with the per-sensor validity described below.
- **Startup.** Opening can take seconds. Status reports "starting" until the first update completes, and the UI shows the readings as unavailable, not zero.
- **Shutdown.** The last lease ending schedules close on the owning worker after the same two-second grace period as the network service. Closing never overlaps a pending read. Idle shutdown, sleep and service stop invalidate the published generation immediately; draining and closing the reader have a 10-second total budget.
- **Generations.** Each open, and each resume from sleep after the previous collector has closed, starts a new GUID generation. Clients discard readings from older generations. Late results from an invalidated generation are never published.
- **Watchdog and recovery.** Open and each update pass have a 10-second budget. On an exception or timeout, snapshots become failed/unavailable immediately, and the pipe remains responsive. A timeout does not cancel a native call. Never abort its thread, close its handles from another thread, or open a replacement collector while the old one may still be running. If the call returns and close completes within a 10-second recovery budget, a subscribed client may retry after a 30-second backoff. Otherwise failure is latched until a sensor-service restart; subscriptions do not create more workers. Settings offers elevated repair/restart. Stop reports failure if cleanup cannot complete within its budget, and the repair helper must confirm the old service process has exited before starting another. If termination cannot be confirmed, keep sensors unavailable and report that restart or reboot is required. Network monitoring stays independent.

## Protocol

Version 1 of its own protocol, with the same framing and four operations as the network service. A snapshot contains:

| Field | Meaning |
| --- | --- |
| `Generation`, `Sequence` | Collector generation and update sequence |
| `UpdatedAt`, `Frequency` | Completed-pass timestamp in system QPC ticks, and ticks per second; pass completion alone does not prove every sensor refreshed |
| `Quality` | Complete, starting, partial or failed |
| `CpuTemperature` | Selected CPU package identity, CPU name, sensor meaning (`Package` or `Tdie`), nullable Celsius, source `PawnIO`, `LastReadAt` in the same QPC clock, and availability/failure reason. One optional reading, not an extensible sensor browser. |

**Acquisition freshness.** The library provider may publish Celsius and advance `LastReadAt` only after characterized acquisition evidence establishes that the selected sensor was read successfully. `Update()` returning by itself is insufficient. Unchanged but successfully read temperatures remain current. A skipped, unsupported or failed acquisition publishes null for that pass without stamping an old value as new. Empty/unsupported results are unavailable, not complete. No retained sensor-value cache is used as proof of acquisition.

There are no configuration-write operations. The service never accepts a register address, module, path, command or hardware write from a client, and has no self-update request.

## Security

- **Account allow-list.** Sensor data covers the whole machine and is not filtered per user. Readers still need an entry in the allow-list, provisioned at setup like network monitoring. A listed user can write their own pipe client; that is accepted, because the data is read-only and not personal.
- **The rest matches the network service:** pipe and SCM permissions, server verification, protected files, quoted executable path, no loading code from user folders, and bounded requests and rates.
- **Third-party code and modules.** LibreHardwareMonitor runs only in the privileged service; its embedded modules execute through PawnIO. Each package or driver update gets the same review as service code. Pin the NuGet graph and signed installer payload; do not load packages or modules from user-writable paths.
- **No WinRing0.** No alternative driver fallback is permitted.

## Coexistence with other tools

LibreHardwareMonitor owns its hardware-access coordination. CAM, iCUE and MSI Center ran on the spike desktop at the same time. Acceptance includes running alongside them without errors, wrong readings or effects on those tools, and checking how library values behave after a lock timeout or read failure. WinMon must not bypass the library's coordination.

## Data model and UI

`ResourceSample` gains an optional immutable CPU-temperature reading containing package identity, hardware name, sensor meaning, source and nullable Celsius. The client validates protocol metadata before constructing it: old generations, starting/failed quality, or a pass or acquisition older than 5 seconds become null. Reject future timestamps and invalid clock frequencies. An unavailable reading never contributes to a peak. Unchanged but successfully reacquired readings remain current.

Only absolute CPU temperature crosses this protocol: finite and within 1–150 °C. The service validates it, and the client repeats validation before display or peaks. The °C/°F preference affects display only.

**CPU gauge.** The secondary line shows the CPU package temperature after the clock, as in `5.42 GHz · 68 °C`, only when this source supplies one.
- **Selection:** use the characterized library sensor for the detected CPU. Initial support is single-package systems only. Identity is fixed for the collector generation. If unavailable, show null. Re-evaluate on a new generation, resetting peak history.
- **Labelling:** the tooltip and accessible name preserve the library's actual sensor meaning, followed by "from LibreHardwareMonitor". Never rename a control, CCD, core or ACPI temperature to package temperature.

**CPU tooltip** gains the die/package reading, source and ten-minute peak. Peaks follow package identity and collector generation as in the temperature note. Fans and motherboard readings are deferred.

All text goes in `Strings.resx` and the nine translations. Settings shows PawnIO and service status.

## Performance

Measure, before and after, as [DEVELOPMENT.md](../DEVELOPMENT.md#performance) describes:
- the service's CPU time, working set and `Update()` duration at the default interval;
- WinMon's added cost per snapshot.

Record both in [performance history](../PERFORMANCE-HISTORY.md). WinMon's sampling path must not wait on the service; it uses the latest snapshot or null.

## Verify before building

1. **Package version.** Pin stable LibreHardwareMonitorLib 0.9.6 and its resolved NuGet graph. Review the corresponding source and required PawnIO version; do not substitute master or a prerelease.
2. **PawnIO admin rights.** Whether PawnIO accepts a non-admin caller. This decides whether a narrower identity than LocalSystem is possible.
3. **AMD, Intel and Memory integrity.** Validate the selected LibreHardwareMonitor CPU reading on the spike desktop's Ryzen 9 9950X3D (family 1Ah) and package acquisition on the Core Ultra 9 275HX laptop. Record CPU family/model, sensor identities and meanings, LibreHardwareMonitor/PawnIO versions and Memory integrity state for each run.
4. **Installer switches.** PawnIO's silent-install and uninstall switches, its exit codes, and whether it reports success when the driver load is blocked. Start by checking the `-install -silent` invocation used by Tray-Temps against the exact official installer selected for WinMon; verify cancellation and reboot-required outcomes too.
5. **Licensing:**
   - The PawnIO driver is GPL-2.0, with an exception for programs that use only its device interface.
   - LibreHardwareMonitorLib is MPL-2.0; its embedded PawnIO modules carry separate LGPL-2.1 notices.
   - PawnIO's signed binaries are proprietary, but the installer may be redistributed.

   WinMon is MIT. Redistributing the package, its embedded modules and PawnIO's installer needs the corresponding notices and licensing check.
6. **Coexistence.** Run with CAM, iCUE and MSI Center at the same time, as on the spike desktop.
7. **Acquisition validity.** Exercise bus-lock contention, unavailable library values and failed reads. Prove that an unsuccessful acquisition cannot receive a new `LastReadAt`, and that unchanged successful reads remain current.

A small LibreHardwareMonitor console prototype investigates these points before service work. Run it unelevated and elevated on both initial acceptance machines, recording versions, CPU identity and Memory integrity state. Print the sensor meaning, nullable Celsius and acquisition failure reason, and compare idle/load readings with CAM or another established monitor. Installer behavior needs separate setup runs, including blocked-driver and reboot-required cases; a successful sensor read alone does not verify those outcomes. Do not install a driver as a side effect of running the prototype.

**Gate:** both CPU paths must work with the pinned stable NuGet package before service implementation starts. If native hardware, driver access or correct sensor semantics cannot be verified, leave the prototype and tests reviewable and record the specific blocker; do not claim that mocked reads establish hardware support or replace the library with a fork.

## Delivery and acceptance

1. Prototype and verification results recorded in this note.
2. Shared service infrastructure generalized, with the network service's tests still passing unchanged.
3. The sensor service, protocol and setup, including PawnIO installation.
4. Client, Settings, gauge and tooltip presentation, strings and translations.
5. Documentation updates: README, user guide, DEVELOPMENT.md, packaging and release docs.

Acceptance requires:
- Unit tests for CPU-only category selection, nullable value validation, stable sensor identity, unambiguous package selection and open/update/close ownership; then protocol, generations, quality, freshness, the watchdog and client null handling. Cover failed reads, unchanged successful acquisitions, expired readings, unsupported CPU identities and generation changes without carrying peaks across identities.
- Lifecycle tests for hangs in open, update and close, late completions, responsive status/snapshot requests during failure, bounded shutdown, and repeated subscriptions producing no replacement worker until cleanup or process exit is confirmed.
- Setup tests:
  - PawnIO absent, current, older and blocked;
  - consent declined;
  - a failure part-way through leaving nothing half-installed;
  - removal leaving PawnIO and the network service in place.
- A smoke mode with the sensor section present, absent and stale, in Dark and Light, in Full and Compact views.
- `./tools/Verify.ps1` and `./tools/Verify.ps1 -Store`. The Store build must compile with the feature hidden.
- Native acceptance on the spike desktop, and on an Intel machine.

## What landed and departures

Implemented: [the CPU prototype](../../tools/CpuTemperatureProbe.cs), its [project](../../tools/CpuTemperatureProbe/CpuTemperatureProbe.csproj), a locked **LibreHardwareMonitorLib 0.9.6** dependency and pure sensor-validation/selection tests. The direct PawnIO register reader and its decoders were replaced following the decision to use LibreHardwareMonitor from the start. The ordinary WinMon app and existing services have no new hardware-library dependency.

Run after separately installing official PawnIO:

```powershell
dotnet run --project tools/CpuTemperatureProbe -- --samples 6
```

A standalone SDK-free prototype is published at `artifacts/temperature-implementation/cpu-prototype-libre/CpuTemperatureProbe.exe`; use `--samples 1..600` (default 6, one second apart). It prints JSON with CPU/sensor identities, nullable Celsius, source version and a package candidate when selection is unambiguous. A candidate preserves the library's sensor meaning. It explicitly does not claim independent native freshness. No external module directory or PawnIOLib DLL is required.

Exit codes: 0 = at least one plausible library temperature observed; 2 = unsupported process/platform or invalid arguments; 3 = PawnIO not registered; 4 = acquisition failed or no plausible temperature. Exit 0 alone does not clear hardware acceptance.

Verified: package restore/build, standalone publish, deterministic value/identity/ambiguity tests, and the expected missing-PawnIO diagnostic on this development machine. No driver was installed. Temperature accuracy, retained-value behavior on failed reads, coexistence, HVCI and installer behavior remain unverified.

Blocked: PawnIO is absent on the available development machine, and the Intel laptop is not accessible here. The existing native gate remains in force before service work. Shared service refactoring, service/protocol/client, setup/repair/removal and CPU gauge integration are **not started**. CPU sensors are the initial scope; fans and other categories can later use the same library provider. Driver installation still uses the explicit consent flow above.
