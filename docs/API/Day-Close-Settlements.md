# Branch-day settlement

POS owns this online THB workflow. It finalizes the exact approved snapshot after source preparation; it does not recalculate tax, move provider funds or post a correction.

Customer routes:

- `GET /api/pos/v1/customer/organizations/{organizationId}/day-close-settlements?branchId={id}&businessDate=yyyy-MM-dd`
- `POST /api/pos/v1/customer/organizations/{organizationId}/day-close-settlements`
- BFF equivalents: `GET/POST /bff/customer/day-close-settlements` and `GET /bff/customer/day-close-settlements/csrf`.

POST requires the protected customer session and antiforgery at BFF, live Directory membership and POS hierarchy/authorization. Organization and actor come from the authenticated server context. POS requires live `pos.day-close.read` and `pos.day-close.finalize`. Authorization 13 defaults finalize to tenant-admin/store-manager, with no accountant/cashier grant. The browser must explicitly load the matching reviewed approval and current prepared proof before constructing:

```json
{
  "branchId": "<branch UUID>",
  "businessDate": "2026-10-07",
  "operationId": "<stable finalize operation UUID>",
  "expectedPreparationVersion": 2,
  "preparationOperationId": "<reviewed preparation operation UUID>",
  "approvalId": "<reviewed approval UUID>",
  "reviewedApprovalVersion": 1
}
```

All bindings must match fresh durable preparation and approved evidence. The intent transaction rechecks the locked local seal/approval/preparation. Replay requires the same actor, scope and complete command, and still requires live customer permission. An uncertain response retains the command; load progress or retry exactly that operation.

The response contains `settlement` (nullable), `canFinalize` and `sourceProofCurrent`. Public settlement progress includes identity, command, status, decision UUID, receipt and bounded source proofs. Private authorization decision IDs, actor-bound fingerprints, preparation claims and recovery trace metadata are excluded. The receipt contains stable event/settlement/decision IDs, original approval/seal version, original THB snapshot and three source references. It is immutable. `sourceProofCurrent=false` labels counts from saved progress; GET attempts source observations without financial mutation. A missing settlement is not proof that an uncertain POST cannot still commit; retry its exact command.

Statuses:

| Status | Meaning |
| --- | --- |
| `arming` | Authorized durable intent; sources may be partially pinned |
| `committing` | Immutable commit and receipt exist; acknowledgement delivery is pending |
| `finalized` | All three sources acknowledged commit |
| `aborting` | Immutable abort exists; release delivery is pending |
| `aborted` | All sources acknowledged abort and original leases were cancelled |

Active settlement owns its preparation. Preparation GET reports `finalizing` or `finalized`; prepare/cancel is rejected. An aborted attempt remains audit history; cancel/refresh the old preparation as needed and create fresh preparation before a new settlement operation. Permanent barriers do not expire. Receipt time is the POS commit-decision time, not the last source acknowledgement time.

Ordinary source financial commands blocked by a permanent barrier return sanitized, correlated HTTP 409 `financial_day_barrier`; temporary protection returns 409 `financial_day_fenced`. Customer cancellation of a pinned source lease returns 409 `financial_day_barrier`. All are no-store and leave the source financial transaction rolled back.

Errors are no-store and sanitized: 400 malformed bindings, 401 missing authentication, 403 tenant/live authority denied, 409 stale reviewed versions/conflicting operation/settlement ownership, 503 dependency/proof unavailable. A 503 may follow persisted intent or commit; it must not be interpreted as rollback. Recover durable progress before changing commands. Request bodies are limited to 4 KiB; BFF responses are bounded to 16 MiB and use the shared correlated adapter.

Internal owning-source routes are `POST /api/{order|payment|pos}/v1/internal/day-settlement-barriers` and `GET .../{settlementId}` with exact organization/restaurant/branch/UTC-window filters. They accept only the existing strict POS service-account principal; customer sessions cannot call them. POST wraps a `SourceBarrierCommand` (settlement ID, finalize operation ID and original `SourceFenceCommand`) with `phase=armed|committed|aborted` and nullable `decisionId`. Armed requests have no decision ID; terminal requests require it. Exact replay returns the retained phase. Proof includes the exact command, phase, terminal decision, change time and retained late-work count. Late payloads are private; counts include held history as well as committed correction custody.

`pos.branch-day-settled.v1` is retained in the POS transaction/outbox boundary with contract version 1 and aggregate type `branch-day-settlement`. `BranchDaySettledV1` includes original scope/date, approval/seal identity, currency, gross/completed-refund/net/cash-variance totals, tenders and source seal/epoch/revision references. No consumer or accounting correction is added in Reporting 20.

See [ADR-029](../Architecture/Decisions/ADR-029-durable-day-settlement.md) and [rollout](../Deployment/Day-Close-Settlements.md).
