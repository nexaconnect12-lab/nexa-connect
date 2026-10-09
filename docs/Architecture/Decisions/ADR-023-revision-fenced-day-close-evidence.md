# ADR-023: Revision-bound day-close evidence and exact-set delivery proof

Seal addendum (2026-10-07): [ADR-024](ADR-024-source-day-seals-and-late-change-journals.md) extends revision-bound observations with source-local serialized seal retention and append-only change journals. The original revision decision below remains historical; global financial cuts, write prohibition and settlement finalization remain open.

Status: Accepted. Date: 2026-10-07.

## Context

ADR-022 retains source observations and compares event identities/hashes. Fingerprints alone cannot detect a mutation followed by restoration of the same selected values. Manifest generation is a capture counter, not a financial mutation counter. Approval needs durable provenance before a future finalization protocol can be designed.

## Decision

Order, Payment and POS each own a durable epoch and monotonic revision per Restaurant/branch. Branch identifiers are globally owned by Restaurant; Application must still resolve organization ownership and live financial authority before any capture or read. POS has no organization column on stores; no foreign database is queried. Revision reads are restricted by the existing scoped manifest API, not exposed as an independent enumeration endpoint.

Revision scope is deliberately conservative: every changed row in an owning financial source table invalidates all retained days for that branch, including changes to older unresolved work, future-day work and store metadata. This avoids silently choosing historical timezone/date allocation rules. Exact-window fingerprints and retained original event inventories continue to define the reviewed financial selection. A later date-specific revision optimization requires its own affected-date and historical-calendar protocol.

Service-owned PostgreSQL triggers advance the revision in the financial transaction. The watched tables are Order `orders`, `order_manual_tender_settlements`, `order_sale_publications`; Payment `payment_intents`, `refunds`, `refund_financial_publications`; POS `stores`, `shifts`, `cash_sessions`, `cash_movements`, `cash_session_review_states`. Child rows resolve scope through owning parents. Reassignment advances both old and new branch keys in sorted order. Identical row updates do not advance; changed operational metadata on these tables does. Outbox retry/publication bookkeeping, retained captures, coordinator state and audit reads do not advance source revisions.

The counter upsert serializes concurrent committed mutations on a branch row, preventing lost increments. Rollback also rolls back the increment. The trigger is persistence defense in depth, not a new financial authorization or workflow rule; Domain/Application retain business invariants and permissions. Runtime roles cannot truncate watched tables or directly reset/delete revisions or epochs. Migration owners remain privileged and must not be runtime identities. Shared Infrastructure supplies only a parameterized snapshot revision reader and equality primitive; each service owns its physical migration and source selection.

Epoch UUID changes on empty downgrade/re-upgrade; revision zero denotes a branch with no tracked mutation since installation. It is not a complete historical mutation ledger. Every manifest captures `{epoch,revision}` in the same repeatable-read snapshot as selected source rows and fingerprint. Protocol version 2 is additive to existing version-one HTTP routes. Exact operation replay returns the original immutable manifest and revision; it never rebinds an old operation to new evidence. Protocol-one manifests remain readable but noncurrent; a new operation is required to capture revision-bound evidence.

Reporting compares exact retained sale/payment/refund identities, values and hash receipts in its local projection snapshot, then rereads both owning sources. Its version-2 response echoes both retained revisions and marks `deliveryComplete` only when the inventories are observed complete and source reads remain current. This is proof for the selected original event set, not a queue cursor or global broker watermark; publisher confirmation alone is insufficient. The POS adapter rejects changed retained revisions, mismatched echoes, unknown protocols and contradictory delivery claims, and rechecks POS after Reporting. POS Domain requires all three valid revision references and delivery proof for readiness. Audit retains the revisions and check result with the existing immutable reviewed snapshot.

Late recovery remains legal. A fresh authorized read/replay invalidates readiness when revisions or selected evidence drift or dependencies fail. Old manifests/reviewed snapshots remain immutable; explicit refresh obtains new generations. No financial worker is blocked and no historical original is fabricated. Legacy readiness is revalidated and blocked before being served by current binaries. The portal rejects Ready without version-2 revisions/delivery and retains legacy blocked history for review.

## Concurrency and limits

This is a revision fence on an observed evidence version, not a lock on source writes. Source reads are sequential local observations; a source can commit after its last validation or immediately after POS completes. There is no atomic global cut, instantaneous background invalidation, settlement approval, provider settlement certification or late-adjustment ledger. A later approval/finalization protocol must explicitly close that race and define append-only corrections. UI wording must describe readiness at validation, never finalized financial truth.

Branch-row serialization adds contention to financial commits. Transactions must remain short, must not hold database locks across provider/HTTP calls, and must retain existing retry/recovery behavior for deadlock or serialization failures. Source scope/date optimization, production capacity and coordinated backup/restore remain release work. Restored databases must not be treated as settlement certificates solely because an old epoch/revision matches; independent retained-event reconciliation and reviewed restore procedures remain required.

## Rollout and verification

Apply Order 13, Payment 12 and POS 10, minimum application compatibility 0.26.0. Reporting remains 20 and Authorization remains 10. Stop cutoff commands while upgrading all source/Reporting/POS/BFF/portal binaries; ordinary preparation/draft remain separate. Missing revision tables fail unavailable. Each new downgrade refuses once any protocol-two source manifest exists; use forward recovery for retained history. Empty-history downgrade/reapply is covered against PostgreSQL 17.

Verify rollback, concurrent mutations, restored-value drift, scope isolation, snapshots, epoch guards, legacy replay and migration lifecycle with `scripts/test-day-cutoffs.ps1 -ConfirmDisposableInfrastructure`. Also run domain/HTTP/architecture, portal contract/browser and joined cashier-cutoff tests. Actual execution evidence and exclusions are recorded separately in [revision evidence](../Evidence/Revision-Fenced-Day-Close.md). See [contract](../../API/Day-Close-Cutoffs.md) and [rollout](../../Deployment/Day-Close-Cutoffs.md).
