# Branch menu categories and display order (UX11)

Catalog owns a branch's ordered category list and ordered product placements. Layout is presentation metadata, separate from menu price/availability/preparation, photo selection, stock, quotes and financial recovery. Category order is array order; item order within each category follows placement array order. Products without a placement or with a null category appear in implicit **Uncategorized**, after named categories. Clients retain the original item objects/prices when ordering the view.

## Staff and private routes

| Route | Authority / behavior |
| --- | --- |
| GET `/api/catalog/v1/customer/branches/{branch}/menu-organization` | User bearer plus tenant headers; live branch `catalog.menu.read` before/after read |
| PUT same route | Live branch `catalog.menu.write`, repeated before the owning mutation; actor/correlation recorded |
| GET `/api/catalog/v1/internal/organizations/{org}/branches/{branch}/menu-organization` | ServiceWorkload policy and exact `azp=nexaconnect-order-service`; private selected-scope projection |
| GET `/bff/customer/menu-organization/csrf` | CustomerSession cookie; existing antiforgery token |
| GET/PUT `/bff/customer/menu-organization/branches/{branch}` | Selected tenant/stable subject and current Directory membership; PUT additionally requires `X-Nexa-CSRF` |

Catalog customer requests require `X-Nexa-Organization-Id` and `X-Nexa-Application-Code: nexa_connect`. There is no customer-route trusted-workload bypass. BFF never trusts browser organization/actor fields. JSON request/response size is at most65536 bytes. Redirect-disabled clients propagate validated correlation; responses are no-store. No new role or permission grant is introduced.

GET returns `{organizationId, branchId, version, categories:[{id,name}], items:[{productId,categoryId}]}`. An unconfigured branch returns version0 and empty arrays. PUT accepts `{expectedVersion,categories,items}` only. IDs are UUIDs; categoryId may be null. Maximum50 categories,80 characters per name,200 unique product placements. Names are trimmed, nonempty, control-free and unique ignoring ordinal case; category IDs are unique. Referenced categories must exist and placed products must belong to the same branch/organization. Saving is explicit; creating or moving an item in the editor alone sends no mutation. Removing a category requires moving its items to another category or null in the same snapshot; the portal does this explicitly in its draft.

The owning transaction serializes first saves and concurrent edits, validates locked product membership and applies expectedVersion. A changed layout increments version once with atomic immutable audit/generic outbox. Exact current no-op retains version/audit count; stale edits return409 `menu_organization_changed`. Invalid shape/membership returns400 `invalid_menu_organization`; missing tenant context400; live permission denial403; dependency failure503. After a conflicting or uncertain save, discard editable proof and reload rather than retrying blindly.

## Guest delivery

GET `/bff/guest/qr/menu-organization` requires enabled QR entry/menu, exact HTTPS origin and the existing secure guest cookie. BFF forwards only the guest capability to GET `/api/order/v1/guest/qr/menu-organization`; no staff token, tenant header or storage URL crosses this boundary. Order's existing workload reads the exact scoped Catalog layout, validates the shape/scope and rechecks live guest context after reading current product membership. Only current menu product placements and categories used by those placements reach the guest as `{categories:[{id,name}],items:[{productId,categoryId}]}`. Organization, branch, version, stations and prices are absent from this presentation contract. The local guest edge allowlists this exact GET path without query parameters.

The existing menu, cart, quote/fingerprint, acceptance, collection, payment and receipt contracts are unchanged. Layout failure falls back to all items/Uncategorized; category and search filters retain the cart. Server order/quote routes still validate live prices and availability.

## Observability

Services `nexaconnect-catalog`, `nexaconnect-customer-bff`, `nexaconnect-order` and `nexaconnect-qr-pilot-edge` use the shared JSON/OTLP/correlation foundation. Query `service.name` plus correlation ID for `Menu organization`, `Menu organization BFF`, `QR shopping` and native operation `catalog.menu-organization`. Failures record safe category/status; authorization denials remain explicit. Do not log cookies, tokens, bodies, category text, image bytes/URLs, payment data or arbitrary headers. [Storage](../Database/Menu-Organization.md), [deployment](../Deployment/POS-UX11-Menu-Organization.md), [ADR-059](../Architecture/Decisions/ADR-059-menu-organization.md).
