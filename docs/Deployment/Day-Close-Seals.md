# Day-close seal rollout and recovery

Apply Order **14**, Payment **13**, POS **11** with compatibility **0.27.0**. Reporting remains **20** and Authorization **10**. Upgrade source/Reporting/POS/BFF/portal binaries together before exposing sealing controls. Existing HTTPS source/Reporting/POS addresses, correlation clients, identity clients and read/prepare grants apply; no new secret, workload bypass or permission grant is needed. Ordinary preparation/cutoff workflows remain available.

Each owner adds `source_day_seals` and `source_financial_changes`; POS also owns separate seal state/operation/immutable audit. Runtime roles need existing owning-table privileges plus seal SELECT/INSERT, revision SELECT/INSERT/UPDATE, change SELECT/INSERT, epoch and parent SELECT. Bootstrap default grants cover these tables; persistent deployments must verify grants explicitly. Runtime must not own schemas/tables/triggers or migration privileges. Direct journal insertion/update/delete/truncate and seal history mutation are guarded. Journal insertion is required for financial commits; missing grants/storage failure rolls back the financial transaction and requires repair followed by existing idempotent recovery.

Seal retention takes a short owning branch advisory lock and repeatable-read transaction; no lock crosses provider/HTTP I/O. The revision trigger uses the same lock. Monitor commit latency, branch contention, deadlock/serialization retries and journal storage growth. Revisions count changed rows, not business operations or monetary corrections. Do not prune journal suffixes needed by retained seals. Production load, retention, backup/restore and target-environment least privilege remain release checks.

Managers first load/refresh Ready cutoff evidence, then load and seal its reviewed version. After response loss or restart, load the saved operation and explicitly resume after its 30-second lease, or replace expired work using the current coordinator and reviewed cutoff versions. Source seals committed before interruption remain immutable. Late changes invalidate readiness at the next authorized validation and retain the old reviewed set; resolve existing financial work, refresh cutoff evidence, then reseal with a new operation. Counts displayed for Blocked state are from the last check, not current polling.

All sealed windows in a changed source branch are conservatively affected, including future-day writes and older unresolved work. Seals retain original UTC boundaries; current Restaurant calendar drift blocks validation. No source financial write is prohibited by sealing. Approval/finalization and fiscal corrections remain separate work.

Order `14→13`, Payment `13→12`, POS `11→10` reject downgrade after any source seal/change journal or POS coordination history. Use forward recovery; never remove history/disable triggers to force rollback. Empty new-migration downgrade/reapply preserves the revision epoch, and full revision lifecycle remains covered separately.

Run from repository root:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --no-restore --filter "FullyQualifiedName~DaySealTests|FullyQualifiedName~DayCutoffTests|FullyQualifiedName~DayClosePreparationTests|FullyQualifiedName~FinancialCompletenessTests|FullyQualifiedName~MigrationRunnerTests"
dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter "FullyQualifiedName~CustomerDaySealBoundaryTests|FullyQualifiedName~CustomerDayCutoffBoundaryTests|FullyQualifiedName~DayCloseHttpTests"
dotnet test tests/Architecture/NexaConnect.ArchitectureTests --no-restore
./scripts/test-day-seals.ps1 -ConfirmDisposableInfrastructure
./scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure
```

The seal PostgreSQL runner requires 31 executed/passed cases without skips and exact generated-container cleanup; it uses cached PostgreSQL 17 and records bounded `.runstate/day-seals/{runId}/verification.json`. The expanded joined cashier-cutoff gate requires 13 distinct real-OIDC browser scenarios plus six Authorization persistence cases, including actual source sealing and a later actual historical sale. It retains only bounded verification summaries; no financial bodies or credentials are uploaded.

From `src/Frontend`, run `npm run check`, focused preparation/cutoff/seal Vitest tests, `npm run test:e2e:day-seal` and `npm run test:e2e:day-cutoff`. Six seal browser scenarios use intercepted BFF responses and remain separate from joined execution. See [execution evidence](../Architecture/Evidence/Day-Close-Seals.md) for actual results and exclusions. Existing service debug queries are in the [API](../API/Day-Close-Seals.md).
