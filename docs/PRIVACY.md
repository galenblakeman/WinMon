# WinMon privacy policy

WinMon monitors resources on your Windows PC. The application has no analytics, advertising, or automatic diagnostic uploads.

When automatic update checks are enabled (the default), WinMon periodically requests public release metadata and a signed update manifest from its GitHub Releases page. The requests include no account, hardware, monitoring, process, preference, or diagnostic data. Turn off automatic checks in Settings; manually checking or selecting Update Now sends the same release requests and downloads only the chosen update asset.

GitHub and its download infrastructure receive normal connection information, including your IP address. The request's User-Agent identifies WinMon and its version.

## Data on your PC

Administrator-elevated WinMon/helper launches use administrator/System-protected logs beneath `%PROGRAMDATA%\WinMon\Logs\Elevated\<Windows SID>` instead of user-writable app/fallback locations. These have the same 10 MiB limit per active app set and are readable by administrators/System.

Diagnostic logs are always collected locally. They record startup/version information, errors, network-service connectivity and recovery, and shutdown. App logs prefer `%PROGRAMDATA%\WinMon\Logs\App\<Windows SID>` and fall back to `%LOCALAPPDATA%\WinMon\Logs`; the service uses `%PROGRAMDATA%\WinMon\Logs\Service`. Each active app set and the shared service set retain up to 10 MiB each through rotation. Other accounts and inactive fallback folders have separate files. Logs persist across launches and service removal; you may delete closed log files manually. Info → About → Open logs folder opens the active app location.

Log entries omit free-form string arguments, exception messages and source file paths. They retain timestamps, random launch identifiers, event labels, safe numeric/version/state values, exception types, error codes and method stacks. Monitored process names, executable paths, endpoints, command lines, setup tokens and sampled monitoring data are excluded. The app folder name identifies its Windows account SID. That account and administrators/System can access its logs; provisioned monitoring accounts can read service logs but cannot write them. Service-log read permission remains after monitoring authorization is revoked. Logs are never uploaded automatically.

WinMon reads local resource counters, process names and executable paths, and hardware information through Windows APIs and local WMI queries. Network charts read adapter counters; they do not inspect packet contents. Hardware information can include identifiers such as motherboard serial numbers.

Connections reads local TCP/UDP endpoint metadata through Windows APIs, including process IDs, addresses, ports and connection states. Endpoint observations and available process traffic are retained in bounded memory for Capture, even while another tab is visible. They are included in saved restart history when that history is written. Optional per-process network monitoring is off by default. When enabled in a supported desktop build, a separately installed Windows service uses ETW to observe system-wide process/network event metadata and maintain bounded in-memory byte counters and delayed rates. It does not inspect packet contents or write packet captures/ETL files. The authenticated local connection returns only process traffic owned by the provisioned Windows account. Captures and CSV exports can contain observed traffic totals and endpoints; review them before sharing. The separate MCP traffic tool subscribes on demand to an installed service and returns process names, lifetime identities, traffic totals, rates and quality, without paths or endpoints.

Provisioning stores authorized Windows account SIDs and an owned-file inventory in the administrator-protected `%ProgramFiles%\WinMon\NetworkMonitor` directory, and registers the shared service and its independent uninstaller. Disabling monitoring releases only that app's subscription; another authorized app can continue collection. An administrator can revoke one provisioned account through the installed setup helper; this interrupts collection while applying the change and preserves the other accounts. Revoking the final account removes the service. The idle service otherwise remains installed until explicitly removed through Settings or Windows Installed apps. Removal affects all users and removes the service's authorization data independently of ordinary WinMon settings.

Resource and process history stays in memory for the current session by default. During an in-app update, WinMon writes that history, including process names, executable paths, service details, network endpoints, observed traffic and period Usage, to the update folder under `%LOCALAPPDATA%\WinMon\Updates` so the new version can continue it, and deletes the file as soon as the new version has read it. If you enable **Keep history across restarts**, WinMon also writes recent samples, including process names, executable paths, measurements, service details, network endpoints, observed traffic and period Usage, to `history.bin` beside settings on a normal exit or Windows sign-out. It reads and deletes that file at the next launch, keeps only samples within your selected retention period, and deletes it when you disable the setting. A failed or interrupted save may leave the previous complete snapshot until it expires or is read. Preferences are stored in `%LOCALAPPDATA%\WinMon\settings.json`, or beside the executable when `portable.flag` is present. Saved pins include process IDs, creation times, names, executable paths, display names, grouping, and app classification, but no sampled resource readings. Uninstalling preserves preferences; delete the settings file and `history.bin` separately if you want to remove them.

Diagnostic and performance command-line modes write reports and, for some modes, screenshots to the output folder you supply. These files can contain process and system details. Choose a local folder if you want to keep them off shared storage, and review them before sharing.

When you turn on automatic captures (off by default), WinMon writes capture reports containing process names, IDs, executable paths and measurements to the folder you choose, `%LOCALAPPDATA%\WinMon\Captures` by default. It keeps the newest 200 and deletes only files it wrote, identified by their name and first line. Nothing is uploaded.

When you choose Export, WinMon saves a CSV file in the location you select. Depending on the dataset, it can contain resource readings, process names, IDs, executable paths, publishers and Usage totals. The export remains on your PC unless you share it.

## Actions you choose

The Info window copies hardware information to the Windows clipboard only when you select Copy. The snapshot card copies selected historical resource readings and displayed process names, IDs and measurements and the active Services or Usage context only when you select Copy capture. Windows clipboard history or synchronization, if enabled, is controlled by your Windows settings. Review the report before pasting it into an issue or message.

Connections copies the selected process/executable and endpoint details only when you select Copy details. When optional per-process monitoring is enabled, those details also include the displayed owner/executable rates, observed totals and quality status. Clipboard history and synchronization follow your Windows settings.

Selecting the source-code link opens GitHub in your default browser. Downloads, issues, and any information you choose to submit there are covered by [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). WinMon does not send a hardware report with that link.

Startup at sign-in and the optional launch shortcut are enabled through settings. Ending a process requires your confirmation and can discard unsaved work. The installer displays its destination and shortcut choices and provides an uninstaller.

## Components and questions

The bundled .NET runtime and System.Management library support local monitoring; WinMon does not configure a third-party telemetry service. Windows services, browser behavior, cloud-synced folders, and clipboard synchronization follow their own settings and policies.

Ask privacy questions through [WinMon issues](https://github.com/galenblakeman/WinMon/issues). Do not include private system reports in a public issue.
