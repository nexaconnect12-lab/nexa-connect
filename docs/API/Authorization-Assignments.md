# Authorization role assignments

## Decision policy

`POST /api/authorization/v1/decisions` evaluates the authenticated subject with `organizationId`, optional `restaurantId`/`branchId`, `permission`, and optional `amount`/`currency`. It returns `200` with `decisionId`, `granted` and `evaluatedLimit`, including denied decisions. Missing subject identity is forbidden. The caller must supply the resource's validated hierarchy; a decision is not proof of ownership by itself.

The most specific active matching user override replaces role fallback: branch precedes restaurant, which precedes organization. At equal specificity, deny wins; an unknown selected effect fails closed. Without an override, an active role assignment and active role must match the requested organization and hierarchical scope and carry the permission. A more specific allow can replace a broader deny.

If an amount is supplied, permission alone is insufficient: the amount must be nonnegative and no greater than a present active approval limit matching restaurant, action and currency. The evaluated limit is the maximum eligible subject or matching-role limit. Roles assigned outside the requested scope, inactive roles and inactive assignments cannot contribute limits. Without an amount, no financial limit is evaluated.

Infrastructure reads overrides, roles and limits in one PostgreSQL statement snapshot. Domain policy evaluates that evidence; Application persists the decision with `policy_version=2` before returning it. A failed audit write fails the request rather than returning an unaudited grant. Every invocation rereads policy without a grant cache. Policy changes after the read snapshot can affect the next decision, but do not cancel an in-flight decision; policy read, audit insertion and the caller's later business transaction are not one atomic transaction. No schema migration or new grant accompanies this policy correction.

Operational decision logs contain only the decision UUID and granted flag, with shared correlation context. Subject, scope, permission, amounts, database diagnostics and request bodies are excluded. The restricted decision audit remains Authorization-owned.

Authorization migration 6 grants `order.manual-payment.confirm` to existing `cashier`, `store-manager`, and `tenant-admin` roles; runtime assignment provisioning grants it to roles created later. Normal branch/restaurant/organization hierarchy still applies and Order revalidates the exact branch. A controlled `6→5` downgrade deletes only these associations; disable manual settlement before rollback.

Authorization migration 7 grants `pos.cash-review.read` and `pos.cash-review.resolve` to existing and newly assigned `tenant-admin` and `store-manager` roles. Existing and newly assigned `accountant` roles receive read only. POS revalidates Restaurant hierarchy and POS-owned store scope before each decision request. A controlled `7→6` downgrade deletes only these review associations; disable POS Cash Review reads and decisions before rollback.

Media mutations additionally require `media.asset.manage`; standard `tenant-admin` and `store-manager` assignments receive it together with `media.asset.read`. Kitchen reads and transitions require `kitchen.ticket.read` and `kitchen.ticket.transition`; Authorization migration 3 backfills both permissions for existing assignments of those operational roles.

Order payment-review reads and operator decisions require `order.payment-review.read` and `order.payment-review.resolve`. Authorization migration 4 backfills both for existing `tenant-admin` and `store-manager` roles. Migration 5 adds `order.payment-review.read` to existing `accountant` roles, while runtime assignment provisioning gives newly created tenant administrators/store managers both permissions and accountants read only. Organization equality and Restaurant-owned hierarchical scope still constrain every decision. A destructive `5→4` downgrade removes only the accountant read associations; a subsequent `4→3` removes all Payment Review associations. Disable the corresponding reader and resolver routes before each downgrade.

Standard `tenant-admin` and `store-manager` assignments include branch read/manage, configuration read/manage, reporting dashboard/sales/activity read, media asset read, Kitchen ticket read/transition, and Payment Review read/resolve. `accountant` receives reporting reads plus Payment Review read; `report-viewer` receives reporting and media reads. Restaurant customer branch/configuration controllers additionally require the coarse `customer-owner` or `customer-admin` realm role, so a product `store-manager` assignment alone cannot enter those endpoints.

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
