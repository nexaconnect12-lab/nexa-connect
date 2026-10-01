# Pre-payment order cancellation

`POST /api/order/v1/orders/{orderId}/cancellations` records and executes an operator-requested cancellation before payment. It is an online, branch-scoped command and requires an authenticated user subject plus a live `order.cancel` decision. Tenant administrators, store managers, and cashiers receive that permission through Authorization migration 8.

## Request

The request body contains `organizationId`, `branchId`, `operationId`, `reason`, and optional `correlationId`. The tenant headers must contain the same organization and `X-Nexa-Application-Code: nexa_connect`. `operationId` is a stable UUID retained across timeout, restart, and response loss. The trimmed reason must contain 1–200 printable characters. Before network send, POS stores the exact reason and pending state with its protected settlement recovery identity, which is reused as the cancellation operation. Restart permits only exact verification while payment remains disabled.

Cancellation is accepted only from `Submitted`, `InventoryReserved`, or `KitchenAccepted`. `PaymentPending`, `PaymentReview`, `PaymentFailed`, `Paid`, rejected, cancellation-review, and already-cancelled Orders reject a new operation. An exact operation-and-reason replay returns the recorded result; reuse with a changed reason or a second operation returns `409`.

## Responses

- `200` with status `completed`: Kitchen work was cancelled when required, Inventory was released when required, and Order committed `Cancelled`.
- `202` with status `pending`: the durable request exists and the recovery worker will retry a dependency failure.
- `409` with status `blocked`: Kitchen reported terminal preparation. Order is `CancellationReview`; Inventory remains reserved to avoid an unsafe partial cancellation.
- `400`: invalid identity or reason. `403`: missing/revoked permission or stable subject. `404`: Order absent or outside the supplied tenant/branch scope. Other `409` responses describe an ineligible state or conflicting replay.

Do not collect payment while status is pending or blocked. A blocked request requires operational investigation; the first slice deliberately provides no automated override. Completed preparation is never converted into an Inventory release by this workflow.

## Reliability and audit

Order migration 10 stores one immutable cancellation identity, original state, actor subject, authorization decision, correlation, dependency plan, attempt state, fencing claim, and terminal result. Creation, state transition, audit, and versioned integration events commit through Order's local transaction/outbox. The worker cancels Kitchen before releasing Inventory; both downstream operations are idempotent by Order identity. Transport failure releases the claim for delayed retry, process loss recovers after lease expiry, and a stale claimant cannot commit. PostgreSQL deployments must keep `WorkflowRecovery:Enabled=true`; its validated poll, lease, and retry settings also drive cancellation recovery.

Events are `order.cancellation-requested.v1`, `order.cancelled.v1`, and `order.cancellation-review-required.v1`, with safe `order.audit.v1` records. Reporting migration 16 accepts their audit vocabulary. Logs exclude reason and identifiers; use the validated correlation ID with `{service_name="nexaconnect-order"}` and inspect `order.cancellation_recovery.steps`. Apply Authorization 8, Order 10, and Reporting 16 before enabling the control.

This contract covers pre-payment voiding only. Captured payments require the separate refund workflow and immutable refund receipt planned next.
