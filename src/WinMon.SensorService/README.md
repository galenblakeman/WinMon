# Optional CPU sensor service

`WinMonSensorMonitor` is a separate, demand-start LocalSystem service. It references locked stable LibreHardwareMonitorLib 0.9.6 and enables CPU hardware only. The WPF app, portable core, CLI and MCP do not load the hardware library. Fans and other categories remain disabled.

The service accepts only provisioned Windows SIDs over `WinMonSensorMonitor.v1`. Authentication, pipe creation, process/policy protection and signed setup share the network service infrastructure; each service retains its own endpoint, protected install directory, allow-list, registration and log files.

One serialized acquisition worker owns each provider lifetime. It opens on the first lease, samples every two seconds and drains after a two-second idle grace. A ten-second watchdog marks reads unavailable without aborting or replacing a live native call. Late results cannot publish. Safe returned failures retry after thirty seconds; cleanup that cannot be established within its recovery budget requires service restart. Suspend invalidates the generation before close; resume waits for prior cleanup.

Selection preserves the actual sensor name and fixes hardware/sensor identity for the generation. The provider resets public sensor extrema before an update and requires evidence that the library assigned a valid value during that pass. Unchanged assigned readings remain usable; retained getter values without assignment are unavailable. This is a library-observation contract, not independent proof of native temperature accuracy.

Production installation uses `WinMon.SensorSetup`, the signed sensor bundle and explicit user approval from desktop Settings → Services or the signed desktop installer. The installer's unchecked hardware sensor option silently installs official PawnIO when missing before setting up the service; Settings setup requires PawnIO to be installed already. Removal leaves PawnIO and network monitoring intact. Unsigned development builds cannot provision the service. Store builds hide the feature and reject embedded setup bundles.

For native provider investigation without installing a service, run the [provider probe](../../tools/CpuTemperatureProbe/README.md). Do not run this executable as an ordinary console process: it accepts no arguments and starts only through SCM as the provisioned LocalSystem service.

See [the design and acceptance record](../../docs/design/HARDWARE-SENSORS.md) and [third-party notices](THIRD-PARTY-NOTICES.md). AMD/Intel physical accuracy, HVCI/coexistence and signed clean-machine setup remain separate acceptance work.
