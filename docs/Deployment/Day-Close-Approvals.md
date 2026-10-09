# Day-close approval rollout

For the current coordinated deployment, use [late-work review rollout](Late-Work-Reviews.md): Order 19 / Payment 18 / POS 19 / Authorization 14, compatibility 0.33.0, Reporting 20 unchanged. Version sets below identify the earlier capability boundary; current review-enabled binaries require the latest coordinated catalog.

The preceding coordinated catalog also supports [temporary finalization preparation](Day-Close-Finalization-Preparation.md): Order 17 / Payment 16 / POS 16 / Authorization 12, compatibility 0.31.0. The POS 14 / Authorization 11 versions below describe the approval introduction boundary.

Deploy Order 16, Payment 15, Reporting 20 with **POS 14 / Authorization 11**, application compatibility **0.30.0**, and the matching Customer BFF/Portal. Existing source/Reporting protocols are unchanged. Stop old POS hosts before applying POS 14 and deploying the updated coordinator: all seal save paths now observe approval state in the same transaction. Apply Authorization 11 before enabling approval routes. `scripts/migrate-databases.ps1` contains current targets.

POS owns `branch_day_approvals` (mutable scoped validity/version), `branch_day_approval_decisions` (immutable actor/reason/reviewed snapshot and operation fingerprint) and `branch_day_approval_audit` (append-only approval/supersession/validation with private Authorization decision UUID). Scoped unique keys/FKs bind the coordinator and approval version. Runtime persistence uses parameterized values. Do not edit historical migration files.

Verify POS runtime SELECT/INSERT/UPDATE on the approval projection and SELECT/INSERT on decision/audit tables, plus existing seal-row SELECT/UPDATE locking privileges. Bootstrap default grants cover newly migrated tables; persistent deployments must verify them explicitly. Runtime must not own tables, schemas or triggers, and must not have migration or trigger-disabling authority. Immutability triggers reject history mutation even if broader bootstrap DML grants exist. Missing audit/ledger privileges roll back the entire approval or seal status transaction; repair privileges and retry the original operation.

Authorization 11 backfills existing tenant-admin/store-manager role links. New assignment defaults use the same Domain-owned role policy. No accountant/cashier approve default exists. Keep current source/Reporting read permissions; a grant of approval does not replace them. Review live overrides separately. Authorization downgrade removes approve role links and user overrides, so disable approval routes first. POS downgrade refuses any retained approval decision/audit or nonzero projection version; only empty/read-only version-zero state may be removed. Archive/export policy is future work; never bypass immutability triggers to force rollback.

No new secrets, environment variables, clients, workload grants or worker are required. Use existing POS datasource, live Authorization/Directory endpoints and correlated source clients. `nexaconnect-pos` / `nexaconnect-customer-bff` expose shared JSON/OTLP diagnostics; query `Day-close approval` and validated `CorrelationId`. Restrict database audit access; stable subjects and financial snapshots are scoped audit data and must not enter logs.

Verification:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --filter "FullyQualifiedName~DayApprovalTests|FullyQualifiedName~MigrationRunnerTests"
dotnet test tests/Integration/NexaConnect.IntegrationTests --filter "FullyQualifiedName~CustomerDayApprovalBoundaryTests"
dotnet test tests/Architecture/NexaConnect.ArchitectureTests
./scripts/test-day-seals.ps1 -ConfirmDisposableInfrastructure
./scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure
```

From `src/Frontend`, run `npm run check`, `npm test -- apps/customer-portal/src/dayClosePreparation.test.ts apps/customer-portal/src/dayCutoff.test.ts apps/customer-portal/src/daySeal.test.ts apps/customer-portal/src/dayApproval.test.ts`, `npm run test:e2e:day-seal` (11 scenarios, including four approval cases) and `npm run test:e2e:day-cutoff` (eight scenarios). The guarded database runner requires at least 51 executed/passed cases with no skips. The joined cashier-to-cutoff runner requires 14 real-OIDC scenarios and its Authorization persistence checks, plus verified disposable cleanup. `.github/workflows/day-seal-verification.yml` includes approval unit/HTTP/portal contracts and the database matrix.

Local joined run `1b4635280a744bb18dcdc17b9dc4d935` passed all 14 browser scenarios and Authorization checks with cleanup on 2026-10-08. Bounded evidence is `.runstate/cashier-day-cutoff/1b4635280a744bb18dcdc17b9dc4d935/verification.json`; this ignored local artifact is not remote CI or production deployment evidence. The [local acceptance record](../Architecture/Evidence/Day-Close-Approval-Acceptance.md) lists final unit, HTTP, architecture, PostgreSQL, TypeScript and browser results with run provenance.

On uncertain POST, retain the exact operation and retry or load history. After source outage, load approval again; current validity stays unverified until proof succeeds. Resolve relevant changes and explicitly recapture/reseal before another approval. A saved decision is immutable history, not settlement completion. See the [contract](../API/Day-Close-Approvals.md) for state and recovery details.
