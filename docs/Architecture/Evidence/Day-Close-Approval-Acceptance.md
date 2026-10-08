# Local day-close approval acceptance

Date: 2026-10-08. Working tree based on `4c7d143453279e82102ab459b445322f7c45769a`; approval changes were uncommitted during verification. This record describes local execution, not remote CI or production deployment.

| Verification | Result |
| --- | --- |
| Scoped domain/migration unit matrix | 105 passed |
| Approval/seal/cutoff/preparation HTTP boundaries | 38 passed |
| Architecture dependency rules | 6 passed |
| Disposable PostgreSQL/Application/source pipeline matrix | 51 passed, no skips |
| Portal preparation/cutoff/seal/approval contracts | 18 passed |
| TypeScript project check | Passed |
| Chromium seal/approval scenarios | 11 passed |
| Chromium cutoff regressions | 8 passed |
| Joined real-OIDC cashier-to-cutoff/approval scenarios | 14 passed; Authorization checks passed |

The PostgreSQL matrix verifies immutable decision/audit storage, expected-version concurrency, exact actor/body-bound replay across a later decision, transaction rollback before commit, read-only original source proof during concurrent resealing, relevant versus unrelated changes, outage recovery, post-preflight changes and revocation before commit. The Authorization migration case covers existing manager-role backfill, fresh assignments, reader separation, live deny override and permission downgrade.

Database run `eb7b42cd9dcc4fc4b53485f1d7e5a136` retains bounded ignored evidence at `.runstate/day-seals/eb7b42cd9dcc4fc4b53485f1d7e5a136/verification.json`. Joined run `1b4635280a744bb18dcdc17b9dc4d935` retains `.runstate/cashier-day-cutoff/1b4635280a744bb18dcdc17b9dc4d935/verification.json`. Both report successful execution and verified cleanup with `productionVerified:false`.

The joined gate exercises the real manager session, protected tenant context, CSRF and live POS authorization against actual Order/Payment/POS seals and Reporting delivery. A real accountant session cannot approve. A later historical cashier sale supersedes the approval while preserving the original reviewed financial snapshot. Chromium fixtures separately cover matching loaded version, exact retry after unavailable response, read-only controls, filter reset and retained history in superseded/unverified states.

Approval is an attributable decision about the observed snapshot. Source commits can occur after preflight and are detected by subsequent validation. Source fences, global finalization, fiscal corrections, offline approval and target-production acceptance remain outside this slice. See [ADR-027](../Decisions/ADR-027-version-bound-day-close-approval.md) and [rollout](../../Deployment/Day-Close-Approvals.md).

## Documentation handoff

The required documentation-maintainer audit completed with no unresolved drift. Overview and canonical project architecture changed to describe approval ownership, current versions and remaining finalization work. Historical ADRs/evidence, the identity client matrix, database guidelines and environment configuration were reviewed and remain unchanged because the existing clients/configuration and historical decisions remain accurate.

Documentation files updated in this change:

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [README.md](../../../README.md)
- [docker/cashier-day-close/README.md](../../../docker/cashier-day-close/README.md)
- [docs/API/Day-Close-Approvals.md](../../../docs/API/Day-Close-Approvals.md)
- [docs/API/Day-Close-Seals.md](../../../docs/API/Day-Close-Seals.md)
- [docs/Architecture/Decisions/ADR-027-version-bound-day-close-approval.md](../../../docs/Architecture/Decisions/ADR-027-version-bound-day-close-approval.md)
- [docs/Architecture/Evidence/Day-Close-Approval-Acceptance.md](../../../docs/Architecture/Evidence/Day-Close-Approval-Acceptance.md)
- [docs/Architecture/Portal-Implementation-Phases.md](../../../docs/Architecture/Portal-Implementation-Phases.md)
- [docs/Architecture/Project-Architecture.md](../../../docs/Architecture/Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../../../docs/Architecture/Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../../docs/Database/Database-Design.md)
- [docs/Deployment/Cashier-Day-Cutoff-Acceptance.md](../../../docs/Deployment/Cashier-Day-Cutoff-Acceptance.md)
- [docs/Deployment/Day-Close-Approvals.md](../../../docs/Deployment/Day-Close-Approvals.md)
- [docs/Deployment/Day-Close-Cutoffs.md](../../../docs/Deployment/Day-Close-Cutoffs.md)
- [docs/Deployment/Day-Close-Seals.md](../../../docs/Deployment/Day-Close-Seals.md)
- [docs/Deployment/Deployment-Guide.md](../../../docs/Deployment/Deployment-Guide.md)
- [docs/Identity/Claims-Contract.md](../../../docs/Identity/Claims-Contract.md)
- [src/BuildingBlocks/NexaConnect.Contracts/README.md](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md)
- [src/BuildingBlocks/NexaConnect.Infrastructure/README.md](../../../src/BuildingBlocks/NexaConnect.Infrastructure/README.md)
- [src/Frontend/README.md](../../../src/Frontend/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/cashier-day-cutoff-live/README.md](../../../src/Frontend/e2e/cashier-day-cutoff-live/README.md)
- [src/Frontend/e2e/day-seal/README.md](../../../src/Frontend/e2e/day-seal/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Authorization/README.md](../../../src/Services/NexaConnect.Services.Authorization/README.md)
- [src/Services/NexaConnect.Services.Order/README.md](../../../src/Services/NexaConnect.Services.Order/README.md)
- [src/Services/NexaConnect.Services.POS/README.md](../../../src/Services/NexaConnect.Services.POS/README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Tools/NexaConnect.DataMigration/README.md](../../../src/Tools/NexaConnect.DataMigration/README.md)
- [src/Tools/NexaConnect.DataMigration/Scripts/README.md](../../../src/Tools/NexaConnect.DataMigration/Scripts/README.md)
