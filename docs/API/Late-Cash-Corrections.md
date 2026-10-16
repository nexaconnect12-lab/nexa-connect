# Verified late-cash corrections

Load a committed settlement, select POS late work and view a record. Mark it `require_correction/correction_needed`, then load its cash correction. The server verifies original Order evidence and derives the negative cash variance adjustment. A manager must confirm the preview before posting; accountants can read verified previews and receipts by default.

Routes:

- `GET/POST /bff/customer/late-cash-corrections`, plus `GET /csrf`.
- `GET/POST /api/pos/v1/customer/organizations/{organizationId}/late-cash-corrections`.
- Source evidence: `GET /api/order/v1/customer/manual-tender-evidence/{eventId}?organizationId={id}&restaurantId={id}&branchId={id}`. It requires live owning financial read, returns the verified original integration contract and is used by the server adapter; no BFF/browser proof endpoint is added.

Correction GET query is `branchId`, original settlement `businessDate` (`yyyy-MM-dd`) and opaque `workId`. BFF takes organization/actor from protected authenticated context; POS derives the original settlement/window from its saved receipt/preparation and checks exact committed work membership. It requires live `pos.day-close.read` and `pos.cash-review.read`. A verified preview also requires the caller's live Order financial reads; existing posted receipt reads do not call Order again.

POST body:

```json
{
 "branchId": "<UUID>", "businessDate": "2026-10-08", "workId": "<opaque UUID>",
 "command": {
  "workId": "<same UUID>", "operationId": "<stable UUID>",
  "expectedReviewVersion": 1, "previewFingerprint": "<64 lowercase hex characters from preview>"
 }
}
```

Amount, currency, posting date, original event/tender, drawer and financial scope are server-derived. There is no arbitrary/backdated adjustment command. POST requires live `pos.day-close.late-cash.post`, with an amount-aware decision for the positive verified tender and THB. An applicable configured financial approval limit is mandatory; a missing limit or exceeded limit denies posting, including exact replay. Managers/admins receive the default permission in Authorization 15; it does not supply a financial cap. Accountants/cashiers do not receive the permission. POS rechecks own live reads/scope and original evidence/calendar before a new append. Antiforgery is mandatory at BFF; JSON body limit is 4 KiB.

Response has `scope`, `workId`, nullable `preview`/`receipt`, `canPost` and nullable `blocker`. A preview includes exact review version, safe Order/tender/drawer UUIDs, THB adjustment, current branch-local posting date/timezone/UTC interval and fingerprint. A receipt includes correction/operation/event UUIDs, original settlement scope, work/review/Order/tender/drawer, adjustment, posting date and posted UTC time. **Adjustment is an exact decimal string**, for example `"-100.0000"`. Private actors, authorization decisions, original broker identity, hashes and retained bodies stay out of correction responses. A posted receipt can refer to another original settlement linked to this same work; organization/Restaurant/branch remain exact.

`unsupported_cash_evidence` indicates no eligible retained cash/drawer evidence; `correction_review_required` requests the correction-required review before preview. New posting fails closed on missing/mismatched Order proof, ambiguous drawer, already-projected cash, stale review/preview, calendar/date change, sealed posting window, duplicate tender/work or lost authority. 400 is invalid request/case; 401/403 is unavailable session/authority; 409 is an evidence, operation, version or posting conflict; 503 is dependency/transport unavailable. Source proof 404 means original history missing and cannot authorize a new posting. All responses, including early failures, are no-store; BFF JSON is bounded to 128 KiB with 30-second deadline, POS to 25 seconds, evidence adapters to 15 seconds.

An uncertain response retains the exact command in memory and disables queue/source/review changes until exact success or a definitive rejection. Settlement progress reload preserves it. On definitive 400/403/404/409, reload current evidence before another command. Navigation/reload loses in-memory command identity; inspect the persisted correction outcome first. Exact retries reauthorize but do not require source evidence again. A different operation for already-posted work conflicts. Posting does not change review status or release custody; use the posted receipt as financial outcome.

`PosDaySummary` adds optional/default-zero `lateCashCorrectionAdjustment` and `lateCashCorrections`; `cashVariance` includes that adjustment. Existing drawer variance uses drawer close time; correction selection/hashing uses posted UTC time within the requested window. Gross sales/tenders are unchanged. Current-day posting becomes available to ordinary end-of-day reads when that branch day completes; the read API still rejects incomplete days. Source-only query tests verify the posting bucket without relaxing this boundary.
