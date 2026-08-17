# NexaConnect POS Service

The POS service owns terminals, stores, shifts, and server-side POS operations. It is a bearer-token API; it does not initiate an interactive Keycloak login. Native POS login belongs to the `nexaconnect-pos` client and uses Authorization Code with PKCE.

## Current endpoints

- `POST /api/pos/v1/shifts/open` opens a shift after validating the branch scope, active store/terminal registration, and the `pos.shift.open` product authorization decision.
- `POST /api/pos/v1/shifts/{shiftId}/close` closes an open shift after validating the restaurant scope and the `pos.shift.close` authorization decision.
- `POST /api/pos/v1/cash-sessions/open` opens a cash session for an open shift.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/movements` records a sale, refund, pay-in, pay-out, or float adjustment.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/close` closes a cash session and calculates the variance.
- `POST /api/pos/v1/terminals/enroll` enrolls or reactivates a terminal after the `pos.terminal.enroll` authorization decision.

All listed endpoints require an authenticated bearer token with the `nexaconnect-api` audience. Missing authentication context is rejected. Shift and terminal-enrollment operations reject invalid branch/store/terminal scope and denied authorization. Concurrent terminal or shift-number conflicts return `409`; a stale close returns `409` rather than overwriting another change. If Restaurant or Authorization is unavailable, shift and terminal-enrollment operations return `503` without exposing provider details.

Every cash movement requires the paired POS outbox headers `X-Client-Operation-Id` and `X-Nexa-Terminal-Id`; the native client persists the operation before its first HTTP attempt. Missing, malformed, or incomplete headers return `400`. PostgreSQL verifies the terminal and authenticated subject against the cash session's shift, then records the terminal-scoped operation in `sync_operations` and commits it with the movement. Retrying the same operation id and payload is accepted without duplicating the cash movement; a scope mismatch returns `403`, and reusing the id with a different movement returns `409`.

## Configuration

- `ConnectionStrings:POS` — the POS-owned PostgreSQL database.
- `Authentication:*` — the Keycloak realm issuer and API audience.
- `WorkloadIdentity:*` — the POS service-account client used to read Restaurant hierarchy data. The client secret must come from a secret store or environment configuration.
- `Services:Restaurant` — the Restaurant API used to resolve branch scope.
- `Services:Authorization` — the Authorization API used to evaluate product permissions.
- `Observability:*` — optional OTLP endpoint and service version settings for service name `nexaconnect-pos`.

Runtime database access is implemented behind POS Application-owned persistence ports and Infrastructure adapters. Shift, cash-session, terminal-enrollment, and replay-idempotency validation and workflow orchestration live in Application services; controllers retain transport authentication context and HTTP mapping, while raw SQL remains parameterized and isolated to Infrastructure.

Production requests must use HTTPS. The service rejects cleartext HTTP requests outside Development and Testing; until an allow-listed forwarded-header configuration is deployed, TLS must terminate at the POS process itself.

For local development, start the service with the `https` launch profile (`https://localhost:7120` and `http://localhost:5225`) and run the Restaurant and Authorization services at their configured development addresses.

POS emits structured JSON request logs and safe cash replay accepted/replayed/denied/conflict events without request bodies, tokens, or cash values. In Grafana Explore, query `{service_name="nexaconnect-pos"} |= "POS offline cash movement"`, then narrow by `CorrelationId`, `CashSessionId`, `TerminalId`, or `ClientOperationId`.

## Verification

Set `NEXACONNECT_ENVIRONMENT=Testing` and `NEXACONNECT_POS_INTEGRATION_DB` to a non-production PostgreSQL database whose role may create and drop isolated schemas, then run `dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter "FullyQualifiedName~PosPostgresStoreTests"`. Seven cases passed locally against PostgreSQL 17. Without both opt-in values, the cases report skipped rather than passed. The tests remove their generated schemas; they do not provide the still-required 0→3→2→3 full migration-runner evidence.
