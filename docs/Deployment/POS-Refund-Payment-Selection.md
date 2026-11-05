# POS original-payment selection for refunds

The native POS lets staff choose the original order and receipt instead of typing a
Payment intent UUID. The corrected matching Order/POS update is installed in the retained
pilot. It recognizes the existing sandbox-to-card translation described below; no
historical receipts were rewritten. No migration, new permission, secret or configuration
variable is required. Fresh operator/provider acceptance remains pending.

## Workflow

1. Open **Receipts → Choose paid order…**. The existing Orders workspace opens with
   all sources and Paid orders selected, retaining bounded 25-row pagination and Refresh.
2. Select the original order using its order number, created date, table and total.
   Choose **Review for refund**. This action only reads; it never submits a refund.
3. Review the original order number, paid time (PC local time), currency, captured amount,
   receipt number, up to three item names/quantities and currently available amount.
4. Enter the refund amount and reason, then check **I checked the original payment,
   amount and reason** and choose **Issue refund**. Editing the amount, reason or advanced
   ID clears confirmation. Changing the ID requires loading the original payment again.
5. For an uncertain response, use **Verify pending refund**. Do not create another
   operation to recover a lost response. Print the immutable completed refund receipt
   using the existing authorized print/reprint workflow.

**Orders → Review for refund** offers the same lookup for a selected Paid order.
**Find by payment ID (advanced)** remains available for support; an entered ID must still
be loaded, scope-checked and explicitly reviewed. Customer names are not required or exposed.

## Boundaries and recovery

Only captured card payments with method `card_omise_test`, `card_omise` or `card`, an
exact original Paid order and a matching immutable sales receipt can be selected. The
current local POS offers Omise sandbox; recognizing a method does not enable a provider
or assert production verification. Cash and manually verified PromptPay orders have no
provider intent and are rejected here. This does not implement their refund policies,
item returns, stock restoration, credit notes or changes to the original Paid order.

Order's existing Omise sandbox adapter translates its `card_omise_test` tender into
Payment's `card` method. Selection recognizes that exact pair as well as identical card
labels; it does not require historical receipt rewrites or accept manual tender labels.

Discovery composes existing authenticated Order detail, sales receipt, Payment intent
and refund-history GETs. Each service retains its live authorization; selection requires
the existing order/receipt, `payment.intent.read` and `payment.refund.read` access for the
exact original branch. Creation still requires `payment.refund.create` and an eligible
monetary approval limit. Displaying a row or enabling a button is not authorization.
Different branches, currency mismatches, unconfirmed capture, stale linkage and
unavailable or invalid history fail closed.

Available amount subtracts completed and reserved/uncertain refunds, excluding definitive
failures. It is an advisory snapshot. POS reloads it before saving and dispatching the
original operation; Payment's existing intent-row lock, transaction and idempotency key
remain authoritative for concurrent refunds. A changing balance requires fresh selection
and review if the requested amount no longer fits the rechecked available amount; a
changed original-payment linkage also blocks submission. A balance change that still
covers the reviewed amount does not itself force reselection. Reads cannot collect
payments or send provider commands.

An uncertain durable pending operation freezes payment selection, amount and reason until
verified. A definitive 401/403 response to the newly issued refund POST occurs before
Payment reservation/provider I/O and clears that newly saved local operation only when
local cleanup succeeds. The cashier must resolve sign-in/permission/approval-limit access
and select/review again. If cleanup fails, the original operation stays blocked for support
recovery. Timeout/transport loss, other uncertain outcomes and denial during a Verify GET
never use this cleanup or authorize another refund command.

Lock/deactivation or leaving Receipts clears memory-only labels and confirmation;
late reads cannot restore them. Durable recovery references survive. Selection has a
20-second cancellation deadline, bounded 128 KiB discovery responses and a maximum of
1,000 history rows; excessive history is unavailable for selection and requires support
review. The existing receipt reader retains its own validation and transport behavior.
Order/Payment clients reject HTTP redirects. No provider references or payment tokens
are copied into selection labels or new local storage.

## Diagnostics and verification

The pending-refund feedback refinement is installed in a matching POS/pilot update. It puts `RefundStatusText` above the form's inner scroll area as a
polite accessibility live region. **Verify pending refund** immediately shows progress
there and in the main status bar; completion replaces progress in both locations.
The read has a 20-second cancellation deadline and preserves the original operation on
missing/denied, interrupted or unavailable results. A 404 deliberately means **not found
or access denied**; it is not evidence that another refund can safely be issued, or proof
of the original attempt's provider/reservation outcome. Ask an
authorized manager to review the original operation. Verification never submits a refund
or automatically discards an unresolved local operation. Late success/error messages are
fenced by the original receipt-session generation and subject after privacy transitions.

The native fixture covers 401/403/404/503, a processing result, interruption, late errors,
visible status outside the inner scroll area and unchanged durable pending recovery.

