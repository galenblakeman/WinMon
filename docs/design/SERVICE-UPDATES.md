# Updating services with the app

Requested November 27, 2026. A 5.2.1 app showed no CPU package power because the installed sensor service was still 5.1.0. In-app updates replaced the app but left both services alone.

## Why services stayed behind

- Package power was an additive snapshot field, so the sensor protocol stayed at version 1. The app treated the 5.1.0 service as compatible and reused it.
- Setup ran only when no service answered or the protocol version differed.
- The silent in-app installer reinstalls a service only when its installer task was selected, and both tasks start unchecked.

## Design

- Setup's `install` operation already upgrades in place, with staging, rollback and a downgrade check. Nothing new is needed on the elevated side.
- The app compares the installed service file version (major.minor.build) with its own version. The check reads the file under Program Files, which needs no elevation, and only counts when the signed setup bundle is present.
- Explicit enable (Settings Save, Repair, and the installer's `--install-*-service` tasks) runs setup when the service is older, instead of reusing it.
- The restart after an in-app update runs that same path for each enabled service. This is the only automatic prompt: an ordinary startup never asks for elevation, so declining doesn't bring a prompt back at every sign-in.
- Declining or failing that setup falls back to the normal restore, so monitoring continues with the older service. Settings → Services shows Repair for a working service that is older than the app, and Repair updates it.

## What landed

`ProtectedServiceEndpoint.IsInstalledOlderThan`, `ServiceOutdated` on both setup brokers, `ServiceUpdateAvailable` and the outdated check in `CpuTemperatureClient` and `NetworkMonitoringController`, the post-update trigger in `MainWindow.CpuTemperatureMonitoring.cs` and `MainWindow.NetworkMonitoring.cs`, and Repair visibility in `SettingsWindow`. Repair keeps its existing label rather than adding an "Update" string. Unit tests cover the version comparison and the reuse-versus-replace behaviour of both controllers.

Departures and limits:

- Each service has its own signed helper, so an update with both services enabled shows two administrator prompts rather than one.
- Updates from the standalone installer or WinGet aren't followed by an automatic service update. Those services update on Repair or the next in-app update.
- UAC, SCM replacement and the post-update restart were not exercised on a real machine. Tests use fake setup and connections.
