# Optional hardware sensors

Date: November 25, 2026. Status: proposal. Nothing is implemented. Several facts must be verified before implementation; see [Verify before building](#verify-before-building).

## Purpose and scope

Show the real CPU package temperature and fan speeds, which [CPU and GPU temperature](TEMPERATURE.md) cannot provide without kernel access. Use [LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib/) in a separate, optional, privileged Windows service, on top of the [PawnIO](https://pawnio.eu/) driver. WinMon itself stays unelevated.

| Area | Decision |
| --- | --- |
| Default | Disabled. Ordinary monitoring installs no service or driver. |
| Hosting | Its own service, `WinMonSensorMonitor`, separate from `WinMonNetworkMonitor`. It reuses the network service's setup, pipe and verification code by generalizing it, not copying it. |
| Driver | WinMon's elevated sensor setup installs PawnIO with PawnIO's own signed installer, after explicit consent. WinMon ships no driver of its own. |
| First presentation | CPU package temperature on the CPU gauge, labelled with its source. Fans and motherboard temperatures in the CPU tooltip. |
| Other sources | None. The unprivileged HWiNFO and LibreHardwareMonitor-app readers listed in the temperature note are not planned. |
| Store channel | Unavailable, as for network monitoring. |
| Channels | Installer and portable desktop builds. |

Outside this design: fan control or any hardware write, overclocking, a sensor browser, chart series, History, incident recordings, automatic-capture conditions, CLI and MCP output, and sensor alerts. The data model leaves room for them.

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

- **Contained failures.** LibreHardwareMonitorLib reads low-level buses and chips. A hang, crash or driver error there must not stop network monitoring.
- **Independent updates.** The library needs releases for new CPUs and motherboard chips. Each update is an elevated service update; it should not force one on network-only users.
- **Smaller exposure.** Network-only users never get the library, PawnIO or their attack surface.
- **Independent removal.** Removing sensors leaves network monitoring alone, and the reverse.

The cost is a second service, a second Apps & Features entry and a second admin prompt. A combined setup that installs both services with one prompt is a later option.

## Implementation evidence: Tray-Temps

Source inspection of [Tray-Temps](https://github.com/nmd-113/Tray-Temps) on November 25, 2026 shows another application using the same sensor stack. These are observations of its `master` source, not native acceptance results for WinMon or a dependency selection for this proposal.

- **Library and runtime.** Its [project file](https://github.com/nmd-113/Tray-Temps/blob/master/Tray-Temps.csproj) targets .NET Framework 4.8 and references `LibreHardwareMonitorLib` **0.9.7-pre722**, a prerelease. It also embeds `PawnIO_setup.exe`. WinMon targets .NET 10; choose and pin a supported library version through the verification below rather than adopting that prerelease automatically.
- **Driver setup.** Its [PawnIO setup helper](https://github.com/nmd-113/Tray-Temps/blob/master/Helpers/Infrastructure/PawnIoSetupHelper.cs) pins the bundled installer to **2.2.0**, checks its expected length and SHA-256, and uses `-install -silent`. It declares **2.0** as its minimum compatible PawnIO version. These are useful prototype inputs; they do not establish WinMon's minimum version, installer exit-code behavior or driver-load success. WinMon also requires the Authenticode and authenticated-payload checks described below.
- **Reading temperatures.** Its [monitoring code](https://github.com/nmd-113/Tray-Temps/blob/master/MainForm.cs) opens a LibreHardwareMonitor `Computer` with CPU and GPU categories enabled. A background task updates the selected CPU hardware, and the app reads the selected temperature sensor's nullable `Value`. Automatic selection prefers names containing `Package`, then `Tctl`, then `Core Max`, then the highest-valued temperature sensor; users can override the selection. The hardware access is supplied by LibreHardwareMonitor and PawnIO, rather than a new Windows CPU-temperature API.
- **Elevation.** Its [startup code](https://github.com/nmd-113/Tray-Temps/blob/master/Program.cs) and monitoring code offer to restart the desktop application with the `runas` verb for more complete sensor access. That is evidence of its chosen privilege model, not proof that every PawnIO operation requires administrator rights.

For WinMon the access path remains **unelevated UI → authenticated local pipe → optional sensor service → LibreHardwareMonitorLib → PawnIO → CPU sensors**. Only setup and the service perform privileged work. CPU, motherboard and controller collection stays in that service; GPU collection stays on WinMon's built-in D3DKMT path. Sensor selection follows the explicit vendor rules below, preserving the sensor's meaning instead of treating Tray-Temps' hottest-sensor fallback as a package-temperature reading.

## Shared infrastructure

Generalize these parts of the network service so both services use them, each keyed by a service descriptor (service name, pipe name, install folder, executable name, protocol version):

- signed setup payload verification and transactional installation;
- the protected install folder and allow-list checks;
- the length-prefixed protocol framing and limits;
- SCM registration checks, pipe-server verification and impersonation-based authorization;
- leases with a reconnect grace period.

Network-specific code (ETW, ledger, ownership probes) stays in the network service. The sensor service gets its own install folder, `%ProgramFiles%\WinMon\SensorMonitor`, and pipe, `WinMonSensorMonitor.v1`.

## Setup and PawnIO

Settings gets a **Hardware sensors** toggle with this explanation: "Show CPU temperature and fan speeds. First-time setup installs a privileged Windows service and the PawnIO hardware-access driver, and needs administrator approval."

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
- **Collection.** The first subscription opens a `Computer` with only the CPU, motherboard and controller categories enabled. GPU stays with the built-in [D3DKMT path](TEMPERATURE.md#reading-sources), and storage, memory, network and battery are off.
- **Timer.** `Update()` runs on its own timer, every 2 seconds by default, never on a client request. Each snapshot is the latest completed update.
- **Startup.** Opening can take seconds. Status reports "starting" until the first update completes, and the UI shows the readings as unavailable, not zero.
- **Shutdown.** The last lease ending closes the `Computer` after the same two-second grace period as the network service.
- **Generations.** Each open, and each resume from sleep, starts a new collector generation. Clients discard readings from older generations.
- **Watchdog.** If `Update()` throws, or takes longer than 10 seconds, the generation is marked failed. Snapshots report unavailable, and the service reopens on the next subscription after a backoff. A hung update thread never blocks the pipe.

## Protocol

Version 1 of its own protocol, with the same framing and four operations as the network service. A snapshot contains:

| Field | Meaning |
| --- | --- |
| `Generation`, `Sequence` | Collector generation and update sequence |
| `UpdatedAt` | Monotonic timestamp of the completed update |
| `Quality` | Complete, starting, partial or failed |
| `Sensors[]` | Library `Identifier` (for example `/amdcpu/0/temperature/2`), hardware type and name, sensor type and name, value. Capped at 512 rows. |

There are no write operations. The service never calls the library's control (fan-set) functions, never accepts a path, command or sensor write from a client, and has no self-update request.

## Security

- **Account allow-list.** Sensor data covers the whole machine and is not filtered per user. Readers still need an entry in the allow-list, provisioned at setup like network monitoring. A listed user can write their own pipe client; that is accepted, because the data is read-only and not personal.
- **The rest matches the network service:** pipe and SCM permissions, server verification, protected files, quoted executable path, no loading code from user folders, and bounded requests and rates.
- **Third-party code in a privileged process.** The library and PawnIO module blobs run as LocalSystem. Each library update gets the same review as a service code change. The release pins exact versions and hashes of the library, its dependencies and PawnIO.
- **No WinRing0.** If the chosen library version still bundles or can fall back to WinRing0, it is rejected.

## Coexistence with other tools

The library takes the shared Windows mutexes that monitoring tools use for these buses and chips (ISA bus, SMBus, PCI). CAM, iCUE and MSI Center ran on the spike desktop at the same time. Acceptance includes running alongside them without errors, wrong readings or effects on those tools.

## Data model and UI

`ResourceSample` gains `ExternalSensors`, an immutable list of `(string Source, string Id, string Hardware, string Name, SensorKind Kind, double? Value)`, where `Source` is "LibreHardwareMonitor". The client applies the [freshness and units rules](TEMPERATURE.md#rules-for-any-external-source): old generations, failed quality or a stale `UpdatedAt` become null.

**CPU gauge.** The secondary line shows the CPU package temperature after the clock, as in `5.42 GHz · 68 °C`, only when this source supplies one.
- **Sensor choice per vendor:** AMD "Core (Tctl/Tdie)", falling back to "CPU Package"; Intel "CPU Package". The chosen identifier is fixed per session.
- **Labelling:** the tooltip and accessible name read "CPU package temperature, from LibreHardwareMonitor". This is the case the temperature note's attribution rule allows: a die temperature from an identified source, never an ACPI zone.

**CPU tooltip** gains a "Hardware sensors" section listing fans (RPM) and motherboard temperatures by the library's sensor names, then the source line. Peaks follow sensor identity as in the temperature note.

All text goes in `Strings.resx` and the nine translations. Settings shows PawnIO and service status.

## Performance

Measure, before and after, as [DEVELOPMENT.md](../DEVELOPMENT.md#performance) describes:
- the service's CPU time, working set and `Update()` duration at the default interval;
- WinMon's added cost per snapshot.

Record both in [performance history](../PERFORMANCE-HISTORY.md). WinMon's sampling path must not wait on the service; it uses the latest snapshot or null.

## Verify before building

1. **Library version.** Find the LibreHardwareMonitorLib version that uses PawnIO and contains no WinRing0. Record its dependencies. Tray-Temps' `0.9.7-pre722` is source evidence of an integration, not the version selected for WinMon; verify a supported release against .NET 10.
2. **PawnIO admin rights.** Whether PawnIO accepts a non-admin caller. This decides whether a narrower identity than LocalSystem is possible.
3. **Zen 5 and Memory integrity.** Whether the library reports Tctl/Tdie on the spike desktop's Ryzen 9 9950X3D (family 1Ah), and whether PawnIO loads with Memory integrity on. Record the PawnIO version and the Memory integrity state for each run.
4. **Installer switches.** PawnIO's silent-install and uninstall switches, its exit codes, and whether it reports success when the driver load is blocked. Start by checking the `-install -silent` invocation used by Tray-Temps against the exact official installer selected for WinMon; verify cancellation and reboot-required outcomes too.
5. **Licensing:**
   - LibreHardwareMonitorLib is MPL-2.0.
   - The PawnIO driver is GPL-2.0, with an exception for programs that use only its device interface.
   - PawnIOLib and the modules are LGPL-2.1.
   - PawnIO's signed binaries are proprietary, but the installer may be redistributed.

   WinMon is MIT. Bundling the library and PawnIO's installer needs a licensing check.
6. **Coexistence.** Run with CAM, iCUE and MSI Center at the same time, as on the spike desktop.

A small console prototype on the spike desktop can investigate points 1 to 4 before any service work. Run it unelevated and elevated, recording the library and PawnIO versions and Memory integrity state for each run. It would reference the library and print sensor identifiers, names and values for CPU temperatures and fans. Compare its readings with CAM. Installer behavior needs separate setup runs, including blocked-driver and reboot-required cases; a successful sensor read alone does not verify those outcomes.

## Delivery and acceptance

1. Prototype and verification results recorded in this note.
2. Shared service infrastructure generalized, with the network service's tests still passing unchanged.
3. The sensor service, protocol and setup, including PawnIO installation.
4. Client, Settings, gauge and tooltip presentation, strings and translations.
5. Documentation updates: README, user guide, DEVELOPMENT.md, packaging and release docs.

Acceptance requires:
- Unit tests for the protocol, generations, quality, freshness, the watchdog, sensor choice per vendor and the client's null handling.
- Setup tests:
  - PawnIO absent, current, older and blocked;
  - consent declined;
  - a failure part-way through leaving nothing half-installed;
  - removal leaving PawnIO and the network service in place.
- A smoke mode with the sensor section present, absent and stale, in Dark and Light, in Full and Compact views.
- `./tools/Verify.ps1` and `./tools/Verify.ps1 -Store`. The Store build must compile with the feature hidden.
- Native acceptance on the spike desktop, and on an Intel machine.

## What landed and departures

Not implemented yet. This section will record what shipped and where it departed from the design.
