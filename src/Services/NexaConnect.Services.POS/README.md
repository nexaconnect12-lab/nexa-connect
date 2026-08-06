# NexaConnect POS Service

The POS service owns terminals, stores, shifts, and server-side POS operations. It is a bearer-token API; it does not initiate an interactive Keycloak login. Native POS login belongs to the `nexaconnect-pos` client and uses Authorization Code with PKCE.

## Current endpoints

- `POST /api/pos/v1/shifts/open` opens a shift after validating the branch scope, active store/terminal registration, and the `pos.shift.open` product authorization decision.
- `POST /api/pos/v1/shifts/{shiftId}/close` closes an open shift after validating the restaurant scope and the `pos.shift.close` authorization decision.
- `POST /api/pos/v1/cash-sessions/open` opens a cash session for an open shift.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/movements` records a sale, refund, pay-in, pay-out, or float adjustment.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/close` closes a cash session and calculates the variance.
- `POST /api/pos/v1/terminals/enroll` enrolls or reactivates a terminal after the `pos.terminal.enroll` authorization decision.

All listed endpoints require an authenticated bearer token; shift operations require the `nexaconnect-api` audience. Missing authentication context, invalid branch/store/terminal scope, and denied authorization are rejected. Concurrent terminal or shift-number conflicts return `409`; a stale close returns `409` rather than overwriting another change. If Restaurant or Authorization is unavailable, the shift API returns `503` without exposing provider details.

## Configuration

- `ConnectionStrings:POS` — the POS-owned PostgreSQL database.
- `Authentication:*` — the Keycloak realm issuer and API audience.
- `WorkloadIdentity:*` — the POS service-account client used to read Restaurant hierarchy data. The client secret must come from a secret store or environment configuration.
- `Services:Restaurant` — the Restaurant API used to resolve branch scope.
- `Services:Authorization` — the Authorization API used to evaluate product permissions.

Runtime database access is implemented behind the POS Infrastructure persistence adapter. Domain invariants and workflow orchestration live outside the controller; raw SQL remains parameterized and isolated to Infrastructure.

Production requests must use HTTPS. The service rejects cleartext HTTP requests outside Development and Testing; until an allow-listed forwarded-header configuration is deployed, TLS must terminate at the POS process itself.

For local development, start the service with the `https` launch profile (`https://localhost:7120` and `http://localhost:5225`) and run the Restaurant and Authorization services at their configured development addresses.
