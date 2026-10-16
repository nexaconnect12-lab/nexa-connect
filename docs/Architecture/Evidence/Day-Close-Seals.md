# Day-close seal verification

Implemented on 2026-10-07 with Order 14 / Payment 13 / POS 11, application compatibility 0.27.0; Reporting 20 / Authorization 10 unchanged. [ADR-024](../Decisions/ADR-024-source-day-seals-and-late-change-journals.md) and the [rollout](../../Deployment/Day-Close-Seals.md) define scope and prerequisites.

The final guarded PostgreSQL 17 run `20d12719dbf1453ca513e7a3fff6976b` passed all **31** database/Application cases without skips and with exact cleanup at `2026-10-07T14:28:52.1528482Z`. Its restricted working-tree [verification JSON](../../../.runstate/day-seals/20d12719dbf1453ca513e7a3fff6976b/verification.json) records success and `productionVerified=false`. Ten seal cases cover actual immutable source retention, journaled changes/resealing, response-loss resume pinned to old manifests after cutoff refresh, manager contention/replacement/revocation, source revision conflicts after values restore, journal rollback/protection, untouched-branch serialization, bounded suffix reads, empty migration/calendar/outage boundaries and byte-compatible legacy preparation/cutoff operation fingerprints. Existing source/refund/financial/revision/cutoff suites remain in this matrix. The earlier thirty-case execution is separate development evidence.

The HTTP suite passed **27** seal/cutoff/preparation cases, including **11** Customer seal session/tenant/member/CSRF/no-store/correlation and required-reviewed-version checks over controlled downstream transport. Architecture verification passed all **six** tests with seal Domain models included. Frontend TypeScript check and **nine** preparation/cutoff/seal contract cases passed. All **six** new synthetic seal browser and **eight** existing cutoff browser cases passed without skips/retries; they exercise actual portal controls over intercepted BFF responses and are separate from real-OIDC/source delivery acceptance.

Focused Domain/Application/financial/migration verification passed all **59** unit cases. The four cashier-cutoff launcher/settings evidence guards also passed, including exact thirteen-scenario accounting.

The expanded joined cashier-cutoff run `75fa7dbfbbf54356bb7efbdb8f4d470f` passed all **13** distinct real-OIDC browser scenarios and **six** Authorization persistence cases without skips/retries and with verified cleanup at `2026-10-07T14:29:49.0465502Z`. Its restricted [runner verification](../../../.runstate/cashier-day-cutoff/75fa7dbfbbf54356bb7efbdb8f4d470f/verification.json) and [browser summary](../../../src/Frontend/test-results/cashier-day-cutoff-live/75fa7dbfbbf54356bb7efbdb8f4d470f/summary.json) prove actual source seals over command-produced financial events and a subsequent actual historical sale with immutable seal preservation/journaled invalidation, alongside prior cashier/delivery/process/identity boundaries. Actual portal/BFF build/publish passed. The earlier run `be02a3fc02e844b7a4de3b7c8a31400c` failed the legacy nullable-field pending-command shape and retired with cleanup; it is not a pass. Current code preserves original five-field transport and operation fingerprints.

Both final runs record base `8d74525b4967e6b0d57ea937fdda47e735541a2e` and `sourceDirty=true`, proving working-tree execution rather than that the base commit contains this slice. No earlier twelve-case run certifies these new boundaries, and authored CI is not remote execution evidence.

These are independently retained source-local cuts. Production provider/WPF/printer/offline acceptance, target-environment capacity/retention/restore, remote CI, settlement approval/finalization and monetary correction ledgers remain open. Journal counts describe changed source rows affecting all sealed branch dates conservatively, not financial corrections or an atomic global business-day close.

## Documentation handoff

The required documentation-maintainer audit completed with no unresolved findings. The project overview, Project Architecture and Restaurant POS Architecture were updated. ADR-005, Claims Contract and Identity Production Runbook were reviewed unchanged; layering, claims and identity provisioning remain accurate. The updated/added documentation files are:

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docker/cashier-day-close/README.md](../../../docker/cashier-day-close/README.md)
- [docs/API/Day-Close-Cutoffs.md](../../../docs/API/Day-Close-Cutoffs.md)
- [docs/API/Day-Close-Seals.md](../../../docs/API/Day-Close-Seals.md)
- [docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md)
- [docs/Architecture/Decisions/ADR-024-source-day-seals-and-late-change-journals.md](../../../docs/Architecture/Decisions/ADR-024-source-day-seals-and-late-change-journals.md)
- [docs/Architecture/Evidence/Day-Close-Seals.md](../../../docs/Architecture/Evidence/Day-Close-Seals.md)
- [docs/Architecture/Phase-10-Product-Integration.md](../../../docs/Architecture/Phase-10-Product-Integration.md)
- [docs/Architecture/Portal-Implementation-Phases.md](../../../docs/Architecture/Portal-Implementation-Phases.md)
- [docs/Architecture/Project-Architecture.md](../../../docs/Architecture/Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../../../docs/Architecture/Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../../docs/Database/Database-Design.md)
- [docs/Deployment/Cashier-Day-Cutoff-Acceptance.md](../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md)
- [docs/Deployment/Day-Close-Cutoffs.md](../../../docs/Deployment/Day-Close-Cutoffs.md)
- [docs/Deployment/Day-Close-Seals.md](../../../docs/Deployment/Day-Close-Seals.md)
- [docs/Deployment/Deployment-Guide.md](../../../docs/Deployment/Deployment-Guide.md)
- [docs/Identity/Client-Matrix.md](../../../docs/Identity/Client-Matrix.md)
- [README.md](../../../README.md)
- [src/BuildingBlocks/NexaConnect.Contracts/README.md](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md)
- [src/BuildingBlocks/NexaConnect.Infrastructure/README.md](../../../src/BuildingBlocks/NexaConnect.Infrastructure/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/cashier-day-cutoff-live/README.md](../../../src/Frontend/e2e/cashier-day-cutoff-live/README.md)
- [src/Frontend/e2e/day-seal/README.md](../../../src/Frontend/e2e/day-seal/README.md)
- [src/Frontend/README.md](../../../src/Frontend/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Order/README.md](../../../src/Services/NexaConnect.Services.Order/README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.POS/README.md](../../../src/Services/NexaConnect.Services.POS/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Tools/NexaConnect.DataMigration/README.md](../../../src/Tools/NexaConnect.DataMigration/README.md)
- [src/Tools/NexaConnect.DataMigration/Scripts/README.md](../../../src/Tools/NexaConnect.DataMigration/Scripts/README.md)
- [tests/Integration/NexaConnect.IntegrationTests/README.md](../../../tests/Integration/NexaConnect.IntegrationTests/README.md)
- [tests/Unit/NexaConnect.UnitTests/README.md](../../../tests/Unit/NexaConnect.UnitTests/README.md)
