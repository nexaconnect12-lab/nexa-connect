# Joined end-of-day acceptance evidence

Local Windows acceptance on 2026-10-03 exercised the dirty working tree based on revision `6cdd864b7af3169d282ae0c5759b9a5f2304be04`. The [guarded launcher](../../Deployment/End-Of-Day-Portal-Acceptance.md) passed with actual OIDC, owning-service summary APIs and retained-event delivery. This is disposable joined read acceptance, not production settlement certification. The prior [draft verification](End-Of-Day-Draft.md) remains the historical component/synthetic baseline.

| Verification | Result | Scope |
| --- | --- | --- |
| Joined end-of-day browser matrix | Eight unique passes, zero skips/retries | Real OIDC/BFF/Reporting/Order/Payment/POS reads; Bangkok day, totals/tenders/current issues, event convergence, scope/date denial, source/Reporting transport failures, stale-response fencing, read-only behavior and live source-permission/membership revocation |
| Joined Authorization persistence prerequisite | Six passes, zero skips | Actual migrated disposable Authorization database |
| Exact joined cleanup | Verified | Eight application child handles, generated Compose project/volumes, exported certificate and launcher environment |
| Full default .NET suite | 816 passed, 111 opt-in skips | 603 unit, 208 integration and five architecture passes; skipped infrastructure gates are not passes |
| End-of-day / existing financial acceptance guards | Four passes each | Disposable opt-in, exact scope/calendar/window/executable, loopback listener restrictions and unique-pass evidence |
| Frontend typecheck / Vitest | Typecheck succeeds; 25 tests passed | Existing frontend contracts; no financial UI implementation changes |
| Customer BFF/portal production publish | Succeeds | Existing bundle over 500 KB retains a build warning; npm reports two moderate dependency advisories, with dependency versions unchanged |
| Realm validation / launcher parsing and missing-opt-in rejection | Passed | Existing workload audiences and guarded entry point |
| Existing joined financial portal default-mode regression | Seven unique browser passes, six Authorization persistence passes, zero skips, cleanup verified | Shared launcher/fixture default mode preserves its existing exact-window workflow |

Successful run `49dd11c5bb0e40fca07cc5328ce7a35f` retained `.runstate/end-of-day-portal/<run-id>/verification.json`: `passed=true`, `authorizationPassed=true`, `cleanupVerified=true`, `productionVerified=false`, `sourceDirty=true`, completed `2026-10-03T08:38:38.3220445+00:00`. Its browser summary at `src/Frontend/test-results/end-of-day-live/<run-id>/summary.json` has `verified=true`, `passed=8`, `total=8`, completed `2026-10-03T08:38:35.118Z`. The documentation audit independently read both bounded files. Prior incomplete setup attempts are not passes and their exact owned resources were cleaned.

Default-mode regression run `454c69c31cde49ca90b05cc4c6653086` retained `.runstate/financial-portal/<run-id>/verification.json` with `passed=true`, `authorizationPassed=true`, `cleanupVerified=true` and `productionVerified=false`. Its `src/Frontend/test-results/financial-completeness-live/<run-id>/summary.json` has `verified=true`, `passed=7` and `total=7`. The documentation audit independently read both files after successful launcher exit. This remains separate from the eight-case end-of-day matrix.

Seven independent service databases applied real migrations: Platform Directory 3, Restaurant 3, Authorization 9, Order 11, Payment 10, POS 7 and Reporting 20. Payment's optional repository clock supplies historical fixture times while production defaults to `TimeProvider.System`. Order fixture timestamps are set before immutable receipt/publication retention; initial POS snapshots use parameterized fixture Infrastructure in a fresh owned database. Actual outbox dispatchers, RabbitMQ and Reporting consumers deliver retained sale/refund events. No Reporting fact is inserted by the fixture. Two loopback TCP proxies interrupt/hold actual transport without inspecting or fabricating responses.

The matrix excludes cashier/payment/refund command HTTP, real provider execution, physical/offline POS, joined DST execution, production TLS/privilege/clock/latency validation, global financial completeness, durable settlement approval/locking and verified cutoffs. Remote Ubuntu CI and required branch protection remain unexecuted. Current timezone metadata and independent source observations cannot certify a historic settlement snapshot. No new production migration, permission, claim, client or shared database contract is introduced.

## Documentation audit

The final documentation audit passed after inspecting tracked/untracked code, launcher/Compose/CI configuration, browser guards/scenarios, Payment clock changes, fixture persistence, tests and affected documentation. The pass repaired corrupted inline code in draft API/deployment text; aligned current status and canonical testing/reporting sections; retained historical evidence and ADR reasoning; and clarified actual owning HTTP reads versus synthetic historical provider/POS inputs. All 23 documentation destinations exist, all 420 local Markdown links resolve, and `git diff --check` passed. The shared default-mode regression passed with verified cleanup. No unresolved implementation/documentation contradiction or repository-resolvable documentation gap remains.

Reviewed unchanged: `docs/Architecture/Decisions/ADR-005-domain-driven-design.md`, ADR-012, ADR-016, ADR-017, ADR-018 and ADR-019 retain their DDD, cash/refund/sale/projection/completeness ownership and durable history choices. `docs/Identity/Client-Matrix.md`, the Keycloak realm/README, `docs/Deployment/production.env.example`, Restaurant and Authorization component READMEs, and `src/BuildingBlocks/NexaConnect.Contracts/README.md` remain accurate: no production client/configuration/grant/contract is added. Existing financial-portal runbook remains its separate six-database/five-host default mode. ADR-020 receives a dated validation follow-up rather than rewriting its original planned-gate reasoning.

## Documentation updated
- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docker/end-of-day-portal/README.md](../../../docker/end-of-day-portal/README.md)
- [docs/API/End-Of-Day-Draft.md](../../../docs/API/End-Of-Day-Draft.md)
- [docs/Architecture/Decisions/ADR-020-branch-end-of-day-draft.md](../../../docs/Architecture/Decisions/ADR-020-branch-end-of-day-draft.md)
- [docs/Architecture/Evidence/End-Of-Day-Joined-Acceptance.md](../../../docs/Architecture/Evidence/End-Of-Day-Joined-Acceptance.md)
- [docs/Architecture/Project-Architecture.md](../../../docs/Architecture/Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../../../docs/Architecture/Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../../docs/Database/Database-Design.md)
- [docs/Deployment/End-Of-Day-Draft.md](../../../docs/Deployment/End-Of-Day-Draft.md)
- [docs/Deployment/End-Of-Day-Portal-Acceptance.md](../../../docs/Deployment/End-Of-Day-Portal-Acceptance.md)
- [docs/Identity/Claims-Contract.md](../../../docs/Identity/Claims-Contract.md)
- [docs/Identity/Production-Runbook.md](../../../docs/Identity/Production-Runbook.md)
- [README.md](../../../README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/end-of-day-live/README.md](../../../src/Frontend/e2e/end-of-day-live/README.md)
- [src/Frontend/e2e/end-of-day/README.md](../../../src/Frontend/e2e/end-of-day/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Order/README.md](../../../src/Services/NexaConnect.Services.Order/README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.POS/README.md](../../../src/Services/NexaConnect.Services.POS/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Tools/NexaConnect.FinancialPortalAcceptance/README.md](../../../src/Tools/NexaConnect.FinancialPortalAcceptance/README.md)
- [tests/Integration/NexaConnect.IntegrationTests/README.md](../../../tests/Integration/NexaConnect.IntegrationTests/README.md)
