# POS drawer intent API

UX8A adds versioned authenticated no-store routes under
`/api/pos/v1/customer/organizations/{organizationId}/drawer-intents`. They are default-off
behind POS `DrawerIntents:Enabled`; no guest route or physical drawer command is added.
Actor comes from bearer `sub`, never JSON. Route organization must equal body organization.
Live Restaurant hierarchy and Authorization decisions remain authoritative.

| Method | Suffix | Input and result |
| --- | --- | --- |
| POST | root | organizationId, restaurantId, branchId, storeId, terminalId, shiftId, cashSessionId, operationId, cause, nullable orderId/reason; returns DrawerIntentV1 |
| GET | `/{operationId}` | Original actor and live `pos.drawer.read`; returns retained original operation/status with maySubmit=false |
| POST | `/{operationId}/outcome` | organizationId, outcome; original actor and current original-cause permission; reports submitted/unknown/cancelled, never grants another submission |

Writes cap at 4096 bytes and each use case has a linked 15-second deadline. Scope/identity
validation, fixed causes/reasons, original facts, custody and state transitions belong to
Application/Domain; parameterized Infrastructure repositories own database operations.

`cause=manual` requires `pos.drawer.manual-open`, null orderId and reason
`change_required`, `cash_check`, or `supervisor_access`. `cause=cash_sale` requires
`pos.drawer.cash-sale-open`, original orderId and null reason. Both need exact active
terminal/store and the authenticated cashier's original open shift/THB cash session.
Cash-sale admission additionally checks the owning POS immutable confirmed cash projection
for that exact scope and a settlement time within 60 seconds. Client Paid flags, receipts,
other tenders and absent/stale projections cannot grant authority.

Response fields: operationId, status, maySubmit, reservedAtUtc, expiresAtUtc, reportedAtUtc.
New successful reservation returns 200, status reserved and maySubmit=true. Successful retry/read/report
responses have maySubmit=false; an eligible sale retry with another operation ID returns the original
operation. New request/lease duration uses database clock and is fixed at 60 seconds. A lost
successful response consumes the intent. No refresh, reset, renew, delete or listing endpoint.

Reservation retries repeat live authorization and current custody/cash-sale freshness checks
before resolving an existing intent. Closure or a cash projection older than 60 seconds can
therefore return 409, including after a response was lost. Use GET of the original operation
to inspect it, or the outcome route to report a past attempt; neither requires open custody
nor grants another opening. Eligible identical reservations and same-outcome reports are
idempotent. Changed original
facts or terminal outcome conflict; unreconciled Reserved/Unknown cannot issue another grant.
Outcome is unverified client evidence, not bank/physical truth. An original actor may report
after shift/cash closure with current permission because reporting cannot open anything.
There is no transaction spanning HTTP authority and PostgreSQL; UX8B must still fence current
session/lock and expiry immediately before a vendor-verified driver call.

Errors: 400 invalid input, 401 missing identity/bearer, 403 live authority denied or other
actor, 404 absent scoped operation, 409 `drawer_intent_conflict`, 503
`drawer_authority_disabled` or `drawer_intent_unavailable`. Dependency exception content is
suppressed; bodies/tokens/subjects are not logged. Never automatically retry physical action.

See [setup, verification and debugging](../Deployment/POS-UX8A-Drawer-Authority.md).
