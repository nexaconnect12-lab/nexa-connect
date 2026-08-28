# Payment capture-recovery runbook

## Purpose and safety boundary

Use this runbook when a Payment intent remains `capturing`, `capture_unknown`, or `requires_action`. Payment is authoritative for provider capture state; Order is authoritative for commercial completion and compensation. Never mark an Order paid from authorization alone, repeat a capture with a new idempotency identity, edit audit/outbox rows, or infer failure from timeout, HTTP 404, malformed provider content, or an exhausted lookup budget.

## Signals and alerts

Payment emits OTLP counters from meter `nexaconnect-payment`:

- `payment.capture_recovery.claims` — claimed recovery work;
- `payment.capture_recovery.outcomes`, tagged by `payment.capture.status`;
- `payment.capture_recovery.failures`, tagged by a bounded category: `concurrency`, `not_found`, `timeout`, `provider_transport`, `persistence`, `invalid_data`, `invalid_state`, or `unexpected`.
- `payment.capture_recovery.worker_enabled` — `1` while the configured capture-recovery worker is registered/instantiated and exporting; this is presence, not heartbeat, evidence;
- `payment.capture_recovery.backlog` and `payment.capture_recovery.oldest_age_seconds` — currently eligible uncertain/expired captures and their oldest age;
- `payment.outbox.unpublished` and `payment.outbox.oldest_age_seconds` — durable Payment events awaiting publication;
- Order exports pending reconciliation inbox work, oldest expired lease age, and equivalent unpublished-outbox count/age gauges. RabbitMQ exports queue and dead-letter depth through its Prometheus plugin.
- `payment.operational_metrics.collection_failures` and `order.operational_metrics.collection_failures` — PostgreSQL gauge-refresh failures; previous gauge samples may be stale after either counter increases.

The checked-in development backlog rules require work to be older than 120 seconds and then remain alert-pending for one minute. Other rules use their explicit windows and pending periods in the Prometheus rule file. Alert on:

- recovery failures are non-zero;
- `capture_unknown` or `requires_action` outcomes increase;
- `capturing` rows have expired leases;
- Payment `payment.capture-reconciled.v1` outbox rows remain unpublished;
- Order reconciliation queue or dead-letter depth increases;
- Order inbox rows remain `processing` beyond their lease.

The local Collector exports service metrics to Prometheus and its debug destination; Prometheus also scrapes RabbitMQ directly. Checked-in development rules alert on recovery failures, `requires_action`, missing worker telemetry, backlog/outbox age beyond two minutes, stalled Order inbox work, reconciliation queue/dead-letter depth, and PostgreSQL metrics-collection failures. The age threshold and pending time are local diagnostic defaults, not production SLOs. Local Alertmanager intentionally has no external receiver. A production metrics backend and reviewed receiver must ingest these instruments before alerts can page operators. Until alert delivery is rehearsed, use the database and RabbitMQ checks below and do not call operational hardening complete for a production environment.

Payment exposes anonymous `GET /health/live` for process liveness and `GET /health/ready` for traffic readiness. PostgreSQL mode is ready only when the database is reachable and migration 6 or newer is present. Provider availability is deliberately excluded: provider incidents must surface through recovery metrics and alerts rather than cause orchestrator restart loops.

## Diagnosis

1. Stop automatic recovery only if it is repeatedly failing or the provider status API is unreliable. Set `PaymentProvider__CaptureRecoveryEnabled=false` and restart Payment; this pauses only capture recovery and does not disable authorization recovery or mutate pending work. Do not stop Order reconciliation consumption unless its downstream compensation dependencies are unsafe. Re-enable the setting and restart Payment after the provider/status path is safe.
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

Run the opt-in Payment/Order/Reporting acceptance matrix against disposable databases and isolated RabbitMQ resources. Required evidence includes Payment `0→6→5→6`, Order `0→2→1→2`, Reporting migration-11 removal/replay, persisted uncertainty followed by recovery, audit/outbox atomicity, duplicate delivery, partial-compensation retry, and resource cleanup. Then run the separately confirmed fault harness: it launches a child test process, calls a local HTTP capture fixture through `HttpPaymentProvider`, writes a marker only after capture success, requires bounded termination of that exact process tree, and starts a fresh scripted status fixture that reports the recorded intent as captured for reconciliation. It also verifies the named Compose `rabbitmq` container publishes the configured loopback AMQP port before restart and proves the established transport boundary publishes afterward. This does not prove concrete-provider persistence/status semantics; repeat concrete-provider and alert-delivery rehearsal in each release environment.

Set `NEXACONNECT_PAYMENT_INTEGRATION_DB`, `NEXACONNECT_REPORTING_INTEGRATION_DB`, `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB`, and `NEXACONNECT_RABBITMQ_INTEGRATION_URI` through secret injection. Run the coordinated matrix with `scripts/test-payment-capture-recovery-release.ps1 -TargetEnvironment Staging -ConfirmDisposableInfrastructure`. For local/destructive fault evidence, run `scripts/test-payment-capture-recovery-faults.ps1 -TargetEnvironment Staging -ConfirmDisposableInfrastructure -ConfirmProcessTermination -ConfirmBrokerRestart -RabbitMqContainer <exact-compose-container>`. The fault script additionally restricts RabbitMQ to loopback, rejects production-looking database settings, verifies the Compose service label before restart, restores environment flags, removes successful-run artifacts, and retains failed-run markers/logs for diagnosis and schema cleanup. Its confirmation switches are operator attestations; neither script independently proves infrastructure ownership or concrete-provider equivalence.

For local alert-routing and rollback-forward evidence, prepare the repository's normal Compose `.env` prerequisites, inject the disposable PostgreSQL administrator setting, and run `scripts/test-payment-capture-recovery-operations.ps1 -TargetEnvironment Staging -ConfirmDisposableInfrastructure -ConfirmAlertDelivery -ConfirmDestructiveRollback`. `TargetEnvironment` is an evidence label; tests deliberately receive the safety value `Testing` and use generated databases rather than a deployed staging database. The script starts the isolated `alert-rehearsal` profile, observes firing and resolved webhooks carrying the current unique rehearsal ID, then runs Payment `0→6→5→6` and Order `0→2→1→2` and parses TRX counters to reject skips/no-match results. It requires successful container cleanup before declaring success and retains failed-run evidence. The synthetic receiver does not replace production paging or live-traffic rollback rehearsal.
