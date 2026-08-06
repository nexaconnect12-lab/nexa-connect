# POS API

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

- `POST /api/pos/v1/cash-sessions/open` opens a cash session for an open shift with a currency and opening amount.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/movements` records a positive sale, refund, pay-in, pay-out, or float-adjustment movement.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/close` closes the session and calculates the variance from the opening amount and movements.

All cash endpoints require an authenticated bearer token. Cash-session state is owned by the POS database; the client must not infer a successful close until the API returns `204`.

## Terminal enrollment

`POST /api/pos/v1/terminals/enroll` enrolls or reactivates a terminal after Restaurant scope validation and the `pos.terminal.enroll` Authorization decision. The request includes branch, store, terminal, code, and device type. Enrollment is an online administrative operation and is not performed from an offline client.
