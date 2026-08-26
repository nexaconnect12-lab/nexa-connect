# Payment capture-recovery runbook

## Purpose and safety boundary

Use this runbook when a Payment intent remains `capturing`, `capture_unknown`, or `requires_action`. Payment is authoritative for provider capture state; Order is authoritative for commercial completion and compensation. Never mark an Order paid from authorization alone, repeat a capture with a new idempotency identity, edit audit/outbox rows, or infer failure from timeout, HTTP 404, malformed provider content, or an exhausted lookup budget.

## Signals and alerts

Payment emits OTLP counters from meter `nexaconnect-payment`:

- `payment.capture_recovery.claims` — claimed recovery work;
- `payment.capture_recovery.outcomes`, tagged by `payment.capture.status`;
- `payment.capture_recovery.failures`, tagged by a bounded category: `concurrency`, `not_found`, `timeout`, `provider_transport`, `persistence`, `invalid_data`, `invalid_state`, or `unexpected`.

Alert when any of the following persists beyond two recovery intervals:

- recovery failures are non-zero;
- `capture_unknown` or `requires_action` outcomes increase;
- `capturing` rows have expired leases;
- Payment `payment.capture-reconciled.v1` outbox rows remain unpublished;
- Order reconciliation queue or dead-letter depth increases;
- Order inbox rows remain `processing` beyond their lease.

The local Collector currently exports metrics to its debug destination. A production metrics backend and alert manager must ingest these OTLP instruments before alerts can page operators. Until then, use the database and RabbitMQ checks below; do not call the operational hardening complete for a production environment.

## Diagnosis

1. Stop automatic recovery only if it is repeatedly failing or the provider status API is unreliable. Do not stop Order reconciliation consumption unless its downstream compensation dependencies are unsafe.
2. Query Payment by the exact internal intent ID and organization. Inspect only status, attempt count, lease expiry, last reconciliation time, bounded failure code, and unpublished outbox age. Do not export provider references or payloads.
3. Query the provider using the existing Payment intent/idempotency identity. A response is definitive only when it explicitly reports captured with a valid sanitized transaction reference, or explicitly failed.
4. Inspect RabbitMQ queue and dead-letter depth for `payment.capture-reconciled.v1`. Preserve the original message and event ID for replay.
5. Correlate safe service logs by `CorrelationId`; never add tokens, card data, provider bodies, or unrestricted personal data to the investigation.

## Recovery actions

- `captured`: allow Payment to transactionally persist reconciliation and publish it. Order may then atomically become paid and publish `PaymentCompletedV1`.
- `failed`: allow Payment to publish definitive failure. Order retries idempotent Inventory release and Kitchen cancellation before atomically becoming payment-failed.
- unknown, unavailable, missing, malformed, or exhausted: retain `payment_pending`/`requires_action`. Escalate to provider/accounting review; do not compensate or retry capture as a new operation.
- dead-lettered event: correct the consumer/configuration fault, verify the Order is still compatible with the event organization and payment intent, then replay the original event ID once. Durable inbox deduplication suppresses completed duplicates.
- partial compensation: restore the failing Inventory or Kitchen dependency and replay the original reconciliation event. Repeating both calls is expected and must remain idempotent by Order identity.

## Rollback and forward recovery

Before rollback, stop new Order capture traffic, stop Payment recovery, drain or stop Payment outbox and Order reconciliation consumers, reconcile provider/accounting state, retain source messages, and verify a current backup. Payment 6→5 is blocked while recovery state exists. Order 2→1 is blocked while `payment_pending` orders or history exist. Prefer forward recovery when provider effects have occurred because database downgrade cannot reverse an external capture.

## Verification

Run the opt-in Payment/Order/Reporting acceptance matrix against disposable databases and isolated RabbitMQ resources. Required evidence includes Payment `0→6→5→6`, Order `0→2→1→2`, Reporting migration-11 removal/replay, persisted uncertainty followed by recovery, audit/outbox atomicity, duplicate delivery, partial-compensation retry, and resource cleanup. Provider-environment process termination and established RabbitMQ-connection recovery must be repeated in each release environment.
