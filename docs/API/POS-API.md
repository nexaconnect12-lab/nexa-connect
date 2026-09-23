# POS API

## Order manual-tender projection

This is an asynchronous integration boundary, not a public POS endpoint. With `OrderSettlementConsumer` enabled after POS migration 4, POS consumes `order.manual-tender-settled.v1`. It verifies restaurant, branch, terminal, and the shift/session time window containing the event. Cash records one Order-linked sale; late delivery updates the historical closed session and recomputes variance. PromptPay never changes drawer totals. Matching delivery is replay; invalid identity/scope contracts dead-letter and infrastructure failures retry.

The POS API is owned by `NexaConnect.Services.POS` and is consumed through the gateway or an authorized native POS client. All endpoints require a Keycloak bearer access token with the `nexaconnect-api` audience.

## Open shift

`POST /api/pos/v1/shifts/open`

```json
{
  "branchId": "00000000-0000-0000-0000-000000000000",
  "storeId": "00000000-0000-0000-0000-000000000000",
  "terminalId": "00000000-0000-0000-0000-000000000000",
  "shiftNumber": "SHIFT-001"
}
```

The service resolves the branch through Restaurant, verifies the active store and terminal belong to that scope, and requests the `pos.shift.open` decision from Authorization. A successful response is `200 OK`:

```json
{
  "shiftId": "00000000-0000-0000-0000-000000000000",
  "authorizationDecisionId": "00000000-0000-0000-0000-000000000000"
}
```

`400` means the request is invalid, `403` means scope or authorization was denied, `409` means the terminal already has an open shift or the shift number is already in use, and `503` means a required Restaurant or Authorization dependency was unavailable.

## Close shift

`POST /api/pos/v1/shifts/{shiftId}/close`

The service loads the open shift, re-resolves its Restaurant scope, requests the `pos.shift.close` decision, and applies an optimistic-concurrency update. A successful close returns `204 No Content`. Missing or already-closed shifts return `404`; denied scope or authorization returns `403`; a concurrent update returns `409`; and an unavailable Restaurant or Authorization dependency returns `503` without exposing provider details.

The API does not redirect to Keycloak. Interactive login is owned by the BFF or the native POS client; this service validates and consumes the resulting access token.

## Cash sessions

`POST /api/pos/v1/cash-sessions/open` opens a cash session for an open shift:

```json
{ "shiftId": "00000000-0000-0000-0000-000000000000", "storeId": "00000000-0000-0000-0000-000000000000", "currency": "USD", "openingAmount": 100.00 }
```

A successful open returns `200 OK` with `{ "cashSessionId": "...", "openedBy": "<subject>" }`.

Each shift owns at most one cash session. A concurrent or repeated open returns `409 Conflict` with safe recovery guidance instead of exposing a database constraint failure. If that session is closed, close the shift and open a new shift before opening another cash session. An existing open session must be restored or reconciled rather than replaced.

`POST /api/pos/v1/cash-sessions/{cashSessionId}/movements` records a positive sale, refund, pay-in, pay-out, or float-adjustment movement:

```json
{ "movementType": "pay_in", "amount": 25.00, "reasonCode": "FLOAT" }
```

Supported movement types are `sale`, `refund`, `pay_in`, `pay_out`, and `float_adjustment`. A successful record returns `202 Accepted`.

Every cash-movement submission supplies both `X-Client-Operation-Id: <uuid>` and `X-Nexa-Terminal-Id: <uuid>`, including its first online attempt. Missing headers, an empty UUID, or malformed UUID returns `400` rather than disabling deduplication. The native client persists both identifiers before that first attempt and uses the same pair for replay after an ambiguous response. PostgreSQL verifies that the terminal and authenticated subject match the cash session's shift, stores the terminal-scoped operation in `sync_operations`, and commits that marker with the cash movement. A scope mismatch returns `403`. Replaying the same operation id and normalized movement payload returns `202 Accepted` without inserting a duplicate movement; reusing the operation id for a different payload returns `409 Conflict`. The native client blocks cash-session close while movements for that session remain queued or rejected.

`POST /api/pos/v1/cash-sessions/{cashSessionId}/close` closes the session and calculates the variance from the opening amount and movements:

```json
{ "actualClosingAmount": 125.00, "expectedConcurrencyVersion": 3 }
```

Supply `X-Nexa-Terminal-Id: <uuid>`. The version must come from the latest reconciliation summary. A successful close returns `204 No Content`; a stale version, closed session, or cashier/terminal mismatch returns `409 Conflict` without changing the drawer.

`GET /api/pos/v1/cash-sessions/{cashSessionId}/summary` returns the terminal-bound cashier's authoritative reconciliation read model. Supply `X-Nexa-Terminal-Id: <uuid>`. The response contains opening amount, signed net movements, expected closing amount, optional actual and variance values, status, timestamps, concurrency version, and ordered movement rows:

