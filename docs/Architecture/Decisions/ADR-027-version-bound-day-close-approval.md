# ADR-027: Version-bound manager approval of day-close evidence

Status: Accepted, implemented locally on 2026-10-08.

## Context

ADR-024 source seals and ADR-026 historical attribution retain immutable financial evidence and detect relevant later writes. Managers need an attributable decision on that reviewed evidence without claiming distributed settlement finality.

## Decision

POS Domain owns approval invariants. Application resolves live hierarchy/authorization and uses a read-only retained-seal adapter to revalidate original source IDs, readiness, journal and Reporting proof. Source seals are GET-only; Reporting reconciliation uses a read-only POST. Infrastructure owns a scoped approval projection, immutable decision ledger and append-only status audit in POS migration 14. Authorization migration 11 grants `pos.day-close.approve` to tenant-admin/store-manager defaults; accountants retain read-only access. Role defaults now belong to Authorization Domain.

Approval binds an exact ready seal coordinator version, original sealed snapshot, fresh validation check/time, actor and bounded reason. Expected approval version serializes competing managers. Organization/operation advisory locking and actor/body/scope fingerprinting make replay durable. Seal row locking precedes approval locking in both approval and seal mutation paths. Decision, projection and private successful authorization attribution commit together. Ledger and audit reject update/delete/truncate; downgrade refuses retained history. Projection decisions are rehydrated from the immutable ledger.

Validity is independently observed: approved, unverified or permanently superseded. Known relevant changes/replacements supersede; unknown/outage cannot certify validity. Unchanged evidence may recover from an approval-only outage; no superseded decision restores. Seal invalidation/replacement and approval status observation share the owning POS transaction. Public exact replay exposes `operationDecision` separately from the latest projection decision. History returns newest 20 without deleting older records.

Customer BFF uses protected tenant context, current membership, CSRF and server-held tokens. Portal enables new approval only after loading the matching ready sealed version and preserves an uncertain exact operation in memory. Shared telemetry preserves correlation and emits safe categories without financial or identity payloads.

## Consequences

No database transaction spans source HTTP. A source mutation after preflight and before POS commit remains possible and is detected on the next validation. Approval certifies the observed snapshot, never an atomic global cut, write fence, completed settlement, correction ledger or fiscal adjustment. Finalization remains a separate future workflow. Branch/offline approval is not implemented.

Concurrency, rollback before commit, immutable storage, exact replay across later decisions, source replacement, relevant/irrelevant changes, outage recovery and permission revocation are covered in the disposable PostgreSQL matrix. The joined real-OIDC gate covers manager approval and accountant denial plus supersession after an actual later historical sale. Deployment/remote CI/production acceptance remain separate gates. See [API](../../API/Day-Close-Approvals.md), [rollout](../../Deployment/Day-Close-Approvals.md) and [local acceptance record](../Evidence/Day-Close-Approval-Acceptance.md).
