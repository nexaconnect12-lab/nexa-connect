# Manager approval of sealed day-close evidence

POS owns online, single-branch THB manager decisions bound to reviewed source seals. Approval records evidence observed at a point in time; approval alone permits later source commits. The separate [finalization preparation](Day-Close-Finalization-Preparation.md) can temporarily fence relevant writes against the reviewed approval. It does not finalize settlement, lock financial writes, create corrections or adjust tax/inventory. See [ADR-027](../Architecture/Decisions/ADR-027-version-bound-day-close-approval.md) and [rollout](../Deployment/Day-Close-Approvals.md).

GET `/bff/customer/day-close-approvals?branchId={uuid}&businessDate=yyyy-MM-dd` loads current validity and immutable history. GET `/bff/customer/day-close-approvals/csrf` obtains the token. POST uses `X-Nexa-CSRF`, maximum 4096 bytes:

```json
{
  "branchId": "11111111-1111-1111-1111-111111111111",
  "businessDate": "2026-09-01",
  "operationId": "22222222-2222-2222-2222-222222222222",
  "expectedApprovalVersion": 0,
  "reviewedSealVersion": 2,
  "reasonCode": "review_complete"
}
```

The other allowed reason is `review_after_changes`. Approval version is nonnegative; reviewed seal coordinator version is positive. Tenant and actor come from the protected session/current Directory membership, never from the body. Owning POS GET/POST uses `/api/pos/v1/customer/organizations/{organizationId}/day-close-approvals`.

Live `pos.day-close.read` is required for reads; POST additionally requires `pos.day-close.approve`. Existing owning-source financial-read and Reporting permissions are required for validation. Application rechecks hierarchy and read/approve authority after source preflight before local persistence. Default tenant administrators and store managers receive approve; accountants and cashiers do not. Accountants retain read-only history. A separately configured live override can change effective authority.

The response includes scoped `identity`, optimistic `version`, `status`, nullable latest `decision`, newest-first `history` (maximum 20), `historyTruncated`, nullable fresh `validatedAtUtc`, `canApprove`, nullable candidate `sealVersion`/`sealSnapshot`, safe `reason`, and nullable `operationDecision`. Candidate evidence is returned only when freshly verified. POST/replay returns the immutable decision for the requested operation in `operationDecision`; the latest `decision` can belong to a later operation. Exact replay is actor/body/scope-bound and still requires current authority. An operation ID cannot be reused for another branch/day in the same organization.

Each decision contains `approvalId`, `operationId`, `identity`, `approvalVersion`, `sealVersion`, `approverSubject`, `reasonCode`, `approvedAtUtc`, `sourceValidatedAtUtc`, `validationCheckId`, and the immutable original sealed `snapshot`. The fresh check identity/time is separate from the original financial snapshot. Scoped authorized readers receive the approver's stable subject as audit attribution. Authorization decision IDs and request fingerprints remain private.

Statuses:

| Status | Meaning |
| --- | --- |
| `not_approved` | No manager decision is retained. |
| `approved` | Same reviewed coordinator/source seals were freshly verified, with complete journal/delivery proof and no blockers. |
| `unverified` | Evidence is unavailable, unknown or otherwise cannot establish current validity. Original decision remains. |
| `superseded` | Known relevant changes, replacement seals or a changed ready reviewed version invalidate that decision permanently. |

Unavailable proof cannot certify approval. Approval-only outage recovery can restore `approved` when the same coordinator version and source references revalidate. Superseded decisions never restore; resolve changes, refresh cutoff, explicitly reseal and approve the new reviewed version. Ordinary seal validation can also invalidate the coordinator; that changed coordinator may require explicit resealing. Decisions and status audits are never rewritten. GET validation may append a status transition audit without creating another approval decision.

Approval preflight GETs the original retained source seals, revalidates financial/operational readiness and journal coverage, and POSTs a read-only Reporting sealed reconciliation to verify delivery. Proof must be no older than 60 seconds at commit. Preflight cannot retain replacement source seals. The local transaction locks the seal coordinator before the approval projection, verifies both reviewed and expected versions, and commits decision/projection/audit together. Source HTTP is outside transactions. A source commit after proof and before local commit can occur; the next validation detects it and supersedes the observed decision. This protocol is not a global cut.

Portal requires explicitly loading the matching ready seal version before a new approval. An uncertain POST keeps its exact operation for `Retry same approval`. Filters clear local state. Pending commands live only in memory; after reload, load saved approval/history before deciding on a new operation. History remains limited in display while all decisions remain retained.

All responses are no-store. Invalid input is 400; authentication/permission failures 401/403; stale reviewed/approval version, mismatched replay or already reviewed same seal is 409; sanitized dependency/storage failure is 503. POS deadline is 25 seconds, BFF 30 seconds, browser 40 seconds; upstream JSON is bounded to 16 MiB. Retry ambiguity with the same actor and exact command.

Shared JSON/OTLP telemetry uses `nexaconnect-pos` and `nexaconnect-customer-bff`. Search `Day-close approval` with validated `CorrelationId`; source diagnostics use the [seal debugging queries](Day-Close-Seals.md). Logs contain safe status/category only, never subjects, snapshots, financial bodies, tokens or Authorization IDs.
