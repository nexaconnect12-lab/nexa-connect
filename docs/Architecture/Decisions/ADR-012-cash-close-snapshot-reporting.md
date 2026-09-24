# ADR-012: Versioned cash-close snapshots for Reporting

- Status: Accepted for the development slice
- Date: 2026-09-23

## Context

POS owns closed cash sessions, movement-derived expected cash, late settlements and immutable supervisor review history. Reporting needs a read-only single-store view without accessing POS tables or granting financial decisions through a stale projection. Existing close/review transactions do not emit a complete reporting contract.

## Decision

An opt-in POS scanner publishes full current snapshots through the existing transactional outbox. It resolves authoritative organization ownership through Restaurant, then locks the session and commits its publication checkpoint, monotonic snapshot version and event atomically. The scanner backfills existing sessions and can coalesce changes between scans. POS financial/review versions remain distinct from publication versions.

Reporting translates `pos.cash-close.snapshot.v1` into its own model and atomically stores a receipt hash and newest fact. Duplicate/stale delivery cannot roll back a newer snapshot; identity, ownership and version conflicts are rejected. The restricted financial contract is separate from the safe activity-audit feed. Every report read asks POS for current exact-store `pos.cash-review.read` access. The Customer BFF independently revalidates membership and derives tenant context.

The UI uses bounded UTC ranges and keyset pages, shows capture/projection timestamps and no aggregate totals, and directs financial decisions to POS. A timestamp is not a completeness watermark. Consumer bindings must exist before publication dispatch is enabled.

## Alternatives and consequences

Adding events inside every close/review/late-settlement transaction could preserve intermediate states but would expand the existing financial write paths. A scanner keeps this slice isolated and backfills history, at the cost of polling delay and coalesced intermediate decisions. A cross-service database query would violate ownership. A projected permission grant could remain valid after revocation; live POS checks instead make Reporting reads dependent on POS, Restaurant and Authorization availability.

POS migration 6 cannot be rolled back after publication history exists. Reporting migration 15 is destructively reversible, but rebuilding requires retained source events and controlled replay; the original reporting slice supplied no replay CLI or joined broker acceptance (see the subsequent implementation below). The report is not a complete audit history or financial-settlement certificate. See [contract, operations and release gates](../../API/Cash-Close-Reporting.md).

## Subsequent recovery implementation

The operational recovery follow-up supplies a standalone CLI with read-only preview, exact tenant/branch/store/capture-range bounds and a SHA-256 manifest checked again before execution. POS migration 7 records append-only intent and per-event started/confirmed outcomes, including self-asserted operator UUID and database `session_user`. It preserves original event identities/payloads and never resets source checkpoints or financial state. A failed or interrupted attempt can be replayed safely by subscribers that deduplicate original events; a broker confirmation is not Reporting completion.

This deliberately uses controlled operational database/broker authority rather than adding a public financial replay API. Replay broadcasts to every matching exchange binding, so all subscribers must be replay-safe. The implementation includes a disposable process/broker recovery runner, startup-readiness barrier, metrics and alerts. Local Windows recovery/restricted-credential execution and synthetic alert evaluation passed on 2026-09-24; remote CI, deployed least privilege and receiver delivery remain release gates; missing retained events cannot be reconstructed by this tool. See [recovery and replay runbook](../../Deployment/Cash-Close-Recovery.md).
