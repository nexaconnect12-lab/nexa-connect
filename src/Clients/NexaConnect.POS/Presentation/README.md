# POS presentation

UX5 receipt pages live in the adjacent [Printing component](../Printing/README.md), with
Standard/80 mm formatting independent of staff density. Staff preview/test/settings clear
or hide at privacy transitions and never enter customer projection or financial commands.
See [setup/verification](../../../../docs/Deployment/POS-UX5-Receipt-Printing.md).

`PosUiPreferences` stores versioned touch-first and independent density choices per current Windows user
and exact terminal scope, outside release and financial storage. Missing/invalid data
defaults to off/Standard. `MainWindow.TouchInput.cs` gates automatic keyboards and applies saved
changes immediately with failed-save rollback. See [input modes](../../../../docs/Deployment/POS-Input-Mode.md).

`PosDensityMetrics`/`PosResponsiveLayout` define staff presentation sizing and logical
breakpoints. `MainWindow.Density.cs` updates window-local resources, preserves keyboard
choice and reflows existing controls without submitting commands. See [UX3](../../../../docs/Deployment/POS-UX3-Display-Density.md).

`CashierWorkflow` projects client state into guidance and explicitly labeled estimate or
original unpaid server totals. `MainWindow.Workflow.cs` pins the original buttons and maps
guarded shortcuts to existing tabs; it invokes no financial commands. See
[POS UX2](../../../../docs/Deployment/POS-UX2-Cashier-Workflows.md).

`TouchKeyboard` renders focus-preserving touch keys. `TouchInputEdit` provides bounded
selection replacement, culture-aware numeric drafts and Unicode backspace boundaries.
`MainWindow.TouchInput.cs` owns the current staff field, docked viewport and privacy/session
guards; it sends no business command. Windows `osk.exe` owns external browser input.
See [POS UX1](../../../../docs/Deployment/POS-UX1-Touch-Input.md) for limits and verification.

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
UX4 adds `PosGalleryReaderCache`, a separate bounded scoped DPAPI reader bound to the
original subject. Locked refresh uses only that gallery credential; active authentication
bootstraps/renews it. Stable playlist rotation survives image lease renewal. See
[UX4](../../../../docs/Deployment/POS-UX4-Locked-Gallery.md).
Only frozen approved images/public text reach the customer screensaver; normal transaction
projection stays separate. See [gallery contract](../../../../docs/API/Screensaver-Gallery.md).

Locked images use `UniformToFill` across the monitor with aspect-preserving edge cropping.
The staff view starts with its centered scrollable authentication panel hidden; the customer view
has optional public captions and no controls. `ScreensaverPlacement` validates a physical
monitor lease, rejects ambiguous spans/overlap and invalidates changed topology.
`PosDisplayCoordinator` snapshots/restores staff bounds/chrome and fences fullscreen moves;
it owns no authentication or financial state. Missing/expired media retains neutral fallback.
Minimized windows remain minimized; a later render after restore can enter fullscreen.

`LockPromptState` is presentation-only. The first tap/click/keypress reveals the centered
panel without authenticating; held/repeated wake-key events are consumed through key-up.
Focus goes to the panel rather than Resume. Escape/Back hides it while preserving the lock;
active authentication keeps cancel controls visible. New locks reset hidden, while ordinary
gallery renders preserve prompt visibility. Original resume/sign-out guards remain authoritative.

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
