# POS presentation

QR8 adds presentation-only order/collection labels and specific CollectionGuidance from current client state. The server eligibility hint remains advisory and original command validation remains authoritative. See [QR8](../../../../docs/Deployment/QR8-Usability.md).

`PosOrderListState` holds only the staff session's bounded branch-history page and cursor.
Generation invalidation rejects stale reads after privacy clear; collection selection
requires the current shift/manual-tender/empty-cart/recovery conditions. The native
**Orders** view uses original IDs through the existing QR preview/claim flow and does
not publish branch history to the customer window. See
[order-list behavior](../../../../docs/Deployment/POS-Order-List.md).

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
