# NexaConnect POS Service

Payment owns the optional Omise test webhook inbox/status recovery; POS service has no callback route or webhook-secret ownership. Its manual-tender cash projection remains unchanged. The five-scenario Omise hosted gate and the separate guarded external signed webhook delivery/process-interruption gate passed in the test account; production activation remains open. See [Payment webhook scope](../../../docs/Deployment/Omise-Webhooks.md).

The WPF client's opt-in Development-only `card_omise_test` handoff calls Order and Payment through their existing boundaries; POS service does not receive or persist the card token. Card capture does not use manual Paid confirmation or create a drawer cash sale. POS-owned shift/cash authorization and manual-tender behavior remain unchanged. See [client configuration and acceptance](../../../docs/Deployment/Omise-POS-Card-Token-Handoff.md).

POS migration 4 and the optional `OrderSettlementConsumer` project Order-owned manual tenders. Attribution uses the shift and THB cash-session time window containing the event, never whichever drawer is currently open. Cash creates one Order-linked `sale`; delayed delivery to a closed matching session recomputes variance atomically. PromptPay creates no drawer movement. Identity conflicts dead-letter; infrastructure failures retry. Safe logs/traces carry correlation, event, Order, and terminal identifiers; query `{service_name="nexaconnect-pos"} |= "POS Order settlement"`.

The POS service owns terminals, stores, shifts, and server-side POS operations. It is a bearer-token API; it does not initiate an interactive Keycloak login. Native POS login belongs to the `nexaconnect-pos` client and uses Authorization Code with PKCE.

## Current endpoints