```json
{
  "cashSessionId": "00000000-0000-0000-0000-000000000000",
  "shiftId": "00000000-0000-0000-0000-000000000000",
  "storeId": "00000000-0000-0000-0000-000000000000",
  "terminalId": "00000000-0000-0000-0000-000000000000",
  "currency": "THB",
  "openingAmount": 1000.00,
  "netMovementAmount": 250.00,
  "expectedClosingAmount": 1250.00,
  "actualClosingAmount": null,
  "varianceAmount": null,
  "status": "open",
  "concurrencyVersion": 3,
  "movements": []
}
```

Sales, pay-ins, and float adjustments increase expected cash; refunds and pay-outs decrease it. A missing bearer token returns `401`, a missing or malformed terminal header returns `400`, and a session outside the authenticated subject/terminal scope returns `404` to avoid resource disclosure. Safe summary-denial and close-conflict events include session and terminal identifiers without amounts, bodies, or credentials.

All cash endpoints require an authenticated bearer token. Cash-session state and calculations are owned by the POS database; the client must not infer a successful close until the API returns `204`.
Invalid identifiers, amounts, currencies, or movement types return `400`. A shift/session state conflict returns `409`.

## Cash reconciliation review

POS migration 5 and Authorization migration 7 add an online supervisor workflow for closed cash sessions. The caller supplies the configured organization, branch, and store identifiers; POS resolves the restaurant from the authoritative branch hierarchy, verifies that the POS-owned store matches it, and asks Authorization for `pos.cash-review.read` or `pos.cash-review.resolve` in that exact derived scope.

- `GET /api/pos/v1/cash-reviews/access?organizationId=...&branchId=...&storeId=...` reports the caller's read and resolve capabilities.
- `GET /api/pos/v1/cash-reviews?organizationId=...&branchId=...&storeId=...&fromUtc=...&toUtc=...&limit=...&cursor=...` returns closed sessions in descending close-time/identifier order. The inclusive/exclusive UTC range may span at most 31 days, `limit` is 1-100, and `cursor` is an opaque stable keyset cursor.
- `GET /api/pos/v1/cash-reviews/{cashSessionId}?organizationId=...&branchId=...&storeId=...` returns the authoritative financial snapshot, movement rows, and append-only decision history.
- `POST /api/pos/v1/cash-reviews/{cashSessionId}/decisions` records `investigate` or `approve`. Its JSON body includes `organizationId`, `branchId`, `storeId`, a required 1-200 character `reason`, `expectedSessionVersion`, `expectedReviewVersion`, and `idempotencyKey` UUID.

List items are `balanced` when variance is zero, `review_required` when a nonzero variance has no decision for its current financial version, and otherwise `investigating` or `approved`. A late Order cash settlement advances the financial version and makes an older decision historical; the session becomes `review_required` when the resulting variance remains nonzero, or `balanced` when it becomes zero. Investigation can advance to approval for the same financial version. An approved current version cannot be decided again.

The decision endpoint commits the current review projection and immutable history in one POS transaction. Exact replay of the same operation identity and normalized payload returns the committed result. Reusing an identity for a different payload, using stale financial/review versions, or racing another decision returns `409`; clients must refresh and must not retry automatically with new values. Invalid filters or decisions return `400`, read/resolve denial returns `403`, an out-of-scope detail returns `404`, and a required dependency failure returns a sanitized `503`.

Review history retains the reviewer subject and Authorization decision identifier for audit. Decisions do not change cash totals or directly publish integration events. The optional POS migration-6 scanner separately publishes coalesced current snapshots to the [cash-close Reporting projection](Cash-Close-Reporting.md); immutable decision history stays in POS. Reasons, amounts, tokens, headers, and request bodies are excluded from structured logs.

## Terminal enrollment

`POST /api/pos/v1/terminals/enroll` enrolls or reactivates a terminal after Restaurant scope validation and the `pos.terminal.enroll` Authorization decision:

```json
{ "branchId": "00000000-0000-0000-0000-000000000000", "storeId": "00000000-0000-0000-0000-000000000000", "terminalId": "00000000-0000-0000-0000-000000000000", "code": "POS-001", "deviceType": "pos" }
```

Supported device types are `pos`, `kiosk`, `kds`, and `edge`. A successful enrollment returns `201 Created`, sets `Location` to `api/pos/v1/terminals/{terminalId}`, and returns `{ "terminalId": "..." }`. Enrollment is an online administrative operation and is not performed from an offline client.

Invalid enrollment input returns `400`, denied branch scope or authorization returns `403`, an active matching store that cannot be found returns `404`, and unavailable Restaurant or Authorization dependencies return `503` without exposing provider details.
