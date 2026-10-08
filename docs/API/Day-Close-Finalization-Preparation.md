# Temporary finalization preparation and source fences

Online single-branch THB preparation binds the exact manager approval, sealed financial snapshot, source IDs and revisions. It temporarily protects relevant source writes; it creates no completed settlement, accounting adjustment, fiscal correction or offline operation. See [ADR-028](../Architecture/Decisions/ADR-028-temporary-day-close-source-fences.md) and [rollout](../Deployment/Day-Close-Finalization-Preparation.md).

## Customer workflow

GET `/bff/customer/day-close-finalization-preparations?branchId={uuid}&businessDate=yyyy-MM-dd` loads saved progress and freshly validates an existing prepared result. GET `/bff/customer/day-close-finalization-preparations/csrf` returns the antiforgery token. POST uses `X-Nexa-CSRF` and a maximum 4096-byte body:

```json
{
  "branchId": "11111111-1111-1111-1111-111111111111",
  "businessDate": "2026-09-01",
  "operationId": "22222222-2222-2222-2222-222222222222",
  "expectedVersion": 0,
  "approvalId": "33333333-3333-3333-3333-333333333333",
  "reviewedApprovalVersion": 1
}
```

POST `/bff/customer/day-close-finalization-preparations/cancel` uses the same CSRF protection and `{branchId,businessDate,operationId}` identifying the preparation being cancelled. It cannot cancel a replacement operation. Another authorized manager may cancel; prepare/resume remains original-actor/body-bound. Owning POS GET/POST/cancel routes use `/api/pos/v1/customer/organizations/{organizationId}/day-close-finalization-preparations` with the same suffixes, without browser antiforgery.

Protected session tenant, server-held bearer and current Directory membership supply authority. Read requires `pos.day-close.read`. Prepare/cancel additionally require `pos.day-close.finalization.prepare`; Authorization 12 gives this only to tenant-admin/store-manager defaults. Existing Order, Payment, POS and Reporting financial-read permissions remain required. Accountants are read-only by default. Application rechecks live preparation authority/hierarchy after preflight and source calls; revocation cannot record prepared success. Cleanup after revocation uses another authorized manager or automatic source expiry.

Response fields: scoped `identity`, optimistic `version`, `status`, nullable exact `pendingCommand`, bound `approvalId`, `reviewedApprovalVersion`, `sealVersion`, nullable `expiresAtUtc` and fresh `validatedAtUtc`, up to three `sources`, bounded safe `blockers`, and `canPrepare`. Each source proof has `{source,sealId,epoch,revision,expiresAtUtc,active,cancelled}`; `revision` is the branch revision observed on acquisition and can exceed the original seal revision solely because unrelated changes are allowed. Private actor, authorization decision, claim and full retained financial snapshot remain in POS audit/storage. The original snapshot and decision remain available through the scoped approval API.

| Status | Meaning |
| --- | --- |
| `not_prepared` | No preparation exists. |
| `preparing` | An exact operation is durable; acquisition may be partial. |
| `prepared` | Fresh approval and all three matching active source leases were verified. |
| `blocked` | Current proof is absent, stale or changed; partial source leases may still be active. |
| `cancelling` | Cancellation intent is durable; source tombstones may be partial. |
| `cancelled` | All three matching source cancellations were acknowledged. |
| `expired` | The operation has reached its conservative expiry boundary; no prepared claim is returned. |

Begin verifies a freshly approved exact projection version, then locks POS seal→approval→preparation rows. The original command, immutable approval and fixed expiry are durable before HTTP. Four-minute leases never renew on replay; a 30-second claim fences concurrent/restarted workers. Same-actor exact resume after claim expiry preserves source operation IDs, seal IDs and expiry. Competing operations conflict while the previous lease window remains live; cancel or wait for expiry before preparing a new operation against freshly reviewed approval/current preparation version. Old operations cannot overwrite replacements. Successful cancellation is irreversible even if late source requests arrive.

Prepare sequentially acquires Order/Payment/POS, revalidates approval including calendar/journal/Reporting evidence, GETs source leases again, rechecks live authority and completes only under the same POS claim and approval/seal versions. No HTTP runs in a database transaction. Prepared validity reserves 15 seconds before nominal expiry and requires fresh proof within 60 seconds. Reads fail closed and invalidate prepared status when proof cannot be obtained; explicit original-operation resume may restore it while the same binding and leases remain valid. Reads never acquire fences or extend expiry.

