# CPU temperature probe

CPU-only acceptance probe for WinMon's planned separate sensor service. It references the stable `LibreHardwareMonitorLib` **0.9.6** NuGet package; `packages.lock.json` records the resolved dependency graph. The WPF application and MonCore do not reference this package.

Install official [PawnIO](https://pawnio.eu/) separately, then run on 64-bit Windows:

```powershell
dotnet run --project tools/CpuTemperatureProbe -- --samples 6
```

The probe enables CPU hardware only, updates on one owning thread, reads nullable temperature values and closes the library in `finally`. It writes one JSON observation per second, preserving CPU/sensor identifiers and actual sensor names. It never installs a driver or loads external modules. Unavailable or implausible values remain null. A package candidate requires an unambiguous characterized sensor meaning; it never substitutes the hottest core.

Values after `Update()` are not independent evidence of a successful fresh native acquisition. Validate idle/load readings and retained-value behavior after failed reads on the Ryzen 9 9950X3D and Core Ultra 9 275HX, recording package/driver versions and Memory integrity state. Both targets remain a gate before service implementation. See [the design](../../docs/design/HARDWARE-SENSORS.md).

Exit status: 0 = plausible library reading observed; 2 = platform/arguments unsupported; 3 = PawnIO not registered; 4 = no plausible reading or acquisition failure. A successful exit does not prove hardware accuracy or freshness.

LibreHardwareMonitorLib is [MPL-2.0](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/Licenses/LICENSE). Its embedded PawnIO modules have separate [LGPL notices](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Resources/PawnIo/COPYING). Package and driver redistribution notices must be included before shipping the optional service.
