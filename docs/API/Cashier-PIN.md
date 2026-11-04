# Cashier PIN API V1

POS owns `/api/pos/v1/customer/organizations/{organizationId}/cashier-pin`.
All routes require a validated bearer, live Authorization decision and an active exact branch/store/terminal in the route organization. Subjects and issuer come from authenticated context/configuration, never self-service request input. Responses are no-store. PINs belong in HTTPS bodies only; never include them in links, diagnostics or screenshots.

| Method / suffix | Input | Output |
| --- | --- | --- |
| GET root | `branchId`, `storeId`, `terminalId` query | Status |
| PUT root | `scope`, `newPin`, optional `currentPin`, `expectedVersion`, `operationId` | Result |
| POST `/verify` | `scope`, `pin`, `expectedVersion`, `attemptId` | Result |
| POST `/staff/status` | `scope`, `subject` | Status; requires manage |
| POST `/staff` | `scope`, `subject`, `expectedVersion`, `operationId`, `action: reset or revoke` | Result; requires manage |

`scope` contains `branchId`, `storeId`, `terminalId`. Status is `{state,version,lockedUntilUtc}`; state is `unset`, `active` or `revoked`. Result is `{accepted,status}`. No verifier, PIN or failure count is returned. Use `pos.cashier-pin.use` for self operations and `pos.cashier-pin.manage` for staff operations. Only tenant-admin has organization-wide manage by default; store-manager can use their own PIN but cannot reset another cashier by default.

Read status before versioned changes; IDs must be nonempty GUIDs and versions nonnegative. A PIN is exactly six ASCII digits. Active changes require the current PIN; reset enables fresh enrollment while revoke blocks it. Five incorrect attempts impose fifteen-minute durable lockout. Non-null malformed verify values up to64characters also count as failures for an active, unlocked credential at the expected version; larger/null input fails validation. Versions advance on changes/reset/revoke, not verification counters. Status reads never grant workspace access.

A duplicate operation/attempt ID returns `accepted:false` without re-executing or consuming another attempt. Successful unlock authorization is never replayed. Concurrent version conflicts, wrong PIN, lockout or revocation also return 200 with `accepted:false` and current status; callers must check `accepted`. After an uncertain write, reload status rather than blindly retry. New PIN input requires a new attempt ID.

Transport errors: 400 `cashier_pin_invalid` for application validation (malformed JSON/model binding may return the framework validation problem), 401 unauthenticated, 403 live scope/permission denied, 429 per-subject rate limit (30 requests/minute per service instance), 503 `cashier_pin_unavailable`. Maximum body 4,096 bytes; application deadline fifteen seconds. Persistent per-credential attempt limits apply across instances independently of the in-process request limiter. No external identity session is created by these routes.

Telemetry uses shared `nexaconnect-pos` correlation/JSON/OTLP. Search `service.name=nexaconnect-pos` and `Cashier PIN boundary`, then the validated correlation ID; categories are `authorization-denied`, `denied-or-conflict`, `unavailable`. Client events use `nexaconnect-pos-client`, `cashier-pin-*` and `pin-session-renewal`. Never log bodies, PINs, verifiers, tokens or subjects. Audit contains restricted actor/scope/decision metadata, not credential values. See [security](../Identity/POS-Session-PIN.md).
