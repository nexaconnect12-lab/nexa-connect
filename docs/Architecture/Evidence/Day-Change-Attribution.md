# Window-aware sealed-change reconciliation verification

Local execution on 2026-10-07 verified the ADR-025 implementation on the working tree based on `7dfd1431acb760712fe05330caa5013fcec7b7d2`. The preceding [ADR-024 seal execution](Day-Close-Seals.md) remains historical. These are local disposable acceptance results, not production certification.

| Verification | Executed/passed |
| --- | ---: |
| Attribution, seal, cutoff, preparation, end-of-day, completeness and migration unit tests | 68 |
| Disposable PostgreSQL source/coordinator/reconciliation matrix | 37 |
| Seal/cutoff BFF and day-close HTTP boundaries | 27 |
| Architecture tests | 6 |
| Portal preparation/cutoff/seal contract tests | 10 |
| Synthetic seal Chromium scenarios, including the baseline/current comparison | 7 |
| Synthetic cutoff Chromium scenarios | 8 |
| Real-OIDC cashier-to-cutoff/seal browser scenarios | 13 |
| Real gate Authorization persistence cases | 6 |

All cases passed without skips. TypeScript checking, service/BFF compilation and the real gate's portal production build passed. The documentation-maintainer audit completed and documentation links/final diff were reviewed.

The database matrix adds next-day Order/Payment/shift/drawer/movement exclusion, backdated before/after mutations, historical drawer attribution despite today's recording time, preserved baseline versus current cash variance, legacy unattributed suffixes, ownership drift, equal-count missing-revision corruption, evaluation overflow and immutable attribution guards. It retains transaction rollback, source locking, replay/restart, competing manager, revocation, calendar drift, exact delivery, migration epoch and old operation-fingerprint regression coverage. The real gate exercises existing actual cashier commands, source seals, original financial publication and later historical sales using new schema targets Order 15 / Payment 14 / POS 12 and compatibility 0.28.0.

Reproduce with:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --no-restore --filter "FullyQualifiedName~DayChangeAttributionTests|FullyQualifiedName~MigrationRunnerTests|FullyQualifiedName~DaySealTests|FullyQualifiedName~DayCutoffTests|FullyQualifiedName~DayClosePreparationTests|FullyQualifiedName~EndOfDayDraftTests|FullyQualifiedName~FinancialCompletenessTests"
dotnet test tests/Architecture/NexaConnect.ArchitectureTests --no-restore
dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter "FullyQualifiedName~CustomerDaySealBoundaryTests|FullyQualifiedName~CustomerDayCutoffBoundaryTests|FullyQualifiedName~DayCloseHttpTests"
./scripts/test-day-seals.ps1 -ConfirmDisposableInfrastructure
./scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure
# From src/Frontend:
npm run check
npm test -- apps/customer-portal/src/dayClosePreparation.test.ts apps/customer-portal/src/dayCutoff.test.ts apps/customer-portal/src/daySeal.test.ts
npm run test:e2e:day-seal
npm run test:e2e:day-cutoff
```

Bounded local summaries: `.runstate/day-seals/fd26747bf18d41eebd89a54c8a44e1a8/verification.json` records 37 passed cases and verified cleanup; `.runstate/cashier-day-cutoff/b28adb115a634cb293be17d13ec3b7cc/verification.json` records passed real acceptance, Authorization verification and cleanup. The browser log has thirteen distinct bounded pass entries, and its Authorization TRX has six passed cases. Run-owned processes/containers/networks were removed; credentials and service logs are not repository artifacts.

Limitations: only proven entirely later-day changes are excluded. Historical changes before the sealed start remain conservatively relevant. Current totals are advisory source observations and do not certify delivery of a revised event set. Exact historical affected-day attribution, immutable historical Restaurant calendars, production capacity/retention acceptance, manager approval, finalization and fiscal adjustment workflows remain follow-up work.
