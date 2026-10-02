# Payment refunds

Payment owns post-capture refunds. The customer API requires `X-Nexa-Organization-Id`, `X-Nexa-Application-Code: nexa_connect`, a bearer token, exact Order/branch ownership, and a live product authorization decision. `payment.refund.create` is granted only to `tenant-admin` and `store-manager`; `payment.refund.read` also permits cashiers and accountants to retrieve receipts. Create sends amount and currency to Authorization. Permission alone is insufficient: an active approval limit for the subject or matching role, restaurant, `payment.refund.create` action, and currency must cover the amount. The evaluated decision ID is stored with the refund but is not returned by the customer API.

`POST /api/payment/v1/intents/{paymentIntentId}/refunds` accepts:

```json
{"operationId":"uuid","amount":25.00,"currency":"THB","reasonCode":"customer_request"}
```

Reason codes are `customer_request`, `duplicate_charge`, `item_unavailable`, `service_issue`, and `other`. Amounts must be positive with at most four decimal places, use the captured intent currency, and keep the sum of completed plus uncertain/reserved refunds at or below the captured amount. The database locks the Payment intent while checking and reserving this total. Replaying the same operation and payload returns the original refund; changing amount, currency, or reason returns `409`.

A definitive successful provider result returns `201` and an immutable receipt. Processing, uncertain, review-required, and definitively failed results return `202`; `failed` releases the reservation for a new operation, while `processing`, `refund_unknown`, and `review_required` keep it reserved. Recovery performs provider status lookup only and never repeats an uncertain command. Exhausted lookup moves the refund to `review_required`.

Read endpoints are:

- `GET /api/payment/v1/intents/{paymentIntentId}/refunds`
- `GET /api/payment/v1/intents/{paymentIntentId}/refunds/{refundId}`
- `GET /api/payment/v1/intents/{paymentIntentId}/refunds/by-operation/{operationId}` for safe response-loss recovery.

The receipt number is `RF-{refundId without separators}` in uppercase and binds the refund, Payment intent, Order, amount, currency, reason, refunded time, original captured amount, and cumulative completed refund amount. Completed refund rows and receipt snapshots are database-immutable. Events are `payment.refund-requested.v1`, `payment.refunded.v1`, `payment.refund-failed.v1`, `payment.refund-uncertain.v1`, and `payment.refund-review-required.v1`; safe audit records use the matching `payment.refund.*` actions.

Create and read responses expose the refund, tenant hierarchy, Order, Payment intent, operation identity, amount, currency, reason code, status, request/completion times, bounded failure category, and completed receipt. They exclude the actor subject, authorization decision, provider reference, recovery lease and attempts, and internal concurrency version. Missing or cross-tenant resources return `404`. Create authorization denial, including an absent or insufficient financial limit, returns `403`; read denial is disclosure-safe `404`. Invalid refund input returns `400` after authorization, while conflicting operation reuse, over-refund, non-captured intent, and stale completion return `409`.

Apply Payment 9, Authorization 9, and Reporting 17 before enabling the POS control, and provision active restaurant/action/currency financial limits for the authorized managers. Payment 9 refuses an upgrade when the baseline `refunds` table already contains rows; migrate that evidence explicitly before applying it. Configure `PaymentProvider__RefundPath`, `PaymentProvider__RefundStatusPath`, `PaymentProvider__MaximumRefundRecoveryAttempts`, `PaymentProvider__RefundRecoveryEnabled`, `PaymentProvider__LeaseDuration`, `PaymentProvider__RecoveryInterval`, and `PaymentProvider__RequestTimeout`. Generic HTTP providers must honor `refund:{refundId}` idempotency. The Omise test adapter uses the official charge-refund API with minor-unit amounts and local refund metadata, then resolves an uncertain command by listing or retrieving the exact bound refund. Manual cash and PromptPay settlement refunds are outside this provider-captured workflow.

Reporting 18 separately consumes `payment.refunded.v1` for refund-time financial facts and dashboard/sales totals. It is opt-in and does not change the Payment refund rollout boundary. See [refund reporting prerequisites and completeness limits](Refund-Financial-Reporting.md).
