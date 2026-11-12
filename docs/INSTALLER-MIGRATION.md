# Moving from bundled WinMon to separate installers

Desktop, CLI and MCP now have separate installers and portable downloads. Desktop updates itself; CLI and MCP offer explicit `update check` and `update install` terminal commands using their own signed metadata and payloads. Installed copies launch their own installer, while portable copies download a verified ZIP for manual extraction. No desktop installation is required for either tool.

## Before upgrading

Download the desired products from the [release page](https://github.com/galenblakeman/WinMon/releases/latest). The first desktop upgrade from a bundled version must be manual: old automatic updaters expect companion executables and reject the new release metadata. Do not uninstall desktop first.

Install CLI and MCP into their separate default directories, `%LOCALAPPDATA%\Programs\WinMon CLI` and `%LOCALAPPDATA%\Programs\WinMon MCP`. Do not select the existing desktop directory. Desktop keeps `%LOCALAPPDATA%\Programs\WinMon` and its existing uninstall identity.

## Move the CLI

1. Install the CLI and run its new executable directly to confirm `snapshot` and interactive monitoring work.
2. Select the optional user PATH task if desired. Select legacy PATH cleanup only to remove an old installer-owned entry. User-managed PATH entries are preserved.
3. Open a new terminal and check `Get-Command WinMon.Cli`. If it still resolves the old desktop copy, review your user-managed PATH and any aliases. Update scripts with hardcoded old paths.

CLI preferences remain at `%LOCALAPPDATA%\WinMon\cli-settings.json`; no settings copy or reset is needed. Service installation remains explicit through `WinMon.Cli network install` in a signed build. Installing or removing the CLI does not automatically install or remove the shared service.

## Move MCP registrations

Close active server sessions before upgrading MCP if Windows prevents file replacement. The standalone executable has a stable installed path. Restart agent sessions after an update to use the new version.

The MCP installer has separate unchecked registration tasks for Codex and Claude Code. Select only the clients you want to use. Existing conflicting `winmon` registrations are preserved; migrate them with the client's manual commands so custom settings are retained. Installing MCP does not install either client.

For manual migration, inspect the current registration first and retain any custom arguments or environment settings. Use the client to remove the old user-scoped entry, then add the standalone path:

```powershell
codex mcp get winmon
codex mcp remove winmon
codex mcp add winmon -- "C:\Users\<user>\AppData\Local\Programs\WinMon MCP\WinMon.Mcp.exe"

claude mcp get winmon
claude mcp remove --scope user winmon
claude mcp add --scope user --transport stdio winmon -- "C:\Users\<user>\AppData\Local\Programs\WinMon MCP\WinMon.Mcp.exe"
```

Replace the sample with your actual path. Claude project/local overrides can still select another server; inspect them separately. Do not remove another server merely because it uses the same name. Installer registration acts on the default user configuration; custom client homes require manual setup.

If you use the Claude plugin, update `WINMON_MCP_EXE` to the new absolute path and restart Claude. Do not enable a duplicate direct registration for that same server. Restart selected clients, verify WinMon tool discovery and request a snapshot before removing the old copy. Registration enables diagnostic data access; it does not relax client approvals or WinMon's privacy defaults.

## Upgrade desktop and retire old copies

Run the new desktop installer over the existing installation. It preserves legacy CLI/MCP files for migration, but no longer updates them. For portable desktop, replace from the desktop-only ZIP while preserving `settings.json`, `portable.flag` and other user files; extract standalone tool archives into separate directories.

After verifying all new paths, remove only the known old `WinMon.Cli.exe` and `WinMon.Mcp.exe` files, with their sessions closed, or let the legacy desktop uninstaller clean its recorded files when you eventually remove desktop. Do not delete the whole directory. Desktop uninstall can remove old bundled files and break registrations still pointing there, so migrate registrations first. Standalone product files live outside that ownership.

Uninstall preserves user data and the shared network service. MCP uninstall removes only unchanged registrations owned by that installation; manually created or edited entries remain. If a client is unavailable or cleanup fails, remove any stale registration manually with that client's commands.

Desktop uninstall also removes its own exact legacy installer-owned CLI PATH entry. The standalone CLI's separate PATH ownership is preserved.
