# Late financial work reviews

Customer Portal exposes this queue after loading a committed settlement. Select Order, Payment or POS, load work, page forward and view an individual record. Accountants with existing source/day read permissions can inspect records and history; tenant-admin/store-manager defaults additionally permit review through live `pos.day-close.late-work.review`. Customer realm roles alone never grant the business permission. Workload identities cannot review through these customer routes.

| Boundary | Routes |
| --- | --- |
| Customer BFF | GET `/bff/customer/day-close-late-work`, GET `/{workId}`, POST base path, GET `/csrf` |
| POS facade | GET `/api/pos/v1/customer/organizations/{organizationId}/day-close-late-work`, GET `/{workId}`, POST base path |
| Owning source | GET `/api/{order|payment|pos}/v1/customer/late-work`, GET `/{workId}`, POST `/reviews` |

BFF/facade GET query requires `branchId`, `businessDate` (`yyyy-MM-dd`) and `source` (`Order`, `Payment`, `POS`); list optionally takes `cursor` (maximum 200 characters) and `limit` (1–50, default 25). Detail requires a nonempty work UUID. The facade derives organization, Restaurant, exact original financial window and settlement UUID from server-owned scope and saved committed settlement. Direct source GET takes `organizationId`, `restaurantId`, `branchId`, `fromUtc`, `toUtc`, `settlementId`, plus list pagination. Sources require a closed window of at most 27 hours and exact committed barrier membership. Armed/aborted custody is excluded.

BFF/facade POST body:

```json
{
  "branchId": "<branch UUID>", "businessDate": "2026-09-01", "source": "POS",
  "workId": "<opaque work UUID>",
  "command": {
    "workId": "<same work UUID>", "operationId": "<stable operation UUID>",
    "expectedVersion": 0, "decision": "investigate", "reasonCode": "investigate_delivery"
  }
}
```

Direct source POST is `{ "scope": { "window": <EndOfDayWindow>, "settlementId": "<UUID>" }, "command": <same command> }`. Only these pairs are admitted:

| Decision | Reason | Resulting review status |
| --- | --- | --- |
| investigate | investigate_delivery | investigating |
| require_correction | correction_needed | correction_required |
| acknowledge | evidence_checked | reviewed |

New decisions append version +1; exact retries retain actor/scope/command/operation and require live permission. Response contains `operationId`, original `operationDecision`, and current `detail` (which can be newer after another manager's decision). The browser keeps uncertain commands in memory and disables source/list changes until exact success or a definitive 400/403/404/409 rejection; rejection requires a fresh load. It does not persist financial data or operations locally. Reload/navigation loses in-memory retry state; inspect current history before a new decision.

List returns `scope`, `items`, `nextCursor` and `canReview`. Detail returns `scope`, `item`, bounded `settlementLinks`, `history`, `historyTruncated` and `canReview`. Each item has opaque `workId`, safe `eventType`, received/optional occurred UTC times, `custodyReason=late_delivery_for_settled_day`, allow-listed UUID `records`, version and status. Version zero has status `pending_review`. History is newest-first, at most 20; settlement references are at most 20 and are not an exhaustive cross-day search. Lists are live seek pages: restart at the first page to observe newly inserted earlier rows. No actor, provider/broker identifier, payload, hash, authorization UUID, amount or sensitive error is public.

Responses, including early authentication/antiforgery errors, are no-store. POST is bounded to 4 KiB. Source/facade deadlines are 25 seconds; BFF uses 30 seconds and bounds JSON to 128 KiB. 400 means invalid request; 401/403 means session/authority unavailable; source detail 404 means no matching scoped committed work; 409 means review version/operation or settlement state conflicts; 503 means dependency/response unavailable. A transport failure may occur after commit: retry the same operation. Reviews never post money, release custody or alter a receipt.
