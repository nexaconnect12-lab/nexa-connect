# Branch menu availability (UX13)

Catalog owns manual branch/product selling availability. Existing menu DTOs already expose
`available`; UX13 adds management without changing price, stock, photo, layout or payment
contracts. All routes use no-store responses and require authenticated staff.

| Route | Behavior |
| --- | --- |
| `GET /api/catalog/v1/customer/branches/{branch}/menu-availability` | Live `catalog.menu.read`; returns `{organizationId,branchId,items:[{productId,name,available,version}]}` (at most200) |
| `PUT /api/catalog/v1/customer/branches/{branch}/menu-availability/products/{product}` | Live `catalog.menu.write`; body `{available:false,expectedVersion:0}`; returns the current item |
| `GET /bff/customer/menu-availability/csrf` | Protected Customer session antiforgery token |
| `GET /bff/customer/menu-availability/branches/{branch}` | Selected-tenant management read |
| `PUT /bff/customer/menu-availability/branches/{branch}/products/{product}` | Same write body, `X-Nexa-CSRF` required |

Catalog derives organization/application from validated tenant headers. BFF derives these
from its subject-bound selected-tenant cookie, obtains a valid server-held bearer and rechecks
current Directory membership. Browser-supplied organization parameters confer no authority.
There is no workload permission bypass or guest management route. PUT bodies are bounded to
4,096 bytes; BFF buffers at most65,536 response bytes. Missing/null availability or expected
version, negative version and invalid actor identifiers are rejected. A missing scoped product
returns404; denied access403; stale versions409 (`menu_availability_changed`); invalid input400;
dependency failure503. Missing session/tenant binding returns401. BFF sanitizes error bodies.

Versions are per organization/branch/product. Exact no-op with the current version does not
advance history. A stale version conflicts even when the desired Boolean is already present.
An uncertain response must be followed by reload; the portal discards editable rows instead
of automatically retrying a write. Saves do not replace a whole menu snapshot.

Existing POS/barcode/QR reads use the same live Catalog flag. Refresh is required to display
a manager change. QR quote review, staff acceptance and original POS placement check fresh
availability. An already quoted request can still be submitted for staff review after a change;
it remains pending and acceptance conflicts while any item is unavailable. Staff may reject
it or reconsider after availability is restored. Accepted orders, original retries, receipts
and payment collection retain their captured identity/state. A request already validated before
a concurrent change may proceed: this is not a distributed inventory reservation or a
linearizable cross-service selling cutoff. Accepted-order recovery never reprices history.

## Diagnostics

Existing service identities `nexaconnect-catalog` and `nexaconnect-customer-bff` provide shared
structured request/OTLP telemetry. The new adapter uses redirect-disabled `MenuAvailabilityCatalog`
and correlation propagation; Directory uses its existing correlated client. Search structured
logs for `Menu availability authorization denied`, `Menu availability BFF rejected`, or
`Menu availability unavailable`, then follow the validated correlation across BFF and Catalog.
Events include safe status/category only; never log callback/session credentials, bodies,
product names, payment information or arbitrary headers. See [schema](../Database/Menu-Availability.md)
and [rollout/tests](../Deployment/POS-UX13-Menu-Availability.md).
