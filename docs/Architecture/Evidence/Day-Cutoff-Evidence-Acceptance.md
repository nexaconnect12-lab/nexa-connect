# Retained day-cutoff foundation verification

Implementation adds immutable source-observation generations, a fresh Reporting original-event comparison and separately retained POS coordination. Order 12 / Payment 11 / POS 9 require compatibility 0.25.0, Reporting 20 and Authorization 10. Customer BFF/Portal expose the separate cutoff workflow. These cuts are evidence for review; source-write fences, delivery watermarks, approval and finalization remain unimplemented. See [ADR-022](../Decisions/ADR-022-retained-day-cutoff-evidence.md), [API](../../API/Day-Close-Cutoffs.md) and [rollout](../../Deployment/Day-Close-Cutoffs.md).

## Local verification on 2026-10-06

- Full solution build: zero warnings/errors.
- Full unit suite: 631 passed, five existing Windows DPAPI opt-in cases skipped, no failures. The earlier focused cutoff/preparation/draft/completeness selection passed 37 without skips; the added Domain retained-reference guard passed in the final full suite.
- Architecture suite: six passed without skips, including cutoff Domain dependency direction.
- Focused cutoff/preparation/end-of-day/completeness HTTP and BFF boundaries: 28 passed without skips. Session/tenant/live membership/CSRF/correlation and sanitized upstream failures are covered; source HTTP transport in the database pipeline is controlled.
- Disposable PostgreSQL/Application matrix: 14 passed without skips. Actual source readers, original-event validation/retention, Reporting inventories/hash receipts and POS coordination persistence are used. Cases cover absent delivery followed by original projection, same-total identity drift, late cash and superseded review, partial capture/restart/exact resume, two-manager contention, expired claims, immutable histories, downgrade guards and Authorization role separation.
- Frontend TypeScript and all 30 unit contracts passed; Customer Portal production build passed with its existing large-bundle warning.
- Eight synthetic cutoff browser cases passed without skips/retries using the actual portal and controlled BFF replies. Cases cover command identity/CSRF, accountant controls, response uncertainty, filter/tenant clearing, malformed readiness and interrupted resume/replacement. This is separate from real-OIDC joined acceptance. Browser screenshots, videos and traces are disabled.
- The existing preparation synthetic browser regression also passed all eight cases without skips/retries; it remains separate from its joined real-OIDC gate.
- Required documentation-maintainer audit completed; documentation describes retained observations and explicitly preserves the remaining finality and production boundaries.

Final database runner command: `pwsh -NoProfile -File scripts/test-day-cutoffs.ps1 -ConfirmDisposableInfrastructure`. Run `8e2850eb398448b389f640b102f23f8e` completed at `2026-10-06T12:29:38.1090596Z` with `passed=true`, `caseCount=14`, `cleanupVerified=true`, `productionVerified=false`, against base commit `93039b441b88cad221732057a3d11129e5b5ee0c` with `sourceDirty=true`. [Bounded local verification](../../../.runstate/day-cutoffs/8e2850eb398448b389f640b102f23f8e/verification.json) is ignored run-owned evidence, not a clean-commit or remote-CI claim. Restricted local TRX is retained alongside it; CI uploads only bounded verification JSON.

## Documentation handoff

Updated root README, AI project overview, Project Architecture and Restaurant-POS Architecture; new ADR-022/API/rollout and this evidence record; Database Design; Deployment Guide, preparation and financial recovery runbooks; Identity Client Matrix; preparation, draft and financial completeness API guides; component READMEs for Order, Payment, POS, Reporting, Customer BFF, Frontend workspace/Customer Portal/day-cutoff browser contracts, Contracts, Infrastructure and DataMigration. Both canonical architecture summaries changed for the new source/POS ownership, fresh comparison and deployment dependencies. ADR-019/020/021 remain historical decisions and were reviewed without rewriting their original scope.

## Remaining release work

No production database migration or financial action was performed. Remote CI, new joined browser/OIDC/provider acceptance, production privileges/TLS/capacity/backup restoration and physical POS/printer validation remain separate release gates. There is no atomic cross-service snapshot or instantaneous invalidation after the last read. A later finalization protocol must define source fences/watermarks, historical calendar ownership and late-correction policy before a day can be certified.
