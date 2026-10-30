# POS drawer intent API

UX8D configuration: list and selected `/review` GETs require `DrawerIntents:Enabled` **or** `DrawerIntents:HistoryEnabled` (both default false). History-only mode preserves every live scoped read check while reserve, outcome and legacy single-record routes still require `Enabled`; those remain503 when it is false. Safe failure codes/correlation telemetry are unchanged. See [rollout](../Deployment/POS-UX8D-Pilot-Acceptance.md).

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
| GET | root | UX8C: restaurantId/branchId/storeId/terminalId, fromUtc/toUtc, limit1–50(default25), optional cursor; own-actor DrawerHistoryPageV1 |
| GET | `/{operationId}/review` | UX8C: same terminal scope query; original own-actor DrawerHistoryItemV1 or404 after live authorization |

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
successful response consumes the intent. No reset, renew or delete endpoint. UX8C adds
read-only history listing/review, never a submission grant.

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

## UX8C history contracts

POS25/matching service/native binaries add an index-only history surface under existing
`pos.drawer.read` and either `DrawerIntents:Enabled` or `DrawerIntents:HistoryEnabled`.
List requires exact terminal relationship,
own authenticated actor and a half-open UTC reserved-at window of at most31days. Limit1–50,
native25; optional opaque keyset cursor is bound to actor/scope/window but is not a credential.
Every list/review checks live hierarchy/permission/relationship before and after database I/O.
Closed custody does not block a read. Wrong actor or absent original is404 after authorization;
invalid scope/permission is403; invalid cursor/window/limit400. No existence read bypasses live
authority. Reads are check-time observations, not distributed atomic revocation.

DrawerHistoryPageV1 echoes organization/restaurant/branch/store/terminal, fromUtc/toUtc, items
and nextCursor. DrawerHistoryItemV1 contains operationId, fixed cause/reason, optional orderId,
original shiftId/cashSessionId, status and reserved/expiry/report times. It contains no grant,
actor identity, Authorization decision, amount or payment-provider data. Selected review
returns that item or404. Native list/selected bodies cap at64KiB/4KiB and complete operations
at10seconds; server use case deadline15seconds. The same correlation foundation and safe
Drawer history events apply, without bodies/cursors/headers/subjects.

See [native workflow, legacy/context limitations and rollout](../Deployment/POS-UX8C-Drawer-History.md).

UX8B's prepared native consumer maps fixed causes/reasons and validates original operation,
status/lease, current subject/issuer/lock and configured scope. Cookie-free, redirect-disabled
HTTP bounds successful JSON to4096bytes and complete operations to10seconds; outcome reporting
has an independent5-second caller deadline. Local intent consumption precedes HTTP. Only a
fresh original-cash completion can reach that prepared hook; no default hardware or new API
contract is enabled. See [native behavior and limits](../Deployment/POS-UX8B-Native-Drawer.md).
