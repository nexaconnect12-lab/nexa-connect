# Branch day-close preparation implementation handoff

Verified on 2026-10-03 UTC / 2026-10-04 Asia/Singapore. Base commit: `31f2476e46bf8b46212b9997a8425fa4bf0e4161`; implementation remains in the working tree. The checkout was clean at task start. No commit, deployment or production mutation was requested or performed.

## Implemented

POS-owned BranchDayClose preparation persists scoped branch/date, current timezone/currency/window, frozen reviewed amounts/tenders, source observation fingerprints/times, historical check identity/time, operational blockers, versioned state, organization-scoped operations and immutable actor/decision audit. Domain owns validity/readiness/lease invariants; Application owns live hierarchy/authorization and orchestration; Infrastructure owns parameterized persistence and Reporting translation.

Preparing commits before remote evidence reads; completion commits Blocked or Ready for review. Expected versions, stable operation fingerprints, row/advisory locks, expiring claims and restart/resume fencing protect duplicates and competing managers. Changed evidence invalidates saved readiness even with identical amounts; outages never return a stale Ready observation. Reads/replay freshly validate Ready, while blocked/preparing reads expose retained state without claiming readiness. Immutable audit cannot be rewritten, and retained history prevents downgrade.

Order/Payment/POS summary totals and bounded owner-selected fingerprints share local repeatable-read snapshots. Reporting remains read-only. Customer BFF adds protected-tenant/current-membership/CSRF POS forwarding with server-held credentials and shared correlation. Portal supports load/prepare/refresh, exact uncertain resume, browser-restart recovery, and another manager's explicit replacement of interrupted work after server-enforced lease expiry. Scope/filter/tenant/failure/late-response guards clear stale evidence. Accountant controls remain read-only.

Deploy POS 8 / Authorization 10 before exposing this feature. Both require application compatibility 0.24.0; the standard migration launcher default advances accordingly. Configure POS→Reporting and BFF→POS against actual HTTPS hosts; preserve existing customer source permissions and independently owned runtime database identities. See [rollout](../../Deployment/Day-Close-Preparation.md).

## Verification performed

| Command / suite | Result |
| --- | --- |
| `dotnet test NexaConnect.sln --no-restore -v quiet` | Unit 625 passed / 5 opt-in skipped; architecture 5 passed; HTTP/component integration 224 passed / 112 opt-in skipped; no failures. Builds completed as part of test execution. |
| `scripts/test-day-close-preparation.ps1 -ConfirmDisposableInfrastructure` | All 9 PostgreSQL cases passed, zero skipped. Five preparation/Authorization cases and four owner hash/source cases. |
| `npm run check` | TypeScript passed. |
| `npm test` | 28 tests across 11 files passed. |
| `npm run build` | Both portals built; existing Vite 500 kB chunk warnings remain. |
| `npm run test:e2e:day-close` | All 8 synthetic Chromium browser contracts passed, no skips/retries. |
| `npm run test:e2e:end-of-day` | All 6 existing draft browser contracts passed, no skips/retries. |
| Documentation maintainer audit | Closed with no unresolved drift after timeout, rollback, recovery and inventory corrections. |
| Final diff review | Checked code/documentation ownership, scope, financial/state invariants, replay/transaction boundaries, safe telemetry and accidental files; `git diff --check` passed. |

The PostgreSQL matrix executed real repositories/migrations against cached PostgreSQL 17-alpine on a random-password localhost-only container, with generated schemas and exact finally cleanup. A subsequent Docker inventory showed no generated `nexa_day_close_it_` containers remaining. It covers concurrent managers, exact/conflicting replay, restored leases/old claims, superseded work, scope isolation, same-total identity/version changes, fingerprint row/byte caps, audit UPDATE/DELETE/TRUNCATE refusal, retained-history downgrade refusal, empty downgrade/reapply, permission backfill/new assignment separation, deny/revocation and rollback removal of materialized grants.

The broader opt-in skips are disclosed, not counted as acceptance. The nine-case PostgreSQL runner explicitly executes its otherwise skipped cases. HTTP/BFF tests use controlled authentication/transport; browser tests use the actual portal with synthetic BFF responses. They are component/contract evidence, not a joined real-OIDC preparation pass. Earlier real-OIDC end-of-day read acceptance remains independent.

