# Frontend dependency hardening — 2026-10-06

## Implementation and provenance

This slice started from clean commit `7dcfaf417fdbd5c83ec707a46d0ad4cb9254da4c`; the following changes are uncommitted workspace changes. Vitest moves from 3.2.6 to pinned 4.1.11, SourceMap.js from 1.2.1 to 1.2.2, and Tinypool is removed from the lockfile. Vite 7.3.6 and direct runtime dependency versions remain unchanged. The lockfile remains version 2. Existing Vitest configuration was reviewed and retained after passing compatibility checks.

The frontend manifest declares `^22.12.0 || >=24.0.0`. Separate production/full-tree audit commands fail at moderate severity. All four joined portal CI jobs invoke them after installation; a new frontend job checks Node 22 and 24 with installation, audits, TypeScript, unit tests and both portal builds. See [the runtime and operational policy](../../Deployment/Frontend-Dependency-Hardening.md).

## Verification

On Windows with Node 24.19.0, `npm ci --ignore-scripts`, both audit commands, TypeScript, all 28 unit tests in 11 files and both portal builds passed. Both audits reported zero vulnerabilities. The earlier full-tree audit reported four entries (one moderate, one high, two critical); the earlier production-only audit already reported zero. These are registry observations, not a production exploitability assessment.

With checksum-verified official Node 22.23.3 / npm 10.9.9, TypeScript, all 28 unit tests in 11 files, both portal builds and both audit commands also passed. The four joined-suite configuration/evidence guard suites passed all 15 cases without skips. The subsequent end-of-day gate additionally verified a fresh `npm ci --ignore-scripts` and BFF portal publishing under Node 22, with zero install audit findings.

The joined day-close gate passed eleven distinct real-OIDC browser scenarios and six Authorization persistence checks without skips/retries. Run `307e11efd83d4193a8428133e4d59ddf` completed at `2026-10-06T04:31:58Z` with `passed=true`, `authorizationPassed=true`, `cleanupVerified=true`, and `productionVerified=false`. Its bounded records are `.runstate/day-close-portal/307e11efd83d4193a8428133e4d59ddf/verification.json` and `src/Frontend/test-results/day-close-live/307e11efd83d4193a8428133e4d59ddf/summary.json`. Command: `pwsh -NoProfile -File scripts/test-day-close-portal.ps1 -ConfirmDisposableInfrastructure -NoBuild`. The unchanged service code used existing binaries; the launcher rebuilt/published the portal against a fresh installation.

The joined end-of-day gate passed eight distinct real-OIDC browser scenarios and six Authorization persistence checks without skips/retries. Run `bc846cd254fd49fea94cd4d6d2e40b56` records `passed=true`, `authorizationPassed=true`, `cleanupVerified=true`, and `productionVerified=false`. Its bounded records are `.runstate/end-of-day-portal/bc846cd254fd49fea94cd4d6d2e40b56/verification.json` and `src/Frontend/test-results/end-of-day-live/bc846cd254fd49fea94cd4d6d2e40b56/summary.json`. Command: `pwsh -NoProfile -File scripts/test-end-of-day-portal.ps1 -ConfirmDisposableInfrastructure -NoBuild`, with the run-owned Node 22 directory prepended to that process's PATH. Both launchers retained the base source revision and marked the source dirty for this change set.

## Documentation and boundaries

Updated documentation comprises root README, frontend workspace README, both portal READMEs and their publishing BFF READMEs, the project overview, canonical project architecture, deployment guide, four joined portal acceptance runbooks, the new frontend dependency policy, this evidence record and a dated follow-up in the earlier day-close acceptance record. Both architecture summaries changed to describe the supported runtime and CI audit boundary. Historical acceptance warnings remain dated evidence; subsequent verification supersedes the dependency follow-up without rewriting earlier observations.

The documentation-maintainer audit closed with no unresolved drift after checking manifest/lock/CI policy, both saved browser/Authorization/cleanup records, provenance and documentation links. Final manifest/lock consistency and `git diff --check` passed. Restaurant/POS architecture and ADR-021 were reviewed and remain unchanged because the financial and service boundaries are unaffected.

No financial/state-transition, tenant authorization, API, schema, or service ownership changes occur. Existing joined regression cases protect those boundaries. The runtime archive used for compatibility checks stays in an ignored run-owned directory and does not replace the installed Node runtime. No deployment, remote CI run or branch-protection configuration is claimed. Existing Vite large-chunk warnings remain a performance follow-up; registry findings must continue to be monitored through CI.
