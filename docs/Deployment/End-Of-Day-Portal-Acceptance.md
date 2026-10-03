# Joined end-of-day portal acceptance

The guarded launcher joins actual Keycloak login, Customer Portal/BFF, Platform Directory, Restaurant, Authorization, Order, Payment, POS and Reporting hosts. Source summary HTTP responses and browser financial responses are not mocked. PostgreSQL 17, Keycloak 26.7.0 and RabbitMQ 4 belong to a fresh generated Compose project; ports bind only to IPv4 loopback.

## Run

Requires PowerShell 7, .NET 10, Node 20.19+, installed Chromium and a local Docker socket. Prepare dependencies first:

```powershell
dotnet restore NexaConnect.sln
Push-Location src/Frontend
npm ci --ignore-scripts
npx playwright install chromium
npm run test:end-of-day:guards
Pop-Location
./scripts/test-end-of-day-portal.ps1 -ConfirmDisposableInfrastructure
```

The wrapper selects end-of-day mode in the existing financial portal lifecycle runner. That mode uses `--no-restore` after preparation. `-DockerExecutable` selects a local CLI; `-NoBuild` requires current fixture/service Debug binaries, while recovery and the published BFF/portal still build. The flag authorizes only freshly generated infrastructure, synthetic fixtures and exact owned cleanup. Remote Docker sockets and existing containers in the generated project are rejected. Local certificate export is removed; no trust-store change is made. Browser TLS relaxation is confined to the guarded loopback environment.

Seven independent service databases apply actual migrations: Platform Directory 3, Restaurant 3, Authorization 9, Order 11, Payment 10, POS 7 and Reporting 20. Migration ownership and service-specific runtime roles are separate. Broad disposable fixture runtime grants do not certify production least privilege. Two distinct customer-viewer OIDC identities have active membership in two organizations; only the first has product permissions. The accountant is branch-scoped and manager restaurant-scoped. All source permissions remain required; no migration or production grant is added.

Eight application children run on generated loopback ports: Platform Directory, Restaurant, Authorization, Order, Payment, POS, Reporting and the Customer BFF serving its published portal. Order uses `Workflow__UseHttpAdapters=true` to compose its owning read dependencies; unused Catalog/Inventory/Kitchen command adapters point to unreachable loopback port 1. This gate sends no Order placement or compensation commands and does not exercise those dependencies.

## Fixture and delivery

The test-only FinancialPortalAcceptance fixture selects yesterday in `Asia/Bangkok`. Restaurant provisioning establishes that timezone and THB currency. Actual Payment intent/refund repositories receive a fixed `TimeProvider` for historical creation, authorization, capture and refund completion; production repositories default to `TimeProvider.System`. No environment option changes the service clock. Synthetic provider success/uncertainty is injected directly into repository workflows, not through an external provider or Payment command HTTP.

Order's unpaid creation time is set in fixture Infrastructure before issuing its immutable receipt; the owning repository then retains the real sale event/outbox. Payment retains the real completed-refund receipt/event/outbox in its owning transaction. Initial POS historical snapshots are inserted with parameterized fixture-only Infrastructure SQL into an empty run-owned POS database. Existing receipts, retained events and financial history are never rewritten; triggers remain enabled. POS close/review command execution is covered by separate component/cash-close gates, not this fixture.

The selected day contains a THB 100 card sale/tender, THB 25 completed refund, THB 75 net sales and THB -5 closed-drawer variance. Separate unresolved Order, pending intent, uncertain refund, older open shift/session and unreviewed closed drawer remain visible. The browser sees projection differences before delivery. The PostgreSQL Order host registers its normal dispatcher with a fixture-only batch size of zero so it cannot publish before the browser delivery phase; Payment dispatch is disabled. Fixture-owned real source outbox dispatchers and RabbitMQ deliver retained events to actual Reporting consumers; the fixture never inserts Reporting facts. The actual recovery CLI records an exact-day observation. Matching source/projection totals still leave the recorded check historical; the draft never certifies settlement.

## Eight required browser scenarios

1. Completed Bangkok date returns exact UTC boundaries, owning totals/tenders/operational counts; real delayed event delivery removes projection differences and a recorded check remains historical.
2. Adjacent completed date excludes selected-day financial amounts; a future date returns 400 and clears results.
3. Denied branch and foreign tenant return 403 and clear old totals.
4. Actual Reporting-to-Payment and BFF-to-Reporting transport outages return sanitized 503 without partial totals, then recover.
5. Filter and tenant changes fence delayed real downstream responses.
6. Accountant reads send no financial commands, expose no settlement controls and POST is rejected with 405.
7. Revoking the accountant's live `payment.refund.read` permission rejects the existing session at the owning source boundary.
8. Suspending the manager's organization membership rejects the existing session.

Two browser-worker TCP proxies bind only to generated loopback ports and forward exclusively to generated Reporting/Payment listeners. They have no control HTTP endpoint and do not inspect or fabricate bodies. Browser network traffic is limited to generated BFF/identity origins. Screenshots, traces and videos are disabled; failure output omits credentials, tokens, cookies, bodies and connection strings. Shared JSON/correlation telemetry remains on actual service hosts. Use the source `End-of-day source`, Reporting `End-of-day draft` and BFF end-of-day events documented in component READMEs.

## Evidence and cleanup

All six Authorization persistence tests and eight distinct browser cases must pass without skips/retries/repetition. Guard tests reject missing opt-in, wrong scopes/calendar/window, remote endpoints, arbitrary fixture executables and colliding source proxy ports. Missing settings fail rather than skip.

Require successful launcher exit plus `passed`, `authorizationPassed` and `cleanupVerified` in `.runstate/end-of-day-portal/<run-id>/verification.json`. Browser evidence is `src/Frontend/test-results/end-of-day-live/<run-id>/summary.json`. Source revision/dirty state and completion time are recorded. Local fixture state/TRX/service logs are restricted diagnostics, excluded from CI uploads. End-of-day service children discard inherited NEXACONNECT_* and double-underscore configuration variables before explicit configuration injection. Cleanup terminates exact retained child handles, removes the exact generated Compose project/volumes and certificate, verifies no containers remain and restores launcher environment variables. Failure to verify cleanup fails the gate. Forced interruption can bypass cleanup; never reuse a partial fixture.

Local Windows acceptance on 2026-10-03 passed all eight browser and six Authorization persistence cases without skips; the successful launcher verified cleanup. The `Joined end-of-day portal gate` CI workflow is configured to run the matrix with Ubuntu, .NET 10 and Node 22 and upload only bounded summary files for 14 days. Remote execution and required branch protection remain separate evidence. See [execution evidence](../Architecture/Evidence/End-Of-Day-Joined-Acceptance.md).

This gate proves joined authenticated reads, live authorization and actual retained-event propagation. It excludes cashier/payment/refund command HTTP, external providers, physical terminals, DST joined execution (covered separately by unit tests), concurrent financial cutoff certification, durable settlement approval/locking, and target-production TLS/clock/privilege/latency acceptance. Production clock alignment and current timezone metadata limitations remain in the [draft rollout](End-Of-Day-Draft.md).
