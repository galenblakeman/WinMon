# WinMon

**See what your PC is doing. Find what's slowing it down.**

WinMon brings CPU, memory, disk, network and GPU activity into one Windows dashboard. Spot a spike, find the busiest processes and services, and look back at what was happening when it occurred.

**Windows x64 · No separate .NET installation · Installer or portable ZIP · MIT licensed**

[**Download latest release**](https://github.com/galenblakeman/WinMon/releases/latest) · [User guide](docs/USER-GUIDE.md) · [Screenshot gallery](docs/example/README.md) · [Report an issue](https://github.com/galenblakeman/WinMon/issues)

![WinMon dashboard with live resource gauges, a recent activity spike and individual process rankings](docs/example/example_individual.png)

*Actual WinMon UI with illustrative data. [Browse the gallery](docs/example/README.md) for more views and capture details.*

## Download and requirements

[Download the latest release](https://github.com/galenblakeman/WinMon/releases/latest), then choose:

| Download | Get started |
| --- | --- |
| **Installer** | Install for your Windows account, then launch from the Start menu. It includes `WinMon.Cli.exe`; its unchecked setup option adds that command to your user PATH. |
| **Portable ZIP** | Extract into a writable folder and run `WinMon.exe` or `WinMon.Cli.exe`. Preferences stay beside the desktop app. |
| **CLI ZIP** | Extract `WinMon-Cli-<version>-win-x64.zip` and run `WinMon.Cli.exe` from a terminal. |

WinMon ships as a self-contained **Windows x64** app, so you do not need to install .NET separately. The desktop installer accepts Windows 10 version 1607 (build 14393) or later. This packaging minimum is not a compatibility test: use a Windows edition and version supported by [.NET 10](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md). Older-Windows acceptance has not been verified for this build. See [platform details](docs/USER-GUIDE.md#platform-requirements) for distribution limits and the environment used for this refresh.

Ordinary resource monitoring and connection lists run without administrator rights. Optional per-app network traffic monitoring requires a separately installed service and administrator approval to install it.

## Investigate a spike

1. **Click a spike in the chart** to inspect that recorded moment. WinMon keeps recording while you look back.
2. **Inspect the process rankings** to see which apps were busy. Group related processes, search, or pin an app to follow it. Use the process More menu to open its Connections or Usage.
3. **Choose Copy capture** to share the timestamp, resource readings, displayed process rows and recorded connection details as plain text. Reports use the existing Processes and Connections **Top** selections independently.

![Capture mode showing an earlier CPU spike and Chrome's increased CPU activity](docs/example/example_snapshot.png)

*In this illustrative workload, Chrome's CPU activity rises during the selected spike and falls afterward. Resource readings describe the selected interval; process CPU uses a five-second average. The gauges at the top remain live.*

History defaults to **30 minutes**, with retention configurable up to **60 minutes**, subject to memory limits. Capture mode reports the retained span and selected-moment availability for each activity view. Optional **Automatic captures** mark threshold events and save text reports while you are away, including app receive/send leaders when network traffic was recorded. [Learn about history and captures](docs/USER-GUIDE.md#catch-the-spikeeven-after-it-passes).

## Explore your system

| Feature | What it helps you do |
| --- | --- |
| **Resource charts** | Compare CPU, memory, disk, network and GPU activity on a shared timeline. Choose overlay, small multiples, heatmap, focused resource or logical processor charts. Each view remembers its preferences. |
| **Processes** | Rank by CPU, GPU, memory, I/O, threads or handles. Group processes by executable, search, and keep important apps pinned. |
| **Services** | Follow Windows services, their hosts and observed workers. Inspect resource use or start, stop and restart eligible services with confirmation and elevation where required. |
| **Connections** | See apps with TCP connections and UDP endpoints. Optional per-app traffic monitoring adds receive/send rates, history and observed byte totals. |
| **Usage** | Find which apps consumed CPU time, I/O and GPU time during the session or a recorded period ending at the selected capture, including apps that have exited. |
| **CSV export** | Save retained resource, process, service and connection observations, or session usage totals, for analysis. Separate connection and process-traffic exports preserve endpoints, owners, observed bytes, rates and coverage flags. |
| **Hardware and help** | Open Info for hardware details, feature instructions and a walkthrough that points out controls in the dashboard. |

[See feature examples](docs/example/README.md#investigate) · [Read the user guide](docs/USER-GUIDE.md)

## Command line

Run `WinMon.Cli` for a keyboard-driven monitor with Processes, Services, Connections and Usage tabs, or use `snapshot` for a one-time table or JSON report. The CLI comes with the installer and portable ZIP, plus a CLI-only ZIP; it is not included in the Microsoft Store package.

```powershell
.\WinMon.Cli.exe
.\WinMon.Cli.exe snapshot --sort memory --top 15
```

See the [CLI user guide](docs/USER-GUIDE.md#command-line) for setup, keyboard shortcuts, saved settings, network monitoring and scripting.

## Coding agent diagnostics

The source tree includes `WinMon.Mcp.exe`, a local read-only MCP server for system snapshots, process details, bounded performance trends and connection observations. Desktop packaging includes it alongside the app; a separate Claude Code plugin adds a diagnostics skill. Command lines and executable paths are excluded, and network addresses/ports require explicit opt-in. See [MCP setup and privacy](docs/MCP.md) for building, supported installation routes and Codex/Claude registration.

## Compact view and customization

Switch to **Compact view** to keep resource readings visible in a slim window. Enable **Always on top**, minimize to the notification area, or show and hide WinMon with a configurable shortcut (**Ctrl+Shift+M** by default).

![WinMon Compact view with live CPU, memory, disk, network and GPU readings](docs/example/example_compact.png)

Choose light, dark or additional appearances, independent chart palettes and seven gauge options, including None. Settings previews the combination before you save. A Colorblind palette adds distinct line patterns. The interface is available in ten languages.

[Compare appearances and palettes](docs/example/README.md#customize) · [Customize WinMon](docs/USER-GUIDE.md#make-it-yours)

## Permissions and limitations

- **Optional network service:** Signed desktop builds can install per-app traffic monitoring through the installer or Settings → Network. Installation and maintenance can request administrator approval. Microsoft Store builds do not offer this service. Endpoint lists remain available without it.
- **Measurement limits:** Disk percentages measure active time; process I/O includes files, network and devices. GPU utilization reports the busiest engine. Very short-lived processes and collection gaps can leave activity unattributed or incomplete. Machines with multiple active processor groups are unsupported.
- **History:** Available history can be shorter on busy systems. Resource and process history can optionally survive normal restarts; service and connection history start fresh. Usage tracks the current session, with continuity across in-app updates.
- **Local data:** Preferences, captures, exports and diagnostic logs stay on this device unless you share them. Exports can contain process paths, service names and network addresses. Startup also removes abandoned or finished update staging folders left untouched for seven days by deleted app copies. See [Privacy](docs/PRIVACY.md).
- **Process and service actions:** Ending a process can lose unsaved work. Service changes can affect other applications. WinMon confirms destructive actions and respects Windows permissions.

WinMon samples once per second and skips drawing while hidden or minimized. A November 9 sample of WinMon 4.0.6 averaged **0.25% CPU** and **162.5 MiB working set** on a 12-thread desktop. This was one 150-second run with history still filling and optional per-app network monitoring off. Overhead varies with hardware, process count, history and enabled features; see the [measurement conditions](docs/PERFORMANCE-HISTORY.md#readme-refresh-profile-november-9-2026) and [profiling method](docs/DEVELOPMENT.md#performance).

## Documentation and source build

- [User guide](docs/USER-GUIDE.md): settings, measurements, captures, exports and updates.
- [Screenshot gallery](docs/example/README.md): examples grouped by task and appearance.
- [Developer guide](docs/DEVELOPMENT.md): architecture, tests, diagnostics and performance.
- [Updates](docs/UPDATING.md), [code signing](docs/CODE_SIGNING.md) and [release packaging](packaging/RELEASING.md).

Development uses **.NET 10**, with SDK **10.0.402** pinned in [global.json](global.json) and latest-patch roll-forward.

```powershell
dotnet run --project src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

To publish a self-contained executable:

```powershell
dotnet publish src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

Output: `src/WinMon/bin/Release/net10.0-windows/win-x64/publish/WinMon.exe`.

WinMon is built with C# and WPF and released under the [MIT License](LICENSE). Official executable and installer releases use Azure Artifact Signing with publisher Galen Blakeman; see the [code signing policy](docs/CODE_SIGNING.md) for verification.

[Report a problem or suggest an improvement](https://github.com/galenblakeman/WinMon/issues).

Copyright © 2026 Galen Blakeman.
