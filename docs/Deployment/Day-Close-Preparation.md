# Day-close preparation rollout and verification

Deploy POS migration 8 (`0008_day_close_preparations`) and Authorization 10 (`0010_day_close_permissions`), both minimum application 0.24.0, before enabling preparation. The standard migration launcher defaults to 0.24.0. Retain Order 11, Payment 10, Reporting 20 and Restaurant 3. Use independently owned database runtime credentials with only required schema/table privileges; do not deploy with migration-owner credentials.

POS runtime needs SELECT/INSERT/UPDATE on `branch_day_closes` and `branch_day_close_operations`, and SELECT/INSERT on `branch_day_close_audit`; it needs no schema creation or audit rewrite privilege. Audit triggers reject UPDATE/DELETE/TRUNCATE even if accidentally granted. Its primary key is organization/branch/date and all repository reads additionally predicate Restaurant ownership. Source reads keep existing owning-table SELECT. State transition, operation status and audit are atomic; no network call occurs under the POS transaction.

Set POS `Services__Reporting` to the actual HTTPS Reporting host and Customer BFF `Services__POS` to the actual HTTPS POS host. Development defaults are POS→Reporting `http://localhost:51227/` and BFF→POS `https://localhost:7120/`; use the service HTTPS profiles and trusted certificates when appropriate. Existing Reporting Order/Payment/POS/Restaurant dependencies and workload identity remain required. POS forwards the server-held customer bearer to Reporting, which independently authorizes all sources. Do not use a workload identity to bypass financial permissions. Align clocks/timezone data and confirm branch currency. See [access, blockers, limits and recovery](../API/Day-Close-Preparation.md).

Deploy the additive source summary fingerprints in Order, Payment and POS together with the POS adapter, BFF and portal. Old/null fingerprints block preparation safely. Reporting stays read-only and needs no new schema or permission. Enable neither POS publication nor outbox dispatch solely for preparation; this slice emits no integration event. Existing retained-event delivery/completeness recording remains a prerequisite for absence of financial blockers.

A source failure results in Blocked; hierarchy/Authorization/database failures or overall cancellation return unavailable without Ready evidence. After response loss or process interruption, load the saved record. Its original authorized actor can explicitly resume the pending command after 30 seconds. Managers may replace expired work using a new operation and current version; old completion is fenced. Resolve source issues through their existing workflows, then explicitly refresh preparation. A historical check or Ready observation never authorizes settlement. No automatic polling or lease reaper is required for this slice.

Disable preparation routes/controls before reverting Authorization 10. Its downgrade removes both role-permission associations and user permission overrides for the two day-close permissions. POS `8→7` refuses while any preparation, operation or audit exists, including blocked records. Use forward recovery; do not remove retained evidence to force downgrade. Empty-schema downgrade/reapply is tested. Back up the POS database and audit under the existing financial evidence retention policy; this slice supplies no purge or export.

Run from repository root:

```powershell
dotnet build NexaConnect.sln --no-restore -v quiet
dotnet test NexaConnect.sln --no-restore -v quiet
pwsh -NoProfile -File scripts/test-day-close-preparation.ps1 -ConfirmDisposableInfrastructure
```

The guarded script uses only cached `postgres:17-alpine`, creates a random-password localhost-only `nexa_day_close_it_<guid>` container, executes day-close/source PostgreSQL cases in generated schemas and removes its exact container in finally. It refuses execution without the disposable-infrastructure flag, preserves parent environment and fails on cleanup error. It does not touch running project infrastructure. Direct database opt-ins require `NEXACONNECT_ENVIRONMENT=Testing` and disposable `NEXACONNECT_POS_INTEGRATION_DB` / `NEXACONNECT_REPORTING_INTEGRATION_DB`; never use production credentials.

Run from `src/Frontend`:

```powershell
npm run check
npm test
npm run build
npm run test:e2e:day-close
npm run test:e2e:end-of-day
```

`DayClosePreparationTests` protect aggregate blockers, frozen evidence, identity drift, live authorization and cancellation. `DayClosePostgresTests` exercise duplicate/conflicting replay, two-manager contention, expired restart/resume, superseded claims, scoped reads, immutable audit, retained-history downgrade refusal and empty downgrade/reapply, plus Authorization permission backfill/new assignments, live scoped denial and removal of associations/overrides on downgrade. `DayCloseHttpTests` run real POS Application/controller and Reporting adapter with controlled transport; `CustomerDayCloseBoundaryTests` cover session/tenant/membership/CSRF/correlation and sanitized errors. Owner PostgreSQL tests cover stable fingerprints and financial/version changes. Browser contracts use synthetic BFF replies and the actual portal, with no trace/video/screenshot retention. They do not certify live OIDC or the complete joined preparation stack.

The earlier [joined end-of-day read gate](End-Of-Day-Portal-Acceptance.md) retains its independently pinned baselines and read-only scenarios. It is not evidence of durable preparation. The separate [joined real-OIDC preparation gate](Day-Close-Portal-Acceptance.md) is implemented for restart/resume/replacement and late evidence, with synthetic source transitions explicitly bounded. Local Windows acceptance passed eleven browser and six Authorization persistence cases without skips/retries and with verified cleanup on 2026-10-06; remote CI, target-production least privilege/latency/capacity, timezone acceptance and eventual settlement approval/cutoff/locking remain follow-up work. This implementation is a reviewable preparation draft, not production settlement readiness.
