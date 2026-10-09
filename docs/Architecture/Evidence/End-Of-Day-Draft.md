# End-of-day draft implementation verification

Local Windows verification on 2026-10-03 exercised the uncommitted working tree based on revision `e12954f052b342c60b2cce2abd6892676d461075`. The result covers the [read-only draft contract](../../API/End-Of-Day-Draft.md), not production settlement readiness.

| Verification | Result | Scope |
| --- | --- | --- |
| Solution build / full default test suite | Build succeeds; 816 passed, 111 opt-in skips (603 unit, 208 integration, five architecture passes) | Existing and new code; skipped infrastructure gates are not passes |
| Draft/auth focused unit tests | 10 passed, including one existing Payment authorizer regression | Three timezone windows, unsupported midnight/date-line history, open dates, scope/currency checks, source failures, issue semantics and live Payment read permissions |
| End-of-day HTTP/PostgreSQL suite with disposable database | 16 passed, zero skips | Reporting and BFF boundaries, three actual source HTTP routes, and three independently owned PostgreSQL schema fixtures |
| Frontend TypeScript / Vitest | Typecheck succeeds; 25 tests pass | Two new parser/request tests plus existing frontend tests |
| Frontend production build | Both portals build successfully; existing chunks over 500 KB retain build warnings | Bundle compilation only; no deployment or production runtime acceptance |
| End-of-day synthetic Chromium | Six passed | Actual portal with synthetic BFF replies, failure/scope/filter/tenant boundaries and read-only behavior |
| Existing financial completeness synthetic Chromium | Nine passed | Regression of the existing Sales reconciliation UI |

The disposable PostgreSQL 17 container used loopback networking, temporary in-memory database storage and no host data mounts. Each database fixture applied actual owning-service migrations in generated schemas. All generated schemas were removed (zero remaining), and the exact labeled container was removed with cleanup verified. No deployment, production database, real customer session or external payment was used.

The database checks distinguish Order creation-time gross sales from Paid-time tenders, exclude exact upper UTC boundaries, isolate organization/restaurant/branch scopes, include uncertain Payment/refund work, and verify late cash recomputes variance and supersedes prior approval. HTTP tests use test authentication and dependency ports; they are not a joined live identity/host gate.

No new migration or role grant is required. All affected summaries, component documentation, API/identity/database/deployment guidance and ADR-020 describe the new read ownership and limitations. The final documentation audit passed after inspecting the tracked/untracked implementation, configuration, tests and documentation. It corrected canonical Reporting principles, topology, component responsibility and implementation status, plus the restaurant bounded context and reporting section. All 27 documentation inventory destinations exist; 428 local Markdown links resolve. `git diff --check` passed. No unresolved implementation/documentation contradiction was found. ADR-005, ADR-012, ADR-016, ADR-017, ADR-018 and ADR-019 were reviewed unchanged: existing DDD, cash-review/refund/sale/completeness ownership and history remain valid, and ADR-020 records the new read orchestration.

Remaining work includes joined live OIDC/owning-host acceptance, target-environment TLS/privileges/clock/timezone/latency checks, durable settlement approval/locking and financial cutoffs, manual-tender refunds/return policy, exports and production acceptance. Source timestamps are observation metadata, not completeness watermarks; hosts require aligned clocks because Reporting rejects future source observations. Owner transport/read failures are sanitized `503`, while existing Directory membership clients may conservatively deny with `403` on non-success access responses. Historical dates use current branch configuration; unsupported timezone histories fail closed.

## Documentation updated

- [AI/architecture/project_overview.md](../../../AI/architecture/project_overview.md)
- [docs/API/Business-Service-API-Slices.md](../../API/Business-Service-API-Slices.md)
- [docs/API/Customer-Product-Configuration-and-Reporting.md](../../API/Customer-Product-Configuration-and-Reporting.md)
- [docs/API/End-Of-Day-Draft.md](../../API/End-Of-Day-Draft.md)
- [docs/API/Financial-Reporting-Completeness.md](../../API/Financial-Reporting-Completeness.md)
- [docs/API/POS-API.md](../../API/POS-API.md)
- [docs/API/Restaurant-Provisioning.md](../../API/Restaurant-Provisioning.md)
- [docs/Architecture/Decisions/ADR-020-branch-end-of-day-draft.md](../Decisions/ADR-020-branch-end-of-day-draft.md)
- [docs/Architecture/Evidence/End-Of-Day-Draft.md](End-Of-Day-Draft.md)
- [docs/Architecture/Project-Architecture.md](../Project-Architecture.md)
- [docs/Architecture/Restaurant-POS-Architecture.md](../Restaurant-POS-Architecture.md)
- [docs/Database/Database-Design.md](../../Database/Database-Design.md)
- [docs/Deployment/End-Of-Day-Draft.md](../../Deployment/End-Of-Day-Draft.md)
- [docs/Deployment/production.env.example](../../Deployment/production.env.example)
- [docs/Identity/Claims-Contract.md](../../Identity/Claims-Contract.md)
- [docs/Identity/Client-Matrix.md](../../Identity/Client-Matrix.md)
- [docs/Identity/Production-Runbook.md](../../Identity/Production-Runbook.md)
- [README.md](../../../README.md)
- [src/BuildingBlocks/NexaConnect.Contracts/README.md](../../../src/BuildingBlocks/NexaConnect.Contracts/README.md)
- [src/Frontend/apps/customer-portal/README.md](../../../src/Frontend/apps/customer-portal/README.md)
- [src/Frontend/e2e/end-of-day/README.md](../../../src/Frontend/e2e/end-of-day/README.md)
- [src/Gateway/NexaConnect.CustomerBff/README.md](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [src/Services/NexaConnect.Services.Order/README.md](../../../src/Services/NexaConnect.Services.Order/README.md)
- [src/Services/NexaConnect.Services.Payment/README.md](../../../src/Services/NexaConnect.Services.Payment/README.md)
- [src/Services/NexaConnect.Services.POS/README.md](../../../src/Services/NexaConnect.Services.POS/README.md)
- [src/Services/NexaConnect.Services.Reporting/README.md](../../../src/Services/NexaConnect.Services.Reporting/README.md)
- [src/Services/NexaConnect.Services.Restaurant/README.md](../../../src/Services/NexaConnect.Services.Restaurant/README.md)
