# ADR-022: Retained source cutoffs and fresh day-close reconciliation

Status: Accepted. Date: 2026-10-06.

## Context

Preparation observes independently changing source summaries and a historical Reporting check. Neither matching totals nor source fingerprints prove delivery. Financial recovery must continue after a manager has reviewed a completed day. Blocking provider completions or late cash delivery would lose financial truth.

## Decision

Implement an online THB evidence foundation for one Restaurant-resolved branch and completed business date. The cutoff is a **retained observation generation**, not a source-write fence, delivery watermark, settlement approval or finalization. Use the existing distinct Order-created, receipt-Paid, refund-completed and drawer-closed time bases. Freeze current Restaurant timezone/currency and half-open UTC boundaries in POS evidence; historical calendar ownership remains deferred.

Order, Payment and POS each retain an immutable manifest inside the same local repeatable-read transaction as their selected rows, fingerprint and summary. Order additionally retains original receipt-validated sale integration events; Payment retains original receipt-validated refund events. Missing retained originals are gaps; this path neither repairs nor invents events. All owners retain their bounded selected identity/version evidence rows. A service-neutral Infrastructure helper supplies only transaction, locking, serialization and immutable snapshot retention mechanics; selection and financial decisions remain service-owned. No service queries another service database.

Manifest operation IDs are organization-scoped and bind exact window/payload and authenticated actor. Session advisory locks precede the snapshot and serialize operation replay and window generation assignment. Generation is monotonically assigned **per owner and exact window** and is not a financial mutation revision. A replay returns the same manifest even after drift. Live-authorized source reads compare its retained fingerprint against a fresh owning snapshot. Unknown or truncated fingerprints cannot establish current evidence. New operations retain new generations; old generations remain immutable. No database lock spans HTTP.

POS coordinates through a separate cutoff workflow/history using the existing Domain preparation invariants and Application use case: Preparing, Blocked, Ready for review; expected versions; durable 30-second lease; explicit original-actor resume; replacement; fenced completion; append-only audit. Customer BFF derives tenant and bearer server-side, revalidates membership and requires CSRF for commands. Existing day-close permissions apply; no financial approval grant or amount-limit bypass is introduced. Source capture requires each owner's existing financial read authority: it retains evidence only and grants no authority to coordinate POS or approve a day.

Reporting reads immutable manifests through live-authorized owning APIs, translates their versioned contracts and compares event identities, commercial values and receipt hashes in one local projection snapshot. It checks summary/event consistency, missing, conflicting and unexpected facts, then rechecks Order and Payment. POS finally rechecks its cash evidence. POS freezes the source manifest IDs/generations, check identity and gap outcome with its audit. Reporting reconciliation itself is read-only and does not update the historical completeness API or publish a settlement event. It has no source command authority or foreign database credentials.

## Late evidence and limitations

The POS-owned `BranchDayCutoff` Domain model composes the existing preparation invariants and additionally requires retained cutoff references. It rejects restoration of Ready history without them; ordinary preparation-only evidence cannot complete or freshly validate a Ready cutoff. `PostgresDayCutoffStore` uses this model for its transitions rather than relying solely on the HTTP adapter to supply references.

Recovery and source writes continue. Relevant late delivery, identity/status/version or financial changes cause source comparison to return noncurrent and a Ready read/replay to invalidate POS readiness while retaining its reviewed snapshot. Explicit refresh captures new generations. An outage blocks readiness; partial source capture remains immutable and is reused on exact resume. Unknown issues and unresolved work block. Approved nonzero cash variance remains visible; pending review independently blocks.

This closes retained provenance and fresh reconciliation, not distributed financial finality. Source reads are sequential observations and can change immediately afterward; there is no instantaneous background invalidation, atomic global cut, monotonic mutation revision, provider settlement certification, write lock or historical midnight reconstruction. Finalization must later choose durable source fences/watermarks and an append-only late-correction protocol. Keeping the current evidence-only semantics avoids falsely certifying a day while that protocol is absent.

Order 12, Payment 11 and POS 9 require application compatibility 0.25.0. Reporting stays at 20; Authorization stays at 10. History prevents destructive downgrade. See [contract](../../API/Day-Close-Cutoffs.md) and [rollout](../../Deployment/Day-Close-Cutoffs.md).
