# Joined day-close preparation acceptance evidence

Local Windows verification on 2026-10-06 (Asia/Singapore). Base revision `31f2476e46bf8b46212b9997a8425fa4bf0e4161`; `sourceDirty=true`. The previous preparation implementation was already present and uncommitted at task start; it was preserved. This slice adds joined acceptance tooling, fixtures, tests, CI configuration and documentation. No commit, production deployment or production data mutation was performed.

## Implemented

- `scripts/test-day-close-portal.ps1` selects a separate guarded mode in the shared lifecycle runner. Old financial/read-only modes retain their baselines/counts. New mode pins POS 8 / Authorization 10, compatibility 0.24.0, three actual OIDC subjects and real Customer BFF→POS→Reporting→owning sources.
- Runner-owned file control accepts only exact run/request identities and stop/start actions for the retained POS handles/configuration. Preparing is privately observed before process interruption; actual 30-second leases, original-operation resume and second-manager replacement execute without clock bypass or production debug endpoints.
- Guarded fixture commands resolve POS operational blockers through owning repositories, dispatch original Order/Payment outboxes through RabbitMQ, invoke the real financial recording CLI and privately assert preparation audit/version/operation and granted-decision consistency. A completed Order repository save tests same-total metadata fingerprint drift; a synthetic manual-tender recipient projection tests late drawer financial-version drift.
- Eleven named real-OIDC browser scenarios require all passes, no skips/retries/duplicates/unknown replacement titles. Settings/reporter guards and the CI bounded-artifact allowlist protect scope and sensitive diagnostics. Existing synthetic browser contracts remain separate.

See [runbook and exact fixture/release boundaries](../../Deployment/Day-Close-Portal-Acceptance.md).

## Joined proof

| Gate | Run ID | Required result | Verified cleanup |
| --- | --- | --- | --- |
| New preparation gate | `14f563dc51d44f929d9bb2bcef37ee60` | 11 browser / 6 Authorization cases passed, zero skipped, no retries | Yes |
| Existing end-of-day read regression | `e33e4861fbd5458698a6ddad6162ca62` | 8 browser / 6 Authorization cases passed, zero skipped, no retries | Yes |

Preparation runner completed at `2026-10-06T03:14:52.9504923Z`, with `passed=true`, `authorizationPassed=true`, `cleanupVerified=true`, `productionVerified=false`. Its browser summary has `verified=true`, `passed=11`, `total=11`, completed `2026-10-06T03:14:49.663Z`. Read regression completed `2026-10-06T03:19:38.8182386Z`, with the same success/cleanup/non-production flags and a verified 8/8 browser summary.

Bounded local records:

- [Preparation verification](../../../.runstate/day-close-portal/14f563dc51d44f929d9bb2bcef37ee60/verification.json)
- [Preparation browser summary](../../../src/Frontend/test-results/day-close-live/14f563dc51d44f929d9bb2bcef37ee60/summary.json)
- [Read regression verification](../../../.runstate/end-of-day-portal/e33e4861fbd5458698a6ddad6162ca62/verification.json)
- [Read regression browser summary](../../../src/Frontend/test-results/end-of-day-live/e33e4861fbd5458698a6ddad6162ca62/summary.json)

These ignored run-owned files are local proof, not committed artifacts. CI uploads only bounded verification/summary JSON for 14 days. Raw fixture/control state, credentials/environment, certificates, TRX and service/browser diagnostics are excluded. No screenshot, trace or video capture is enabled.

An earlier fresh run stopped after seven successful scenarios when the test incorrectly attempted to reopen an already-approved drawer review. The service correctly rejected that unsupported transition. The corrected fixture uses supported completed Order metadata persistence without changing immutable receipt/publication or totals. The failed run also verified cleanup; it is not counted as acceptance.

## Other verification

| Verification | Result |
| --- | --- |
| `dotnet build src/Tools/NexaConnect.FinancialPortalAcceptance --no-restore -v quiet` | Passed, zero warnings/errors |
| `dotnet test NexaConnect.sln --no-restore -v quiet` | Unit 625 passed / 5 opt-in skipped; architecture 5 passed; integration 224 passed / 112 opt-in skipped; no failures |
| `npm run check` | Passed |
| `npm test` | 28 tests across 11 files passed |
| New / existing end-of-day / existing financial settings guards | 4 / 4 / 4 passed, zero skipped |
| Published BFF/customer portal | Built successfully in both joined runs |
| Shared runner regression | Existing eight-scenario read gate passed under its original POS 7 / Authorization 9 baselines |
| Final review | Scope, exact process ownership, scoped nonce/ack control, privacy, old-mode compatibility and code/documentation boundaries reviewed; `git diff --check` passed |

Broader opt-in skips are disclosed and not counted as acceptance. The joined gates explicitly ran their six Authorization database cases without skips. This slice does not re-execute all optional provider/broker/physical-client matrices. The standalone financial seven-browser gate was not rerun in this slice; its guard suite passed and the unchanged default-mode branches were reviewed.

## Documentation updated

- [Root README](../../../README.md)
- [Project overview](../../../AI/architecture/project_overview.md)
- [Canonical project architecture](../Project-Architecture.md)
- [Restaurant/POS architecture](../Restaurant-POS-Architecture.md)
- [Preparation API](../../API/Day-Close-Preparation.md)
- [Database design](../../Database/Database-Design.md)
- [Identity claims contract](../../Identity/Claims-Contract.md)
- [Deployment Guide](../../Deployment/Deployment-Guide.md)
- [Preparation rollout](../../Deployment/Day-Close-Preparation.md)
- [Joined preparation runbook](../../Deployment/Day-Close-Portal-Acceptance.md)
- [Earlier preparation handoff, dated follow-up](Day-Close-Preparation.md)
- [Disposable Docker topology README](../../../docker/end-of-day-portal/README.md)
- [Acceptance fixture tool README](../../../src/Tools/NexaConnect.FinancialPortalAcceptance/README.md)
- [Frontend workspace README](../../../src/Frontend/README.md)
- [Customer Portal README](../../../src/Frontend/apps/customer-portal/README.md)
- [Customer BFF README](../../../src/Gateway/NexaConnect.CustomerBff/README.md)
- [POS service README](../../../src/Services/NexaConnect.Services.POS/README.md)
- [Joined browser suite README](../../../src/Frontend/e2e/day-close-live/README.md)
- This new evidence handoff.

The project overview and canonical architecture were updated to reflect the implemented and locally verified joined gate. ADR-021 was reviewed and remains unchanged: service ownership, preparation/cutoff policy and public financial contracts are unchanged. The documentation-maintainer audit reviewed the actual runner/fixture/browser/CI implementation and evidence; final provenance review is recorded by the primary-agent handoff.

## Limits and follow-up

This proves actual authenticated preparation HTTP, source reads, durable state/replay/concurrency, restart/resume/replacement, conservative metadata/late-cash invalidation, failure/deadline behavior and live access revocation in a disposable local stack. Synthetic provider outcomes, owner-repository close/review transitions and a synthetic late-event recipient projection are explicitly test fixtures; they do not prove provider/physical cashier command execution or original late-event production/broker delivery.

Remote CI execution and required branch protection, production TLS/least privilege/clock/timezone/capacity/latency, offline POS, financial cutoff/delivery-watermark certification and settlement approval/locking remain open. Both runs set `productionVerified=false`.

`npm ci` reported four existing dependency advisories (one moderate, one high, two critical); Vite reports existing large chunks. This slice changed no package versions or lockfile. Dependency triage and bundle optimization remain follow-up work; these observations do not determine production exploitability.
