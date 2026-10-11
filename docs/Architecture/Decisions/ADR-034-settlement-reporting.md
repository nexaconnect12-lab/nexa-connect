# ADR-034: Settlement reporting with immutable correction proof

Status: Accepted

POS owns branch-day commit decisions, receipts, source barriers and original publications. Reporting needs an accountant-facing ledger that distinguishes committed evidence from delivery gaps without recomputing financial history or granting settlement authority.

New POS commits atomically publish `pos.branch-day-settled.v2` with their receipt/audit/decision. V2 retains the original V1 financial vocabulary plus decision identity, exact selected UTC window and verified correction manifest/epoch/revision/hash/counts/items. The outer and nested event identities agree. Existing V1 outbox rows and receipts remain unchanged; no replacement V2 event is synthesized for old history. New commits publish one version, not both. Already-decided recovery reuses its original publication.

Reporting owns a framework-free settlement fact model and validates financial arithmetic, scope, source identities and complete correction proof before projection. An opt-in consumer binds both versions. Fact, immutable event-hash receipt and checkpoint commit together; acknowledgment follows commit. Exact duplicates do nothing, conflicting event or settlement/day identities are rejected, transient failures retry, and invalid deliveries dead-letter. A checkpoint measures processing only; it is not a source watermark.

Reporting 22 adds two independently owned tables. POS remains schema22; matching binaries and compatibility 0.37.0 are required. The catalog grows to199 tables (Reporting 19, POS 50). Empty downgrade succeeds; retained facts, receipts or settlement checkpoint history block downgrade. No cross-owner FK, new permission or financial mutation is added.

The POS inventory selects durable commit decisions and verifies original receipt and outbox provenance in one local repeatable-read transaction, filtered by authorized organization/Restaurant/branch and business-date range. A missing committed receipt fails the inventory rather than hiding that day. Reporting compares exact normalized identities and hashes in its own snapshot. Missing/conflicting/unexpected delivery remains visible even when totals agree. Expected rows use authorized source values; foreign conflicting values are never returned. Legacy V1 rows explicitly report correction proof unavailable. V2 requires proof even when inventory is empty.

Customer reads revalidate BFF membership, Reporting financial permission and POS financial scope. The ledger presents immutable committed receipt totals, separate correction adjustments and delivery status. A POS commit receipt may exist while source acknowledgment recovery is still pending; Reporting does not claim the coordinator is finalized, or certify global completeness. Date ranges are bounded to 31 business dates and 1000 selected source correction items. Comparison permits at most 62 combined expected/unexpected rows and 2000 correction items; the source adapter and BFF enforce 2 MiB response limits. Users narrow a range that exceeds those bounds.

Closed-day financial history is never enriched, re-priced or re-settled. Provider/fiscal corrections, exports, offline settlement and production certification remain separate. Historical publications already dispatched before the new queue existed remain visible as missing until an approved original-event replay procedure is provided; this slice does not reset dispatcher flags or invent replay events. Operators bind the durable queue before enabling new V2 publication.

See [API](../../API/Settlement-Reporting.md), [operations](../../Deployment/Settlement-Reporting.md), [verification](../Evidence/Settlement-Reporting-Acceptance.md) and [ADR-033](ADR-033-correction-aware-day-close-evidence.md).
