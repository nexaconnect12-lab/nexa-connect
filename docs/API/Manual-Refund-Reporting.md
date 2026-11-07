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

POS 29 / Reporting 24 (application compatibility 0.49.0) extend permanent day-close
with protocol-four POS cutoffs. The retained `summary.manualRefunds` contains
separate cash and PromptPay totals, count and canonical inventory digest. Cutoff
and sealed reconciliation return `manualRefunds` proof bound to the POS manifest,
epoch and revision. Reporting compares the exact restaurant/branch/window inventory
in one repeatable-read snapshot; matching totals alone cannot establish delivery.
Missing, conflicting or unexpected completions leave `matched=false` and block readiness.
No transfer references or staff identities enter this proof.

POS journals payout start and completion atomically with the original mutation.
Unresolved payouts and drawer-linkage gaps block sealing. Source fences and durable
barriers reject intersecting financial writes. A later-day refund of an earlier
sale affects its completion day, without changing a finalized earlier day. Fresh
captures require protocol four; historical protocol-three evidence is readable but
cannot authorize a new settlement. No historical journal entries are fabricated.

New settlements publish `pos.branch-day-settled.v3`: an unchanged V2 envelope plus
`manualRefunds` with manifest/epoch/revision, cash amount, PromptPay amount, count
and digest. Receipt and outbox commit together. Reporting validates the original
binding, projects idempotently and displays/exports both tender totals. Original
V1/V2 receipts remain unchanged and show historical proof as unavailable, not zero.
The previous `manual_refund_finalization_pending` blanket guard is replaced by
exact delivery, unresolved payout, drawer and source-revision checks.

Apply POS 29 and Reporting 24 before matching producers/consumers, along with
Authorization 18. Enable the Reporting manual-refund and settlement consumers and
verify all three settlement bindings (V1/V2/V3) before enabling new POS refunds.
In disposable verification configure
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
