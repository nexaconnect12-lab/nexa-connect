# Joined cash-close portal acceptance

The opt-in runner joins the real Customer Portal, Customer BFF, Reporting, POS, Restaurant, Authorization and Platform Directory with a disposable Keycloak realm, PostgreSQL 17 and RabbitMQ 4. It does not mock BFF responses or service authorization. The read-only report remains eventually consistent and does not certify settlement or completeness.

## Run locally

Requires PowerShell 7, the repository's .NET SDK, Node/npm, Playwright Chromium and a local Docker socket. Run from the repository root:

```powershell
Push-Location src/Frontend
npm ci --ignore-scripts
npx playwright install chromium
node --test e2e/cash-close-live/settings.test.mjs
Pop-Location
./scripts/test-cash-close-portal.ps1 -ConfirmDisposableInfrastructure
```

The switch authorizes only newly generated infrastructure, synthetic fixture writes and cleanup. `-DockerExecutable` accepts a Docker CLI path; automatic discovery also checks per-user Windows Docker Desktop. `-NoBuild` skips fixture/service builds only after current artifacts have been built; the portal/BFF is always published. The runner exports a development HTTPS certificate for its BFF, uses a random password, removes the export during cleanup, and does not change certificate trust. Browser certificate verification is relaxed only for this guarded local acceptance.

The runner applies the real migration runner from empty databases to Platform Directory 3, Restaurant 3, Authorization 7, POS 7 and Reporting 15. It uses separate service database roles and generated secrets, creates two real OIDC users, and provisions through service-owned Application/Infrastructure capabilities. The manager role is restaurant-scoped; the accountant is branch-scoped. Both are members of a second organization without cash-review grants, allowing a valid tenant switch followed by foreign-resource denial.

The standalone fixture tool guards exact generated database names, loopback hosts, distinct subjects and its run-specific state file. Minimal store bootstrap and explicit read-permission denial are fixture-only Infrastructure operations. Close, approval and late settlement use existing POS repositories. They do not prove cashier/supervisor UI execution, the POS mutation HTTP boundary, or Order settlement ingestion. No production fault endpoint is introduced.

## Required five scenarios

1. Existing closed-session backfill reaches the real portal; approval propagates; a late settlement changes expected cash and supersedes approval.
2. Branch-scoped access rejects another branch/store, mismatched branch/store and foreign-tenant resources; displayed financial rows disappear.
3. A real Reporting-to-POS connection failure returns unavailable, clears displayed rows, and recovers after reconnection.
4. A real accountant session reads the report; the report has no decision controls or POST operation.
5. An explicit `pos.cash-review.read` deny rejects subsequent reads in the existing signed-in session and removes displayed rows.

Reporting's actual RabbitMQ binding must exist before POS publication starts. A test-process-owned TCP proxy listens only on loopback and forwards only to the generated POS host. Disconnecting it supplies the dependency-failure case; it has no control HTTP endpoint. The browser may contact only the generated BFF and identity origin. No credentials, cookies, financial payloads, screenshots, traces or videos are included in CI evidence.

## Evidence and cleanup

Exactly five unique browser cases must pass, with no skips, retries or repeated cases. Browser summaries live at `src/Frontend/test-results/cash-close-live/<run-id>/summary.json`; runner evidence lives at `.runstate/cash-close-portal/<run-id>/verification.json`. A successful browser result is insufficient unless `cleanupVerified` is also true. The runner records source revision and whether the working tree was dirty.

Local fixture identifiers and service logs remain under the run directory and are excluded from CI uploads. Treat local diagnostic and Playwright failure artifacts as restricted; they may contain synthetic financial data. Application services use the shared structured logging/correlation foundation (
exaconnect-customer-bff`, 
exaconnect-reporting`, 
exaconnect-pos`); follow the safe [cash-close diagnostic queries](Cash-Close-Recovery.md). The fixture CLI emits bounded stage/type/SQLSTATE failures, never exception bodies or connection strings.

Cleanup kills only child processes retained by the runner, removes its exact generated Compose project and volumes, verifies no containers remain, removes the exported certificate, and restores modified environment variables. Forced termination can bypass `finally`; investigate only the recorded run's resources. Never reuse a partially provisioned run.

The `Joined cash-close portal gate` GitHub job runs this matrix on Ubuntu with .NET 10 and Node 22. It uploads only allowlisted verification summaries for 14 days. Remote CI execution and required branch protection are separate verification steps. Production topology, real-user operation, retained-source restoration, external alert delivery, report completeness and multi-store export remain outside this acceptance.

## POS workload audience prerequisite

The development realm explicitly maps 
exaconnect-api` into POS workload access tokens so the scanner can resolve Restaurant hierarchy. Existing realms need an explicit reviewed mapper update; realm import skips existing realms. Obtain a new workload token after updating the mapper (restart the local POS host or allow its cached token to expire). Token validation remains unchanged. This requirement also applies outside the disposable harness.
