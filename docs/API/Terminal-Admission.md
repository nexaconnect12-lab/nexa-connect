# Manual-tender terminal admission

New manual settlements require matching Order/POS binaries and Order `Services:POS`.
The draft launcher and checkout development launcher supply this dependency. There is no
new schema, customer permission, Payment-provider call or shared database access.

`POST /api/pos/v1/internal/manual-tender-admission` accepts only the exact
`nexaconnect-order-service` client-credentials principal with API audience `nexaconnect-api`,
matching `azp` and `preferred_username=service-account-nexaconnect-order-service`. The realm
template supplies the access-token-only `order-service-account-username` user-property mapper;
persisted realms require a reviewed mapper update and fresh Order token before enabling
checkout. Restarting Keycloak does not update an existing realm. Customer
tokens, POS workload tokens and unrelated workloads receive `403`. Requests contain
`organizationId`, `restaurantId`, `branchId`, `terminalId`, `operatorSubjectId`, `method`
(`cash` or `promptpay_manual`) and `currency` (`THB`); the body is bounded to 4096 bytes.
Order supplies scope from the authorized stored Order and the authenticated cashier,
never an operator identity selected by the customer payload.

POS Application resolves Restaurant-owned hierarchy and requires exact organization,
restaurant and branch equality. Its Infrastructure query requires an active terminal
and store in that branch, an open shift for that terminal and cashier, and for cash an
open THB drawer on that shift. PromptPay requires the shift but does not require a drawer.
Unknown, inactive, wrong-scope or unready terminals return `409`; malformed input `400`;
unavailable hierarchy/persistence `503`. Responses are no-store. A successful `200` returns
`admittedAtUtc`, observed by POS before its eligibility query.

Order calls admission after validating the manual tender and before mutating the aggregate
or committing Paid, receipt, audit and outbox. Conflict maps to `409 terminal_not_ready`;
transport, timeout or malformed proof maps to `503 terminal_admission_unavailable`.
Admission time must be within thirty seconds of Order's pre-call clock. The admitted time
is retained as the original settlement/event/receipt occurrence time so a later drawer
close does not lose attribution. Matching committed idempotent replay is resolved first,
with the existing live customer permission, and remains readable after shift closure or
POS outage. Changed retries still conflict. No second collection is authorized by a retry.

Admission is a point-in-time cross-service observation, not an atomic reservation or
distributed lock. Closure/revocation after the observation may race an in-flight commit;
the historical terminal/shift/drawer projection checks remain authoritative. Clock skew,
terminal revocation policy for in-flight work and broader distributed financial fencing
remain distinct operational concerns. This slice does not repair prior invalid-terminal
settlements or claim general offline device authorization.

Both hosts retain shared JSON/correlation/optional OTLP telemetry. Order's adapter propagates
validated correlation and its existing workload bearer; it has a fifteen-second timeout.
Query `Manual tender admission` in `nexaconnect-pos`, `Manual tender terminal admission` /
`Manual settlement admission unavailable` in `nexaconnect-order`, then follow `CorrelationId`.
Events contain bounded status/category only; no tokens, bodies, operator subjects or tender
values are logged by this boundary.

Verification: unit authorization/scope/replay guards and the joined draft runner's invalid
terminal and wrong-cashier attempts before the original successful cash settlement. See
[Windows draft acceptance](../Deployment/First-Draft-Windows-POS.md) for executed status.
