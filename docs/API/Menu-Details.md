# Menu item editor (UX14)

Catalog owns branch/product name, unit price and preparation station. UX14 edits existing
items only. Product identity, currency, availability, category placement, photos, barcodes,
stock and accepted Order/payment/receipt snapshots remain separate.

| Route | Behavior |
| --- | --- |
| `GET /api/catalog/v1/customer/branches/{branch}/menu-details` | Live `catalog.menu.read`; `{organizationId,branchId,items:[{productId,name,unitPrice,currency,preparationStation,available,version}]}`; at most200 |
| `PUT /api/catalog/v1/customer/branches/{branch}/menu-details/products/{product}` | Live `catalog.menu.write`; existing product only |
| `GET /bff/customer/menu-details/csrf` | Protected Customer session antiforgery token |
| `GET /bff/customer/menu-details/branches/{branch}` | Selected-tenant management read |
| `PUT /bff/customer/menu-details/branches/{branch}/products/{product}` | Same input, `X-Nexa-CSRF` required |

Write input: `{name:"Soup",unitPrice:108.25,preparationStation:"kitchen",expectedVersion:0}`.
All four fields are required/non-null. Name is trimmed, nonempty, at most120 characters;
station is trimmed, nonempty, at most80. Control characters are rejected. Current editor
supports the existing THB checkout profile, price0–99,999,999 inclusive with at most two
decimal places. Currency cannot be changed by this command. A zero-priced line does not
relax the existing positive-total order rule. The reply is the updated item; availability is
an observation under the owning row lock and is never written by this editor.

Per-item details version is independent of availability/photo/layout/barcode versions.
Exact current-version no-op does not advance history. Stale version returns409
`menu_details_changed`, even for an otherwise identical draft. Unknown scoped product404,
invalid input400, denied permission403, missing session/tenant binding401, dependency failure503.
GET and PUT responses are no-store. PUT bodies are limited to4,096 bytes and BFF response
buffering to524,288 bytes, covering bounded multilingual menus. Error bodies are sanitized.

BFF derives tenant context from its subject-bound selected cookie and rechecks current
Directory membership with server-held bearer credentials. Catalog checks live branch
permissions; no guest/workload management bypass exists. Body/query organization identifiers
cannot replace the selected tenant. The portal requires local review then confirmation,
invalidates review after draft changes and clears stale/uncertain edits before reloading.

The legacy menu POST/upsert contract remains available. Matching writers advance menu_version
on actual metadata differences, preserve availability and use their existing audit/event path.
They remain unversioned for legacy callers; a concurrent legacy change invalidates an old
editor review. Detailed before/after audit is written by the new editor, not retroactively
backfilled for legacy upserts. Do not mix older binaries that omit this version increment.

Order already rereads current commercial data and compares fingerprints before a new
placement. Guest quote/acceptance uses the same live prices and price-consent workflow.
Existing accepted/replayed orders retain original names, prices, stations, receipts and
payments. A change does not move existing Kitchen tickets or reprice history. Cross-service
admission is not a distributed selling lock; an already validated command may proceed.

## Diagnostics

Existing `nexaconnect-catalog` and `nexaconnect-customer-bff` observability covers the new routes.
`MenuDetailsCatalog` and `MenuDetailsDirectory` use existing Services settings, disable
redirects and propagate validated correlation identifiers. Search structured logs for
`Menu details authorization denied`, `Menu details BFF rejected` or `Menu details unavailable`,
then follow correlation across the BFF and Catalog. Safe events contain status/category only;
never log bodies, names/prices, credentials, personal or payment data, or arbitrary headers.
See [storage](../Database/Menu-Details.md) and [rollout](../Deployment/POS-UX14-Menu-Details.md).
