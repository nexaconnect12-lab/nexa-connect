# Manual refund reporting and day-close draft

POS enqueues `pos.manual-refund-completed.v1` in the same transaction as completion.
The outbox dispatcher publishes it after commit.
Cash also inserts its linked drawer movement in that transaction. PromptPay publishes
without a drawer movement. The event carries operation, Order and settlement IDs,
tenant scope, tender, currency, amount, completion time and applicable cash movement ID.
It excludes staff identities and transfer proof.

Reporting 23 projects the event once by event and operation identity. Cash movement
identity is unique. Exact duplicates acknowledge without adding a fact. Conflicting
events are rejected. Existing sales and dashboard responses include
`manualCashRefunds` and `manualPromptPayRefunds`. Both are included once in refunds
and net sales by completion time, using `[fromUtc, toUtc)`. The original order,
receipt and existing net-paid measure retain their prior meaning.

The authorized end-of-day draft compares POS and Reporting amounts by tender, counts
and SHA-256 evidence over exact operations, originals, times and movement identities.
It exposes `pos.manualRefunds` with source totals, count, unresolved payouts and
drawer gaps. Issues are `unresolved_manual_refunds`,
`manual_refund_drawer_evidence_gap` and `manual_refund_projection_evidence_gap`.
A missing digest on a nonempty range is a gap. Cash linkage checks include the
original order, session, actor, amount and posting time. Started payouts without
completion carry forward.

POS day-close preparation blocks permanent settlement of a day with completed
manual refunds with `manual_refund_finalization_pending` until durable cutoff,
seal and settlement proof include this source. Existing provider refund
completeness and historical checks do not certify manual refunds.

Apply Reporting 23 at application compatibility 0.48.0 with matching POS and
Reporting code. Use POS 28. Enable the Reporting consumer and verify its binding
before publishing from the POS outbox. In disposable verification configure
`ManualRefundConsumer__Enabled=true`, a secret-managed
`ManualRefundConsumer__ConnectionString` and `ManualRefundConsumer__Exchange`
matching the POS outbox. Default queue is `nexaconnect.reporting.manual-refunds.v1`.
Keep new refunds off in the retained pilot. Completions from earlier binaries have
no automatic backfill. Exact comparison flags their absence. Reporting 23 refuses
downgrade after facts exist.

Use service `nexaconnect-reporting` and fixed `Manual refund financial` events with
validated correlation IDs. The worker emits `manual-refund.project` and
`reporting.manual_refund.outcomes` through shared telemetry. POS uses the existing
authorized day summary and refund boundary logs. No new permission is required.
