# Devices settings

Requested November 26, 2026. Add a Devices page directly after Monitoring, grouping device targets in dashboard order: primary and optional secondary disk volumes, network adapter, graphics processor. Keep the Network page for per-process network monitoring/service controls and keep the existing sensor settings separate.

## Behavior

- GPU defaults to Automatic, preserving the busiest physical engine across all adapters. Manual selection chooses the busiest engine within that GPU. Process GPU readings continue to cover all GPUs.
- GPU selection controls the live resource gauge, chart, memory and paired temperature/power readings. Existing historical samples retain their original device/readings; switching devices does not rewrite history.
- Native, keyboard-operable selectors have visible labels and full-name tooltips. Disk choices preserve their existing behavior. Network and GPU fields use the page width and keep pending edits while navigating. Save applies all preferences; Cancel leaves them unchanged.
- Available GPUs use model names; identical names also show their hardware identity. A missing selected GPU remains selectable with an explicit recovery message. No alternative GPU is silently substituted.
- Discovery happens on a worker after first render. The automatic GPU's current name refreshes while Devices is visible. Discovery failure gives a close/reopen recovery instruction.

## Identity and sampling

`MonitorPreferences.GpuDeviceId` and `SamplingOptions.GpuDeviceId` are nullable; null means Automatic. Existing settings load Automatic. Settings merge, validation and persistence preserve manual selection. No Windows dependency enters MonCore.

DXGI enumeration supplies descriptions and current LUIDs. D3DKMT opens each adapter and queries physical hardware PnP keys. Persist only the device instance beneath `Enum`, excluding mutable ControlSet prefixes and the `Device Parameters` suffix. Resolve the saved key to current LUID/physical index on selection change or every 30 seconds while manually selected. Automatic avoids discovery on the sampling path. Duplicate hardware keys are ambiguous and are omitted from manual choices; Automatic still works. Unsupported discovery does not interrupt other monitoring.

The native contracts target the existing Windows/.NET 10 application: [physical adapter PnP keys](https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/ns-d3dkmthk-_d3dkmt_query_physical_adapter_pnp_key) require Windows 10 1703 / WDDM 2.2, below the Store variant's Windows 10 1809 target. [D3DKMTQueryAdapterInfo](https://learn.microsoft.com/windows-hardware/drivers/ddi/d3dkmthk/nf-d3dkmthk-d3dkmtqueryadapterinfo) reports NTSTATUS; adapter handles and unmanaged buffers are released on all normal/error paths.

## Implementation and acceptance

Implemented in SettingsWindow, the preference/sampling options pipeline, WindowsGpuSampler and WindowsGpuDevices. Disk validation now navigates to Devices. All ten language resources include the new text. Capture smoke includes Devices in full-height overflow checks and screenshots.

The exact Devices-only commit projection, without the pre-existing sensor work, builds and passes 406 focused desktop tests and 380 focused Store tests. `Devices_GpuDefaultsToAutomaticAndKeepsManualIdentityAcrossDiscoveryAndApply`, `Devices_MissingGpuRemainsSelectedWithRecoveryMessage`, `DevicesGpuSelectionAppliesOnlyOnSave`, `GpuDeviceChoiceRoundTripsAndLegacySettingsDefaultToAutomatic`, `Selection_UsesOnlyChosenAdapterAndDoesNotFilterProcessReadings` and `DeviceIdentity_DropsBootControlSetAndKeepsPhysicalInstance` cover the new behavior. SamplingSeriesCharacterizationTests verifies preference propagation. Capture smoke renders Devices in Dark and Light; both PNGs were inspected, including full-width network/GPU fields and the current-device label. The full working-tree Verify run passes capture (61 renders) and feature (131 renders), but is not green: pre-existing HardwareFans fixture failures and stalled locked-image WPF tests prevent full acceptance. The Store full gate hits the same limitations. Logs and thread stacks are under `artifacts/devices-*`; aggregation measurements are recorded in PERFORMANCE-HISTORY.

Physical multi-GPU/reboot/hot-plug acceptance requires suitable hardware. The current environment exposes ambiguous virtual software adapters; native queries were exercised, but that does not establish physical-driver acceptance.

What landed: the proposed Devices page and GPU Automatic/manual choice. Departure: adapters without a unique persistent hardware key remain available through Automatic only, rather than exposing a manual choice that cannot safely survive a restart.
