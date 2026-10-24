# Revision-bound day-close verification

Implementation date: 2026-10-07. Source compatibility: Order 13 / Payment 12 / POS 10, application 0.26.0; Reporting 20 / Authorization 10 unchanged. [ADR-023](../Decisions/ADR-023-revision-fenced-day-close-evidence.md) defines the exact scope and limitations.

The final guarded PostgreSQL 17 run `f606c3ab34714ca7951fd8e60e7f48f9` passed all **21** database/Application cases without skips and with verified cleanup. Its restricted working-tree [verification JSON](../../../.runstate/day-cutoffs/f606c3ab34714ca7951fd8e60e7f48f9/verification.json) records success and `productionVerified=false`, completed at `2026-10-07T13:35:18.0835788Z`. Seven revision cases cover restored-value changes, concurrent Payment increments, rollback, repeatable-read snapshots, branch isolation, epoch guards, empty migration downgrade/re-upgrade, history refusal, least-privilege runtime mutation/reset denial and legacy operation replay. The remaining matrix exercises actual owning refund/sale/cash workflows and Reporting projection/replay checks. The earlier 20-case run remains separate development evidence.

Focused domain/Application/financial/migration verification passed **54** unit cases. HTTP verification passed **16** Customer cutoff/DayClose cases; the architecture suite passed all **six** cases. The portal TypeScript check and **six** preparation/cutoff contract cases passed. All **eight** synthetic cutoff browser scenarios passed without skips/retries. These use intercepted BFF replies and do not substitute for real identity/broker acceptance. Source revision numbers are visible in saved evidence; Ready rejects missing revisions/protocol/delivery, while legacy blocked history remains readable.

The joined cashier-cutoff launcher pins the new schemas and 0.26.0. Run `9fd89b722c294b5cbb5d5eea9c74a32f` passed all **12** real-OIDC browser scenarios and **six** Authorization persistence cases, without skips/retries and with verified cleanup. Its restricted [runner verification](../../../.runstate/cashier-day-cutoff/9fd89b722c294b5cbb5d5eea9c74a32f/verification.json) and [browser summary](../../../src/Frontend/test-results/cashier-day-cutoff-live/9fd89b722c294b5cbb5d5eea9c74a32f/summary.json) record completion at `2026-10-07T13:33:24.9736267Z`, `productionVerified=false`. It exercises actual cashier commands, financial publication/consumption, delayed delivery, source generations, POS process interruption/resume/replacement, manager contention, late historical sale and live identity/permission/membership boundaries. The runner also builds/publishes the actual portal/BFF.

Both final runs use base `f189e97e8532de21df82bf4874cfe2f2872b92cc` with `sourceDirty=true`; these ignored run-owned records prove the working-tree implementation, not that the base commit contains it. Earlier 0.25.0 evidence remains historical and does not certify this protocol. No production provider, physical WPF/printer, offline, load, security-hardening, remote CI or production settlement claim is made. Selected-set delivery proof and durable revisions do not supply source-write locks, an atomic global cut, approval/finalization or a late-correction ledger.

## Documentation handoff

The required documentation-maintainer audit completed without unresolved findings. The project overview, Project Architecture and Restaurant POS Architecture were updated. ADR-005 and Identity documents were reviewed unchanged because layering, claims, clients and permission grants remain accurate. The following documentation files were updated or added:

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docker/cashier-day-close/README.md](../../../docker/cashier-day-close/README.md)
- [docs/API/Day-Close-Cutoffs.md](../../../docs/API/Day-Close-Cutoffs.md)
- [docs/Architecture/Decisions/ADR-022-retained-day-cutoff-evidence.md](../../../docs/Architecture/Decisions/ADR-022-retained-day-cutoff-evidence.md)
- [docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md](../../../docs/Architecture/Decisions/ADR-023-revision-fenced-day-close-evidence.md)
- [docs/Architecture/Evidence/Revision-Fenced-Day-Close.md](../../../docs/Architecture/Evidence/Revision-Fenced-Day-Close.md)
- [docs/Architecture/Phase-10-Product-Integration.md](../../../docs/Architecture/Phase-10-Product-Integration.md)
- [docs/Architecture/Portal-Implementation-Phases.md](../../../docs/Architecture/Portal-Implementation-Phases.md)
- [docs/Architecture/Project-Architecture.md](../../../docs/Architecture/Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../../../docs/Architecture/Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../../docs/Database/Database-Design.md)
- [docs/Deployment/Cashier-Day-Cutoff-Acceptance.md](../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md)
- [docs/Deployment/Day-Close-Cutoffs.md](../../../docs/Deployment/Day-Close-Cutoffs.md)
- [docs/Deployment/Deployment-Guide.md](../../../docs/Deployment/Deployment-Guide.md)
- [README.md](../../../README.md)
- [src/BuildingBlocks/NexaConnect.Contracts/README.md](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md)
- [src/BuildingBlocks/NexaConnect.Infrastructure/README.md](../../../src/BuildingBlocks/NexaConnect.Infrastructure/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/cashier-day-cutoff-live/README.md](../../../src/Frontend/e2e/cashier-day-cutoff-live/README.md)
- [src/Frontend/e2e/day-cutoff/README.md](../../../src/Frontend/e2e/day-cutoff/README.md)
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
