# Store sign-in startup

## Problem

Installer and portable builds start at sign-in through a value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. An MSIX package's HKCU writes go to a private per-app copy of the registry that Explorer never reads. The Store build therefore showed **Start WinMon when I sign in** as on while nothing started. Until this change, Store builds hid the option.

## Design

- **Use the package's startup task.** `packaging/msix/AppxManifest.xml` declares a `windows.startupTask` extension with TaskId `WinMonStartup`, off by default. `StartupRegistration` switches it through the Windows Runtime `StartupTask` API: `RequestEnableAsync` and `Disable`, and `State` to read it. Installer and portable builds keep the Run key and are unchanged.
- **Respect Windows' decision.** The task has five states. Two let the app change it (`Disabled`, `Enabled`). `DisabledByUser` means the user turned WinMon off in **Settings > Apps > Startup** or Task Manager, and only Windows can turn it back on. `DisabledByPolicy` and `EnabledByPolicy` come from group policy. For the last three, Settings shows the box in its current state, greyed out, with a line saying why.
- **Hide it without a package.** A Store build run unpackaged, as the tests and smoke modes run it, has no startup task, so the option is hidden there. `GetCurrentPackageFullName` detects package identity without loading the Windows Runtime projection.
- **Keep the cost out of startup.** The Store build targets `net10.0-windows10.0.17763.0` (the package's MinVersion) to get the projection, which adds about 23 MB (`Microsoft.Windows.SDK.NET.dll`) to the package. Only `NoInlining` methods that run from Settings reference it, so normal startup never loads it. Normal builds keep `net10.0-windows`, and the test and benchmark projects match the Store TFM when `StoreBuild=true`.

## What landed

As designed: the manifest extension, `StartupAvailability` and the state mapping in `StartupRegistration`, the two explanation strings in all ten languages, and `New-MsixPackage.ps1` naming the task after the package's display name.

Tests check that the manifest's TaskId matches the code, the state mapping (Store build), and the Settings states. In a locally registered development package (Developer Mode, `Add-AppxPackage -Register`), turning the option on in Settings set the task to `Enabled`, and Settings showed it checked when reopened. `Disable()` called from inside the package set it to `Disabled`.

Departures and limits:

- The startup state's registry mirror under `AppModel\SystemAppData\<family>\WinMonStartup` is not the live value: it stayed at 2 after the task was disabled. Check state through the API, not the registry.
- Not verified here: an actual sign-in launch, and the `DisabledByUser` and policy screens in a real package. Those need a sign-out, or a Windows-side toggle and a group policy on a test machine.
