# Authorization role assignments

`POST /api/authorization/v1/role-assignments` creates or reactivates an idempotent, branch-scoped role assignment. The caller must satisfy the `system-admin` policy. The cashier assignment provisions both `pos.shift.open` and `pos.shift.close`; the service resolves the branch scope and persists the assignment and scoped overrides through Infrastructure.

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
