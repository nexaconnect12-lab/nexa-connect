# ADR-025: Window-aware sealed-change reconciliation

Status: Accepted. Date: 2026-10-07. Supersedes the affected-date policy in ADR-024.

## Decision

The conservative historical policy in this ADR is superseded by [ADR-026](ADR-026-exact-historical-day-attribution.md). The original execution evidence remains historical.

Order, Payment and POS retain safe before/after effective timestamps in the same financial transaction as their branch revision and append-only journal row. Source Infrastructure captures only a version, owning table kind and timestamp arrays. It does not persist bodies, receipts, provider identifiers, personal data or monetary payloads in attribution. Each owning Domain decides window relevance; shared persistence supplies locking, snapshot and bounded stream mechanics only.

A change is proved unrelated to a seal only when both available states have complete anchors and every effective timestamp is at or after the seal's exclusive UTC end. Historical changes remain relevant, including work before the window start: older unresolved work and cash review can affect later readiness. This intentionally conservative first implementation removes unrelated next-day trading invalidation; it does not attempt exact historical-day assignment or a monetary adjustment ledger. Recording time is never used as the financial date. UTC boundaries remain the immutable sealed boundaries.

Order anchors include creation, completion, receipt paid time and the update-time fallback used by its financial reader. Tender/publication children inherit their Order anchors. Payment intent anchors use creation; refunds use requested/completed timestamps and publication children inherit their refund. POS shifts and drawers use open/close anchors; movement/review children inherit their drawer, so a movement delivered today to a historical drawer still affects its seal. Store metadata, missing parents, unknown kinds/versions, malformed descriptors and ownership/reassignment uncertainty remain unknown. Both resolved old/new branch scopes continue to receive revisions on reassignment.

## Integrity and reconciliation

Existing journal rows are left unattributed, never reconstructed from current rows. They count as unknown pending changes. Reads verify every revision in the suffix, including unrelated entries. A source-local repeatable-read transaction observes the seal, original manifest, current revision, journal and current source summary together. Evaluation is bounded to 10,000 rows/16 MiB; unevaluated entries count as unknown and incomplete coverage prevents readiness. The response lists at most 256 relevant/unknown entries. Unknown entries block readiness even when the revision chain is intact; display truncation alone does not.

POS requires attribution protocol one, matching immutable identities, nondecreasing suffix/pending counts, intact journals, zero unknown/relevant pending changes and original Reporting delivery proof. It rereads the sources after Reporting. Current totals are advisory source observations, not proof that current events reached Reporting. Manager detail compares sealed sales, refunds, net sales, cash variance and tenders with current source totals. The original snapshot remains preserved; last observed comparison/pending counts are separate. Blocked reads return saved last-check detail without polling. Restored totals never acknowledge a historical change away. Explicit reviewed-cutoff refresh and reseal remain required.

## Rollout and limits

Order 15 / Payment 14 / POS 12 require compatibility 0.28.0. Reporting 20 and Authorization 10 are unchanged. Migrate before upgrading source, POS, BFF and portal binaries; old source responses cannot satisfy new POS protocol validation. Existing permissions, tenant checks, no-store responses, correlation propagation, structured JSON/OTLP and safe failure/authorization events remain. No worker, permission or approval route is introduced. After any nonnull attribution history, downgrade refuses and forward recovery is required; empty/unknown-only migration rollback restores the conservative prior trigger.

No global transaction or lock spans services. Source changes after the final observations can be detected only on a later validation. Exact historical affected-date selection, immutable Restaurant calendar history, production capacity/retention acceptance, manager approval, finalization and fiscal adjustments remain follow-up work. See [API](../../API/Day-Close-Seals.md), [deployment](../../Deployment/Day-Close-Seals.md) and [executed verification](../Evidence/Day-Change-Attribution.md).
