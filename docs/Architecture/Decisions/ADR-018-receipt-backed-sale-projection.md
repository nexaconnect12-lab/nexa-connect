# ADR-018: Receipt-backed sale and payment projection

Status: Accepted

Order owns sale completion and immutable receipt evidence, while Reporting owns financial read models. Older Payment completion contracts do not carry sufficient tenant or pricing scope, and manual settlement has a different payment identity.

Publish one versioned `order.sale-completed.v1` from the committed receipt on every Paid persistence path. Retain one immutable publication keyed by Order and enqueue within the Order transaction. Deterministic event identity and retained payload preserve evidence under concurrency, outbox cleanup and replay. Reporting translates this contract into its own fact and atomically commits sales, payment, event hash and checkpoint. Never join operational service databases to build a report. Provider intent and manual settlement identities are explicitly distinguished.

Keep refund-time facts separate so out-of-order delivery does not change sale evidence or require another source before projection. Preserve the existing HTTP measures and accounting time bases; completion-only payment facts do not claim refund-adjusted settlement accounting. Scope replay by tenant, branch and bounded Paid-time window, require operator attribution, and reconcile retained publications against Reporting without report writes. Missing historic receipts remain gaps; evidence is never inferred from current prices.

The extra Order ledger and append-only replay audit require migration 11; Reporting dedupe/payment-origin support requires migration 19. Both refuse downgrade after financial evidence. Scoped reconciliation establishes agreement for retained publications, not global completeness. See [contract and rollout](../../API/Sale-Financial-Reporting.md).
