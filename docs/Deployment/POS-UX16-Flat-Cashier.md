# POS UX16 — flat cashier presentation

UX17 subsequently hardens reproduced cleanup failures and adds safe lifecycle diagnostics for the shutdown follow-up. The historical UX15 cause is not inferred from passing controlled fixtures. See [UX17 verification/rollout](POS-UX17-Shutdown-Diagnostics.md).

## Scope and status

UX16 refines the Windows POS cashier interface with a flat visual style: square content
panels, restrained control corners, a dark labeled navigation rail, solid selected
navigation, neutral table headers and simple separators. Category and Station filters share
one row, leaving more room for menu tiles. Current-order lines and the pinned action dock
have clear separators. There are no gradients or elevation effects in the shared cashier theme.

This is a refinement of the existing Windows 11-inspired direction, retaining clear typography,
spacing, native WPF controls, keyboard navigation and visible focus. It introduces no new theme
setting or framework. Standard density and keyboard/mouse remain the defaults; optional touch
input and independently saved Touch density remain available. Selection colors stay inside
navigation templates so light rail labels never become inherited transaction text colors.

UX16 passes complete source verification and is activated in the retained local pilot on
31 October 2026. All twelve services are Ready and the matching POS reopened responsively.
Actual operator/device acceptance remains pending. Synthetic preview images are not screenshots
or acceptance evidence from the running installed terminal.

## Workflow and boundaries

All eight cashier destinations, named controls and handlers are retained. Menu selection,
barcode input, cart edits, estimates, quote review, original-order collection, tender selection,
payment confirmation, uncertainty recovery, receipt printing and shift operations retain
their existing guards and service authority. Visual styles do not submit business commands.
No endpoint, schema, permission, pricing, stock, payment or recovery format changes are added.
The customer display, public menus and portals are not redesigned in this slice.

Existing lock/fullscreen gallery/authentication behavior is retained. The current staff
authentication overlay keeps its existing surface; the flat operational presentation does
not reveal private work while locked. Printer/drawer integrations remain unchanged and
physical drawer opening remains disabled. Actual hardware resolution, scaling, touch and
dual-display acceptance still require the operator's equipment.

## Verification and rollout

The isolated native fixture exercises all eight destinations at 1000×680, 1366×768 and
1920×1080 logical window sizes, content/navigation separation, native selector popup width
and selection markers. UX16 additionally checks selected/unselected navigation text contrast
and isolation of light rail labels from inherited operational text. Existing native fixtures
cover Standard/Touch sizing, optional keyboards, barcode entry, payment proof readiness,
original-cashier/session recovery, shutdown/restart and temporary protected state.

Run the synthetic preview from repository root:

```powershell
dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --shell-preview
```

The compatibility preview filename remains `.runstate/pos-win11-cashier-preview.png`.
The UX16 reviewed copy is `.runstate/pos-ux16-flat-preview.png`. Preview uses synthetic menu/cart
items and temporary fixture storage; it never authenticates against the retained pilot or
submits an order/payment. Review layout in addition to automated checks.

Complete the source-bound release gate before preparing a matching terminal package. Finish
the current sale and close POS before local activation; preserve original orders, shifts,
protected recovery, menu data, certificates, galleries and input/density/printer preferences.
No migration or preference reset is required. Fresh target-bound operator/device evidence
remains separate from automated verification, with no automatic promotion of prior passes.

## Measured source verification

Candidate `d64d08c9-b15f-4821-b1ea-407b21165fb2` passes the complete source-bound release gate:
1,390 unit, seven architecture, 136 HTTP and 90 frontend unit tests; 75 QR, 34 portal menu/gallery
and 45 financial browser cases; native POS build/input/session/shutdown/printing fixtures;
hosting/profile/evidence guards; thirteen owning persistence cases with broker recovery/cleanup
and four drawer-authority cases with cleanup. Both dependency audits pass. The isolated synthetic
preview was rendered and visually reviewed. One initial accountant-view browser timeout passed
in isolation and in the subsequent complete gate, without a source change.

Matching package `e8183059af0e4015820ed9e0a7dfd7e1` is installed and validated against the
current terminal inventory. The complete gate independently validates its full source inventory.
Actual operator/device acceptance remains pending.
No retained data or device command was issued during source/preview verification.

## Retained local activation

Fresh QR pilot `9eb38dc1b8924cc29068ab0d0a4a4744` retains the existing HTTPS origin and
certificate bytes. All eleven databases were backed up. Eight protected local files remain
byte-identical before POS launch, including the observed background-process cleanup.
Business columns/data compare unchanged across 30 Catalog, 54 POS, 10 Restaurant, 31 Order
and five Media tables (migration/outbox/inbox metadata excluded). Original details versions/audit
history, category organization, availability and approved images are preserved. No menu,
financial, printing or physical-drawer command was issued during activation.

All twelve services are Ready. Scope, service addresses, terminal configuration and decrypted
credential values match the backup; original encrypted secret bytes were restored after
comparison. Catalog9 baseline/runtime grants, safe PNG delivery and workload denial pass.
No migration, preference reset, certificate replacement or public DNS change was required.
The installed native window is responsive and bound to the matching package path.

Fresh operator pack `e484fdb04c874a329f56fa06b5eeb86e` has a valid installed-target binding
and all seventeen cases pending. Fresh phone packs remain pending. Local installation does
not establish physical display/touch/operator acceptance or production release readiness.

## Installed shutdown observation (31 October 2026)

During the UX16 update, the user confirmed the POS window was closed, but the installed UX15
process remained running with no main window. The exact prior-release process was verified
and stopped for the authorized update; all eight protected local files remained byte-identical.
This is an unresolved real-environment shutdown observation, not a defect fixed by the flat
presentation change. Existing synthetic shutdown/session tests pass, but do not complete the
real operator shutdown/reopen case. Investigate retained process lifetime and safe shutdown
separately; do not reset recovery or infer successful closure merely from a hidden window.
