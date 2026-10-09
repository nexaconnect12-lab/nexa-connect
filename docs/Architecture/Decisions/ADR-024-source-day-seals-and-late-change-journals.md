# ADR-024: Source day seals and late-change journals

Status: Accepted. Date: 2026-10-07.

## Context

Revision-bound manifests detect changes, but source observations can change after validation. Approval needs immutable source evidence whose retention is serialized with financial commits, explicit recovery of partial coordination, and visible later changes. Financial/provider recovery must continue rather than being frozen by a manager's review.

## Decision

Order, Payment and POS own immutable source seals referencing their existing protocol-two manifests. Each source's Domain validates matching expected manifest/epoch/revision, THB and zero operational/evidence blockers. Application revalidates existing source financial-read authorization and exact tenant/Restaurant/branch/completed UTC window; capture is evidence retention, not financial approval. POS coordination requires existing `pos.day-close.prepare`; accountants retain read-only coordination access. No role grants, approval limits or workload bypass are added.

Seal retention and source revision advancement acquire the same owning-database Restaurant/branch advisory lock. Seal session locks precede its repeatable-read snapshot, including a branch with revision zero. Expected revision is checked while holding that lock; an earlier competing financial commit conflicts. A financial transaction committing afterward advances its revision and appends a change journal entry in that same transaction. No database lock spans HTTP or provider I/O. Runtime roles have no schema/trigger ownership; source revision/epoch guards remain active.

Every subsequent tracked revision has an immutable `source_financial_changes` entry containing only branch scope, epoch, revision and recording time. Identical source updates do not advance or append. Journal insertion failure rolls back the financial mutation; existing owning recovery/idempotency handles retry after repair. Journal updates/deletes/truncation and direct insertion are rejected. Source seals retain actor/payload fingerprints privately; changing actor/window/manifest/revision under an operation identity conflicts. Exact retry returns the original seal even after later changes, followed by a current journal read.

## Affected dates and calendar ownership

The policy in this section is historical and superseded by [ADR-025](ADR-025-window-aware-sealed-change-reconciliation.md): proven later-day changes are now excluded; historical/unknown changes remain blocking.

The explicit affected-date policy remains conservative: a changed source branch affects **all sealed windows for that branch**, including older unresolved work, current/future-day writes and metadata changes on watched tables. We do not assign a mutation to a local date from its recording timestamp. That timestamp is audit timing, not a financial effective date. Independent branches remain isolated. The watched tables are unchanged from ADR-023.

Each seal binds its original half-open UTC window and original manifest. POS freezes the reviewed Restaurant calendar/timezone/currency in coordinator evidence. Current calendar drift blocks validation without rewriting the old window. A targeted historical-day policy, immutable Restaurant calendar ledger and monetary adjustment attribution are future work; this slice supplies change evidence, not fiscal corrections or a restated financial ledger.

## Durable POS coordination

POS owns separate `branch_day_seals`, operation and immutable audit tables. It reuses preparation's short lease, optimistic version, actor-bound operation fingerprint and explicit original-actor resume/manager replacement, through a service-owned `BranchDaySeal` Domain aggregate. A new command includes a positive `reviewedCutoffVersion`. Begin locks and checks the exact Ready cutoff and freezes its snapshot before remote work. Resume uses that pinned snapshot even if ordinary cutoff evidence was subsequently refreshed. It never silently selects another generation.

Sources retain seals independently using the coordinator operation identity. Partial source commits survive cancellation/process loss; exact resume reuses them. Another manager can replace expired work with a new operation/current coordinator version and newly reviewed cutoff version; old claims cannot complete. The source seals from abandoned or blocked attempts remain immutable evidence. Commands that lose their overall deadline may remain Preparing; no background retry or automatic polling is added.

Reporting reads owning seal APIs, translates their original manifests and reuses the existing exact financial identity/value/hash inventory policy. Its sealed reconciliation references both seal IDs and original manifest IDs. Journal integrity must be valid. Delivery proof can be established for the retained selection even when subsequent revisions exist; unexpected projected facts still create gaps. POS separately rereads all three journals after reconciliation and requires zero pending changes, intact journals, complete delivery and existing readiness rules before serving Ready. This is evidence that was reconciled when checked, not settlement approval.

Fresh Ready reads invalidate on late changes, gaps, calendar drift, epoch/journal inconsistency or outages while preserving the original reviewed seal snapshot. `pendingSealChanges` separately retains the count observed at the last validation; it is not overwritten into the immutable snapshot. Blocked reads expose saved state without fresh source polling. Explicit cutoff refresh followed by a new seal operation creates a new set; it does not erase or acknowledge away old change history.

## API, UI and observability

Owning routes extend `/api/{owner}/v1/customer/day-cutoffs` with POST `/seals` and GET `/seals/{id}`. Source reads return at most 256 change entries plus full count/integrity/truncation indicators. Reporting adds POST `/api/reporting/v1/customer/day-seal-reconciliation`. POS and Customer BFF add separate `/day-close-seals` read/command/CSRF boundaries. Existing current membership, protected tenant, server-held bearer, no-store and safe failure mappings apply.

The portal requires a loaded Ready cutoff version before new sealing, preserves uncertain command identity, restores pending commands after reload, and labels successful evidence as sealed evidence. It shows pending changes **at the last check** and does not imply automatic invalidation. It stores no financial snapshots/tokens in browser storage. New outbound calls use existing correlation-enabled clients; structured JSON/OTLP request telemetry and bounded authorization/failure events contain no financial payload, journal body, subject, token, cookie or arbitrary header. No worker is introduced.

## Limits and rollout

These are separate source-local cuts. Source writes can occur after the last journal check or after POS completes; subsequent reads detect them. There is no atomic global cut, financial write prohibition, settlement approval/finalization, provider settlement certificate or monetary late-correction ledger. Production capacity/branch-lock contention, journal retention and coordinated restore remain release gates.

Deploy Order 14 / Payment 13 / POS 11 with application compatibility 0.27.0; Reporting remains 20 and Authorization 10. New source seal/change history and POS coordination history prevent downgrade; empty migration downgrade/reapply preserves the existing revision epoch. Forward recovery is required once history exists. See [API](../../API/Day-Close-Seals.md), [rollout](../../Deployment/Day-Close-Seals.md) and [verification](../Evidence/Day-Close-Seals.md).
