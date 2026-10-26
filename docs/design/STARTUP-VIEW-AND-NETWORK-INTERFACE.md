# Startup view and network interface settings

## Intent

Let people choose the view WinMon opens in and the network adapter it measures, without changing the existing defaults.

## Behavior

- General → Startup view offers Last used, Full and Compact. Last used remains the default. A fixed choice applies when the window loads; the title bar can still switch views, and the last actual view continues to be saved.
- Monitoring → Network interface offers Automatic and the Ethernet and Wi-Fi adapters Windows reports, including disconnected ones. Automatic shows the adapter name currently being measured when known and selects another connected adapter in the same sample when that one disappears. Choices are stored by adapter ID so a rename does not change the selection.
- A manually selected adapter never falls back to another. If absent or disconnected, the network metric is unavailable. When the selection changes, rate and since-start totals start with a fresh baseline.

## Implementation

`MonitorPreferences` stores `StartupView` and `NetworkInterfaceId`. `MainWindow` resolves the startup view after loading preferences and sends the selected adapter ID with each sampling option. Settings discovers interfaces after its first render, preserving a saved ID even when it is missing from the current list. The Windows sampler maintains the automatic policy and matches manual IDs among connected candidates.

## Landed result and departures

Implemented in `src/WinMon/Preferences/`, `src/WinMon/MainWindow.xaml.cs`, `src/MonCore/Monitoring/MonitorSession.cs`, and `src/MonCore.Windows/Monitoring/`. The behavior follows the design above. Adapter discovery is limited to Ethernet and Wi-Fi, matching the existing sampler; other interface types are not offered.
