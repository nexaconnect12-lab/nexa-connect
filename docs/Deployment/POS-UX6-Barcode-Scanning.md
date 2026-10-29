# POS UX6 — product barcodes and guarded scan-to-cart

Implemented in source on 28 October 2026 and activated in the retained local pilot on
29 October 2026 (Asia/Singapore). Actual scanner and native operator acceptance remain
pending as described below.

## Setup and use

1. Deploy matching Catalog, Customer BFF/portal and native POS. Apply **Catalog 5** before
   barcode use; financial compatibility remains 0.38.0. Other schema/financial state and
   default-off QR/provider settings are unchanged. See [storage](../Database/Product-Barcodes.md).
2. In the staff portal, open **Configuration → Product barcodes** with a manager who has
   existing branch `catalog.menu.write`. Select an authorized branch, **Load products**,
   choose an existing product, **Load barcode**, enter a code and **Save barcode**. Empty
   removes this branch's binding. Read-only users need menu.read; no grants are added.
3. Use 1–64 ASCII letters/digits/dots/underscores/hyphens. Case and leading zeroes are
   significant. One primary code per branch product is supported. The same code may be
   assigned in multiple branches only to the same product. Duplicates or stale/uncertain
   saves require reload and review, with no automatic write retry.
4. Sign in to POS with the configured branch and open/resume the original shift. In
   **Checkout**, select **Enable barcode scan entry (this session)**. It defaults off on
   startup, independently of touch-first (also default off) and Standard/Touch density.
   Here “this session” means the running POS window: lock/resume or sign-out clears input
   and pending lookup but can retain the checkbox until the window is closed. Restart
   defaults off; no per-user scanner preference is saved.
5. Focus **Product barcode scan entry**, then scan a supported keyboard-emulating device
   configured with **Enter-only suffix**, or type the code and select **Add barcode**.
   Wait for each lookup. A successful lookup adds one unit through the existing editable
   cart; review the cart and authoritative quote before placing/paying as usual.
6. Unknown/unavailable codes, currency mismatch, expired access and network failure provide
   guidance and do not add an item. Identical successful scans inside 750 ms are ignored
   with a message; wait briefly or use quantity + for another unit. Scans are not queued
   while lookup is busy. Tab/context/lock/deactivation clears partial input and cancels
   pending lookup; stale results cannot add to a new context. There is no offline lookup.

The feature captures no global keyboard stream and cannot identify the physical device.
Only the explicit field submits a lookup; another application's focused field retains its
own keyboard behavior. Do not scan into amount/reference fields or external Windows/browser
dialogs. Supported configuration excludes Tab/Space/control prefixes/suffixes, weighted
codes, GS1 interpretation, serial/SDK integration and card login. Confirm the actual scanner
model, connection, Windows layout and Enter suffix before hardware acceptance.

While scan mode is enabled, POS-owned order, payment, cancellation, cash-review and display
role confirmations consume scanner text/mnemonics and Enter. Confirm by deliberate click/
tap, or Tab then Space; Escape cancels. Session/lock invalidation cancels the protected
dialog. Other Windows/browser dialogs retain native behavior. Outside the scan field,
Enter is consumed in the main POS while scan mode is enabled. Scan mode off preserves
existing standard keyboard and native-confirmation behavior.

## Invariants and failure recovery

Every lookup uses live customer branch menu.read and a current menu snapshot. Native code
checks exact organization/branch/code, currency and bounded product fields. Availability and
original cart rules remain authoritative; the existing server quote/placement reserves and
prices the order. Scanning never submits pricing confirmation, stock reservation, Kitchen,
payment, refund or cash commands. Existing pending checkout/payment blocks editable cart.
Customer display receives only its existing allowed cart projection, never barcode input.
No scanner preference, barcode map or scan queue is persisted on the terminal.

Barcode management uses live menu.write, version checks and one owning Catalog transaction
for registry/binding/audit/outbox. Other branches' mappings and financial snapshots remain
unchanged. BFF derives tenant/subject and requires CSRF for save. In-memory Catalog is only
a non-durable development adapter. See [API/errors/telemetry](../API/Product-Barcodes.md)
and [ADR-052](../Architecture/Decisions/ADR-052-product-barcodes-and-scanner-entry.md).

## Verification and pending acceptance

Source-bound candidate `476fcf99-8577-40b2-9063-6d36bd17a40a` passed the complete local
gate on 28 October 2026: 1,192 unit tests, 6 architecture checks, 55 HTTP tests, 47 hosting
checks, 90 frontend tests, 57 QR browser cases, 45 financial browser cases and eight owning
persistence cases with cleanup/broker recovery verified. Both required Windows fixtures
and candidate/acceptance guards passed. Documentation audit resolved current table/version
summaries and clarified the running-window scanner lifetime. Package
`c6c5ea2eb29040fcb211c5ae978ccbe6` was installed after the operator confirmed POS closure.
This is a dirty-source local candidate, not a signed/accepted production release.

### Local activation — 29 October 2026

Retained stack `c30b628ab23141f389119daa565f894f` rebuilt without reset, applied Catalog
5 and reached Ready with all 12 services. Fresh pilot `1f104b6a9f28429899d85203baaa1b94`
configured against the same verified candidate and unchanged certificate at
`https://192.168.0.191:8443`. Trusted HTTPS diagnostics passed: guest 200 and staff-route
404. Earlier phone observations were not reused; all 24 fresh phone cases remain pending.

Release `c6c5ea2eb29040fcb211c5ae978ccbe6` installed into the existing current-user
DraftTerminal and reopened with a responsive exact-release native window. All seven
recorded recovery/gallery/preference fingerprints matched before launch; branch/menu
references, terminal scope and service URLs were unchanged. No preference file was changed.

Read-only local cashier PKCE verified the current branch's barcode binding route (200)
and an unknown-code lookup (404) through the real runtime service and live authority.
No barcode assignments, native sign-in, cart scans or financial commands were submitted.
This verifies activation/read access, not native scan-to-cart, manager save or physical
scanner acceptance. Run the explicit operator steps below to establish those observations.

`BarcodeTests` and `BarcodeTransportTests` cover identifiers, collisions/release, live denial,
versions, all scan-context guards, repeat handling, body-only lookup and scope validation.
`BarcodeBoundaryTests`/`CustomerBarcodeBoundaryTests` cover roles, tenant, no-store, CSRF,
conflicts and downstream-error suppression. Portal tests cover explicit save, uncertain
outcome without retry and context reset. `BarcodePersistenceTests` is included in the guarded
owning runner with concurrency, rollback, audit and empty/history downgrade checks.

The complete `scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure` gate now requires 55
HTTP cases, 90 frontend cases, eight owning cases and the Windows `pos-scanning-fixture`.
The Windows fixture tests optional controls, short-screen layouts, tab/privacy clearing,
invalidated dialogs and actual WPF Enter/text consumption without real authentication,
barcode assignment to retained data or financial submission.

After activation, bind a chosen test menu item to a code (for example `0001`) in the correct
branch. Sign in/open shift, enable scanning, type the code and Add barcode. Verify one unit,
unknown code, repeated scans, quantity changes, pricing review and pending-payment blocking.
Test lock, app switch, branch/tenant changes, connection loss and protected confirmations
without submitting an unwanted transaction. On the delivered device, repeat with its actual
scanner and both input/density modes; verify no scan can approve POS-owned decisions.
Actual hardware, operator/financial end-to-end acceptance, previous PDF inspection, physical
displays, phones and long-lock slideshow remain pending until explicitly observed.
