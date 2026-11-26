# Optional CPU temperature

Date: November 26, 2026. Status: separate LibreHardwareMonitor CPU service, signed setup path and app integration implemented; automated verification completed. The user approved implementing the service before AMD/Intel hardware acceptance on November 25. See [Validation](#validation) and [What landed and departures](#what-landed-and-departures).

## Purpose and scope

Show real CPU die/package temperature, which [CPU and GPU temperature](TEMPERATURE.md) cannot provide without kernel access. Use the official stable **LibreHardwareMonitorLib 0.9.6 NuGet package** inside a separate, optional, privileged Windows service. WinMon itself stays unelevated. LibreHardwareMonitor owns hardware identification, register access and decoding; WinMon owns sensor selection, validation, freshness, authorization and presentation. Enable CPU hardware first. Fans and other sensors can later use the same provider without replacing its acquisition stack.

| Area | Decision |
| --- | --- |
| Default | Disabled. Ordinary monitoring installs no service or driver. |
| Hosting | Its own service, `WinMonSensorMonitor`, separate from `WinMonNetworkMonitor`. It reuses the network service's setup, pipe and verification code by generalizing it, not copying it. |
| Driver | Official PawnIO must already be installed separately. Signed WinMon setup provisions only its optional sensor service; removal leaves PawnIO installed. |
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

- **Version and boundary:** reference [LibreHardwareMonitorLib 0.9.6](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6) only from the hardware provider/service, the service and its isolated provider probe. Keep it out of MonCore, the WPF process, CLI and MCP. Commit the package lock file. No prerelease, library fork, direct register decoder or WinRing0 fallback.
- **Lifecycle:** one owning worker creates `Computer` with `IsCpuEnabled = true` and every other category disabled. It opens once, calls `Update()` before reading CPU temperature sensors and closes in `finally`. Updates, resets and close never overlap. See the [exact-version lifecycle source](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Hardware/Computer.cs).
- **Identity and selection:** preserve hardware identifier, sensor identifier and the actual sensor name. Prefer a characterized `CPU Package`, `Core (Tdie)`, `Core (Tctl/Tdie)` or `Core (Tctl)` reading, in that order; never relabel the latter meanings as Tdie. Reject ambiguous duplicate meanings and multiple packages until explicit package selection exists. A hottest core is not a package fallback.
- **Driver:** 0.9.6 embeds its PawnIO modules and uses the installed PawnIO device directly; it does not require WinMon to load external module files or PawnIOLib. The probe checks the library's installed-PawnIO indication before opening hardware. Registry presence alone does not prove driver access. See the [exact-version PawnIO implementation](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/PawnIo/PawnIo.cs).
- **Future sensors:** add explicitly enabled categories and typed sensor projections behind this same provider when fans or other readings are approved. Do not enable motherboard/controller/storage scanning during the CPU-only phase. Never expose library control/write APIs over the pipe.

**Acquisition contract:** `Update()` returning and a retained non-null `ISensor.Value` alone are insufficient. The service resets public Min/Max before each pass and accepts only a valid value that the library assigned during that pass, including unchanged temperatures. It stamps the confirmed library observation in QPC time; this is not an independent native-accuracy guarantee. Physical lock-contention, driver-failure and sensor-accuracy behavior remain native acceptance work.

## Shared infrastructure

Generalize these parts of the network service so both services use them, each keyed by a service descriptor (service name, pipe name, install folder, executable name, protocol version):

- signed setup payload verification and transactional installation;
- the protected install folder and allow-list checks;
- the length-prefixed protocol framing and limits;
- SCM registration checks, pipe-server verification and impersonation-based authorization;
- leases with a reconnect grace period.

Network-specific code (ETW, ledger, ownership probes) stays in the network service. The sensor service gets its own install folder, `%ProgramFiles%\WinMon\SensorMonitor`, and pipe, `WinMonSensorMonitor.v1`.

## Setup and PawnIO

Settings has a **CPU temperature** section. Monitoring defaults off. Its note explains that the optional service needs administrator approval and official PawnIO must be installed separately. This delivery does not bundle or execute a PawnIO installer.

1. **Check without elevation.** Connecting checks the fixed registered sensor service, starts it with provisioned SCM rights, connects to its local pipe and verifies its LocalSystem process identity.
2. **Explicit setup.** Enabling or repairing can launch the signed sensor helper with UAC. Restoring a saved preference only reconnects/retries an installed service; it never silently requests elevation.
3. **Driver prerequisite.** Sensor installation checks the official PawnIO registration before provisioning. Registration alone does not prove driver load or hardware support. Missing, blocked or inaccessible acquisition remains unavailable. Removal remains possible without PawnIO.
4. **Signed payload.** The helper, sensor ZIP, versioned manifest and RSA signature use fixed sensor-specific names and the existing trusted release key. Hash, size, Authenticode, requester identity and protected-path checks reuse network setup. Unsigned development builds cannot provision services.
5. **Transactional setup.** Install/repair health-checks the service before the app commits the transaction. Failure or cancellation rolls back the sensor installation. Its policy, protected directory and independent Installed apps registration do not replace network monitoring.
6. **Independent removal.** Remove shuts down and removes only the sensor service and owned files/policy/registration. It leaves PawnIO, network monitoring and other tools alone. A close/process-exit failure reports restart required instead of starting another collector over live handles.

Automatic PawnIO installation is a later setup enhancement requiring a selected signed upstream installer and separate acceptance; it is not implied by a successful library read.

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
| `Quality` | Idle, starting, complete, partial, failed or suspended |
| `CpuTemperature` | Selected CPU package identity, CPU name, actual library sensor name, nullable Celsius, source `LibreHardwareMonitorLib 0.9.6`, `LastReadAt` in the same QPC clock, and availability/failure reason. One optional reading, not an extensible sensor browser. |

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

`ResourceSample` gains an optional immutable CPU-temperature reading containing package identity, hardware name, sensor meaning, source and valid Celsius; unavailable readings are null. The client validates protocol metadata before constructing it: old generations, starting/failed quality, or a pass or acquisition older than 5 seconds become null. Reject future timestamps and invalid clock frequencies. An unavailable reading never contributes to a peak. Unchanged but successfully reacquired readings remain current.

Only absolute CPU temperature crosses this protocol: finite and within 1–150 °C. The service validates it, and the client repeats validation before display or peaks. The °C/°F preference affects display only.

**CPU gauge.** The secondary line shows the CPU package temperature after the clock, as in `5.42 GHz · 68 °C`, only when this source supplies one.
- **Selection:** use the characterized library sensor for the detected CPU. Initial support is single-package systems only. Identity is fixed for the collector generation. If unavailable, show null. Re-evaluate on a new generation, resetting peak history.
- **Labelling:** the tooltip and accessible name preserve the library's actual sensor meaning, followed by "from LibreHardwareMonitor". Never rename a control, CCD, core or ACPI temperature to package temperature.

**CPU tooltip** gains the die/package reading, source and ten-minute peak. Peaks follow package identity and collector generation as in the temperature note. Fans and motherboard readings are deferred.

All text goes in `Strings.resx` and the nine translations. Settings reports service availability and explains the separately installed PawnIO prerequisite; the provider probe records the registered driver version.

## Performance

Measure, before and after, as [DEVELOPMENT.md](../DEVELOPMENT.md#performance) describes:
- the service's CPU time, working set and `Update()` duration at the default interval;
- WinMon's added cost per snapshot.

Record both in [performance history](../PERFORMANCE-HISTORY.md). WinMon's sampling path must not wait on the service; it uses the latest snapshot or null.

## Validation

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

The provider probe exercises the same LibreHardwareMonitor provider as the service for these checks. Run it unelevated and elevated on both initial acceptance machines, recording versions, CPU identity and Memory integrity state. Print the sensor meaning, nullable Celsius and acquisition failure reason, and compare idle/load readings with CAM or another established monitor. Installer behavior needs separate setup runs, including blocked-driver and reboot-required cases; a successful sensor read alone does not verify those outcomes. Do not install a driver as a side effect of running the prototype.

**Implementation decision (November 25):** the user approved implementing the separate service before AMD/Intel physical hardware validation. The former pre-implementation gate is superseded. Accurate readings, HVCI, coexistence and signed installation acceptance remain release acceptance work; fake tests and a VM with unavailable sensors do not establish those outcomes.

## Delivery and acceptance

1. Prototype and verification results recorded in this note.
2. Shared service infrastructure generalized, with the network service's tests still passing unchanged.
3. The sensor service, protocol and setup, requiring separately installed official PawnIO.
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

Implemented in phase 2:

- `WinMon.SensorService`: CPU-only stable LibreHardwareMonitorLib 0.9.6 provider and locked graph, with one selected sensor identity per generation. Public sensor extrema are reset before updates so a retained getter alone cannot masquerade as a new library assignment. This proves a library assignment, not independent native sensor accuracy.
- Serialized acquisition, two-second updates, leases with two-second idle grace, responsive status during native hangs, ten-second watchdog/shutdown/recovery budgets and restart-required failure latching when safe cleanup cannot be established.
- Shared protected pipe creation/authentication, bounded framing, policy/process DACL and descriptor-based service verification. Existing network regression tests remain part of acceptance.
- `WinMon.SensorSetup` reuses hardened setup sources, transactions and signed-package verification with sensor-specific paths/registration. Release packaging embeds a separately signed sensor bundle; unsigned development and Store builds cannot install it.
- A cached nonblocking app client, opt-in Settings with status/repair/removal, CPU gauge/details/peaks in Full and Compact, translated strings and unavailable/stale handling. Portable core carries only immutable readings; Libre stays outside WPF.
- CPU service readings are omitted from persisted incidents and restart history; both format versions are unchanged. The provider probe exercises the actual service provider and reports update cost and working set.

Phase 1 is committed as `81bac70`. Phase 2 implements the separate CPU service and app integration. Full test projects passed 11,327 desktop and 11,158 Store tests, with zero failures and repository-allowlisted skips. Final Gauge/Compact/Capture galleries passed and the CPU states and Settings were reviewed. Diagnostic-only fixes increased the Capture gallery budget to 90 seconds and render client content when native activation is denied. Eight UI performance pairs and four native sampler pairs are recorded in [performance history](../PERFORMANCE-HISTORY.md); detailed commands, interim failures and final evidence are in `artifacts/cpu-service/verification.md`.

Native observation: this VM has registered PawnIO 2.1.0.0. The earlier library probe identified AMD Ryzen 9 9950X3D and `Core (Tctl/Tdie)` but returned null Celsius. Six observations through the exact service provider also returned null (exit 4). Service and probe self-contained publishes pass, with pinned library/module notices present. NativeAOT setup publish is blocked by the incomplete Desktop C++ workload/Windows SDK environment. No service or driver was installed or changed. Physical AMD/Intel accuracy, HVCI/coexistence, real idle/load behavior and signed clean-machine setup acceptance remain unverified.

Departures: PawnIO is separately installed rather than automatically bundled. The user removed the native-before-implementation gate; native acceptance still precedes claims of supported physical hardware. Fan/motherboard categories remain disabled and can extend this provider later.
