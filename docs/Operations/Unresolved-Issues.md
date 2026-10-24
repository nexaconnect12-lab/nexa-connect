# Development issue register

## Reporting OpenAPI GUID schema export

**Status:** Open; observed during slice 1 local joined startup on 14 October 2026.

Authenticated `GET /openapi/v1.json` in Reporting throws `System.Text.Json.JsonException` while the OpenAPI schema exporter converts a value to `Guid`. The failing default/type has not been isolated. This is metadata-generation evidence, not proof that financial queries fail. The local draft uses an explicit anonymous `/health/live` and separately verifies migration/runtime grants, workload audiences and active financial consumers. Fix the exporter/contract incompatibility with a focused metadata regression before relying on this document for client generation. No customer financial validation or authorization bypass was introduced. See [draft initialization and limitations](../Deployment/First-Draft-Test-Stack.md).

## POS shift authorization persistence mismatch

**Status:** Resolved in the current development build and verified on 2026-08-07.

**Observed behavior:** The WPF POS client authenticates successfully, Restaurant returns the branch authorization scope with HTTP 200, and the Authorization decision endpoint returns HTTP 200 with `granted: false`. The POS displays “No active shift on this terminal. Your account is not authorized for this terminal.”

**Verified data:** The Docker PostgreSQL query shows an active `cashier` assignment and active `allow` override for subject `nexa_pos`, permission `pos.shift.open`, organization `f0955e44-e8c1-56b6-a5d9-43cb1ebb17fe`, restaurant `fa9711c5-4a59-5910-b824-83392f64a533`, and branch `042f9a90-c603-52f8-9683-6dd865bd3467`. The development Platform Directory sample also provisions this subject as an active member of the same organization so tenant-scoped Catalog reads reach product authorization.

**Root cause:** The role-assignment provisioning path created the role assignment but did not reliably materialize the scoped `authorization_user_permission_overrides` row used by the decision query. The service therefore evaluated the same subject and branch with `explicitGrant=false` even when the assignment table appeared populated.

**Fix:** Infrastructure provisioning now reads back the persisted active scope from the assignment and inserts/reactivates the scoped `allow` override in the same database connection. The direct decision request returned `granted: true` for `nexa_pos` and `pos.shift.open` after reapplying the assignment.

**Operational note:** Development services now use per-service writable Data Protection key directories under `.runstate/data-protection-keys`. Production still requires a durable, service-owned key store with explicit access control.

**Database lifecycle note:** The scoped assignment is business data. Recreating PostgreSQL with a volume reset (for example, `docker compose down -v`) removes it. After any database reset, provision the cashier assignment again through the authenticated assignment endpoint before testing POS shifts; do not add a controller-level bypass or seed a permanent administrator grant.
