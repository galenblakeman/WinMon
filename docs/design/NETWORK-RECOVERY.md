# Automatic network recovery

Keep enabled per-process monitoring working after temporary startup, named-pipe or collector failures, without asking for administrator access.

The existing controller owns recovery inside its polling task. Healthy connections retain the one-second poll. Unavailable snapshots and connection errors release the old subscription, clear live readings, then retry the installed service after five seconds. Repeated failures continue at that interval. A fresh subscription lets the service replace a failed collector. Restoring an enabled preference starts the same retries if the service is initially unavailable.

Recovery preserves the logical client session so observed totals survive; existing session accounting marks missing coverage incomplete and waits for fresh rate windows. Missed traffic cannot be reconstructed. Protocol mismatch remains Update required and stops retries, leaving signed installation/update to explicit Repair. Cancellation or rejected explicit setup does not start recovery. Microsoft Store builds continue to avoid the service entirely.

Disable, manual repair, removal and shutdown cancel and await the same task, including an in-flight reconnect, before changing the connection. No new UI controls, strings, settings or service binary are needed.

## Result

Landed in `NetworkMonitoringController` with regression coverage for pipe errors, request timeouts, unavailable collector readings, repeated startup failures, retained session identity and cancellation while waiting or reconnecting. The implementation follows the design; native service restart and real sleep/resume remain manual acceptance checks.

Validation: 25 focused controller tests passed. Final `tools/Verify.ps1` and `tools/Verify.ps1 -Store` runs passed 1,222 and 1,224 tests respectively. Connections (15 renders) and capture/Settings (22 renders) smokes passed; representative healthy/unavailable Connections and Network Settings renders were inspected. The initial full desktop run hit the existing setup rollback test `RevocationTransaction_PreservesRecoveryBackupWhenRollbackFails`; its isolated rerun and both final full runs passed. Smoke output: `artifacts/verify/20261108-085643/`. No installed service was stopped, repaired or replaced for these checks.
