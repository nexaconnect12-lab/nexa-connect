# ADR-028: Temporary source fences for finalization preparation

Status: Accepted; implemented and locally verified, 2026-10-08.

## Context

ADR-027 manager approval is immutable observed evidence; relevant writes remain legal and supersede it when detected. Finalization needs a stable source basis without holding cross-service database locks across HTTP or pretending sequential approval observations are completed settlement.

## Decision

Add independently owned temporary branch/window fences to Order 17, Payment 16 and POS 15. POS 16 owns a durable finalization-preparation state, immutable operation identities and append-only audit. Authorization 12 grants manager-only `pos.day-close.finalization.prepare`. Application compatibility is 0.31.0; Reporting 20 is unchanged.

Each source's Domain `FinancialDayFence` composes its existing financial attribution decisions and validates journal admission. Shared Infrastructure owns only parameterized lease/locking/journal mechanics and accepts source policy callbacks; service-specific SQL backstops mirror the Domain selection for all tracked financial writes. Acquisition takes the same revision lock as financial mutation before a repeatable-read snapshot. Generation updates on acquisition/cancellation ensure pre-existing repeatable-read writers serialize or abort, including empty branches with revision zero. Generation changes are control metadata, not financial changes.

Fences are scoped to the original source seal, full window, POS approval ID and operation, with immutable binding/expiry and irreversible cancellation. A cancelled-before-acquired tombstone prevents delayed HTTP activation. The original actor/body-bound identity cannot renew its expiry. Source audit and coordinator operation/audit history reject update/delete/truncate; nonempty history refuses downgrade. No other service reads an owning database.

POS begins against a freshly validated exact approval/seal version and preserves the original immutable financial snapshot. Claim leases serialize prepare/resume; cancellation supersedes a claim and cannot target a replacement operation. Completion locks seal→approval→preparation and requires all three exact, fresh, active source acknowledgments and unchanged live approval. The lease is four minutes, source acceptance allows at most five minutes, claims are 30 seconds and prepared proof reserves 15 seconds before expiry. No HTTP spans a database transaction. Read-only validation can invalidate prepared status; explicit exact resume rechecks and can recover under the original unexpired binding.

POS message consumption requeues fence-blocked financial publication with bounded delay instead of treating it as permanent conflict or spinning immediately. Provider recovery does not repeat uncertain financial commands; owning transactions retain existing idempotency/recovery semantics.

The BFF protects tenant/session/CSRF and delegates business policy to POS. Portal requires a loaded fresh approval, exposes partial progress and exact resume/cancel, and expires displayed proof locally. Shared service JSON/OTLP/correlation continues with safe failure categories and no financial/identity payload logging.

## Consequences

This slice provides temporary finalization preparation, not completed settlement or permanent write locks. Expiry releases admission automatically and preserves history. Partial failure leaves bounded leases and explicit resume/cancellation recovery. Clock synchronization is a prerequisite; freshly observed prepared status is not a perpetual guarantee against later authorized cancellation or expiry. A future finalization protocol must durably coordinate permanent source decisions and late corrections; it must never finalize solely from cached prepared state.

Read-only Reporting contracts remain unchanged; retained original-event proof is still required. Branch-offline preparation, accounting/fiscal adjustments, source write routing into a correction ledger and production acceptance remain future work. See [contract](../../API/Day-Close-Finalization-Preparation.md), [rollout](../../Deployment/Day-Close-Finalization-Preparation.md) and [local acceptance evidence](../Evidence/Finalization-Preparation-Acceptance.md).
