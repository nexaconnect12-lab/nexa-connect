# ADR-021: POS-owned branch day-close preparation

Status: Accepted. Date: 2026-10-03.

## Context

A read-only reconciliation draft cannot retain what a manager reviewed, detect same-total identity changes, or coordinate competing preparation attempts. Settlement authority and reliable cutoffs across independently owned Order, Payment and POS databases need a later protocol.

## Decision

POS owns a BranchDayClose aggregate for a single organization/Restaurant-resolved branch/business date. Application performs live authorization and orchestrates a Reporting read adapter; Infrastructure owns parameterized transactions. Reporting retains read-only financial projection/source orchestration responsibility and no foreign database access. A private POS preparation snapshot, organization-scoped operation identity and append-only state audit commit locally. Separate Preparing and completion commits bracket remote reads, with a durable 30-second lease, optimistic reviewed versions and claim fencing for restart recovery.

Each source owns selection and a bounded fingerprint computed in the same repeatable-read snapshot as its summary. Only a low-level SHA-256 framing primitive is shared. Preparation rejects absent versions, known or unknown blockers and invalid translated scope/calendar/financial evidence. Approved drawer variance may remain nonzero if no current drawer review is pending. Historical completeness observations are required evidence but are never upgraded to a fresh completeness certificate.

Ready for review means an evidence observation without blockers. GET and completed operation replay freshly revalidate Ready; drift/outage invalidates and audits while retaining the reviewed snapshot. Concurrent changes cause conflict instead of returning a different unvalidated Ready version. Invalidation through authorized GET is an explicit POS operational side effect; Reporting GET remains read-only. Authorization decisions may also retain their normal private audit.

## Cutoff and consequences

The selected cutoff is the completed local business date under current Restaurant timezone/currency. It is a query boundary, not a financial write fence or immutable historical-calendar ledger. Current unresolved work includes earlier items. Owner snapshots and Reporting observations are independent; changes after a successful read may occur immediately. There is no background instant invalidation, settlement approval, distributed transaction, approval limit, source lock, repair or settlement integration event in this slice. A later approval/finalization protocol must define fresh completeness and delivery watermarks, source write policy, authorization, late-event recovery and publication before certifying a day.

Keeping this state in Reporting would conflate read projections with POS operational ownership. Holding database locks during remote reads would create unsafe failure/concurrency coupling. Pretending aggregate equality proves identity completeness is rejected. POS migration 8 and Authorization 10 require application compatibility 0.24.0; retained preparation history prevents destructive downgrade. Existing POS/Reporting read dependencies are bidirectional only at HTTP use-case level: preparation calls Reporting, whose source summary calls POS; the source endpoint does not call preparation, so no recursion occurs. Separate request deadlines and correlation apply. See [API](../../API/Day-Close-Preparation.md) and [rollout/verification](../../Deployment/Day-Close-Preparation.md).
