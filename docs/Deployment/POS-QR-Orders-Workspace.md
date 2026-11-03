# Unified Windows POS QR orders workspace

The **QR orders** workspace combines guest request review and accepted-order collection in
one native destination. It replaces the separate QR requests and QR collection navigation
entries. Portal manager review and the all-branch **Orders** tab remain available.

## Cashier workflow

Choose one of three views:

| View | Data and actions |
| --- | --- |
| Pending review | Existing request queue and status filter, including guest price review and accepted/rejected/expired proposals. Select a request, review items/server totals, confirm the decision and explicitly accept or reject. |
| Awaiting payment | Existing paginated `channel=qr&state=open` order history. Preparing and otherwise uncollectible orders remain visible with their status. Select a ready original order and choose **Load for payment**. |
| Paid / history | Existing paginated `channel=qr&state=paid` history. Select a paid order and choose **View receipt**. There is no collection action in this view. |

Acceptance never collects payment or claims cashier custody. **View original order** after
acceptance switches to Awaiting payment (or Paid / history for a paid original), selecting
its original UUID if it is on the first page. Use Refresh/Next page if it is elsewhere;
never create a replacement order. No UUID entry is needed for normal row selection.

**Load for payment** explicitly invokes the unchanged read-only preview, durable original
checkout/custody save and claim flow, then opens the existing **Payment** tab. Review original
items and totals there and explicitly confirm cash or manual PromptPay once. The open shift,
original cashier/terminal/store, matching currency, manual tender, empty cart, no competing
pending work and live server payment authority remain mandatory. An uncertain result keeps
the original protected recovery identity; use the existing verify/resume controls.

**View receipt** invokes existing scoped receipt retrieval and opens **Receipts**. It may
save the last receipt lookup reference, but never places, claims, settles or reopens an order.
Receipt/reprint authority is rechecked by the owning service. Paid rows cannot be collected again.

The collapsed **Find by reference** section is a secondary route for an existing guest
collection reference/original Order UUID. It preserves the same preview/claim/recovery
guards. Collapsing it clears transient reference/details; it does not erase a pending checkout.
The usual browse-order shortcut and all-branch Orders collection action remain compatible.

## Ownership, authorization and failure behavior

This is native presentation/orchestration over existing [QR acceptance](../API/QR-Acceptance.md),
[branch order list](../API/Branch-Order-List.md), [collection](../API/QR-Collection.md) and receipt
contracts. No new route, migration, permission, dependency, tax calculation, payment method or
provider integration is introduced. Order retains commercial validation, live scope/authority,
version checks, original identity, transactional acceptance/audit/outbox and Kitchen/Inventory
idempotency. POS retains original cashier/shift admission; local protected recovery is unchanged.

The initial [native review implementation](POS-QR-Request-Review.md) remains the reference for
request decisions, guest price consent, server feature flags and transport bounds. This guide
supersedes only its separate-screen navigation. Acceptance and collection remain distinct
authorized commands; combining their entry point grants no extra capability.

Accepted-order views reuse the bounded 25-row/cursor adapter, twenty-second deadline and
server branch filters. A native projection guard rejects non-QR rows or paid-state mismatches.
Review and accepted-order lists use generation-fenced memory state. Switching view, tab exit, lock/sign-out,
deactivation and shutdown clear private rows/selection/cursors, cancel in-flight reads and
reject late responses. View changes also collapse manual lookup and hide the on-screen keyboard.
Neither review data nor payment-history rows reach customer-display projections or support exports.

Unselected active views poll their current page every fifteen seconds. Selecting a row pauses
polling to preserve the reviewed snapshot; explicit refresh clears selection. No polling or
view change accepts, rejects, claims or settles. Read failures clear rows; 401 requires sign-in,
403 reports denied branch access and other failures offer an explicit refresh. Unknown acceptance
outcomes require reloading original requests; no automatic decision/payment retry is introduced.

There is no offline commerce added. A history or availability read cannot authorize payment;
the existing preview/claim/receipt boundaries revalidate their own state and permissions.

## Implementation and verification

The change moves existing reference-entry controls in `MainWindow.xaml`, adds the three-view
selector and accepted-order grid, and coordinates them through `MainWindow.QrOrders.cs`.
`PosQrWorkspacePolicy` enforces view projection/receipt readiness while existing
`PosOrderListState` continues to gate collection. Existing review, Orders, session and payment
handlers remain the owning workflows. No UI framework or persistence mechanism is replaced.

