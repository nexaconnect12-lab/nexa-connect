# QR2 menu and cart quote

See [ADR-045](../Architecture/Decisions/ADR-045-qr-menu-and-authoritative-cart.md) and
[enablement/verification](../Deployment/QR2-Menu.md). Contracts use
`NexaConnect.Contracts.RestaurantQr`. Existing staff endpoints and QR1 remain unchanged.

| Surface | GET menu | POST quote |
| --- | --- | --- |
| Customer BFF | `/bff/guest/qr/menu` | `/bff/guest/qr/quote` |
| Private Order | `/api/order/v1/guest/qr/menu` | `/api/order/v1/guest/qr/quote` |

BFF accepts only `__Host-nexa-qr-guest`; inbound bearer/session headers, query parameters
and staff cookies do not replace it. Exact configured HTTPS Host is required; quote also
requires exact HTTPS Origin and JSON. Order accepts only explicit `X-Nexa-Guest-Session`
and validates it with Restaurant before reading dependencies and again before responding.
Guest-supplied organization, branch, restaurant, table or currency never determine scope.
Generated pilot business-service edges hide direct guest/internal routes; browser access
uses the BFF. Enabled hosted QR configuration remains a later rollout gate.
QR1 context responses add `menuAvailable` from BFF configuration; `orderingAvailable`
remains false. This flag is UI discovery, not owning-service authorization.

Menu: `{items:[{productId,name,unitPrice,currency,available}]}`. Maximum 200 items, 200
characters/name; overflow is an unavailable menu, not partial pagination. Prices must be
valid two-decimal THB. Unavailable menu flags may be displayed but cannot be quoted.
There are no stock quantities, preparation stations, unpublished images or internal IDs
other than menu product UUIDs. Availability does not reserve or certify Inventory stock.

Quote input: `{"lines":[{"productId":"<uuid>","quantity":2}]}`. Maximum 8 KiB,
1–50 unique products, quantity 1–99. Unknown JSON fields (including nested prices) return
400; null/empty/duplicate/invalid lines fail closed. No payment-method selection.

Quote output: `fingerprint,currency,expiresAtUtc,lines,menuAmount,subtotalAmount,
serviceChargeAmount,taxAmount,totalAmount,taxPercent,taxInclusive,serviceChargePercent`.
Lines contain `productId,name,unitPrice,quantity,lineTotal`. The existing
[Order pricing rules](Order-Pricing.md) apply, including the THB 99,999,999 menu-amount
limit and decimal midpoint-away-from-zero rounding. The fingerprint covers live scope,
cash preview, policy/version and authoritative lines. It is not an Order ID, payment proof,
reservation or acceptance credential. Expiry is min(now + 2 minutes, visitor expiry).

Each quote freshly reads Catalog/Restaurant. Changed price/policy returns a new quote;
the browser warns and displays the new authoritative breakdown for review. Missing or
unavailable products return 409 `cart_unavailable` at Order. Browser requests must review
again after edits/expiry/offline. BFF sanitizes downstream errors. No submission endpoint.

Private Catalog `GET /api/catalog/v1/internal/organizations/{organization}/branches/{branch}/guest-menu`
requires the service-workload policy and exact `azp=nexaconnect-order-service`. It returns
`{items:[{productId,name,unitPrice,currency,available,preparationStation}]}` only to Order.
The private station (max64 characters) participates in the commercial fingerprint and is
removed by Order/BFF. SQL uses organization and branch parameters plus LIMIT 201 to detect
overflow; there is no new database grant/schema. Restaurant's existing internal pricing
route still requires the exact Order workload and checks live hierarchy.

All responses are no-store. Disabled feature: 404; invalid/missing/revoked visitor: 401;
origin/workload denial: 403; cart/policy invalid: 400; unavailable cart: 409; throttle: 429 with
Retry-After 60; dependency/invalid/oversized response: 503. Catalog returns 409 for overflow;
Order treats non-success dependencies as 503. BFF never exposes dependency error bodies.

Bounds: BFF QR1 shared 60/client/minute and 600/global/minute; Order 1,000/global/minute;
Restaurant's existing shared1,000/global/minute also counts both context reads. These are
per-process safeguards, not multi-instance abuse certification. Response bounds: 512 KiB
for Catalog/Order menu and quote adapters, 4 KiB context/pricing; Order deadline 12s,
individual Order dependencies 10s, BFF 15s including streamed body reads. Redirects are
disabled. No automatic financial retry, state write, stock/Kitchen/payment side effect.

Shared service names: `nexaconnect-customer-bff`, `nexaconnect-order`,
`nexaconnect-catalog`, `nexaconnect-restaurant`. Query the service's JSON/OTLP logs with
`|= "QR"` and validated `CorrelationId`; pricing failures also use `Order pricing`.
Bounded operation/status/outcome only: no capabilities, cookies, arbitrary headers,
request/response bodies, product names, prices, totals, table labels or IPs in logs.
