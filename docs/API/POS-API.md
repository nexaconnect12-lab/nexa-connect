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
