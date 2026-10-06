# Joined financial completeness portal acceptance

The guarded runner joins real Keycloak login, Customer BFF/portal, Platform Directory, Restaurant, Authorization, Reporting, PostgreSQL 17 and RabbitMQ 4. Browser report responses and resource authorization are not mocked. Synthetic retained sale/refund evidence is created through owning Order and Payment repositories; provider outcomes are fixture inputs, not external provider calls or checkout HTTP acceptance.

## Run

Requires PowerShell 7, the repository .NET SDK, Node 22.12+ (22.x) or 24+, installed Playwright Chromium and a local Docker socket:

```powershell
Push-Location src/Frontend
npm ci --ignore-scripts
npx playwright install chromium
npm run test:financial-completeness:guards
Pop-Location
./scripts/test-financial-completeness-portal.ps1 -ConfirmDisposableInfrastructure
```

The confirmation covers only newly generated local infrastructure, synthetic fixtures and its cleanup. The runner rejects remote Docker sockets and existing containers in its generated Compose project. Optional `-DockerExecutable` selects the CLI; discovery includes per-user Windows Docker Desktop. `-NoBuild` requires current fixture/service Debug artifacts; recovery CLI and published BFF/portal are still built. The runner exports a password-protected development certificate, never changes trust, and removes the export during cleanup. Browser TLS verification is relaxed only for this guarded loopback test.

Six independent databases use migration ownership and separate runtime roles: Platform Directory 3, Restaurant 3, Authorization 9, Order 11, Payment 10 and Reporting 20/application 0.23.0. The real migration runner applies each from empty history. These disposable runtime grants support fixtures; they are not proof of a deployed least-privilege operator credential policy. The imported run-specific Keycloak realm creates two distinct users. Both belong to two organizations; the accountant's sales grant is branch-scoped and the manager's is restaurant-scoped. Neither receives grants in the second organization.

The [fixture tool](../../src/Tools/NexaConnect.FinancialPortalAcceptance/README.md) uses existing provisioning and aggregate repositories. It completes one synthetic captured intent, Paid receipt-backed Order and partial refund, retaining original financial publications/outboxes. No Order/Payment HTTP hosts or real provider funds are needed. It waits for the next UTC minute before closing the browser's minute-aligned exact window. Source mutations remain owning local transactions; no cross-database SQL or distributed transaction is added.

Reporting starts both actual financial consumers. The runner verifies both broker bindings before browser delivery. Fixture-owned outbox dispatchers publish retained events with confirmed RabbitMQ delivery; a temporary run-owned sink receives other source event types without projecting them. The actual Reporting consumer writes facts/hash receipts. The existing recovery executable records observations with `--record`, using validated run-owned connections and bounded fixture attribution. It treats exit 1 as a valid recorded gap, exit 0 as observed complete, and exit 2 as failure. The portal never runs repair or records a check.

## Seven required scenarios

1. Not checked becomes recorded Gaps detected before delivery, then Observed complete after actual outbox/consumer delivery and another operator record. Separate financial inventories, source/check labels and gross/refund/net totals are visible.
2. Branch-scoped accountant denial and valid foreign-tenant selection reject the original resources and remove financial evidence.
3. A real BFF-to-Reporting TCP outage produces unavailable results, clears old evidence and recovers after reconnection.
4. Filter and UI tenant changes clear results while the test-owned proxy holds real downstream requests; released late responses cannot restore previous evidence.
5. The accountant reads identical sales/observation windows; no write controls or automatic financial mutations exist, and an explicit POST probe is rejected with 405.
6. An explicit sales-permission deny rejects the existing accountant session.
7. Suspending organization membership rejects the existing manager session.

The fault proxy is owned by the browser worker, binds only to loopback and forwards only to the generated Reporting host. It has no control HTTP endpoint. Browser network traffic is restricted to generated BFF/identity origins. Screenshots, traces and videos are disabled. Safe failure output excludes browser error bodies, credentials, cookies, financial payloads and connection strings. Local service logs and fixture state remain restricted run diagnostics, excluded from CI uploads.

## Evidence and cleanup

Six Authorization persistence tests and all seven unique browser cases must pass without skips, retries or repetition. Missing settings or invalid run/scope/window/endpoints fail configuration. The browser writes `src/Frontend/test-results/financial-completeness-live/<run-id>/summary.json`; the runner writes `.runstate/financial-portal/<run-id>/verification.json` after cleanup. Require `passed`, `authorizationPassed` and `cleanupVerified` plus a successful launcher exit. Revision and dirty-worktree provenance are recorded; no production acceptance is implied.

Cleanup terminates only retained child-process handles, removes the exact generated Compose project/volumes, verifies no containers remain, removes the exported certificate and restores every environment variable set by the runner. A cleanup failure fails the run. Forced interruption can bypass cleanup; inspect only that recorded run's resources and never reuse its partially mutated fixtures.

The `Joined financial-completeness portal gate` GitHub workflow repeats the matrix on Ubuntu with .NET 10 and Node 22, uploading only the two bounded summary paths for 14 days. Remote execution, required branch protection and target-production acceptance are separate evidence. See [acceptance record](../Architecture/Evidence/Financial-Completeness-Joined-Acceptance.md).

Existing JSON/correlation telemetry remains on `nexaconnect-customer-bff` and `nexaconnect-reporting`. Query `{service_name="nexaconnect-customer-bff"} |= "Financial completeness BFF"` or Reporting's consumer/authorization outcomes; see [read contract](../API/Financial-Reporting-Completeness.md). Historical observations do not certify settlement/global accounting, all historical source evidence, provider capture/refund acceptance, or cashier UI.

The joined matrix exposed a missing Restaurant hierarchy in Reporting's customer authorization decision. Deploy the corrected Reporting authorization adapter with `Services__Restaurant`, `WorkloadIdentity__Authority`, `WorkloadIdentity__ClientId=nexaconnect-reporting-service` and a distinct secret-managed `WorkloadIdentity__ClientSecret`. Provision the dedicated client/audience in persisted Keycloak realms through a reviewed update; restart/import does not apply it. Restaurant's endpoint-specific branch-scope policy permits this read without granting Reporting generic workload access. Coordinate Restaurant policy and identity configuration before enabling corrected Reporting reads. No schema or customer permission widening is required; see [Reporting setup](../../src/Services/NexaConnect.Services.Reporting/README.md).

The joined CI job runs both production and full-tree npm audits before acceptance, rejecting moderate-or-higher advisories. Use Node 22.12+ (22.x) or 24+; see [frontend dependency policy](Frontend-Dependency-Hardening.md).
