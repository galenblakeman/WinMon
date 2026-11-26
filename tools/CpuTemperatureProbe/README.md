# CPU temperature probe

CPU-only acceptance probe for WinMon's separate sensor service. It references the service project and exercises its actual provider, using stable `LibreHardwareMonitorLib` **0.9.6**. Lock files record the resolved dependency graph. The WPF application and MonCore do not reference this package.

Install official [PawnIO](https://pawnio.eu/) separately, then run on 64-bit Windows:

```powershell
dotnet run --project tools/CpuTemperatureProbe -- --samples 6
```

The probe enables CPU hardware only and closes the provider when finished. It writes one JSON observation every two seconds, preserving the selected CPU/sensor identity and actual sensor name when available. It reports update wall time, process CPU-time delta and working set. It never installs a driver or service or loads external modules. Unavailable or implausible values remain null. Selection requires an unambiguous supported sensor meaning and remains fixed while the provider is open; it never substitutes the hottest core.

The service resets public sensor extrema before updating and requires evidence of a new library assignment, including when Celsius is unchanged. This does not independently prove native hardware accuracy. Validate idle/load readings and failure behavior on the Ryzen 9 9950X3D and Core Ultra 9 275HX, recording package/driver versions and Memory integrity state. The user deferred those hardware checks until after service implementation. See [the design](../../docs/design/HARDWARE-SENSORS.md).

Exit status: 0 = plausible library reading observed; 2 = platform/arguments unsupported; 3 = PawnIO not registered; 4 = no plausible reading or acquisition failure. A successful exit does not prove hardware accuracy or freshness.

LibreHardwareMonitorLib is [MPL-2.0](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LICENSE). Its embedded PawnIO modules have separate [LGPL notices](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/v0.9.6/LibreHardwareMonitorLib/Resources/PawnIo/COPYING). The service publish output includes these notices; signed distribution acceptance remains required.
