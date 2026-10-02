# Refund financial reporting

Reporting migration 18 (minimum application `0.21.0`) projects Payment-owned `payment.refunded.v1` completion events into separate refund facts. Payment remains authoritative for refund eligibility, authorization, completion and receipt history. This consumer never queries or modifies Payment or Order databases.

## Read contracts and accounting time

Existing tenant-authorized dashboard and sales routes retain their branch and UTC range inputs and permissions. Dashboard adds `netSales`; its existing `refunded` field now sums completed refund facts by refund occurrence time. Sales adds `grossSales`, `refundedAmount` and `netSales`; `grossSales` is an explicit alias of the retained `totalSales`, which remains the completed-order gross total over the full range, independent of the 1,000-row display limit. Both calculate net sales as gross sales minus refunds.

Sales are selected by `orderedAtUtc` and refunds by `refundedAtUtc`, using the half-open interval `[fromUtc,toUtc)`. A refund of an earlier sale reduces the refund period; it does not rewrite the sale period. A refund-only period can have negative net sales. Dashboard `netPaid` retains its existing `payment_facts` calculation (`paid_amount - refunded_amount` selected by `paid_at_utc`); it is a separate payment measure and is not derived from the new refund facts. Do not subtract refunds from it again.

Mixed currencies across participating sales/payment/refund facts return `409` instead of summing unlike amounts; sales uses sales and refund currencies, while dashboard also includes payment currencies. Tenant and branch predicates apply independently to each fact source. Existing range limits, live organization/product permission decisions, `400` invalid ranges and fail-closed dependency/database errors remain in effect. No new refund-detail read route is added.

The Customer Portal dashboard displays gross sales, refunds, net sales and the existing net-paid measure. The sales page displays gross sales, refunds and net sales. `latestGlobalCheckpointUpdatedAtUtc` remains the newest checkpoint update across projectors; it does not establish branch freshness, complete delivery, or reconciliation.

## Projection and failure behavior

Reporting translates the versioned contract into its own validated domain fact: event/refund/payment/Order/tenant identifiers, positive amount, three-letter currency, bounded reason, receipt number, refund time and captured/cumulative totals. No provider payload, payment credential, actor subject or authorization evidence is copied into this financial projection.

One PostgreSQL transaction writes the event's normalized fact hash receipt, refund fact and `payment-refund-financial` / `payment.refunded.v1` checkpoint. Matching event replay acknowledges without adding a fact or checkpoint count. Changed content under the same event ID, a reused refund or receipt identity, and malformed events dead-letter. Valid distinct partial refunds project independently even when delivery order differs. Transient persistence failures requeue; acknowledgement follows commit, so redelivery after interruption is safe.

The durable consumer declares its topic exchange, queue, dead-letter queue and bindings, then manually acknowledges with bounded prefetch. Its readiness task proves initial consumer registration only. Reconnection retries after five seconds; it is not a completeness or ongoing-health signal.

## Rollout, recovery and verification

Apply Reporting 18 before deploying these read queries. Configure:

- `PaymentRefundConsumer__Enabled=true` (default false).
- Secret-managed `PaymentRefundConsumer__ConnectionString`, using broker TLS outside local development.
- `PaymentRefundConsumer__Exchange=nexaconnect.events`, matching Payment's outbox exchange.
- `PaymentRefundConsumer__Queue=nexaconnect.reporting.payment-refunds.v1` and optional `PaymentRefundConsumer__PrefetchCount=16` (clamped to 1–100).

Verify the `payment.refunded.v1` binding and `Payment refund financial consumer ready` before publishing. Payment's outbox must be enabled separately. Existing published events are not automatically backfilled; retain authoritative Payment source events and arrange controlled replay. Monitor queue/dead-letter depth and Payment outbox age. Correct rejected events against Payment evidence before replaying; do not edit financial rows as a repair.

Before `18→17`, stop the consumer, disable affected reads/deploy compatible older Reporting code, retain Payment source events, and explicitly permit destructive downgrade. The downgrade deletes refund facts, hash receipts and this projector's checkpoint. Re-upgrade and replay retained events to rebuild. There is no refund replay CLI or certified completeness watermark in this slice.

Service name is `nexaconnect-reporting`. Query Loki `{service_name="nexaconnect-reporting"} |= "Payment refund financial"`; validated correlation IDs scope successful/retry processing. The `reporting.payment_refund.outcomes` metric records `applied`, `replayed`, `rejected`, `retry`, and `connection_retry`. OTLP uses the existing Reporting observability configuration. Logs exclude financial bodies and amounts.

Run focused `RefundFinancialReportingTests` and `RefundFinancialProjectionPostgresTests`. Database/broker acceptance requires Development/Test/Testing, `NEXACONNECT_REPORTING_INTEGRATION_DB`, `NEXACONNECT_RABBITMQ_ACCEPTANCE=1` and `NEXACONNECT_RABBITMQ_INTEGRATION_URI`; it owns an isolated schema, exchange and queues and must use disposable infrastructure. Tests cover scoped aggregates, exact/conflicting replay, partial facts, mixed currency, downgrade/re-upgrade and consumer restart/duplicate acknowledgement. The lifecycle case executes migration 18 SQL against a fixture schema; it does not certify a clean-install migration-runner upgrade.

## Completeness boundary

The current repository has no operational sales-fact or payment-fact consumers. Their tables/read queries predate this slice and tests seed them explicitly. The refund pipeline is implemented, but gross sales, net paid and net sales are not certified complete production accounting reports until source projections, backfill/replay, reconciliation and joined production acceptance exist. Manual-tender refunds, return/restock allocation, fiscal credit notes and settlement accounting remain separate work.

See [ADR-017](../Architecture/Decisions/ADR-017-refund-time-financial-reporting.md) and [Payment refund contract](Payment-Refunds.md).

Local verification on 2026-10-02 passed three PostgreSQL/RabbitMQ cases without skips: scoped aggregation/replay, refund-time boundaries/mixed currency/negative net, and consumer duplicate acknowledgement/restart. This is local projection acceptance; production source completeness and joined portal acceptance remain open.
