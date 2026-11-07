# Manual cash and PromptPay refunds

The POS API uses the prefix
`/api/pos/v1/customer/organizations/{organizationId}/branches/{branchId}/orders/{orderId}/manual-refunds`.
Send a customer bearer with the `nexaconnect-api` audience, the matching
`X-Nexa-Organization-Id` and `X-Nexa-Application-Code: nexa_connect`.
POS checks live Platform Directory membership and Restaurant branch ownership.

| Method and suffix | Behavior |
| --- | --- |
| GET `/preview` | Manager reviews the verified original tender, paid total and advisory available amount. |
| GET `/cashiers` | Lists available branch cashiers for this tender, up to 50. |
| GET `/managed` | Lists the signed-in manager's approvals for the selected order, up to 25, with untouched approvals first. |
| POST `/approve` | Manager reserves the amount using `operationId`, `amount`, `reason`, `cashierSubject` and `cashierShiftId`. New amounts use whole satang. |
| GET `/{operationId}` | Original manager or designated cashier reads the exact operation after a live permission check. |
| POST `/{operationId}/release` | The original manager releases an untouched approval. A started payout returns a conflict. Release and start serialize on the same sale. |
| POST `/{operationId}/start` | Designated cashier binds payout using `terminalId`, `shiftId` and the paying `cashSessionId` for cash. |
| POST `/{operationId}/complete` | Designated cashier submits the same IDs under `custody`. PromptPay also requires `transferVerified: true` and a `transferReference`. Cash uses `transferVerified: false` and no transfer reference. |

`GET /api/pos/v1/customer/organizations/{organizationId}/branches/{branchId}/manual-refunds/assigned`
returns up to 25 outstanding approvals for the signed-in cashier. Started operations
appear before untouched approvals, oldest approval first within each group.
`moreAvailable` indicates outstanding work beyond the first batch. Reload after
resolving existing items to see more. The queue requires
current branch payout permission and organization membership. It never returns
another cashier's rows, completed operations, or transfer proof. Like exact recovery,
this read remains available when new refund work is disabled.

`POST /api/pos/v1/customer/organizations/{organizationId}/branches/{branchId}/manual-refunds/available`
registers the signed-in cashier's current `terminalId` and `shiftId` for five minutes
after a live membership and payout permission check. Approval rechecks that
registration and the open shift. Cash also requires the open THB drawer.
The cashier must still have live membership and payout permission when taking action.
The manager-only cashier picker returns the subject bound to the shift ID so the
approval can identify the selected cashier. The Windows picker labels candidates by
terminal and shift.

An approval needs branch access for `pos.manual-refund.approve` and an active
restaurant-level THB approval limit that covers the amount. Payout requires
`pos.manual-refund.payout` and the
designated cashier's identity. A matching original operation is replayable. A replay
of `start` is evidence of an earlier start. Never instruct the cashier to pay again
from a replay. After a lost response, read the exact operation before moving funds.
Operation responses return `approved`, `started`, `completed` or `released`, its timestamps
and a replay indicator. They omit transfer references, authorization decision IDs,
and identity subjects. Authentication or customer-role rejection can return 401 or 403.
Missing or denied scoped operations return 404, invalid input 400,
state conflicts 409, and unavailable dependencies or timeouts normally return 503.
Order's existing membership and branch authorization can also return a coarse 404
when a dependency declines the access lookup. Successful and error
responses are non-cacheable.

Order provides `GET /api/order/v1/orders/{orderId}/manual-refund-sale` with query
`restaurantId` and `branchId` and the same tenant headers. It requires live
`order.read` and `pos.manual-refund.approve`, independently checks scope and validates
the original settlement against the immutable Paid receipt. It returns only the
scoped settlement identity, method, currency, amount and paid time. Cash and manual
PromptPay qualify. Provider payments use Payment's existing refund workflow.

`ManualRefunds__Enabled` defaults to false. It gates preview, cashier availability,
cashier selection, approval and start. Scoped lookup, untouched release and completion
of an existing started operation remain available so
disabling new work does not strand an original payout. Keep it off for real payouts
until operator workflow and reconciliation are ready.
[Deployment and verification](../Deployment/POS-Manual-Refund-Review.md)

Follow the validated correlation ID through `nexaconnect-pos` and
`nexaconnect-order`. Use fixed events starting with `Manual refund` and dependency
HTTP status to diagnose failure and authorization boundaries. Keep bearer tokens,
request/response bodies and transfer proof out of logs.
The native client uses `pos.manual-refund.assigned`, `pos.manual-refund.start`,
`pos.manual-refund.read`, and `pos.manual-refund.complete` under `nexaconnect-pos-client`.
Manager and cashier readiness calls use the same prefix with the operation name
`available`, `cashiers`, `preview`, `managed`, `approve` or `release`.