- `POST /api/pos/v1/shifts/open` opens a shift after validating the branch scope, active store/terminal registration, and the `pos.shift.open` product authorization decision.
- `POST /api/pos/v1/shifts/{shiftId}/close` closes an open shift after validating the restaurant scope and the `pos.shift.close` authorization decision.
- `POST /api/pos/v1/cash-sessions/open` opens the single cash session allowed for an open shift. Reopening cash on that shift returns a safe `409`: restore/reconcile an existing open session, or close a shift whose session is already closed and start a new shift.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/movements` records a sale, refund, pay-in, pay-out, or float adjustment.
- `GET /api/pos/v1/cash-sessions/{cashSessionId}/summary` returns the cashier/terminal reconciliation, expected cash, movement history, status, and concurrency version.
- `POST /api/pos/v1/cash-sessions/{cashSessionId}/close` closes a session using its reviewed concurrency version and calculates the stored expected amount and variance.
- `GET /api/pos/v1/cash-reviews/access`, `GET /api/pos/v1/cash-reviews`, and `GET /api/pos/v1/cash-reviews/{cashSessionId}` expose exact-scope access, cursor-paged closed-session history, and immutable decision detail.
- `POST /api/pos/v1/cash-reviews/{cashSessionId}/decisions` records an idempotent, version-fenced `investigate` or `approve` decision without changing financial totals.
- `POST /api/pos/v1/terminals/enroll` enrolls or reactivates a terminal after the `pos.terminal.enroll` authorization decision.

All listed endpoints require an authenticated bearer token with the `nexaconnect-api` audience. Missing authentication context is rejected. Shift and terminal-enrollment operations reject invalid branch/store/terminal scope and denied authorization. Concurrent terminal or shift-number conflicts return `409`; a stale close returns `409` rather than overwriting another change. If Restaurant or Authorization is unavailable, shift and terminal-enrollment operations return `503` without exposing provider details.

Every cash movement requires the paired POS outbox headers `X-Client-Operation-Id` and `X-Nexa-Terminal-Id`; the native client persists the operation before its first HTTP attempt. Missing, malformed, or incomplete headers return `400`. PostgreSQL verifies the terminal and authenticated subject against the cash session's shift, then records the terminal-scoped operation and commits it with the movement and an advanced cash-session version. Retrying the same operation id and payload is accepted without duplicating the movement or advancing the version again; a scope mismatch returns `403`, and reusing the id with a different movement returns `409`. Summary and close also require the terminal header. Summary uses the shift subject and terminal as resource predicates. Close additionally compares the reviewed concurrency version in the same update that stores expected cash, counted cash, variance, closure time, and the next version.

Cash Review requires POS migration 5 and Authorization migration 7. Tenant administrators and store managers receive `pos.cash-review.read` and `pos.cash-review.resolve`; accountants receive read only. Restaurant hierarchy and the POS-owned store must match the requested organization/restaurant/branch/store scope. Lists use an opaque close-time/UUID keyset cursor over at most 31 days and 100 rows per page. A nonzero variance is `review_required` until investigated or approved for the current financial version. A late financial movement advances that version and supersedes the prior decision. The current projection and append-only history commit together, exact operation replay is safe, and stale or conflicting attempts return `409` for refresh. This workflow is online only. The optional migration-6 scanner publishes current cash-close snapshots separately; individual decisions do not emit a complete history stream.

## Cash-close publication

POS migration 6 adds the publication checkpoint. `CashClosePublication__Enabled` and `Outbox__Enabled` default to false. When enabled, the scanner resolves organization through Restaurant, locks each session and atomically queues `pos.cash-close.snapshot.v1` with the new checkpoint/version. It scans 100 candidates per batch at 15-second intervals, backfills existing closed sessions, and can coalesce intermediate changes. Enable the Reporting consumer/binding before POS outbox dispatch; configure `Outbox__ConnectionString` and matching exchange. POS `6→5` refuses after publication history exists. Query `{service_name="nexaconnect-pos"} |= "cash-close"`; never log the financial payload. See [contract, configuration, rollback and remaining production acceptance](../../../docs/API/Cash-Close-Reporting.md).

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

POS emits structured JSON request logs and safe cash replay accepted/replayed/denied/conflict events plus reconciliation and review authorization/conflict events without request bodies, reasons, tokens, or cash values. In Grafana Explore, query `{service_name="nexaconnect-pos"} |= "POS cash"`, then narrow by `CorrelationId`, `CashSessionId`, `TerminalId`, or `ClientOperationId`; use `{service_name="nexaconnect-pos"} |= "cash-review"` for supervisor reads and decisions.

## Verification

Run `powershell.exe -NoProfile -File scripts/test-pos-order-settlement-operations.ps1 -ConfirmDisposableInfrastructure -ConfirmDestructiveRollback`. Secret-inject `NEXACONNECT_POS_INTEGRATION_DB`, `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB`, and `NEXACONNECT_RABBITMQ_INTEGRATION_URI`; values are never printed. The current POS lifecycle, PostgreSQL cash/PromptPay projection, and hosted RabbitMQ restart/replay/dead-letter cases must execute. Review-focused PostgreSQL and HTTP cases are available through `PosPostgresStoreTests` and `PosShiftApiTests`. Evidence is retained under `.runstate/pos-order-settlement/<run-id>`.

Set `NEXACONNECT_ENVIRONMENT=Testing` and `NEXACONNECT_POS_INTEGRATION_DB` to a non-production PostgreSQL database whose role may create and drop isolated schemas, then run `dotnet test tests/Integration/NexaConnect.IntegrationTests/NexaConnect.IntegrationTests.csproj --filter "FullyQualifiedName~PosPostgresStoreTests"`. The suite includes the manual-tender projection case. The guarded live matrix passed its PostgreSQL projection/replay case on 2026-09-03 UTC. Without both settings, database cases report skipped.

For the full migration lifecycle, also set `NEXACONNECT_POS_CLEAN_INSTALL_ACCEPTANCE=1` and `NEXACONNECT_POSTGRES_ADMIN_INTEGRATION_DB` to a disposable Development/Test administrator allowed to create and drop databases, then filter `PosMigrationRunnerAcceptanceTests`. It manages only generated names matching `nexaconnect_pos_clean_it_<guid>`, runs 0→5→4→3→5, verifies review objects and downgrade refusal after immutable history exists, and force-drops that validated database. Never use production credentials.

POS migration 7 supports the separate operational replay CLI: manifest-pinned original-event selection and append-only run/attempt audit, with no financial/checkpoint/outbox mutation. Backlog polling runs when publication or outbox dispatch is enabled, with 15-second count/age samples and failure counters; values can be stale after collection failure. Publication outcomes expose queued/retry/scan-failure counts. See [replay, least privilege and recovery acceptance](../../../docs/Deployment/Cash-Close-Recovery.md).
