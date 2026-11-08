# Installer network monitoring

Signed desktop installers offer an unchecked option to install and enable per-process network monitoring. Keep the ordinary per-user installation unelevated; only the existing signed network setup broker requests administrator approval. Unsigned installers and Store packages omit the option.

After copying files, the installer waits for `WinMon.exe --install-network-service` under the original installer user. The command acquires the normal app instance lock, uses the existing controller to reuse or provision a service, and saves the enabled preference only on success. It exits without opening the monitor. The existing broker verifies service health before committing and preserves its rollback behavior. The task is not automatically selected again on upgrades.

If setup fails or UAC is cancelled, WinMon remains installed and the installer directs the user to Settings → Network. Silent installs log this failure. Service removal remains independent of app uninstall.

## Landed and departures

Implemented in `packaging/WinMon.iss`, `tools/New-Release.ps1` and `src/WinMon/App.xaml.cs`, with controller/preference and packaging-gate checks. No new service installer or signing path was added. Native signed installer/UAC acceptance remains required; unit tests cannot establish it.
