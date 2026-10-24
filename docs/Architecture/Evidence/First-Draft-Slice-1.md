# First-draft slice 1 verification

Implemented on 14 October 2026 against base commit `7e91480429838f092093b4b142a2b0a64cce4c9f` with the current working-tree changes. See [the runbook](../../Deployment/First-Draft-Test-Stack.md) for commands, ownership, security, prerequisites and limitations. No production database or provider was used.

## Verification status

- Full Release unit suite: 807 passed, zero skips/failures with the Windows recovery opt-in enabled; six new cases protect explicit Redis configuration and fail-closed production defaults. Log: `.runstate/slice1-unit.log`.
- Architecture suite: six passed, zero skips/failures. Log: `.runstate/slice1-architecture.log`.
- Lifecycle guards: 29 passed, including traversal/run-ID rejection, manual tender/QR configuration, distinct loopback ports, PID reuse, environment restoration, Windows DPAPI round-trip, exclusive operation locking, lock release after rejected state and credential preservation when state is missing. The final missing-state guard is verified independently; it rejects an invalid run before normal startup and does not change the passed valid-run lifecycle.
- Controlled broker cookie ordering: root health probe reproduces permission failure/exit 1; user-scoped probe remains healthy. Cleanup verified. Bounded receipt: `.runstate/rabbit-health/b069f0c813c041448baf32c0252f4900/verification.json`. The artificial delay isolates the mechanism; it does not reproduce the exact remote filesystem or certify a pushed Ubuntu run.
- Joined draft run `ccc4918daecf47f282723c028b297d62` passed manual configuration, ten owning runtime/history grant checks, real OIDC login, Redis-backed BFF restart, Kitchen restart, full Stop/Start session persistence, Status and exact Reset cleanup. Fresh runs `ede4788120024b58b79f8b53491beee1` and final `641f0b29d8b2486c94031a6126c7e376` independently passed all those checks plus anonymous liveness payload checks and verified cleanup. The final run includes exclusive lifecycle locks, idempotent identity-role repair and the final cleanup-receipt implementation. Bounded receipts: `.runstate/first-draft/<run-id>/verification.json`.
- Existing joined end-of-day gate after the Compose fix passed eight real-OIDC browser cases plus six Authorization persistence cases without skips/retries and with verified cleanup. Run `937e215155b3428685862175d9530f50`; bounded receipt: `.runstate/end-of-day-portal/937e215155b3428685862175d9530f50/verification.json`, browser summary under `src/Frontend/test-results/end-of-day-live/937e215155b3428685862175d9530f50/summary.json`. This local Windows pass does not certify the pending Ubuntu rerun.

Generated draft builds use normal service binaries and system clocks. The tests create only identity-level smoke access and generated service schemas, not accepted Orders, settled tenders or branch/product permissions. A user may be authenticated while having no product access. That distinction is intentional.

## Boundaries and remaining work

Branch/menu/stock/terminal/role setup and `Asia/Singapore` business-calendar provisioning remain slice 2. POS generated scope UUIDs are placeholders and existing local recovery is untouched. QR possession/ownership, actual manual bank verification, physical WPF/PDF printing, checkout/Kitchen/business acceptance and backup/restore remain separate gates. Test scope remains THB.

Reporting OpenAPI GUID schema export fails; its root cause remains open in [the issue register](../../Operations/Unresolved-Issues.md). `/health/live` proves only a live HTTP host; initialization also requires catalog/schema/grants/audience/binding evidence. The shared structured JSON/correlation foundation remains in use; no raw tokens, cookies, bodies, personal/payment data or container configuration enter the bounded receipts.

Remote CI is not mutated or rerun in this slice. The base commit's failed end-of-day startup remains historical evidence until the changed definitions run successfully on Ubuntu. Local broker and end-of-day evidence must not be described as a remote pass.

Root README, both architecture summaries, API liveness guidance, database/identity/deployment instructions, affected component/docker READMEs and the plan are updated to describe the implementation. The restaurant architecture is reviewed without change: bounded contexts, financial invariants and the planned offline topology are unaffected. No new ADR is needed for local lifecycle tooling or honoring explicitly configured Redis; existing production storage and service-ownership decisions are preserved.

Documentation files updated:

- `README.md`, `AI/architecture/project_overview.md`, `docs/Architecture/Project-Architecture.md`.
- `docs/Deployment/First-Draft-Test-Stack.md`, `Deployment-Guide.md`, `CI-Clean-Checkout-Verification.md`, `Day-Close-Preparation.md`, `Day-Close-Cutoffs.md`, `Day-Close-Seals.md`, `Day-Close-Approvals.md`, `Day-Close-Finalization-Preparation.md`, `Day-Close-Settlements.md`.
- `docs/API/API-Guidelines.md`, `docs/Database/Database-Guidelines.md`, `docs/Identity/Client-Matrix.md`, `docs/Operations/Unresolved-Issues.md`.
- `docs/Planning/November-2026-First-Draft.md`, `docs/Planning/November-2026-Slice-0-Baseline.md`, this evidence file.
- READMEs in `src/BuildingBlocks/NexaConnect.Infrastructure`, `src/Clients/NexaConnect.POS`, `src/Gateway/NexaConnect.CustomerBff`, `src/Gateway/NexaConnect.PlatformAdminBff`, and Order/POS/Reporting service directories.
- READMEs in `docker/first-draft`, `docker/financial-portal`, `docker/end-of-day-portal`, `docker/cashier-day-close`, and `src/Frontend/e2e/first-draft`; `.env.example` points to the generated-secret workflow.

The required documentation-maintainer audit passed after current-version/history corrections. Links and final whitespace checks pass. No documentation blocker remains; the runtime/business/remote limitations above remain explicit follow-up work.
