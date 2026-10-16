# ADR-019: Original refund retention and scoped financial completeness observations

Status: Accepted. Date: 2026-10-03.

## Context

Receipt-backed sale/payment projection and refund-time projection are independently durable, but projection checkpoints cannot establish missing history. Refund completion IDs are random and cannot be safely regenerated after source outbox cleanup. Reconciliation must inspect every financial projection and distinguish absent source evidence from missing or conflicting report rows while preserving service data ownership.

## Decision

Payment 10 atomically retains the original refund completion payload with receipt/state/audit/outbox. Replay uses this immutable original or validates a surviving historical outbox event; missing original history is an explicit gap. Order 11 remains the receipt-backed sale publication owner. Combined selection uses original Order creation or receipt Paid time; refund selection uses completion time, and Reporting compares each fact's independent half-open time basis.

A privileged bounded CLI invokes each service's Infrastructure adapter through its own connection and translates integration contracts into Reporting-owned facts. Reporting performs one repeatable-read comparison, checking inventory, values, scope, identity and normalized dedupe receipts. Source reads remain separate; no operational service or customer read queries another database. Attributed source repairs commit per aggregate and can be rerun after partial failure.

Reporting 20 retains immutable scoped observations with source/check times, source manifest hash and independent gap counts. The live-authorized sales read boundary exposes only the latest recorded exact-window observation. `observed_complete` denotes available source evidence matching the snapshot, not a watermark, financial certification or settlement authorization. No browser/BFF write or polling automation is added.

## Consequences and alternatives

Original event identity survives outbox cleanup. Legacy evidence cannot be silently fabricated, replays remain idempotent and audits survive repairs. Bounded counts/time windows control work; late completion or projection changes require another check. No global snapshot is claimed across independently owned databases. New immutable histories make downgrade forward-only after use.

Regenerating refund IDs was rejected because Reporting cannot equate them with the original financial event. Reading operational databases from Reporting/customer routes was rejected because it couples availability, credentials and ownership. A global watermark/certification was deferred because this slice lacks complete historical provenance, all-source inventory and settlement/provider acceptance. See [contract](../../API/Financial-Reporting-Completeness.md) and [operations](../../Deployment/Financial-Reporting-Recovery.md).
