# Branch order history and POS selection

Order provides the authenticated staff read route:

```http
GET /api/order/v1/organizations/{organization}/branches/{branch}/orders?restaurantId={restaurant}&channel=all&state=all&limit=25
```

The route requires a customer-role bearer with a nonempty `sub`, matching protected
organization/application headers (`nexa_connect`), and a fresh branch `order.read`
decision. It does not accept a guest capability or workload-only identity. The restaurant,
organization and branch are all required nonempty UUIDs; the owning query and Application
projection check the exact scope. Knowing an Order UUID or cursor grants no access.

| Query | Contract |
| --- | --- |
| `restaurantId` | Required UUID in the configured branch scope |
| `channel` | `all` (default), `qr`, or `pos`; `all` includes all persisted source channels |
| `state` | `all` (default), `open`, or `paid` |
| `limit` | 1–100, default 25; Windows POS requests 25 |
| `cursor` | Optional opaque, canonical base64url continuation, at most 128 characters |

`open` excludes persisted completed/cancelled Orders; it includes draft, preparation,
payment-review and other unresolved states, not just Orders eligible for payment.
Pending/price-review guest requests have no executable Order and appear only in the
existing staff QR request queue until accepted.

The response contains `organizationId`, `restaurantId`, `branchId`, `items` and nullable
`nextCursor`. Each item contains the original `orderId`, `orderNumber`, `createdAtUtc`,
`channel`, canonical `status`, `currency`, `totalAmount`, nullable logical `tableCode`,
nullable `collectionReference`/`collectionStatus`, and `canCollectQr`. A staff collection
reference is `QR-` plus the original UUID in N format. Accepted QR history can retain that
identifier even when unavailable/paid; it is not the guest's capability or payment authority.
No guest token, visitor identity, collecting actor, bank reference, tender detail or line
payload is returned.

Pages use descending creation time and UUID, fetching at most limit+1 rows for continuation.
The cursor is a position, not a signed authorization grant; scope is authorized afresh on
every page. An explicit POS Refresh starts from the newest rows; QR8 automatic refresh
reuses the current page cursor while that window is active. Changes while paging can change filter
membership; this is operational history, not an immutable financial statement.

`canCollectQr` additionally requires enabled `QrCollection:Enabled` and a current
`order.manual-payment.confirm` decision. It is false for non-QR, unaccepted, unready,
provider-backed, unpriced, paid or already claimed Orders. A readiness hint is not POS
admission: the existing [preview/claim](QR-Collection.md) still verifies original scope,
cashier, store, terminal and open shift before retaining/claiming the original operation.
Paid/claimed history never supplies another collecting operation or transfers custody.

Responses are no-store. Anonymous requests return 401; role/tenant/branch denial 403;
invalid scope/filter/limit/cursor 400; storage/dependency failure 503 without private
diagnostics. PostgreSQL with current Order migration22 is required for durable history;
the in-memory configuration returns bounded 503. No new migration, table, index, role or
permission is added. Other existing Order routes retain their behavior.

`OrderListController` handles transport; `OrderListService` performs scope/authorization
and projection; `IOrderListRepository`/`PostgresOrderListRepository` own the parameterized,
single-statement read snapshot. The read does not expire guest requests, claim Orders,
reserve Inventory, send Kitchen work, settle payments or write an Order audit/outbox event.

Shared observability uses service `nexaconnect-order`, validated correlation propagation
and safe structured events `Order list tenant authorization denied`, `Order list branch
authorization denied`, and `Order list unavailable`. Windows POS uses operation
`order.list` through its existing `nexaconnect-pos-client` correlation/logger foundation.
Follow `CorrelationId` between the client and Order; never log tokens, request/response
bodies, arbitrary headers, cursor text, guest capabilities or financial values.

See [cashier usage and verification](../Deployment/POS-Order-List.md) and
[read ownership](../Database/QR-Collection.md).