## Documentation files updated

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docs/API/Day-Close-Preparation.md](../../../docs/API/Day-Close-Preparation.md)
- [docs/API/End-Of-Day-Draft.md](../../../docs/API/End-Of-Day-Draft.md)
- [docs/Architecture/Decisions/ADR-020-branch-end-of-day-draft.md](../../../docs/Architecture/Decisions/ADR-020-branch-end-of-day-draft.md)
- [docs/Architecture/Decisions/ADR-021-branch-day-close-preparation.md](../../../docs/Architecture/Decisions/ADR-021-branch-day-close-preparation.md)
- [docs/Architecture/Project-Architecture.md](../../../docs/Architecture/Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../../../docs/Architecture/Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../../docs/Database/Database-Design.md)
- [docs/Deployment/Day-Close-Preparation.md](../../../docs/Deployment/Day-Close-Preparation.md)
- [docs/Deployment/Deployment-Guide.md](../../../docs/Deployment/Deployment-Guide.md)
- [docs/Deployment/End-Of-Day-Draft.md](../../../docs/Deployment/End-Of-Day-Draft.md)
- [docs/Deployment/Financial-Reporting-Recovery.md](../../../docs/Deployment/Financial-Reporting-Recovery.md)
- [docs/Identity/Claims-Contract.md](../../../docs/Identity/Claims-Contract.md)
- [README.md](../../../README.md)
- [src/BuildingBlocks/NexaConnect.Contracts/README.md](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md)
- [src/BuildingBlocks/NexaConnect.Infrastructure/README.md](../../../src/BuildingBlocks/NexaConnect.Infrastructure/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/day-close/README.md](../../../src/Frontend/e2e/day-close/README.md)
- [src/Frontend/README.md](../../../src/Frontend/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Authorization/README.md](../../../src/Services/NexaConnect.Services.Authorization/README.md)
- [src/Services/NexaConnect.Services.Order/README.md](../../../src/Services/NexaConnect.Services.Order/README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.POS/README.md](../../../src/Services/NexaConnect.Services.POS/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Tools/NexaConnect.DataMigration/README.md](../../../src/Tools/NexaConnect.DataMigration/README.md)
- [tests/Integration/NexaConnect.IntegrationTests/README.md](../../../tests/Integration/NexaConnect.IntegrationTests/README.md)
- [tests/Unit/NexaConnect.UnitTests/README.md](../../../tests/Unit/NexaConnect.UnitTests/README.md)

This handoff itself is new. Both `AI/architecture/project_overview.md` and `docs/Architecture/Project-Architecture.md` were updated, including POS ownership/domain inventory and implementation status; the canonical restaurant/POS architecture was also updated. ADR-021 records preparation ownership/cutoff limits and ADR-020 links its subsequent source snapshot evolution. Related API, deployment, database, identity, shared-component, gateway, service, frontend and test READMEs describe the same implementation. No unrelated cosmetic documentation changes were made.

## Limitations and follow-up

Preparation is an independently observed readiness draft. It does not approve settlement, certify fresh financial completeness, freeze source writes, publish a settlement event, reconstruct historical calendar configuration or provide a distributed snapshot/watermark. Source changes can occur immediately after successful validation. No background instant invalidation or automatic lease reaper is introduced; recovery is explicit.

Large/old source summaries without a complete bounded fingerprint block readiness. Historical financial check/projection gaps must be resolved through existing owning workflows/tools. Current/future incomplete dates cannot produce a usable snapshot; Reporting rejection is recorded as Blocked/source_unavailable. A failed refresh retains its prior evidence in immutable audit rather than returning an invented new snapshot.

The next release-validation slice should add a joined real-OIDC preparation/restart/late-evidence gate. Remote CI, target-production capacity/latency/clock/timezone/least-privilege acceptance, and eventual settlement approval/cutoff/locking protocol remain open. Existing frontend chunk warnings remain a separate optimization task.

## Joined-gate follow-up — 2026-10-06

The [joined preparation gate](../../Deployment/Day-Close-Portal-Acceptance.md) is now implemented with eleven real-OIDC browser scenarios, restart/resume/replacement and bounded synthetic source transitions. The preceding next-slice statement records the original implementation handoff. Local Windows run `14f563dc51d44f929d9bb2bcef37ee60` subsequently passed eleven real-OIDC browser and six Authorization persistence cases without skips/retries, with verified cleanup on 2026-10-06. This handoff's earlier synthetic/component results remain separate evidence. Remote CI and production/finalization gates remain separate.
