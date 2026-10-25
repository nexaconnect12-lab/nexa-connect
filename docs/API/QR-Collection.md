# QR5 cashier collection

Windows POS can choose an eligible original Order from the
[branch order list](Branch-Order-List.md) instead of manual reference entry. That staff
GET is read-only; its readiness hint does not replace the preview/claim routes below or
grant custody. Paid/claimed rows remain history and cannot start another collection.

Order exposes authenticated customer-role routes:

- `POST /api/order/v1/organizations/{organization}/branches/{branch}/qr-collections/{orderId}/preview`
- `POST /api/order/v1/organizations/{organization}/branches/{branch}/qr-collections/{orderId}`

Both require protected organization/application headers, authenticated subject and a live
Directory/branch `order.manual-payment.confirm` decision. A collection reference is
`QR-` plus the original Order UUID in N format (35 characters); Windows POS also accepts
the original UUID. It is an identifier, never a capability or settlement permission.
No table-wide combined bill or table account is created.

The strict JSON body (2048 bytes maximum) contains `restaurantId`, `terminalId`, `storeId`,
`shiftId`, `settlementKey`. The Order UUID must be an accepted QR request's original Order,
with exact organization/restaurant/branch, channel `qr`, retained pricing, no provider intent,
and KitchenAccepted state. Submitted/rejected/cancelled/provider/unrelated Orders fail closed.
The existing private POS admission validates the active registered terminal/store and exact
open shift/current cashier. Collection uses shift-only admission; actual cash settlement
separately requires its open THB drawer. Preview performs those reads without creating custody.

The claim commits immutable collecting actor/terminal/store/shift/settlement identity and
audit outbox in one Order transaction. Same-operation concurrency/replay returns one binding;
different operation, cashier or terminal conflicts. There is no automatic timeout, release,
transfer or new manager override. Preserve retained state and reconcile lost custody.

Responses contain original Order ID, organization/restaurant/branch, logical table code,
status, THB total, original pricing/lines and settlement UUID. Staff sees no guest capability.
Malformed input is400; missing Order/scope404; authorization403; state/custody/admission409;
dependency/persistence failure503. Responses are no-store. Default-off `QrCollection:Enabled`
hides both routes; startup requires durable QR acceptance. Keep it enabled until collections
have reconciled. Existing settlement recovery does not depend on this new flag.

Payment remains `POST /api/order/v1/orders/{id}/manual-settlement`, with existing live
authorization, exact total/method/confirmation, financial guards, atomic Paid/receipt/sale/
audit/outbox and idempotency rules. For QR, Application and transactional persistence require
the original claim's actor/terminal/settlement UUID. New settlement admission also checks its
exact original shift/store. A committed exact replay remains readable after shift closure or
POS outage, with live staff authorization and original custody. No placement, Catalog repricing,
Inventory or Kitchen operation runs during collection. Existing cancellation authority is separate.

Guest private request status includes safe `collectionStatus` observations (`preparing`,
`awaiting_cashier`, `collecting`, `paid`, `unavailable`); it never exposes collecting custody.
It includes `collectionReference` for accepted Orders while collection is available;
unavailable original Orders expose no collection reference. When
paid, it includes an allow-listed original receipt: number/time/tender/currency, name/quantity/unit-price/
line-total and subtotal/service/tax/total. No staff, decision, terminal, bank reference or payment
credential is exposed. It requires the original live visitor before/after owning receipt reads;
expired/revoked visits cannot retrieve receipts by knowing a reference. No guest payment endpoint.
Staff receipt/PDF retrieval still requires `order.read`. Customer display shows only current
accepted lines/totals/payment guidance and clears transaction details after completion/lock.

Current Order QR readers and manual-settlement SQL require migration22 even when collection
is disabled. Matching POS admission must return validated `admittedShiftId`/`admittedStoreId`
for expected scope; missing or mismatched proof fails closed. Legacy timestamp-only proof
is supported only for non-QR calls without expected scope.

Services retain shared JSON/OTLP/correlation. Query service.name=`nexaconnect-order` for
`QR collection` authorization/conflict/unavailable events and follow validated `CorrelationId`
to `nexaconnect-pos` `Manual tender admission` events. POS client uses correlated
`order.qr.collection` transport. Never log references, subjects, bodies, credentials or tender
values. Admission is a point-in-time observation, not a distributed lock; concurrent remote
closure/revocation and financial projection custody retain existing operational rules.

See [schema](../Database/QR-Collection.md), [operations](../Deployment/QR5-Collection.md)
and [ADR048](../Architecture/Decisions/ADR-048-qr-cashier-collection.md).
