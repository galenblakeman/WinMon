# Services settings

Requested November 26, 2026. Implement the second displayed mockup: a compact page with two service groups separated by horizontal rules. Replace the separate Sensors and Network sidebar entries with Services, directly after Devices.

Hardware sensors retains its enable control, prerequisite explanation, current status, Repair and Remove actions. Per-process network monitoring retains the corresponding controls and status. Short maintenance labels retain service-specific accessible names and tooltips. Repair visibility, removal confirmation, setup permissions, cancellation and Save/Cancel behavior are unchanged. Connections' Enable monitoring entry point and service setup progress/errors now open Services.

Live sensor readings appear on the gauges and in resource details. Services can scroll for long translations and detailed error messages. Store builds retain their existing unsupported-service behavior.

Temperature units remain in Monitoring → Resource gauges. Primary/secondary disk volumes, network adapter and GPU selection remain in Devices.

The static layout lives in ServicesSettings.xaml; SettingsWindow continues to own the existing service actions and pending preferences. All new text is localized in English and the nine translations. Existing navigation/layout assertions were updated to refer to Services.

Validation: no tests, smoke modes or performance benchmarks were run, at the user's request. Desktop and Microsoft Store Release builds completed successfully, each with zero warnings and zero errors (`artifacts/services-settings-build.log` and `artifacts/services-settings-store-build.log`). Interactive layout and translated-size acceptance remain unverified.

What landed: the compact Services page with both service groups. Departures: production status text retains detailed availability/recovery messages; Repair is shown only in the existing repairable states.
