# Product barcodes v1

Catalog customer routes require a customer-role bearer, validated NexaConnect tenant
headers and existing live branch `catalog.menu.read` or `catalog.menu.write`. No workload
bypass is added. Application repeats organization/product membership, active owning
Restaurant hierarchy and scoped Authorization through existing adapters. Request bodies
are limited to 4096 bytes and responses are no-store.

| Route | Authority | Request / response |
| --- | --- | --- |
| `GET /api/catalog/v1/customer/branches/{branch}/products/{product}/barcode` | menu.read | `ProductBarcode` |
| `PUT /api/catalog/v1/customer/branches/{branch}/products/{product}/barcode` | menu.write | `{barcode: string|null, expectedVersion: long}` → `ProductBarcode` |
| `POST /api/catalog/v1/customer/branches/{branch}/barcode-lookup` | menu.read | `{barcode:string}` → `{organizationId,branchId,barcode,item}` |

`ProductBarcode` has `organizationId`, `branchId`, `productId`, nullable `barcode` and
`version`. An existing branch product without a binding returns null/version 0. A changed
assignment increments version; clearing stores a versioned tombstone. Same value at the
current version is a no-op. Stale versions always return 409: reload after an uncertain save.
Codes preserve case and leading zeroes, with 1–64 ASCII letters/digits/dots/underscores/
hyphens. Null clears; empty/whitespace strings are invalid. A code may be shared only by
the same product across branches in the organization. Changing one branch never changes
another branch's binding. A code is released after its last binding is removed.

Lookup is read-only despite POST; codes are kept out of paths/query strings. It returns
the current branch menu item `{productId,name,unitPrice,currency,preparationStation,available}`,
not a price promise or stock reservation. Unavailable products remain explicitly unavailable.
The bounded native projection admits names up to 200 characters and stations up to 64;
invalid snapshots fail closed. POS separately verifies exact scope/code/currency, clears
late results, and uses existing cart/quote/checkout rules. No guest QR response is changed.

Missing authentication returns 401, unsupported role or live denial 403, invalid tenant/
input 400, missing branch product/lookup 404, collision/stale version 409 and dependency/
invalid-snapshot failure 503. Unexpected provider failures can surface as generic server
errors. Tenant scope is derived from validated headers and live authority, never inferred
from a selected product ID.

The staff BFF adds `GET /bff/customer/barcodes/csrf` and GET/PUT
`/bff/customer/barcodes/branches/{branch}/products/{product}`. PUT requires `X-Nexa-CSRF`.
It derives the organization from the protected selected workspace, validates current
membership/stable subject, bounds successful binding responses to 4096 bytes, suppresses
downstream bodies and performs no automatic write retry. The dedicated Catalog HTTP client
disables redirects and propagates validated correlation. BFF deadline is 20 seconds; POS
lookup has a 10-second whole deadline and a 16 KiB response bound. Barcode/actor values are
never forwarded from unrelated body fields.

Services `nexaconnect-catalog`, `nexaconnect-customer-bff`, `nexaconnect-pos-client` use
shared observability. Search correlation plus `Barcode authorization denied`, `Barcode
assignment conflict`, `Barcode dependency unavailable`, `Barcode BFF` or client operation
`catalog.barcode.lookup`. No barcode, body, actor, token, cookie, price or arbitrary header
is logged. The generic Catalog audit integration event contains scoped identifiers/action,
not barcode values; detailed changes stay in Catalog's append-only audit.

See [ADR-052](../Architecture/Decisions/ADR-052-product-barcodes-and-scanner-entry.md) and
[rollout/tests](../Deployment/POS-UX6-Barcode-Scanning.md).
