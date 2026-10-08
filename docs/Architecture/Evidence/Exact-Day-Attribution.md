# Exact historical-day attribution verification

Local execution on 2026-10-08 (Asia/Singapore) verified the ADR-026 implementation on the working tree based on `ce535e337b422894e2284a8fd801e6be9f348604`. Earlier [ADR-025 execution](Day-Change-Attribution.md) remains historical. These are local disposable acceptance results, not production certification.

| Verification | Executed/passed |
| --- | ---: |
| Exact attribution, legacy timestamp helper, seal/cutoff/preparation, end-of-day, completeness and migration unit tests | 91 |
| Disposable PostgreSQL financial source/coordinator/reconciliation matrix | 42 |
| Seal/cutoff BFF and day-close HTTP boundaries | 27 |
| Architecture tests | 6 |
| Portal preparation/cutoff/seal contract tests | 11 |
| Synthetic seal Chromium scenarios, including record details and truncation | 7 |
| Synthetic cutoff Chromium scenarios | 8 |
| Real-OIDC cashier-to-cutoff/seal scenarios | 13 |
| Joined gate Authorization persistence cases | 6 |

All 211 cases passed without skips. TypeScript checking, service/BFF compilation and the joined gate's portal production build passed. The documentation-maintainer audit completed without unresolved code/documentation drift. Final diff and documentation links were reviewed.

New Domain cases distinguish Order creation and tender dates, terminal versus uncertain Payment work, refund completion, drawer closure versus carry-forward review, before/after date moves, half-open boundaries, missing financial history and ownership uncertainty. New PostgreSQL cases prove unrelated historical seals survive permitted completed-Order metadata changes, restored values remain pending for their original selected windows, date moves preserve before-state relevance, completed refund immutability and publication parent/completion capture, prior approved drawer exclusion versus financial-version mismatch, and timestamp-only legacy upgrade refusal to infer history. Existing transaction, source lock, replay/restart, authorization/revocation, calendar, delivery/integrity, overflow, epoch and legacy operation-fingerprint cases remain covered. Immutable paid/refund guards were preserved; tests do not introduce a financial correction endpoint.

Reproduce with:

```powershell
dotnet test tests/Unit/NexaConnect.UnitTests --no-restore --filter "FullyQualifiedName~ExactDayAttributionTests|FullyQualifiedName~DayChangeAttributionTests|FullyQualifiedName~MigrationRunnerTests|FullyQualifiedName~DaySealTests|FullyQualifiedName~DayCutoffTests|FullyQualifiedName~DayClosePreparationTests|FullyQualifiedName~EndOfDayDraftTests|FullyQualifiedName~FinancialCompletenessTests"
dotnet test tests/Architecture/NexaConnect.ArchitectureTests --no-restore
dotnet test tests/Integration/NexaConnect.IntegrationTests --no-restore --filter "FullyQualifiedName~CustomerDaySealBoundaryTests|FullyQualifiedName~CustomerDayCutoffBoundaryTests|FullyQualifiedName~DayCloseHttpTests"
./scripts/test-day-seals.ps1 -ConfirmDisposableInfrastructure
./scripts/test-cashier-day-cutoff.ps1 -ConfirmDisposableInfrastructure
# After the joined runner finishes its dependency install, from src/Frontend:
npm run check
npm test -- apps/customer-portal/src/dayClosePreparation.test.ts apps/customer-portal/src/dayCutoff.test.ts apps/customer-portal/src/daySeal.test.ts
npm run test:e2e:day-seal
npm run test:e2e:day-cutoff
```

Bounded local summaries: `.runstate/day-seals/97fb120af01949d88750ff755e2ebdfb/verification.json` records 42 passed cases and verified cleanup. `.runstate/cashier-day-cutoff/8b890599b8fd4ce3b2f49cdd6066225e/verification.json` records joined acceptance, Authorization verification and cleanup. Its bounded browser log has thirteen distinct pass entries and its Authorization TRX has six passed cases. Both use Order 16 / Payment 15 / POS 13, compatibility 0.29.0. Run-owned processes/containers/networks were removed; credentials and service logs are not repository artifacts.

Limits remain: legacy/missing evidence and ownership/calendar uncertainty block readiness; selected changes remain pending after totals are restored. Current totals are advisory and do not prove delivery of a revised event set. Sequential source checks are not a global cut. Historical Restaurant calendar ownership, production capacity/retention acceptance, fiscal adjustments, manager approval and finalization remain follow-up work.
