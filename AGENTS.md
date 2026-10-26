# WinMon

A WPF resource monitor on .NET 10: the app (`src/WinMon`), portable monitoring library (`src/MonCore`), Windows adapter (`src/MonCore.Windows`), three NUnit test projects and a UI benchmark (`tools/WinMon.Performance`). Run commands from the repository root in PowerShell.

## Verify

```powershell
./tools/Verify.ps1                          # build the app, tests and benchmark, then run the tests
./tools/Verify.ps1 -Smoke compact, search   # also run those smoke modes
```

A change is done when `Verify.ps1` passes and, for anything visible, the smoke modes covering it pass and the PNGs they list look right. Pick modes from the **Smoke modes** table in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#smoke-modes); `-Smoke all` runs every one. Smoke windows open on screen, so they need an interactive desktop.

- For manual `dotnet` commands, pass `-m:1 -p:UseSharedCompilation=false` as the scripts do.
- Warnings are errors. Fix the warning in the code.
- After touching update or Settings code, also run `./tools/Verify.ps1 -Store`, which builds and tests the Microsoft Store variant (`WINMON_STORE`).
- If the test host fails to connect inside a sandbox, rerun the tests outside it.

## Architecture rules

- **`MonCore` is a portable leaf.** It references no WPF, Windows Forms, `WinMon.Controls`, `WinMon.Preferences`, Windows-only dependencies or native imports. `MonitoringLayerTests` and `AssemblyBoundaryTests` enforce the boundary.
- **`MonitorSession` owns sampling**: the loop, queue and history. It reaches the UI thread through the `post` delegate the window passes in. Tests feed it through a fake `IFrameSource`.
- **A view is a UserControl updated through one `Update(...)` call.** It raises events when the user changes something; `MainWindow` keeps the settings in `MonitorPreferences` and passes them back in.
- **XAML for layout and window chrome**; build elements in code only for rows updated every tick.
- **Diagnostics and the benchmark use internal members.** When one needs more, add a named member to `MainWindow.Internal.cs` or the view. The benchmark compiles against WinMon through `InternalsVisibleTo`, so a rename breaks its build rather than its run.
- **Performance is a requirement.** For structural or hot-path changes, measure before and after as [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md#performance) describes, and add the results to the top of [docs/PERFORMANCE-HISTORY.md](docs/PERFORMANCE-HISTORY.md). An unexplained regression blocks the change.

[docs/design/STRUCTURE.md](docs/design/STRUCTURE.md) gives the reasons behind each rule.

## Text and translations

- Put all UI text, including tooltips and accessibility names, in `src/WinMon/Resources/Strings.resx`. Then regenerate the typed properties, since `Strings.Designer.cs` is generated:

  ```powershell
  pwsh -NoProfile -File tools/Generate-StringResources.ps1
  ```

- Add every new key to all nine `Strings.<culture>.resx` files, with the same placeholders, format specifiers and line breaks as English. `StringResourceTests` fails on any mismatch.
- Translate display labels only. Stored identifiers such as theme and palette names, shortcut strings and WMI property names stay the same in code.

## Docs

- `README.md` describes user-facing behaviour. Update it with any change a user would notice.
- `docs/DEVELOPMENT.md` describes the current code: build, verification, structure, measurement semantics and performance method. Update it when any of those change.
- `docs/PERFORMANCE-HISTORY.md` is a dated log of past measurements. Its sections describe the builds they measured, so the code wins where they disagree.
- Each feature gets a note in `docs/design/`, listed in `docs/design/README.md`. The note ends with what landed and where the result departed from the design.

## Releases and keys

- The version lives in `Directory.Build.props` and `src/WinMon/app.manifest`; bump both together.
- Releases follow [packaging/RELEASING.md](packaging/RELEASING.md). CI runs only for `v*` tags.
- `src/WinMon/Updates/update-public-key.pem` is what in-app updates trust. Replace it only through `tools/Set-UpdatePublicKey.ps1`, and keep private signing keys out of the repository.

## Commits

Use an imperative subject in sentence case with no prefix or trailing period, such as `Add the Usage tab`. Make one commit per logical change.