Pending verification uses the existing by-operation GET with client operation
`payment.refund.read`; selection discovery uses `payment.refund.selection` as below.
Follow its validated correlation and HTTP status through `nexaconnect-pos-client` and
the owning `nexaconnect-payment` service without logging response bodies or provider data.

Use service `nexaconnect-pos-client`, fixed operation `payment.refund.selection`, validated
`CorrelationId` and HTTP status to follow Order/Payment reads. Receipt reads retain
`order.receipt.read`. Local discovery/validation failures emit the fixed event
`Refund selection unavailable {CorrelationId}` without exception/payload detail.
Order service `nexaconnect-order` emits `Order detail authorization
denied` through its shared observability foundation. Never log bearer tokens, payloads,
provider references or financial details to diagnose selection.

`PosRefundSelectionTests` use synthetic HTTP responses for scope/linkage, card/capture/
receipt consistency, reserved balance, malformed history, cancellation and dependency
denial. `RestaurantWorkflowCrossServiceTests` verifies the additive original intent
reference, no-store response and disclosure-safe Order denial over isolated HTTP.
The Windows `NexaConnect.PosTouchTests` fixture verifies navigation, readable labels,
confirmation invalidation, stale-balance rejection without a command, frozen pending
recovery, definitive POST-denial cleanup, original-operation verification after transport
loss without a second command, and late-response/privacy fences. These fixtures do not issue live refunds or
constitute provider/hardware/operator acceptance.

An authorized operator must now verify the readable
selection and approved sandbox partial-refund/recovery workflow. Existing operator
attestations must not be transferred to the new source/installed target.

## Retained pilot activation

This original-selector snapshot is historical and is superseded by the [pending-refund feedback activation](#pending-refund-feedback-activation) below.

The corrected source-bound candidate is `9be05e70-6f1a-4fab-ae60-d4b723cf3a20`, with
binding `91bc4636ad43d0ced9a61a92aa8d2ef2f6ef0339ed5fb79b3fb6b3264f102602`.

Corrected release `58831a86eec64c05abec726a49d0690b` was installed on run `c30b628ab23141f389119daa565f894f`. QR pilot
`5a5b83eb861c4830882af0647df8ed9a` and independent scheduler host `69b1328c1d274387ab249d98dd903b62` were running;
automatic startup at Windows sign-in remains off. The exact installed executable reopened
and responds. The source-bound gate passed, including 1,500 unit tests, eight architecture
checks, 150 HTTP, 99 frontend unit and 161 browser cases, native checks and 15 owning/four
drawer-authority persistence cases with verified cleanup. Existing 11 local files, 161 business tables, five stable Authorization tables,
role permissions and two original key files were preserved. Credentials, configuration,
URLs and certificates remain unchanged. Read-only real cashier checks verify the original
captured card/receipt/refund-history translation, menu and PIN status; manager portal
and trusted HTTPS checks pass. No retained-pilot order, payment, refund, PIN or media mutation was sent.
The corrected read-only refund smoke checks three Order details and verifies one original
card payment with no unselectable-receipt mismatch and zero refund commands. PIN metadata
remains active at version 1; this does not attest a real PIN unlock or successful refund.

The then-fresh acceptance `c6f24e3bb2e24908b5e8b160d96c20e5` was bound to that installed target, with all
17 operator cases pending; no historical pass was transferred. Source/installation checks
are separate from operator, provider, physical-device and production readiness. Detailed
local receipts are retained in `.runstate/pos-refund-selection-corrected-activation/` and
`.runstate/pos-refund-selection-current.json`.

## Pending-refund feedback activation

Release `2707248d6f6b4ecaa449ffefefbf3305` is installed on retained run `c30b628ab23141f389119daa565f894f`.
Candidate `064ddd9e-fa74-44b2-bbdc-4a46537ec9af` has binding
`aad118625867c4b26de8828bb3cf738ec447eee9dc72a0290831792a7e4ee703`. QR pilot `f283f0235e724cf3a411d6837249e910` and independent scheduler
host `a86bafdb2c9c4151a2ad3d3acbe4dc4c` are running; automatic startup at Windows sign-in remains off.
The exact installed POS reopened and responds. The source-bound release gate passes,
including 1,500 unit/8 architecture/150 HTTP boundary/99 frontend/161 browser cases,
native fixtures and disposable owning/recovery fixtures with cleanup.

The update preserves 9 checked local files, 161 business tables, five stable
Authorization tables, existing role grants and two original key files. Credentials,
configuration, URLs and certificates remain unchanged. Read-only cashier card/receipt/
refund-history, PIN, menu, manager portal and trusted HTTPS checks pass. No retained-pilot
order, payment, refund, PIN or media mutation was sent. The unresolved saved operation
was not discarded or resubmitted; a missing/access-denied lookup still requires manager
review and is not proof of its original financial outcome.

Fresh acceptance `721d384c86dd42b28dba456e9c22cc68` is bound to this target, with all 17 operator
cases pending. Source/native simulation does not attest the operator or provider outcome.
Detailed receipts are retained in `.runstate/pos-refund-verification-activation/` and
`.runstate/pos-refund-verification-current.json`.
