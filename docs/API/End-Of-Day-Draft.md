# Single-branch end-of-day reconciliation draft

The Customer Portal **End-of-day draft** is a read-only observation of one completed branch business date. Reporting orchestrates authorized owning-service reads; it never connects to their databases, runs financial repair, approves reviews or closes a day. `status` is always `draft`. See [ADR-020](../Architecture/Decisions/ADR-020-branch-end-of-day-draft.md) and the [deployment guide](../Deployment/End-Of-Day-Draft.md).

## Routes and access

`GET /bff/customer/reports/end-of-day?branchId={uuid}&businessDate=2026-09-01` requires the Customer session, matching protected tenant subject, current Directory product membership and `nexa_connect`. Organization and bearer token come from the server session; browser-supplied organization/actor/UTC/timezone parameters are not forwarded.

`GET /api/reporting/v1/customer/organizations/{organizationId}/reports/end-of-day?branchId={uuid}&businessDate=2026-09-01` requires an authenticated customer role and live branch `reporting.sales.read`, resolved through Restaurant hierarchy. Every operational read independently authorizes the same customer bearer:

| Owning API | Live permission | Data returned |
| --- | --- | --- |
| `GET /api/order/v1/customer/end-of-day` | `order.read`, with Directory membership and Restaurant hierarchy | Gross sales, completed order count, gross receipt tenders, unresolved orders, missing evidence count |
| `GET /api/payment/v1/customer/end-of-day` | Both `payment.intent.read` and `payment.refund.read`, with Directory membership and Restaurant hierarchy | Completed refunds, unresolved intent/refund counts, missing evidence count |
| `GET /api/pos/v1/customer/end-of-day` | Branch `pos.cash-review.read`, with Restaurant hierarchy | Open shifts/cash sessions, pending reviews, closed drawer variance |

These source APIs accept `organizationId`, `restaurantId`, `branchId`, `fromUtc`, `toUtc`. Application rejects empty IDs and open, nonpositive or over-27-hour windows; all SQL values are parameterized. Order/Payment predicates include all three owner IDs. POS stores own restaurant/branch IDs, so Application must first resolve and validate organization through Restaurant before reading them. Source APIs require customer roles; workload tokens cannot replace customer financial permission. Existing role assignments are not expanded: an accountant may need reviewed additional read grants if their current role lacks `order.read` or `payment.intent.read`.

Restaurant adds `GET /api/restaurant/v1/branches/{branchId}/business-calendar` under the existing `BranchScopeReader` policy, returning active organization/restaurant/branch IDs, configured timezone and currency. The existing authorization-scope HTTP response remains three IDs. Reporting's dedicated workload token is used only for metadata reads; source financial reads carry the customer token.

All report/source/calendar responses are no-store. Invalid input returns `400`; denied access `403` (missing session/authentication `401`); mixed source/branch currencies `409`; unavailable source/database, invalid metadata or timeout `503`. Owner-service transport/read failures return sanitized `503` and never partial totals. Existing live Directory membership clients conservatively deny with `403` on non-success access responses, which may include dependency failures; a denial does not distinguish revoked membership from an unavailable access lookup. Reporting has a 25-second overall Application deadline, owning-service HTTP clients 15 seconds, Customer BFF 30 seconds and browser 40 seconds. Caller cancellation propagates.

## Business date and amounts

Reporting uses Restaurant's current branch timezone, never the browser timezone. Local midnight to next local midnight becomes an inclusive/exclusive UTC window. Normal DST days can be 23 or 25 hours. Invalid/ambiguous midnight boundaries and boundaries that cannot round-trip are rejected. Dates governed by historical base-offset differences of 12 hours or more are also rejected because Windows timezone history can omit date-line gaps. Conversion uses host .NET/OS timezone data; keep it current and verify the branch zone on the deployment host. This draft does not reconstruct historical timezone configuration. Default/max dates, current incomplete dates and future dates are rejected.

| Value | Authoritative basis |
| --- | --- |
| `grossSales` / Order `completedOrders` | Currently completed Orders created in the day, matching existing sales report order-time semantics |
| `tenders` | Completed Order immutable receipts paid in the day, grouped by receipt tender and currency; gross receipts before refunds |
| `completedRefunds` | Payment refunds completed in the day, including refunds of older sales |
| `netSales` | Gross sales minus completed refunds; may be negative |
| `cashVariance` | Counted minus expected cash for sessions closed in the day, recomputed from current authoritative movements |

Orders created on an earlier day but paid in this day contribute to tenders, not this day's gross sales. Expected cash includes opening float, sales, refunds, pay-ins, pay-outs and float adjustments, so drawer variance is not tender receipts minus refunds. Multiple drawer variances can cancel in the summed value; pending review counts remain independently visible.

Unresolved Orders are all nonterminal Orders created before day end. Unresolved payments exclude captured/failed/cancelled/expired/voided; unresolved refunds exclude completed/failed. Open shifts include `closing`, open cash sessions include `counting`. Pending reviews include earlier nonzero closed drawers whose current financial version lacks approval. These are **current** states as observed for items originating before the selected day ended, not reconstructed historical states at midnight. Closed-session variance selects close time; earlier unresolved work remains visible.

## Evidence and response

The response contains `businessDate`, `branch`, exact `window`, `status`, top-level amounts/tenders, individual `order`/`payment`/`pos` summaries with scope and `observedAtUtc`, current Reporting `projection`, nullable `recordedCompleteness`, and stable `issues` codes.

Order uses one repeatable-read transaction for aggregates/tenders; Payment and POS each use one statement snapshot. Observation times mark read completion, not a completeness watermark. Reporting rejects source observations later than its own current clock, so source and Reporting hosts require aligned clocks. Reporting compares source gross sales/refunds/completed-order counts against its projection and flags `projection_totals_differ`; matching aggregate totals alone cannot prove identity-level completeness. Its projection and latest exact-window recorded check are separate reads. There is no distributed snapshot or day lock.

`missing_source_evidence` counts receipt-backed sale candidates selected by creation or Paid time without a receipt or retained sale publication, and completed refund candidates without a receipt or retained original refund publication. These are existence checks, not full receipt/hash validation; the privileged financial completeness tool remains responsible for identity/value/hash checks. Missing older tender receipts cannot be reconstructed and must not be treated as zero verified tender receipts.

Issue codes are `unresolved_orders`, `unresolved_payments`, `unresolved_refunds`, `open_shifts`, `open_cash_sessions`, `pending_cash_reviews`, `cash_variance`, `missing_source_evidence`, `projection_totals_differ`, `financial_evidence_not_checked`, `recorded_check_is_historical`, `recorded_financial_gaps`. Every recorded check is labeled historical relative to independent fresh reads, including `observed_complete`; it cannot certify settlement. The page shows source times, operational counts, missing source evidence and readable issues. Filter, reload, tenant changes, denied access, failed/malformed reads and late responses clear or discard old results. No polling, exports or mutations are supplied.

New service events use existing shared JSON/OTLP telemetry and validated outbound correlation propagation. Query `{service_name="nexaconnect-reporting"} |= "End-of-day draft"`, the owning `nexaconnect-order`, `nexaconnect-payment`, `nexaconnect-pos` streams for `End-of-day source`, and `nexaconnect-customer-bff` for `End-of-day draft BFF`. Request telemetry also covers Restaurant calendar reads. Bodies, financial amounts, credentials, actor/reason data and arbitrary headers are never logged by these paths.
