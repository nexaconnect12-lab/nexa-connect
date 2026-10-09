# ADR-032: Separate correction Reporting and source-backed reconciliation

Status: Accepted and implemented; execution evidence is tracked [separately](../Evidence/Cash-Correction-Reporting-Acceptance.md).

ADR-031 commits verified POS cash variance corrections and their original outbox publications. Reporting previously had no consumer for these events. Releasing the old cash tender or amending the settled drawer would violate immutable financial evidence and could double-count original sales.

Reporting now translates correction integration events into its own Domain facts, with separate aggregate uniqueness and transactional event-hash receipts/checkpoints. The opt-in durable consumer commits before acknowledgement, rejects identity conflicts and retries transient failures. Existing sale/payment/refund/cash-close projections retain their meaning. Correction amount/count/detail remain separate; this release does not add them to sales or tender totals.

A live authorized report compares POS-retained original publication evidence with a repeatable-read Reporting inventory. Source/Reporting observations and manifest hash are explicit. Expected identities are checked independently of totals and scope errors; foreign conflicting facts are never disclosed. Missing source originals fail closed; missing Reporting delivery appears as a gap. This additive report does not alter earlier cutoff/seal/settlement protocols or the recorded sale/refund completeness observations.

Bounded administrative replay reads only POS-owned ledger/outbox evidence. Preview pins exact selected originals. A durable operator/database-actor run and a started attempt precede each broker call; confirmations are appended afterward. Unconfirmed attempts are uncertain and may safely replay the same original identities after a fresh preview. The tool cannot write financial rows, manufacture events or certify delivery. Shared-exchange replay broadcasts to matching subscribers.

Targets are Reporting 21 and POS 21 with compatibility 0.35.0; Order 19, Payment 18 and Authorization 15 remain unchanged. Reporting adds fact/receipt tables and POS adds two immutable replay audit tables. Reporting downgrade is controlled destructive projection removal followed by original-event replay after re-upgrade; retained POS audit prevents downgrade. No new permissions or workload clients are introduced. Correlation propagation, safe structured outcomes, JSON/OTLP and bounded time/body limits apply to new boundaries.

The portal exposes authorized live correction delivery comparison. Persisted observation history, generalized exports, fiscal documents, provider corrections, reversal, offline posting, global delivery watermarks and production certification remain separate work. See [contract](../../API/Cash-Correction-Reporting.md) and [operations](../../Deployment/Cash-Correction-Reporting.md).
