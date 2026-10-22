# WinMon privacy policy

WinMon monitors resources on your Windows PC. The application has no analytics, advertising, or automatic diagnostic uploads.

When automatic update checks are enabled (the default), WinMon periodically requests public release metadata and a signed update manifest from its GitHub Releases page. The requests include no account, hardware, monitoring, process, preference, or diagnostic data. Turn off automatic checks in Settings; manually checking or selecting Update Now sends the same release requests and downloads only the chosen update asset.

GitHub and its download infrastructure receive normal connection information, including your IP address. The request's User-Agent identifies WinMon and its version.

## Data on your PC

WinMon reads local resource counters, process names and executable paths, and hardware information through Windows APIs and local WMI queries. Network charts read adapter counters; they do not inspect packet contents. Hardware information can include identifiers such as motherboard serial numbers.

Resource and process history stays in memory for the current session and is cleared on exit. Preferences are stored in `%LOCALAPPDATA%\WinMon\settings.json`, or beside the executable when `portable.flag` is present. Saved pins include process IDs, creation times, names, executable paths, display names, grouping, and app classification, but no sampled resource readings. Uninstalling preserves preferences; delete the settings file separately if you want to remove them.

Diagnostic and performance command-line modes write reports and, for some modes, screenshots to the output folder you supply. These files can contain process and system details. Choose a local folder if you want to keep them off shared storage, and review them before sharing.

## Actions you choose

The Info window copies hardware information to the Windows clipboard only when you select Copy. The snapshot card copies selected historical resource readings and displayed process names, IDs and measurements only when you select Copy snapshot. Windows clipboard history or synchronization, if enabled, is controlled by your Windows settings. Review the report before pasting it into an issue or message.

Selecting the source-code link opens GitHub in your default browser. Downloads, issues, and any information you choose to submit there are covered by [GitHub's privacy statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). WinMon does not send a hardware report with that link.

Startup at sign-in and the optional launch shortcut are enabled through settings. Ending a process requires your confirmation and can discard unsaved work. The installer displays its destination and shortcut choices and provides an uninstaller.

## Components and questions

The bundled .NET runtime and System.Management library support local monitoring; WinMon does not configure a third-party telemetry service. Windows services, browser behavior, cloud-synced folders, and clipboard synchronization follow their own settings and policies.

Ask privacy questions through [WinMon issues](https://github.com/galenblakeman/WinMon/issues). Do not include private system reports in a public issue.
