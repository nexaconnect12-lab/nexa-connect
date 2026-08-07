# Development issue register

## POS shift authorization persistence mismatch

**Status:** Resolved in the current development build and verified on 2026-08-07.

**Observed behavior:** The WPF POS client authenticates successfully, Restaurant returns the branch authorization scope with HTTP 200, and the Authorization decision endpoint returns HTTP 200 with `granted: false`. The POS displays “No active shift on this terminal. Your account is not authorized for this terminal.”

**Verified data:** The Docker PostgreSQL query shows an active `cashier` assignment and active `allow` override for subject `nexa_pos`, permission `pos.shift.open`, organization `f0955e44-e8c8-56b6-a5d9-43cb1ebb17fe`, restaurant `fa9711c5-4a59-5910-b824-83392f64a533`, and branch `042f9a90-c603-52f8-9683-6dd865bd3467`.

**Root cause:** The role-assignment provisioning path created the role assignment but did not reliably materialize the scoped `authorization_user_permission_overrides` row used by the decision query. The service therefore evaluated the same subject and branch with `explicitGrant=false` even when the assignment table appeared populated.

**Fix:** Infrastructure provisioning now reads back the persisted active scope from the assignment and inserts/reactivates the scoped `allow` override in the same database connection. The direct decision request returned `granted: true` for `nexa_pos` and `pos.shift.open` after reapplying the assignment.

**Operational note:** The attached log also contains ASP.NET Data Protection DPAPI/key-folder warnings. They do not affect the bearer-token decision, but should be cleaned up before production by giving the service account ownership of its key directory or configuring a service-owned key store.

**Database lifecycle note:** The scoped assignment is business data. Recreating PostgreSQL with a volume reset (for example, `docker compose down -v`) removes it. After any database reset, provision the cashier assignment again through the authenticated assignment endpoint before testing POS shifts; do not add a controller-level bypass or seed a permanent administrator grant.
