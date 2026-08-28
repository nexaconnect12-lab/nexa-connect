# Authorization role assignments

Media mutations additionally require `media.asset.manage`; standard `tenant-admin` and `store-manager` assignments receive it together with `media.asset.read`. Kitchen reads and transitions require `kitchen.ticket.read` and `kitchen.ticket.transition`; Authorization migration 3 backfills both permissions for existing assignments of those operational roles.

Order payment-review reads and operator decisions require `order.payment-review.read` and `order.payment-review.resolve`. Authorization migration 4 backfills both for existing `tenant-admin` and `store-manager` assignments; organization equality and Restaurant-owned branch scope still constrain every decision. Downgrade removes these permission associations and therefore must follow route disablement.

Standard `tenant-admin` and `store-manager` assignments include branch read/manage, configuration read/manage, reporting dashboard/sales/activity read, media asset read, and Kitchen ticket read/transition. `accountant` receives reporting reads; `report-viewer` receives reporting and media reads. Restaurant customer branch/configuration controllers additionally require the coarse `customer-owner` or `customer-admin` realm role, so a product `store-manager` assignment alone cannot enter those endpoints.

`POST /api/authorization/v1/role-assignments` creates or reactivates an idempotent hierarchical role assignment. The caller must hold `system-admin`, `platform-owner`, or `platform-admin`; Platform Admin BFF proxies it at `POST /bff/platform-admin/authorization/role-assignments`. The service persists the selected role's implemented permission set and scoped overrides through Authorization Infrastructure.

Scope is role-specific: `tenant-admin` requires organization scope (`restaurantId` and `branchId` omitted or null), `store-manager` requires restaurant scope (`restaurantId` present and `branchId` omitted or null), and the remaining supported roles require organization, restaurant, and branch. A branch cannot be supplied without its restaurant. Broader active scopes apply to matching descendants, while the organization predicate always prevents cross-tenant decisions.

The current customer Branch list and Media list APIs authorize at organization scope because their requests do not carry a restaurant filter. Use an organization-scoped `tenant-admin` assignment for those organization-wide pages. A restaurant-scoped `store-manager` assignment covers restaurant-bound operations but does not implicitly satisfy an organization-wide list decision; restaurant-filtered list contracts remain follow-up work.

Example request:

```json
{
  "subjectId": "nexa_pos",
  "organizationId": "f0955e44-e8c8-56b6-a5d9-43cb1ebb17fe",
  "restaurantId": "fa9711c5-4a59-5910-b824-83392f64a533",
  "branchId": "042f9a90-c603-52f8-9683-6dd865bd3467",
  "roleCode": "cashier"
}
```

The endpoint returns `404` when no active matching scope or role/permission exists and `403` for non-administrators.

Success returns `200` with `{ "assignmentId": "<uuid>" }`; repeating the same subject, role, and scope reactivates the assignment and returns its active identifier. Invalid or unsupported role/scope input returns `400`. Supported codes are `tenant-admin`, `store-manager`, `cashier`, `inventory-controller`, `accountant`, and `report-viewer`; the first two receive the full currently implemented tenant API permission set, while the remaining mappings are listed in [Business Service API Slices](Business-Service-API-Slices.md). Direct API authorization accepts `system-admin`, `platform-owner`, or `platform-admin`; the BFF additionally requires its `PlatformAdmin` session policy and same-origin mutation check before forwarding.
