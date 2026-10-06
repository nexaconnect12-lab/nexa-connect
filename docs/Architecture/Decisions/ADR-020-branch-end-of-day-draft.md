# ADR-020: Authorized branch end-of-day draft reads

Status: Accepted. Date: 2026-10-03.

## Context

Reporting projections and immutable completeness checks are useful evidence but cannot establish current operational closure. POS owns drawer movements/review versions; Payment owns uncertain financial operations; Order owns receipt-backed sales. A branch business date also requires Restaurant-owned timezone metadata. A useful first reconciliation screen must expose all these facts without giving Reporting database ownership or settlement authority.

## Decision

Reporting Application orchestrates customer-authorized version-one read summaries from each owning API and reads its own projection and exact-window recorded observation. Restaurant provides current active branch business-calendar metadata through the existing workload metadata policy. Shared contracts contain only read DTOs; service-specific authorization, query selection and persistence remain with each owner.

Completed local dates use half-open UTC boundaries from configured host timezone data. Unsupported midnight transitions and dates governed by historical base-offset differences of 12 hours or more fail rather than silently selecting an offset. The latter protects against omitted historical date-line gaps in Windows timezone data. Sales retain existing order-time semantics; receipt tenders use Paid time; refunds completion time; closed drawer variance close time. Earlier unresolved work remains visible using current status. Mixed currencies fail closed.

There is no distributed transaction. Order's aggregate/tender reads share a local repeatable-read snapshot, Payment/POS summaries each use a statement snapshot, and all sources expose observation times. Failures deny the whole draft. Recorded financial checks remain historical and aggregate matches do not prove event completeness. A GET never repairs, approves, locks or creates settlement state; Authorization retains its existing decision-audit side effect.

## Alternatives and consequences

Reading other service databases would bypass their authorization and ownership. Combining only asynchronous projections would hide current uncertain payments and late drawer changes. Building a durable settlement aggregate now would require additional cutoffs, authorization, locking, publication completeness and recovery policy outside this draft slice.

No migration, new product permission or expanded role is introduced. Reporting gains Order/Payment HTTPS dependencies and customer read access must satisfy every owning permission. Current timezone configuration is not a historical configuration ledger. A joined production-like acceptance gate, settlement approval/locking, verified financial cutoffs, exports and manual-tender refund/return policy remain follow-up work. See [contract](../../API/End-Of-Day-Draft.md) and [rollout/verification](../../Deployment/End-Of-Day-Draft.md).

## Validation follow-up — 2026-10-03

The [joined disposable acceptance gate](../../Deployment/End-Of-Day-Portal-Acceptance.md) is now implemented for actual OIDC, owning-service read APIs, retained-event delivery and live revocation. The preceding follow-up list records the original draft decision. Repository historical clock inputs and fixture-only initial POS snapshots do not change production clocks, financial contracts or persistence ownership. [Execution evidence](../Evidence/End-Of-Day-Joined-Acceptance.md) is separate from settlement approval/locking and target-production acceptance.

## Preparation follow-up — 2026-10-03

[ADR-021](ADR-021-branch-day-close-preparation.md) adds POS-owned durable preparation while preserving this Reporting read boundary. Owner totals and newly added bounded fingerprints now share local repeatable-read transactions in Order, Payment and POS; this supersedes the statement-only Payment/POS snapshot description above. POS preparation GET may invalidate its own Ready record and append audit; Reporting GET still creates no preparation or financial state.
