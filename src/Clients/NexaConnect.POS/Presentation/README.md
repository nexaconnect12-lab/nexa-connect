# POS presentation

Phase 5's `PosGalleryPolicy` validates scoped leased PNG bundles; `PosGalleryCache` atomically
stores one DPAPI-protected current-user bundle. `MainWindow.Gallery.cs` handles independent
refresh, decode and rotation without extending operator activity or blocking authentication.
Only frozen approved images/public text reach the customer screensaver; normal transaction
projection stays separate. See [gallery contract](../../../../docs/API/Screensaver-Gallery.md).

`PosTheme.xaml` provides reusable native styles. `CustomerDisplayState` creates a detached,
allow-listed read model of only the current transaction. It calls no service and stores no
credentials/recovery. `MainWindow.Displays.cs` publishes it; receipt/refund/history lookups
are excluded. `CustomerDisplayWindow` renders it without activation, input or commands.

`DisplayTopology` owns the pure explicit-role/geometry policy. `IPosDisplayPlatform` isolates
native display access; `WindowsDisplays` implements it for
Win32 monitor enumeration/physical placement; `PosDisplayCoordinator` owns window lifetime
and native display/DPI/movement event fencing. Topology loss clears customer output and
masks/locks staff work rather than moving it to an unreviewed display. Reconnect/restart
requires explicit reassignment. OS duplication is not a supported privacy boundary.

See [ADR-039](../../../../docs/Architecture/Decisions/ADR-039-pos-staff-customer-displays.md)
and [setup, verification and device checklist](../../../../docs/Deployment/Next-Iteration-Phase-4.md).
