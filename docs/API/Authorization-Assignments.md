# Authorization role assignments

Standard `tenant-admin` and `store-manager` assignments include branch read/manage, configuration read/manage, reporting dashboard/sales read, and media asset read. `accountant` receives reporting reads; `report-viewer` receives reporting and media reads. Restaurant customer branch/configuration controllers additionally require the coarse `customer-owner` or `customer-admin` realm role, so a product `store-manager` assignment alone cannot enter those endpoints.

`POST /api/authorization/v1/role-assignments` creates or reactivates an idempotent, branch-scoped role assignment. The caller must hold `system-admin`, `platform-owner`, or `platform-admin`; Platform Admin BFF proxies it at `POST /bff/platform-admin/authorization/role-assignments`. The service persists the selected role's implemented permission set and scoped overrides through Authorization Infrastructure.

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

Success returns `200` with `{ "assignmentId": "<uuid>" }`; repeating the same subject, role, and scope reactivates the assignment and returns its active identifier. Invalid or unsupported role/scope input returns `400`. Supported codes are `tenant-admin`, `store-manager`, `cashier`, `inventory-controller`, `accountant`, and `report-viewer`; the first two receive the full currently implemented tenant API permission set, while the remaining mappings are listed in [Business Service API Slices](Business-Service-API-Slices.md). Direct API authorization accepts `system-admin`, `platform-owner`, or `platform-admin`; the BFF additionally requires its `PlatformAdmin` session policy before forwarding.
