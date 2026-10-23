# Order QR3 persistence

Order migration20 `0020_qr_guest_requests`, additive optional capability, minimum migration
compatibility0.38.0. Apply before enabling QR requests. Current full schema catalog adds
three tables to QR1's204=207. No existing financial schema/owner/history is rewritten.

- `order_qr_quotes`: exact public commercial JSON snapshot, opaque quote UUID, fingerprint,
  owning organization/restaurant/branch, visitor/entry UUIDs, table label and expiry.
  Maximum stored JSON131072bytes. No raw visitor/entry capability or browser cookie.
- `order_qr_requests`: quote UUID is its primary/idempotency identity, scoped composite FK,
  visitor/entry, state/version, submitted/deadline timestamps and fixed rejection category.
  Partial unique index prevents two pending rows per visitor/owner/branch.
- `order_qr_request_audit`: immutable transition UUID, scope/request/version/action,
  actor, optional authorization decision, correlation and timestamp. Unique request/version;
  append-only trigger denies UPDATE/DELETE. It deliberately has no retention-blocking FK.

Supporting indexes are `ix_order_qr_quotes_scope_expiry` for scoped expiry,
`ix_order_qr_requests_queue` for branch/state/submission order and
`ix_order_qr_requests_visitor` for private status. `ux_order_qr_pending_visitor` is the
partial pending uniqueness guard; audit also uniquely binds request/version.

Infrastructure takes a PostgreSQL transaction advisory lock over the owning org/branch
using a parameterized fixed namespace. Hash collisions serialize extra unrelated scopes,
never merge their data. All predicates remain parameterized organization+branch/visitor.
Reads apply expiry and cleanup atomically under that lock; they therefore require write
privileges for this capability. Domain evaluates admission counts and state/version rules;
SQL constraints support the documented invariants. Transactions never span HTTP.

Runtime grants on these three tables: quote SELECT/INSERT/DELETE; request SELECT/INSERT/
UPDATE/DELETE; audit SELECT/INSERT only, plus existing outbox INSERT. No audit UPDATE/DELETE/
TRUNCATE or migration ownership. Migration identity owns objects; existing Order data
credentials stay isolated. Verify actual least-privilege grants on deployment; disposable
tests use the generated administrator, not production privilege certification.

Quote expiry2minutes/visitor expiry; pending deadline15minutes/visitor expiry. Scoped
access expires pending aggregates and appends audit/outbox. Terminal requests older than
24hours and unreferenced expired quotes are pruned. Consumed quotes remain until their
request is pruned. Audit/outbox is retained under operating retention, without automatic
deletion; access-driven cleanup is not a guaranteed deletion schedule. Replay retention
exceeds maximum visitor lifetime. Ending/rescanning a visit does not cancel a request.

Downgrade refuses any request/audit history, even after request cleanup; use forward
recovery. With no request history, disable QR3 and explicit destructive downgrade removes
quotes and these objects only. Migration20 does not modify sale tables or branch schema.
Restore quotes/requests/audit/outbox coherently; restored admissions must follow QR1's
rotation/session-discard policy before reopening traffic. See [operations](../Deployment/QR3-Requests.md).

The disposable QR persistence launcher now requires three cases: Restaurant admission,
Catalog menu isolation and full Order migration/request replay/concurrency/audit rollback.
No retained database is targeted. See [contract/invariants](../API/QR-Requests.md).
