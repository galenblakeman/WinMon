# Network service installation recovery

An interrupted file cleanup can leave the service unregistered and only part of its inventory on disk. Previously both Repair and Remove required every inventory-listed file to exist, trapping this state.

Repair now accepts a subset of the owned inventory. It still requires a valid administrator-protected inventory, protected owners and permissions throughout the remaining tree, no reparse points, no unexpected files, and no unrelated directories. Missing/corrupt inventories and untrusted trees require manual intervention. Existing policy is retained when present; missing policy authorizes only the requester. The service-executable version, or uninstall version when the executable is missing, prevents known downgrades. If both are missing the previous version cannot be established.

The normal verified staging/health/commit flow stays in place. Incomplete installations are renamed to a unique protected `.NetworkMonitor-quarantine-*` sibling before replacement. Before commit, rollback restores that tree. After commit, that damaged tree is retained for diagnosis; complete old installations retain their existing backup cleanup behavior.

Removal validates the same safe subset, removes the owned service and uninstall registration, and moves the installation to a unique `.NetworkMonitor-removed-*` sibling before file cleanup. A cleanup I/O or access failure emits a warning trace and leaves the remainder there. The canonical path is free for a new install, including after process interruption during file deletion. The installed helper can still schedule its relocated image/directory for deletion at reboot.

No background elevation, automatic quarantine sweeping, or recovery from unknown/untrusted files is added. This does not provide crash journaling across every deployment or SCM operation.

## Landed and departures

Implemented in `InstalledFiles` and `SetupService`, shared by desktop Repair, CLI install, UI removal and installed-helper uninstall. Revoking a user still requires a complete installation. Tests cover subset validation, rejected contents, protected filesystem checks, removal cleanup interruption and failed rename. There are no UI or sampling changes; no performance benchmark change is needed for these maintenance-only operations. Signed release/UAC acceptance remains separate from source verification.