Service `nexaconnect-pos-client` retains shared JSON/optional OTLP and existing correlated
`order.list`, `order.qr.review.*`, `order.qr.collection` and `order.receipt.read` boundaries.
Search safe boundary operation/status/correlation events and correlate with `nexaconnect-order`.
Never log request/response bodies, tokens, guest capabilities, payment data or table/actor labels.

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter 'FullyQualifiedName~PosQrWorkspaceTests|FullyQualifiedName~PosQrReviewTests|FullyQualifiedName~PosOrderListTests|FullyQualifiedName~QrCollectionTests|FullyQualifiedName~QrRequestTests|FullyQualifiedName~QrAcceptanceTests'
dotnet run --project tests/Windows/NexaConnect.PosTouchTests -- --qr-review-preview
./scripts/test-qr6.ps1 -ConfirmDisposableInfrastructure
```

Tests must cover original-ID handoff without automatic collection, shift/pending-work denial,
QR-only open/paid filters, paid receipt retrieval, view-switch/late-response privacy, reference
clearing and existing acceptance/rejection/price-consent behavior. Native fixtures use synthetic
HTTP/identity and temporary recovery only; they do not certify real bank or physical device work.

The final source-bound verification passes 1,429 unit tests, 7 architecture tests, 136 HTTP
integration tests, 90 frontend tests and 154 browser cases, along with native printing/input/
session/shutdown and build/type/dependency checks. Thirteen owning workflow and four
drawer-authority cases pass with verified cleanup. Focused verification passes 76 tests;
native checks include original UUID details, small/full-HD receipt reachability and late
response privacy. The prepared 23-file Windows package and gate match their respective
current source inventories. Source verification, documentation audit and matching local
installation are complete; actual operator acceptance remains pending.

## Rollout

Source implementation is separate from activation. After verification, close POS before
using the existing [same-scope refresh](POS-UX22-Local-Pilot-Startup.md) and
[host maintenance](POS-UX23-Background-Hosting.md). Preserve orders, shifts, settings, galleries,
payment configuration and protected recovery; no new migration is required. Prepare matching
QR/host bindings and a fresh installed-target acceptance pack. Earlier review-release observations
remain historical, and no source/native fixture pass transfers into operator acceptance.

### Retained local activation — 3 November 2026

After confirmed POS closure, the verified package was installed in the existing account
and scope. Twelve services, the guest QR edge and a matching scheduled host are running.
The host worker is parented by `svchost`; automatic Windows sign-in startup remains off.
POS was reopened through the Windows desktop shell and remained responsive after the
launch command exited.

| Binding | Activated value |
| --- | --- |
| Run | `c30b628ab23141f389119daa565f894f` |
| Release | `3b9eff916e2a45309a90597237b1d62e` |
| QR pilot | `e3d976580f054e39bc674c5a58426ac4` |
| Background host | `cff626c441ff4d7886c3652fe677e7c6` |
| Fresh acceptance pack | `c3d008f2f9fa411fabe6eb7a373f9eef` |

Pre-open comparison confirmed nine checked protected local files and 159 checked business
tables unchanged, along with reference data, credential values, configuration, addresses
and certificates. No reset or replacement provisioning occurred. Real cashier PKCE reads
of menu, request queue and QR-only open/paid lists returned 200, with the expected branch
and bounded projections. Anonymous menu/queue reads returned 401. Trusted Windows HTTPS
returned 200 for portal and guest `/order`, while guest-edge `/kitchen` returned 404.
These checks accepted/rejected no requests and submitted no payments.

All seventeen cases in the fresh pack remain pending, with valid target binding and fresh
candidate proof. Previous review-release and UX24 packs remain historical. Actual guest-phone,
cashier decision/payment, physical equipment and production readiness are not inferred from
startup/read checks or automated fixtures.

## Next slice: guided acceptance and recovery hardening

After matching activation, use synthetic orders and the new target-bound pack to verify:

1. Original-cashier sign-in/resume, retained branch/terminal/shift/menu and all three QR views.
2. Guest submission stays out of Kitchen until explicit POS acceptance; original identity
   survives refresh and a competing portal decision produces no duplicate Kitchen work.
3. Changed prices require the original guest's confirmation; rejection creates no executable
   order. Navigation/view switches alone make no decision or payment.
4. Collect one cash and one authorized test-proof manual PromptPay order through Awaiting
   payment. Review original Paid status/receipts; history cannot collect either again.
   Do not make a real bank transfer solely for acceptance testing.
5. Receipt/reprint access, reference lookup, pagination, lock/view/deactivation privacy and
   dual-display isolation on available equipment; keep unavailable hardware checks pending.
6. Test delayed responses, network loss and restart with pending work only in the appropriately
   isolated recovery context. Reconcile original IDs/operations rather than creating replacements.

Record explicit observations, investigate reproduced defects, apply focused fixes and retest.
Automated tests and earlier releases' passes cannot substitute for these observations.