Cancellation can interrupt an active preparer by replacing its claim. Each owning source retains a tombstone even if acquisition has not arrived. A delayed acquisition cannot reactivate cancelled/expired identity. Partial cancellation retains `cancelling`; retry it explicitly. A request timeout/crash may leave a live claim and partial leases. Load progress, wait for the claim to expire and resume/cancel the exact operation. No background cleanup worker is required: lease admission checks database time, not physical row removal.

The portal enables a new prepare only after explicitly loading/reviewing a fresh current approval. It loads source progress, resumes saved operations, retains uncertain prepare/cancel commands in memory for exact retry, clears scope changes and stops displaying prepared verification when its freshness/lease boundary expires. After reload, load saved progress. A different manager can cancel another manager's stranded operation but cannot impersonate the original preparer.

## Owning source contract and write admission

For each owner, POST `/api/{owner}/v1/customer/day-cutoffs/fences` accepts `{operationId,window,approvalId,sealId,expiresAtUtc}`. `window` is the existing organization/restaurant/branch/fromUtc/toUtc scoped closed window. POST `/fences/cancel` accepts the identical command. GET `/fences/{operationId}` uses the full exact window as query parameters. Source routes require live owning financial-read authority; mutations additionally require the branch-scoped preparation permission. Approval identity is a correlation binding at the source; POS alone validates the approval decision. Source acknowledgments by themselves never certify settlement.

Source response is `{operationId,window,approvalId,sealId,expiresAtUtc,cancelled,active,acquiredRevision}`. Cancellation-before-acquisition may have null revision. Scope, seal, actor/body replay and window overlaps are fenced. Initial active expiry must be after source database time and at most five minutes ahead; coordinator uses a four-minute millisecond-precision timestamp. Source storage normalizes arbitrary input to PostgreSQL microsecond precision. Exact replay neither extends expiry nor reacquires expired protection. Cancelled records reject actor changes for acquisition and retain cancellation irreversibly.

Acquisition obtains the owning branch revision advisory lock before its repeatable-read snapshot, validates the exact retained seal and full contiguous journal suffix through the owning Domain attribution policy, then records the lease and audit atomically. Missing history, epoch changes, unknown/relevant suffixes or bounded evaluation overflow reject admission. The suffix limit is 10,000 entries / 16 MiB. Overlapping active windows cannot have competing operations.

Every tracked source financial mutation advances its journal within the same owning transaction. The new database backstop mirrors the documented Domain before/after policy: selected old or new states, unresolved carry-forward, unknown metadata and ownership uncertainty are blocked while a matching lease is active. Unrelated branches and evaluated next-day changes remain available. No financial row, revision, receipt, audit, inbox or outbox can partially commit when the guard rejects a write. APIs map the bounded `financial_day_fenced` database rejection to sanitized 409. Generation markers change on acquisition/cancellation without creating financial journal entries; an older repeatable-read writer aborts with serialization conflict instead of missing a new fence.

POS manual-settlement consumption treats a temporary fence as retryable: safe `fenced_retry` telemetry, one-second delay and broker requeue, with no inbox acknowledgment or financial partial commit. Existing recovery remains status/idempotency-bound; do not retry an uncertain provider command merely because storage is fenced. After cancel/expiry, replay owning work or recheck its authoritative state. Permanent source locking and an append-only correction workflow are future slices.

## Failures and observability

No-store applies to success and failure. Invalid input is 400, live authentication/access failure 401/403, operation/version/lease/approval conflicts 409, absent scoped source reads 404 and sanitized dependency/storage failures 503. POS/source deadline is 25 seconds, BFF 30 seconds, browser 40 seconds; source fence responses are bounded to 16 KiB, BFF forwarded JSON to 16 MiB. Cancellation is best effort across services with durable retry intent, not an atomic distributed rollback.

Query shared JSON/OTLP service names `nexaconnect-pos`, `nexaconnect-order`, `nexaconnect-payment`, `nexaconnect-customer-bff` for `Finalization preparation`, `Day-cutoff source` or `Financial day fence`, narrowed by validated `CorrelationId`. POS worker metrics include `pos.order_settlement.outcomes` status `fenced_retry`. Logs include safe categories/status only, never bodies, financial data, credentials or unrestricted actor details.

Prepared is a freshly observed, temporary protected basis. Authorized cancellation, outages, clock skew and lease expiry still require verification before any future finalization protocol. Database/service clocks must remain synchronized; the 15-second reserve is a conservative boundary, not a permanent cross-service commit guarantee. No finalized state or durable settlement publication is implemented here.
